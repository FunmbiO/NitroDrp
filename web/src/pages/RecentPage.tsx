import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, type ShareSummary } from '../lib/api'
import { useAuth } from '../lib/auth'
import { formatBytes, formatExpiry } from '../lib/format'
import { getRecent, removeRecent } from '../lib/storage'
import { PixelLoader } from '../loader/PixelLoader'

interface Row {
  token: string
  fileCount: number
  totalSize: number
  expiresAt: string
  /** Only present when this browser made the link. A signed-in device doesn't need it. */
  ownerKey?: string
}

export function RecentPage() {
  const { signedIn } = useAuth()
  const [local, setLocal] = useState(getRecent)
  const [remote, setRemote] = useState<ShareSummary[] | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!signedIn) return
    api.listMine().then(setRemote, () => setRemote([]))
  }, [signedIn])

  // The server's list is the same on every device. Links this browser made before that existed are added below it.
  const rows = useMemo<Row[]>(() => {
    const keys = new Map(local.map((r) => [r.token, r.ownerKey]))
    const seen = new Set<string>()
    const out: Row[] = []
    for (const s of remote ?? []) {
      seen.add(s.token)
      out.push({ token: s.token, fileCount: s.fileCount, totalSize: s.totalSize, expiresAt: s.expiresAt, ownerKey: keys.get(s.token) })
    }
    for (const r of local) if (!seen.has(r.token) && new Date(r.expiresAt) > new Date()) out.push(r)
    return out
  }, [local, remote])

  const del = async (row: Row) => {
    if (!confirm('Delete this link and its files now?')) return
    try {
      await api.deleteShare(row.token, row.ownerKey)
    } catch (e) {
      // 404 just means it already expired or was removed; drop it from the list either way.
      if (!(e instanceof Error && 'status' in e && e.status === 404)) {
        setError(e instanceof Error ? e.message : 'Delete failed.')
        return
      }
    }
    removeRecent(row.token)
    setLocal(getRecent())
    setRemote((cur) => cur && cur.filter((s) => s.token !== row.token))
  }

  return (
    <main className="page narrow">
      <h1 className="sm">Recent <mark>links</mark></h1>
      <p className="muted" style={{ margin: 0 }}>
        {signedIn
          ? 'The same list on every device you sign in on. Links stop working when they expire.'
          : 'Saved in this browser only. Clearing site data forgets them (the links keep working until they expire).'}
      </p>
      {error && <p className="error" role="alert">{error}</p>}
      {signedIn && remote === null ? (
        <div className="card pad"><PixelLoader label="Loading your links" /></div>
      ) : rows.length === 0 ? (
        <div className="card pad"><span className="muted">Nothing here yet. <Link to="/">Send something.</Link></span></div>
      ) : (
        <div className="card">
          {rows.map((r) => (
            <div className="recent-row" key={r.token}>
              <div className="stack" style={{ gap: 2 }}>
                <Link className="mono" to={`/created/${r.token}`}>/d/{r.token}</Link>
                <span className="meta">{r.fileCount} file{r.fileCount === 1 ? '' : 's'} · {formatBytes(r.totalSize)} · expires {formatExpiry(r.expiresAt)}</span>
              </div>
              <button type="button" className="link-btn" onClick={() => void del(r)}>Delete</button>
            </div>
          ))}
        </div>
      )}
    </main>
  )
}
