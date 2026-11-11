import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api, ApiError, type ShareView } from '../lib/api'
import { formatBytes, formatExpiry } from '../lib/format'
import { DownloadIcon } from '../components/Icons'

type State =
  | { kind: 'loading' }
  | { kind: 'ready'; share: ShareView }
  | { kind: 'gone'; message: string }

/** Starts a browser download without navigating away from the page. */
function trigger(url: string) {
  const a = document.createElement('a')
  a.href = url
  a.rel = 'noopener'
  document.body.appendChild(a)
  a.click()
  a.remove()
}

export function DownloadPage() {
  const { token = '' } = useParams()
  const [state, setState] = useState<State>({ kind: 'loading' })
  const [password, setPassword] = useState('')
  const [unlocked, setUnlocked] = useState<ShareView | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api.getShare(token).then(
      (share) => setState({ kind: 'ready', share }),
      (e: ApiError) => setState({ kind: 'gone', message: e.status === 404 ? "This link doesn't exist." : e.message }),
    )
  }, [token])

  if (state.kind === 'loading') return <main className="page narrow"><p className="muted mono">Loading…</p></main>
  if (state.kind === 'gone') {
    return (
      <main className="page narrow">
        <div className="card roomy">
          <h1 className="sm">This link is <mark>gone</mark></h1>
          <p className="muted" style={{ margin: 0 }}>{state.message} Ask the sender for a new one.</p>
        </div>
      </main>
    )
  }

  const share = unlocked ?? state.share
  const files = share.files

  async function unlock(e: React.FormEvent) {
    e.preventDefault()
    setError('')
    setBusy(true)
    try {
      setUnlocked(await api.unlock(token, password))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not unlock.')
    } finally {
      setBusy(false)
    }
  }

  async function download(fileId?: string) {
    setError('')
    setBusy(true)
    try {
      const links = await api.download(token, password || undefined, fileId)
      // Browsers may block many programmatic downloads at once; space them out.
      for (const [i, l] of links.entries()) {
        if (i > 0) await new Promise((r) => setTimeout(r, 400))
        trigger(l.url)
      }
      api.getShare(token).then((s) => setState({ kind: 'ready', share: s })).catch(() => {})
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Download failed.')
    } finally {
      setBusy(false)
    }
  }

  /** Several files arrive as one streamed .zip; a single file downloads directly. */
  async function downloadAll() {
    if (share.fileCount <= 1) return download()
    setError('')
    setBusy(true)
    try {
      trigger(await api.zipLink(token, password || undefined))
      api.getShare(token).then((s) => setState({ kind: 'ready', share: s })).catch(() => {})
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Download failed.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="page narrow">
      <div className="card roomy" style={{ gap: 24 }}>
        <div className="stack" style={{ gap: 8 }}>
          <h1 className="sm">
            {share.senderName} sent you <mark>{share.fileCount} file{share.fileCount === 1 ? '' : 's'}</mark>
          </h1>
          <p className="muted" style={{ margin: 0 }}>
            {formatBytes(share.totalSize)} total · link expires {formatExpiry(share.expiresAt)}
            {share.downloadsRemaining !== null && ` · ${share.downloadsRemaining} download${share.downloadsRemaining === 1 ? '' : 's'} left`}
          </p>
        </div>

        {share.passwordRequired && !files ? (
          <form className="stack" onSubmit={unlock}>
            <div className="field">
              <label htmlFor="pw">Password</label>
              <input id="pw" className="input big" type="password" autoFocus autoComplete="off" value={password} onChange={(e) => setPassword(e.target.value)} />
            </div>
            {error && <p className="error" role="alert">{error}</p>}
            <button type="submit" className="btn big" disabled={busy || !password}>Unlock files</button>
          </form>
        ) : (
          <>
            <button type="button" className="btn big" disabled={busy} onClick={downloadAll}>
              <DownloadIcon />
              {share.fileCount > 1 ? 'Download all as .zip' : 'Download'}
            </button>
            {error && <p className="error" role="alert">{error}</p>}
            {files && (
              <div className="dl-list">
                {files.map((f) => (
                  <div className="dl-row" key={f.id}>
                    <div className="body">
                      <span className="name">{f.name}</span>
                      <span className="meta">{formatBytes(f.size)}</span>
                    </div>
                    <button type="button" className="icon-btn lime" disabled={busy} aria-label={`Download ${f.name}`} onClick={() => download(f.id)}>
                      <DownloadIcon size={22} />
                    </button>
                  </div>
                ))}
              </div>
            )}
          </>
        )}
        <p className="note">Files are deleted automatically when the link expires.</p>
      </div>
    </main>
  )
}
