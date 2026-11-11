export interface CreatedFile { id: string; name: string; size: number; partCount: number }
export interface CreateShareResponse {
  token: string
  ownerKey: string
  expiresAt: string
  partSize: number
  files: CreatedFile[]
}
export interface UploadStatus {
  token: string
  expiresAt: string
  partSize: number
  finalized: boolean
  files: { id: string; name: string; size: number; partCount: number; complete: boolean; parts: { partNumber: number; eTag: string }[] }[]
}
export interface ShareView {
  token: string
  senderName: string
  expiresAt: string
  passwordRequired: boolean
  downloadsRemaining: number | null
  totalSize: number
  fileCount: number
  files: { id: string; name: string; size: number }[] | null
}
export interface DownloadLink { id: string; name: string; size: number; url: string }

import { demo } from './demoInstance'
import { ApiError } from './errors'
export { ApiError }

/** A zip is downloaded by navigating to `url`. `fileName` is only needed when the server can't name it (demo mode). */
export interface ZipLink { url: string; fileName?: string }

async function call<T>(method: string, path: string, opts: { body?: unknown; headers?: Record<string, string> } = {}): Promise<T> {
  const res = await fetch(`/api${path}`, {
    method,
    headers: { ...(opts.body !== undefined ? { 'Content-Type': 'application/json' } : {}), ...opts.headers },
    body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined,
  })
  if (res.status === 204) return undefined as T
  const data = await res.json().catch(() => null)
  if (!res.ok) {
    const fallback = res.status === 429 ? 'Too many requests. Wait a minute and try again.' : 'Something went wrong.'
    throw new ApiError(res.status, data?.code ?? 'error', data?.message ?? fallback)
  }
  return data as T
}

const owner = (key: string) => ({ 'X-Owner-Key': key })

export const realApi = {
  createShare: (
    apiKey: string,
    body: { files: { name: string; size: number; contentType: string }[]; expiresInDays: number; password?: string; downloadLimit?: number },
  ) => call<CreateShareResponse>('POST', '/shares', { body, headers: { 'X-Api-Key': apiKey } }),

  presign: (token: string, fileId: string, ownerKey: string, partNumber: number) =>
    call<{ urls: { partNumber: number; url: string }[] }>('POST', `/shares/${token}/files/${fileId}/presign`, {
      body: { partNumbers: [partNumber] },
      headers: owner(ownerKey),
    }).then((r) => r.urls[0].url),

  completeFile: (token: string, fileId: string, ownerKey: string, parts: { partNumber: number; eTag: string }[]) =>
    call<void>('POST', `/shares/${token}/files/${fileId}/complete`, { body: { parts }, headers: owner(ownerKey) }),

  uploadStatus: (token: string, ownerKey: string) => call<UploadStatus>('GET', `/shares/${token}/upload`, { headers: owner(ownerKey) }),

  finalize: (token: string, ownerKey: string) => call<void>('POST', `/shares/${token}/finalize`, { headers: owner(ownerKey) }),
  deleteShare: (token: string, ownerKey: string) => call<void>('DELETE', `/shares/${token}`, { headers: owner(ownerKey) }),

  getShare: (token: string) => call<ShareView>('GET', `/shares/${token}`),
  unlock: (token: string, password: string) => call<ShareView>('POST', `/shares/${token}/unlock`, { body: { password } }),
  zipLink: (token: string, password?: string): Promise<ZipLink> =>
    call<{ url: string }>('POST', `/shares/${token}/zip-link`, { body: { password } }),
  download: (token: string, password?: string, fileId?: string) =>
    call<{ files: DownloadLink[] }>('POST', `/shares/${token}/download`, { body: { password, fileId } }).then((r) => r.files),
}

export type Api = typeof realApi

export const api: Api = demo ? demo.api : realApi
