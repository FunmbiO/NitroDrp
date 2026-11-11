import { useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../lib/api'
import { formatBytes, formatExpiry } from '../lib/format'
import { getRecent, removeRecent } from '../lib/storage'

export function RecentPage() {
  const [items, setItems] = useState(getRecent)
  const [error, setError] = useState('')

  const del = async (token: string, ownerKey: string) => {
    if (!confirm('Delete this link and its files now?')) return
    try {
      await api.deleteShare(token, ownerKey)
    } catch (e) {
      // 404 just means it already expired or was removed; drop it from the list either way.
      if (!(e instanceof Error && 'status' in e && e.status === 404)) {
        setError(e instanceof Error ? e.message : 'Delete failed.')
        return
      }
    }
    removeRecent(token)
    setItems(getRecent())
  }

  return (
    <main className="page narrow">
      <h1 className="sm">Recent <mark>links</mark></h1>
      <p className="muted" style={{ margin: 0 }}>Saved in this browser only. Clearing site data forgets them (the links keep working until they expire).</p>
      {error && <p className="error" role="alert">{error}</p>}
      {items.length === 0 ? (
        <div className="card pad"><span className="muted">Nothing here yet. <Link to="/">Send something.</Link></span></div>
      ) : (
        <div className="card">
          {items.map((r) => (
            <div className="recent-row" key={r.token}>
              <div className="stack" style={{ gap: 2 }}>
                <Link className="mono" to={`/created/${r.token}`}>/d/{r.token}</Link>
                <span className="meta">{r.fileCount} file{r.fileCount === 1 ? '' : 's'} · {formatBytes(r.totalSize)} · expires {formatExpiry(r.expiresAt)}</span>
              </div>
              <button type="button" className="link-btn" onClick={() => del(r.token, r.ownerKey)}>Delete</button>
            </div>
          ))}
        </div>
      )}
    </main>
  )
}
