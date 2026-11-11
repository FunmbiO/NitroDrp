import { useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, ApiError } from '../lib/api'
import { formatBytes } from '../lib/format'
import { addRecent, getApiKey, setApiKey } from '../lib/storage'
import { uploadParts, xhrPutPart } from '../lib/upload'
import { CheckIcon, CloseIcon, FileIcon, UploadIcon } from '../components/Icons'

interface Item {
  file: File
  uploaded: number
  done: boolean
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
  const [apiKey, setKey] = useState(getApiKey)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const total = items.reduce((a, i) => a + i.file.size, 0)

  const addFiles = (list: FileList | null) => {
    if (!list || busy) return
    const fresh = [...list].map((file) => ({ file, uploaded: 0, done: false }))
    setItems((cur) => [...cur, ...fresh])
  }
  const patch = (index: number, p: Partial<Item>) => setItems((cur) => cur.map((it, i) => (i === index ? { ...it, ...p } : it)))

  async function send() {
    setError('')
    setBusy(true)
    const ctl = new AbortController()
    abort.current = ctl
    let created: Awaited<ReturnType<typeof api.createShare>> | undefined
    try {
      setApiKey(apiKey.trim())
      created = await api.createShare(apiKey.trim(), {
        files: items.map((i) => ({ name: i.file.name, size: i.file.size, contentType: i.file.type || 'application/octet-stream' })),
        expiresInDays: days,
        password: usePw && password ? password : undefined,
        downloadLimit: limit ? Number(limit) : undefined,
      })
      const share = created

      for (const [index, meta] of share.files.entries()) {
        const parts = await uploadParts(items[index].file, share.partSize, meta.partCount, {
          signal: ctl.signal,
          presign: (n) => api.presign(share.token, meta.id, share.ownerKey, n),
          putPart: xhrPutPart,
          onProgress: (uploaded) => patch(index, { uploaded }),
        })
        await api.completeFile(share.token, meta.id, share.ownerKey, parts)
        patch(index, { uploaded: meta.size, done: true })
      }
      await api.finalize(share.token, share.ownerKey)

      addRecent({
        token: share.token,
        ownerKey: share.ownerKey,
        createdAt: new Date().toISOString(),
        expiresAt: share.expiresAt,
        fileCount: items.length,
        totalSize: total,
        passwordProtected: usePw && !!password,
        downloadLimit: limit ? Number(limit) : null,
      })
      nav(`/created/${share.token}`)
    } catch (err) {
      if (created && !ctl.signal.aborted) await api.deleteShare(created.token, created.ownerKey).catch(() => {})
      if (!ctl.signal.aborted) setError(err instanceof ApiError || err instanceof Error ? err.message : 'Upload failed.')
      setItems((cur) => cur.map((i) => ({ ...i, uploaded: 0, done: false })))
      setBusy(false)
    }
  }

  async function cancel() {
    abort.current?.abort()
    setBusy(false)
    setItems((cur) => cur.map((i) => ({ ...i, uploaded: 0, done: false })))
  }

  const canSend = items.length > 0 && !busy && apiKey.trim().length > 0 && (!usePw || password.length > 0)

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
                <button key={o.days} type="button" className="pill" aria-pressed={days === o.days} onClick={() => setDays(o.days)}>
                  {o.label}
                </button>
              ))}
            </div>
          </div>

          <div className="stack" style={{ gap: 10 }}>
            <label className="check">
              <input type="checkbox" checked={usePw} onChange={(e) => setUsePw(e.target.checked)} />
              <span>Require a password</span>
            </label>
            {usePw && (
              <div className="field">
                <label htmlFor="pw" className="muted" style={{ fontWeight: 400, fontSize: 14 }}>Password</label>
                <input id="pw" className="input" type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} />
              </div>
            )}
          </div>

          <div className="field">
            <label htmlFor="limit">Download limit</label>
            <input id="limit" className="input" type="number" min={1} max={10000} placeholder="Unlimited" value={limit} onChange={(e) => setLimit(e.target.value)} />
          </div>

          <div className="field">
            <label htmlFor="key">Access key</label>
            <input id="key" className="input" type="password" autoComplete="current-password" placeholder="Set in the server's .env" value={apiKey} onChange={(e) => setKey(e.target.value)} />
          </div>

          {error && <p className="error" role="alert">{error}</p>}

          <div className="stack" style={{ paddingTop: 4 }}>
            {busy ? (
              <button type="button" className="btn big" onClick={cancel}>Cancel upload</button>
            ) : (
              <button type="submit" className="btn big" disabled={!canSend}>Create link</button>
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
