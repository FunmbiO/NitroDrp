import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, ApiError, type AppConfig, type EmailResponse } from '../lib/api'
import { EmailFields, draftProblem, emptyDraft, type EmailDraft } from '../components/EmailFields'
import { parseRecipients } from '../lib/email'
import { formatBytes } from '../lib/format'
import { matchFiles, sigOf } from '../lib/resume'
import { addRecent, clearPending, getApiKey, getPending, savePending, setApiKey, type PendingUpload } from '../lib/storage'
import { DEMO } from '../lib/config'
import { putPart } from '../lib/transport'
import { uploadParts, type UploadedPart } from '../lib/upload'
import { CheckIcon, CloseIcon, FileIcon, UploadIcon } from '../components/Icons'

interface Item {
  file: File
  uploaded: number
  done: boolean
}

/** What to upload for one selected file, whether the share is brand new or being resumed. */
interface FilePlan {
  id: string
  size: number
  partCount: number
  complete: boolean
  parts: UploadedPart[]
}

const EXPIRY = [
  { days: 1, label: '1 day' },
  { days: 7, label: '7 days' },
  { days: 30, label: '30 days' },
]

export function UploadPage() {
  const nav = useNavigate()
  const input = useRef<HTMLInputElement>(null)
  const abort = useRef<AbortController | null>(null)
  const [items, setItems] = useState<Item[]>([])
  const [over, setOver] = useState(false)
  const [days, setDays] = useState(7)
  const [usePw, setUsePw] = useState(false)
  const [password, setPassword] = useState('')
  const [limit, setLimit] = useState('')
  const [apiKey, setKey] = useState(DEMO ? 'demo' : getApiKey())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [pending, setPending] = useState<PendingUpload | null>(getPending)
  const [config, setConfig] = useState<AppConfig | null>(null)
  const [useEmail, setUseEmail] = useState(false)
  const [draft, setDraft] = useState<EmailDraft>(emptyDraft)
  useEffect(() => {
    // Email is optional on the server; the section only shows when it is set up. A failed lookup just hides it.
    api.getConfig().then(setConfig, () => setConfig(null))
  }, [])
  const emailing = useEmail && !!config?.emailEnabled
  const emailProblem = emailing
    ? parseRecipients(draft.recipients).valid.length === 0
      ? 'Add at least one email address, or turn off sending by email.'
      : draftProblem(draft, config?.maxRecipientsPerRequest ?? 10)
    : ''

  // Resuming = the user re-picked exactly the files of an unfinished upload.
  const resumeMap = pending ? matchFiles(pending.files, items.map((i) => sigOf(i.file))) : null
  const resuming = resumeMap !== null
  const locked = busy || resuming

  const total = items.reduce((a, i) => a + i.file.size, 0)

  const addFiles = (list: FileList | null) => {
    if (!list || busy) return
    const fresh = [...list].map((file) => ({ file, uploaded: 0, done: false }))
    setItems((cur) => [...cur, ...fresh])
  }
  const patch = (index: number, p: Partial<Item>) => setItems((cur) => cur.map((it, i) => (i === index ? { ...it, ...p } : it)))

  async function discardPending() {
    if (pending) await api.deleteShare(pending.token, pending.ownerKey).catch(() => {})
    clearPending()
    setPending(null)
  }

  async function send() {
    setError('')
    setBusy(true)
    const ctl = new AbortController()
    abort.current = ctl
    try {
      const key = apiKey.trim()
      if (!DEMO) setApiKey(key)

      let token: string
      let ownerKey: string
      let expiresAt: string
      let partSize: number
      let finalized = false
      let plans: FilePlan[] // aligned with `items`

      if (pending && resumeMap) {
        let status
        try {
          status = await api.uploadStatus(pending.token, pending.ownerKey)
        } catch (e) {
          clearPending()
          setPending(null)
          throw new Error(
            e instanceof ApiError && e.status === 410
              ? 'That unfinished upload expired. Your files are still selected: press Create link to start again.'
              : 'Could not resume that upload. Your files are still selected: press Create link to start again.',
          )
        }
        ;({ token, ownerKey } = pending)
        expiresAt = status.expiresAt
        partSize = status.partSize
        finalized = status.finalized
        plans = []
        pending.files.forEach((_, p) => {
          plans[resumeMap[p]] = status.files[p]
        })
      } else {
        const created = await api.createShare(key, {
          files: items.map((i) => ({ name: i.file.name, size: i.file.size, contentType: i.file.type || 'application/octet-stream' })),
          expiresInDays: days,
          password: usePw && password ? password : undefined,
          downloadLimit: limit ? Number(limit) : undefined,
        })
        ;({ token, ownerKey, expiresAt, partSize } = created)
        plans = created.files.map((f) => ({ id: f.id, size: f.size, partCount: f.partCount, complete: false, parts: [] }))
        const p: PendingUpload = {
          token,
          ownerKey,
          createdAt: new Date().toISOString(),
          expiresAt,
          files: items.map((i) => sigOf(i.file)),
          passwordProtected: usePw && !!password,
          downloadLimit: limit ? Number(limit) : null,
        }
        savePending(p) // from here on a crash or closed tab can be resumed
        setPending(p)
      }

      if (!finalized) {
        for (const [index, plan] of plans.entries()) {
          if (plan.complete) {
            patch(index, { uploaded: plan.size, done: true })
            continue
          }
          const parts = await uploadParts(items[index].file, partSize, plan.partCount, {
            signal: ctl.signal,
            alreadyUploaded: plan.parts,
            presign: (n) => api.presign(token, plan.id, ownerKey, n),
            putPart,
            onProgress: (uploaded) => patch(index, { uploaded }),
          })
          await api.completeFile(token, plan.id, ownerKey, parts)
          patch(index, { uploaded: plan.size, done: true })
        }
      }
      await api.finalize(token, ownerKey)

      const info = getPending()
      addRecent({
        token,
        ownerKey,
        createdAt: info?.createdAt ?? new Date().toISOString(),
        expiresAt,
        fileCount: items.length,
        totalSize: total,
        passwordProtected: info?.passwordProtected ?? false,
        downloadLimit: info?.downloadLimit ?? null,
      })
      clearPending()

      // The link exists now, so a failed email must never lose it: report it on the next page instead.
      let emailOutcome: { response?: EmailResponse; error?: string } | undefined
      if (emailing) {
        try {
          emailOutcome = {
            response: await api.sendEmail(token, ownerKey, {
              recipients: parseRecipients(draft.recipients).valid,
              message: draft.message.trim() || undefined,
              senderName: draft.senderName.trim() || undefined,
              senderEmail: draft.senderEmail.trim() || undefined,
            }),
          }
        } catch (e) {
          emailOutcome = { error: e instanceof Error ? e.message : 'The email could not be sent.' }
        }
      }
      nav(`/created/${token}`, { state: { emailOutcome } })
    } catch (err) {
      if (ctl.signal.aborted) return // the user cancelled; cancel() already cleaned up
      // Keep the pending upload: the files are still selected, so the button turns into "Resume upload".
      const base = err instanceof Error ? err.message : 'Upload failed.'
      setError(getPending() ? `${base} Your progress is saved: press Resume upload to continue.` : base)
      setPending(getPending())
      setItems((cur) => cur.map((i) => (i.done ? i : { ...i, uploaded: 0 })))
      setBusy(false)
    }
  }

  async function cancel() {
    abort.current?.abort()
    setBusy(false)
    setItems((cur) => cur.map((i) => ({ ...i, uploaded: 0, done: false })))
    await discardPending()
  }

  const canSend = items.length > 0 && !busy && apiKey.trim().length > 0 && (resuming || !usePw || password.length > 0) && !emailProblem

  return (
    <main className="page">
      <div className="stack" style={{ gap: 8 }}>
        <div className="prompt">$ nitrodrp send --private</div>
        <h1>
          Send files, <mark>privately</mark>.<span className="cursor" aria-hidden="true" />
        </h1>
        <p className="lede">Upload, set an expiry, share one link. Files are deleted automatically when it runs out.</p>
      </div>

      <div className="layout">
        <div className="main">
          {pending && !busy && (
            <div className="notice" role="status">
              <p>
                {resuming
                  ? 'Those are the files from your unfinished upload. Press Resume upload to carry on.'
                  : `Unfinished upload: ${pending.files.length} file${pending.files.length === 1 ? '' : 's'} (${formatBytes(pending.files.reduce((a, f) => a + f.size, 0))}). Pick the same files to resume it.`}
              </p>
              <button type="button" className="link-btn" onClick={discardPending}>Discard</button>
            </div>
          )}
          <div
            className={`drop${over ? ' over' : ''}`}
            onDragOver={(e) => {
              e.preventDefault()
              setOver(true)
            }}
            onDragLeave={() => setOver(false)}
            onDrop={(e) => {
              e.preventDefault()
              setOver(false)
              addFiles(e.dataTransfer.files)
            }}
          >
            <UploadIcon />
            <div className="title">Drop files here</div>
            <div className="muted" style={{ fontSize: 15 }}>or pick them from your computer</div>
            <input ref={input} type="file" multiple hidden onChange={(e) => { addFiles(e.target.files); e.target.value = '' }} />
            <button type="button" className="btn outline" disabled={busy} onClick={() => input.current?.click()}>
              Browse files
            </button>
          </div>

          {items.length > 0 && (
            <div className="card files">
              {items.map((it, i) => {
                const pct = it.file.size ? Math.round((it.uploaded / it.file.size) * 100) : 0
                const active = busy && !it.done && it.uploaded > 0
                return (
                  <div className="file-row" key={`${it.file.name}-${i}`}>
                    <FileIcon />
                    <div className="body">
                      <div className="top">
                        <span className="name">{it.file.name}</span>
                        {it.done ? (
                          <span className="meta done"><CheckIcon />{formatBytes(it.file.size)}</span>
                        ) : (
                          <span className="meta">{formatBytes(it.file.size)}{active ? ` · ${pct}%` : ''}</span>
                        )}
                      </div>
                      {active && (
                        <div className="bar" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100} aria-label={`Uploading ${it.file.name}`}>
                          <div style={{ width: `${pct}%` }} />
                        </div>
                      )}
                    </div>
                    <button type="button" className="icon-btn" disabled={busy} aria-label={`Remove ${it.file.name}`} onClick={() => setItems((cur) => cur.filter((_, j) => j !== i))}>
                      <CloseIcon />
                    </button>
                  </div>
                )
              })}
            </div>
          )}
        </div>

        <form className="card pad side" onSubmit={(e) => { e.preventDefault(); if (canSend) void send() }}>
          <div className="stack" style={{ gap: 10 }}>
            <div className="group-title">Link expires after</div>
            <div className="pills">
              {EXPIRY.map((o) => (
                <button key={o.days} type="button" className="pill" disabled={locked} aria-pressed={days === o.days} onClick={() => setDays(o.days)}>
                  {o.label}
                </button>
              ))}
            </div>
          </div>

          <div className="stack" style={{ gap: 10 }}>
            <label className="check">
              <input type="checkbox" checked={usePw} disabled={locked} onChange={(e) => setUsePw(e.target.checked)} />
              <span>Require a password</span>
            </label>
            {usePw && (
              <div className="field">
                <label htmlFor="pw" className="muted" style={{ fontWeight: 400, fontSize: 14 }}>Password</label>
                <input id="pw" className="input" type="password" autoComplete="new-password" disabled={locked} value={password} onChange={(e) => setPassword(e.target.value)} />
              </div>
            )}
          </div>

          <div className="field">
            <label htmlFor="limit">Download limit</label>
            <input id="limit" className="input" type="number" min={1} max={10000} placeholder="Unlimited" disabled={locked} value={limit} onChange={(e) => setLimit(e.target.value)} />
          </div>

          {config?.emailEnabled && (
            <div className="stack" style={{ gap: 12 }}>
              <label className="check">
                <input type="checkbox" checked={useEmail} disabled={busy} onChange={(e) => setUseEmail(e.target.checked)} />
                <span>Also send the link by email</span>
              </label>
              {useEmail && (
                <>
                  <EmailFields draft={draft} onChange={setDraft} idPrefix="up" maxPerRequest={config.maxRecipientsPerRequest} disabled={busy} />
                  {config.simulated && <p className="hint" style={{ margin: 0 }}>Demo: no email is really sent.</p>}
                </>
              )}
            </div>
          )}

          {!DEMO && (
            <div className="field">
              <label htmlFor="key">Access key</label>
              <input id="key" className="input" type="password" autoComplete="current-password" placeholder="Set in the server's .env" value={apiKey} onChange={(e) => setKey(e.target.value)} />
            </div>
          )}

          {resuming && <p className="hint">Resuming with the settings you chose the first time.</p>}
          {error && <p className="error" role="alert">{error}</p>}

          <div className="stack" style={{ paddingTop: 4 }}>
            {busy ? (
              <button type="button" className="btn big" onClick={cancel}>Cancel upload</button>
            ) : (
              <button type="submit" className="btn big" disabled={!canSend}>{resuming ? 'Resume upload' : 'Create link'}</button>
            )}
            <div className="summary">
              {items.length} file{items.length === 1 ? '' : 's'} · {formatBytes(total)} total
            </div>
          </div>
        </form>
      </div>
    </main>
  )
}
