import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api } from '../lib/api'
import { formatBytes, formatExpiry } from '../lib/format'
import { findRecent, removeRecent } from '../lib/storage'
import { CheckIcon } from '../components/Icons'

export function CreatedPage() {
  const { token = '' } = useParams()
  const nav = useNavigate()
  const share = findRecent(token)
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState('')

  if (!share) {
    return (
      <main className="page narrow">
        <div className="card roomy">
          <h1 className="sm">That link isn't saved in this browser</h1>
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
  const del = async () => {
    if (!confirm('Delete this link and its files now?')) return
    try {
      await api.deleteShare(token, share.ownerKey)
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

        <div className="actions">
          <Link to="/" className="btn outline">Send more files</Link>
          <button type="button" className="link-btn" onClick={del}>Delete link now</button>
        </div>
      </div>
    </main>
  )
}
