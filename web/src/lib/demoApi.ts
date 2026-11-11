/**
 * Demo mode: the whole backend, in the browser. Same rules and error codes as the real API (expiry,
 * passwords, download limits, resume, zip), so the UI behaves identically, but nothing leaves the
 * page: uploaded files are never read or sent anywhere, and "downloads" are small placeholder files.
 * Shares live in localStorage so a link opened in another tab of the same browser works.
 */
import type { Api, CreateShareResponse, DownloadLink, ShareView, UploadStatus, ZipLink } from './api'
import { ApiError } from './errors'
import type { UploadedPart } from './upload'
import { buildStoredZip } from './zip'

const PART_SIZE = 16 * 1024 * 1024
const DAY = 86_400_000
const BLOCKED = ['exe', 'bat', 'cmd', 'com', 'scr', 'msi', 'vbs', 'ps1', 'jar', 'dll']
const STORAGE_KEY = 'nitrodrp.demo.v1'

interface DemoFile {
  id: string
  name: string
  size: number
  status: 'Uploading' | 'Complete'
  parts: Record<number, string>
}
interface DemoShare {
  token: string
  ownerKey: string
  status: 'Pending' | 'Active'
  senderName: string
  expiresAt: number
  password: string | null
  limit: number
  count: number
  files: DemoFile[]
}
export interface DemoState {
  shares: Record<string, DemoShare>
}
export interface DemoStore {
  load(): DemoState | null
  save(state: DemoState): void
}

export function browserStore(): DemoStore {
  let memory: DemoState | null = null // fallback when storage is blocked (private mode, sandboxed iframe)
  return {
    load: () => {
      try {
        const raw = localStorage.getItem(STORAGE_KEY)
        if (raw) return JSON.parse(raw) as DemoState
      } catch {
        /* fall through to memory */
      }
      return memory
    },
    save: (s) => {
      memory = s
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(s))
      } catch {
        /* keep the in-memory copy */
      }
    },
  }
}

export interface DemoOptions {
  now?: () => number
  /** How long a part "takes" to upload. 0 resolves immediately (tests). */
  uploadMs?: (bytes: number) => number
  makeUrl?: (bytes: Uint8Array) => string
}

const BASE62 = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'
const rand = (n: number) =>
  Array.from(crypto.getRandomValues(new Uint8Array(n)), (b) => BASE62[b % 62]).join('')
const partCount = (size: number) => Math.ceil(size / PART_SIZE)

export function createDemoBackend(store: DemoStore, opts: DemoOptions = {}) {
  const now = opts.now ?? Date.now
  const uploadMs = opts.uploadMs ?? ((bytes) => Math.min(2500, Math.max(250, bytes / 40_000)))
  const makeUrl = opts.makeUrl ?? ((bytes) => URL.createObjectURL(new Blob([bytes as BlobPart])))
  const enc = new TextEncoder()

  const state: DemoState = store.load() ?? { shares: {} }
  const persist = () => store.save(state)

  // Evergreen examples, refreshed on every load so they never lapse for a returning visitor.
  const seed = (token: string, files: [string, number][], o: { days?: number; password?: string } = {}) => {
    state.shares[token] = {
      token, ownerKey: 'demo-owner', status: 'Active', senderName: 'NitroDrp demo',
      expiresAt: now() + (o.days ?? 7) * DAY, password: o.password ?? null, limit: 0, count: 0,
      files: files.map(([name, size], i) => ({ id: `f${i + 1}`, name, size, status: 'Complete', parts: {} })),
    }
  }
  seed('demo1234abcd', [['Smith-Wedding-Gallery.zip', 4_200_000_000], ['Ceremony-Highlights.mp4', 1_800_000_000], ['Album-Proofs.pdf', 86_000_000]])
  seed('locked123456', [['Contract-Signed.pdf', 2_400_000], ['Invoice-0042.pdf', 310_000]], { password: 'nitro' })
  seed('expired00000', [['Old-Gallery.zip', 1_000_000]], { days: -1 })
  persist()

  const iso = (ms: number) => new Date(ms).toISOString()
  const placeholder = (f: DemoFile) =>
    enc.encode(`NitroDrp demo\n\n"${f.name}" (${f.size} bytes) is a placeholder.\nDemo mode never uploads or stores your files.\n`)

  function active(token: string): DemoShare {
    const s = state.shares[token]
    if (!s || s.status !== 'Active') throw new ApiError(404, 'not_found', "This link doesn't exist.")
    if (now() >= s.expiresAt) throw new ApiError(410, 'expired', 'This link has expired.')
    if (s.limit > 0 && s.count >= s.limit) throw new ApiError(410, 'limit_reached', 'This link has reached its download limit.')
    return s
  }
  function checkPassword(s: DemoShare, password?: string) {
    if (!s.password) return
    if (!password) throw new ApiError(401, 'password_required', 'This link needs a password.')
    if (password !== s.password) throw new ApiError(401, 'wrong_password', "That password isn't right.")
  }
  function owned(token: string, ownerKey: string): DemoShare {
    const s = state.shares[token]
    if (!s) throw new ApiError(404, 'not_found', "This link doesn't exist.")
    if (ownerKey !== s.ownerKey) throw new ApiError(403, 'bad_owner_key', "You can't change this link.")
    return s
  }
  const view = (s: DemoShare, unlocked: boolean): ShareView => ({
    token: s.token,
    senderName: s.senderName,
    expiresAt: iso(s.expiresAt),
    passwordRequired: !!s.password,
    downloadsRemaining: s.limit > 0 ? Math.max(0, s.limit - s.count) : null,
    totalSize: s.files.reduce((a, f) => a + f.size, 0),
    fileCount: s.files.length,
    files: !s.password || unlocked ? s.files.map(({ id, name, size }) => ({ id, name, size })) : null,
  })
  const countDownload = (s: DemoShare) => {
    s.count++
    persist()
  }

  const api: Api = {
    async createShare(_apiKey, body) {
      if (![1, 7, 30].includes(body.expiresInDays)) throw new ApiError(400, 'bad_expiry', 'Choose one of the allowed expiry times.')
      if (!body.files.length) throw new ApiError(400, 'no_files', 'Add at least one file.')
      const bad = body.files.find((f) => BLOCKED.includes(f.name.split('.').pop()?.toLowerCase() ?? ''))
      if (bad) throw new ApiError(400, 'blocked_type', `'.${bad.name.split('.').pop()}' files can't be sent.`)
      if (body.files.some((f) => f.size <= 0)) throw new ApiError(400, 'bad_size', 'A file is empty.')

      const token = rand(12)
      const ownerKey = rand(43)
      const s: DemoShare = {
        token, ownerKey, status: 'Pending', senderName: 'NitroDrp demo', expiresAt: now() + body.expiresInDays * DAY,
        password: body.password || null, limit: body.downloadLimit || 0, count: 0,
        files: body.files.map((f, i) => ({ id: `f${i + 1}`, name: f.name, size: f.size, status: 'Uploading', parts: {} })),
      }
      state.shares[token] = s
      persist()
      const res: CreateShareResponse = {
        token, ownerKey, expiresAt: iso(s.expiresAt), partSize: PART_SIZE,
        files: s.files.map((f) => ({ id: f.id, name: f.name, size: f.size, partCount: partCount(f.size) })),
      }
      return res
    },

    async presign(token, fileId, ownerKey, partNumber) {
      owned(token, ownerKey)
      return `demo://${token}/${fileId}/${partNumber}`
    },

    async completeFile(token, fileId, ownerKey, parts: UploadedPart[]) {
      const s = owned(token, ownerKey)
      const f = s.files.find((x) => x.id === fileId)
      if (!f) throw new ApiError(404, 'file_not_found', "That file isn't part of this link.")
      const expected = partCount(f.size)
      const nums = parts.map((p) => p.partNumber).sort((a, b) => a - b)
      if (nums.length !== expected || nums.some((n, i) => n !== i + 1))
        throw new ApiError(400, 'bad_parts', 'Every part must be uploaded exactly once.')
      f.status = 'Complete'
      persist()
    },

    async finalize(token, ownerKey) {
      const s = owned(token, ownerKey)
      if (s.files.some((f) => f.status !== 'Complete')) throw new ApiError(409, 'incomplete', "Some files haven't finished uploading.")
      s.status = 'Active'
      persist()
    },

    async deleteShare(token, ownerKey) {
      owned(token, ownerKey)
      delete state.shares[token]
      persist()
    },

    async uploadStatus(token, ownerKey): Promise<UploadStatus> {
      const s = owned(token, ownerKey)
      return {
        token, expiresAt: iso(s.expiresAt), partSize: PART_SIZE, finalized: s.status === 'Active',
        files: s.files.map((f) => ({
          id: f.id, name: f.name, size: f.size, partCount: partCount(f.size), complete: f.status === 'Complete',
          parts: Object.entries(f.parts).map(([n, eTag]) => ({ partNumber: Number(n), eTag })),
        })),
      }
    },

    async getShare(token) {
      return view(active(token), false)
    },

    async unlock(token, password) {
      const s = active(token)
      checkPassword(s, password)
      return view(s, true)
    },

    async zipLink(token, password): Promise<ZipLink> {
      const s = active(token)
      checkPassword(s, password)
      countDownload(s)
      const zip = buildStoredZip(s.files.map((f) => ({ name: f.name, data: placeholder(f) })))
      return { url: makeUrl(zip), fileName: `${s.senderName} files.zip` }
    },

    async download(token, password, fileId): Promise<DownloadLink[]> {
      const s = active(token)
      checkPassword(s, password)
      const targets = fileId ? s.files.filter((f) => f.id === fileId) : s.files
      if (!targets.length) throw new ApiError(404, 'file_not_found', "That file isn't part of this link.")
      countDownload(s)
      return targets.map((f) => ({ id: f.id, name: f.name, size: f.size, url: makeUrl(placeholder(f)) }))
    },
  }

  /** Stands in for the browser PUT to S3: animates progress, records the part, never touches the file's bytes. */
  function putPart(url: string, body: Blob, onProgress: (loaded: number) => void, signal?: AbortSignal): Promise<string> {
    const m = url.match(/^demo:\/\/([^/]+)\/([^/]+)\/(\d+)$/)
    if (!m) return Promise.reject(new Error('Unexpected upload URL in demo mode'))
    const [, token, fileId, n] = m
    const etag = `"demo-${n}"`
    const record = () => {
      const f = state.shares[token]?.files.find((x) => x.id === fileId)
      if (f) {
        f.parts[Number(n)] = etag
        persist()
      }
    }
    const ms = uploadMs(body.size)
    if (ms <= 0) {
      onProgress(body.size)
      record()
      return Promise.resolve(etag)
    }
    return new Promise((resolve, reject) => {
      const start = now()
      const timer = setInterval(() => {
        const f = Math.min(1, (now() - start) / ms)
        onProgress(Math.floor(body.size * f))
        if (f >= 1) {
          clearInterval(timer)
          record()
          resolve(etag)
        }
      }, 50)
      signal?.addEventListener(
        'abort',
        () => {
          clearInterval(timer)
          reject(new DOMException('Aborted', 'AbortError'))
        },
        { once: true },
      )
    })
  }

  return { api, putPart }
}
