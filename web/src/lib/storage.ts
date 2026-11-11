// Per-browser convenience state. Nothing here is a source of truth: the API is.
const KEY_API = 'nitrodrp.apiKey'
const KEY_RECENT = 'nitrodrp.recent'
const KEY_PENDING = 'nitrodrp.pending'

export interface RecentShare {
  token: string
  ownerKey: string
  createdAt: string
  expiresAt: string
  fileCount: number
  totalSize: number
  passwordProtected: boolean
  downloadLimit: number | null
}

function read<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(key)
    return raw ? (JSON.parse(raw) as T) : fallback
  } catch {
    return fallback
  }
}
function write(key: string, value: unknown) {
  try {
    localStorage.setItem(key, JSON.stringify(value))
  } catch {
    /* storage unavailable (private mode): the app still works, it just won't remember */
  }
}

export const getApiKey = () => read<string>(KEY_API, '')
export const setApiKey = (k: string) => write(KEY_API, k)

export const getRecent = () => read<RecentShare[]>(KEY_RECENT, [])
export const findRecent = (token: string) => getRecent().find((r) => r.token === token)
export const addRecent = (s: RecentShare) => write(KEY_RECENT, [s, ...getRecent().filter((r) => r.token !== s.token)].slice(0, 50))
export const removeRecent = (token: string) => write(KEY_RECENT, getRecent().filter((r) => r.token !== token))

/** An upload that started but hasn't been finalized. One at a time; the files themselves must be re-picked. */
export interface PendingUpload {
  token: string
  ownerKey: string
  createdAt: string
  expiresAt: string
  /** In the order the API created them, so index i here is index i in the share. */
  files: { name: string; size: number; lastModified: number }[]
  passwordProtected: boolean
  downloadLimit: number | null
}

export const getPending = (): PendingUpload | null => {
  const p = read<PendingUpload | null>(KEY_PENDING, null)
  return p && new Date(p.expiresAt) > new Date() ? p : null
}
export const savePending = (p: PendingUpload) => write(KEY_PENDING, p)
export const clearPending = () => {
  try {
    localStorage.removeItem(KEY_PENDING)
  } catch {
    /* ignore */
  }
}
