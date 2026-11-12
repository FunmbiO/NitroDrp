import { useEffect, useState } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { api, type AppConfig, type EmailResponse, type EmailResult } from '../lib/api'
import { useAuth } from '../lib/auth'
import { EmailFields, draftProblem, emptyDraft, type EmailDraft } from '../components/EmailFields'
import { parseRecipients } from '../lib/email'
import { formatBytes, formatExpiry } from '../lib/format'
import { findRecent, removeRecent, type RecentShare } from '../lib/storage'
import { CheckIcon } from '../components/Icons'
import { PixelLoader } from '../loader/PixelLoader'

export function CreatedPage() {
  const { token = '' } = useParams()
  const nav = useNavigate()
  const { signedIn } = useAuth()
  const local = findRecent(token)
  // A link made on another device isn't in this browser's storage, but the server knows it.
  const [fetched, setFetched] = useState<RecentShare | null>(null)
  const [looked, setLooked] = useState(false)
  useEffect(() => {
    if (local || !signedIn) return
    api
      .listMine()
      .then((all) => {
        const s = all.find((x) => x.token === token)
        if (s) setFetched({ ...s, ownerKey: '', createdAt: s.createdAt, downloadLimit: s.downloadLimit || null })
      })
      .catch(() => {})
      .finally(() => setLooked(true))
  }, [local, signedIn, token])
  const share = local ?? fetched
  const ownerKey = local?.ownerKey // none for a link from another device: the sign-in cookie covers it
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState('')
  const handedOver = (useLocation().state as { emailOutcome?: { response?: EmailResponse; error?: string } } | null)?.emailOutcome
  const [config, setConfig] = useState<AppConfig | null>(null)
  const [open, setOpen] = useState(false)
  const [draft, setDraft] = useState<EmailDraft>(emptyDraft)
  const [sending, setSending] = useState(false)
  const [emailError, setEmailError] = useState(handedOver?.error ?? '')
  const [results, setResults] = useState<EmailResult[]>(handedOver?.response?.results ?? [])
  const [remaining, setRemaining] = useState<number | null>(handedOver?.response?.remaining ?? null)
  useEffect(() => {
    api.getConfig().then(setConfig, () => setConfig(null))
  }, [])

  if (!share && !local && signedIn && !looked) return <main className="page narrow"><PixelLoader /></main>

  if (!share) {
    return (
      <main className="page narrow">
        <div className="card roomy">
          <h1 className="sm">{signedIn ? "That link isn't in your account" : "That link isn't saved in this browser"}</h1>
          <Link to="/">Send files</Link>
        </div>
      </main>
    )
  }

  const url = `${location.origin}/d/${token}`
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(url)
      setCopied(true)
    } catch {
      setError('Copy failed. Select the link and copy it manually.')
    }
  }
  const { valid } = parseRecipients(draft.recipients)
  const problem = draftProblem(draft, config?.maxRecipientsPerRequest ?? 10)
  const sendEmail = async () => {
    setSending(true)
    setEmailError('')
    try {
      const r = await api.sendEmail(token, ownerKey, {
        recipients: valid,
        message: draft.message.trim() || undefined,
        senderName: draft.senderName.trim() || undefined,
        senderEmail: draft.senderEmail.trim() || undefined,
      })
      setResults((prev) => [...r.results, ...prev])
      setRemaining(r.remaining)
      // Keep the addresses that failed so they can be fixed and retried; clear the ones that went out.
      const failed = r.results.filter((x) => !x.sent).map((x) => x.email)
      setDraft((d) => ({ ...d, recipients: failed.join(', ') }))
      if (!failed.length) setOpen(false)
    } catch (e) {
      setEmailError(e instanceof Error ? e.message : 'The email could not be sent.')
    } finally {
      setSending(false)
    }
  }
  const del = async () => {
    if (!confirm('Delete this link and its files now?')) return
    try {
      await api.deleteShare(token, ownerKey)
      removeRecent(token)
      nav('/')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Delete failed.')
    }
  }

  return (
    <main className="page narrow">
      <div className="card roomy">
        <div className="stack">
          <div className="badge"><CheckIcon size={24} /></div>
          <h1 className="sm">Your link is <mark>ready</mark><span className="cursor" aria-hidden="true" /></h1>
          <p className="muted" style={{ margin: 0 }}>Anyone with this link can download the files until it expires.</p>
        </div>

        <div className="copy-row">
          <label htmlFor="link" style={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' }}>Share link</label>
          <input id="link" className="input big" readOnly value={url} onFocus={(e) => e.currentTarget.select()} />
          <button type="button" className="btn big" style={{ minWidth: 120 }} onClick={copy}>{copied ? 'Copied' : 'Copy link'}</button>
        </div>
        {error && <p className="error" role="alert">{error}</p>}

        <div className="rows">
          <div><span>Files</span><span>{share.fileCount} file{share.fileCount === 1 ? '' : 's'} · {formatBytes(share.totalSize)}</span></div>
          <div><span>Expires</span><span>{formatExpiry(share.expiresAt)}</span></div>
          <div><span>Password</span><span>{share.passwordProtected ? 'On' : 'Off'}</span></div>
          <div><span>Downloads</span><span>{share.downloadLimit ? `up to ${share.downloadLimit}` : 'Unlimited'}</span></div>
        </div>

        {config?.emailEnabled && (
          <section className="email-card stack" aria-label="Send by email" style={{ gap: 14 }}>
            <div className="row-between">
              <h2 className="group-title" style={{ margin: 0 }}>Send by email</h2>
              {!open && remaining !== 0 && (
                <button type="button" className="btn outline" onClick={() => setOpen(true)}>{results.length ? 'Email more people' : 'Email this link'}</button>
              )}
            </div>
            {results.length > 0 && (
              <ul className="results" aria-live="polite">
                {results.map((r, i) => (
                  <li key={`${r.email}-${i}`} className={r.sent ? 'ok' : 'bad'}>
                    <span>{r.email}</span>
                    <span>{r.sent ? 'Sent' : r.error || 'Not sent'}</span>
                  </li>
                ))}
              </ul>
            )}
            {emailError && <p className="error" role="alert" style={{ margin: 0 }}>{emailError}</p>}
            {remaining !== null && <p className="hint" style={{ margin: 0 }}>{remaining > 0 ? `You can email ${remaining} more ${remaining === 1 ? 'person' : 'people'} with this link.` : 'This link has reached its email limit. Copy the link to share it another way.'}</p>}
            {open && (
              <>
                <EmailFields draft={draft} onChange={setDraft} idPrefix="cr" maxPerRequest={config.maxRecipientsPerRequest} disabled={sending} />
                {config.simulated && <p className="hint" style={{ margin: 0 }}>Demo: no email is really sent.</p>}
                <div className="actions">
                  <button type="button" className="btn big" disabled={sending || !valid.length || !!problem} onClick={sendEmail}>
                    {sending ? 'Sending…' : `Send to ${valid.length || ''} ${valid.length === 1 ? 'person' : 'people'}`.replace('  ', ' ')}
                  </button>
                  <button type="button" className="link-btn" disabled={sending} onClick={() => setOpen(false)}>Cancel</button>
                </div>
              </>
            )}
          </section>
        )}

        <div className="actions">
          <Link to="/" className="btn outline">Send more files</Link>
          <button type="button" className="link-btn" onClick={del}>Delete link now</button>
        </div>
      </div>
    </main>
  )
}
