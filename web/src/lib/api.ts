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
/** What a link may hold, so the page can check files before uploading. Absent on an older server. */
export interface AppConfigLimits { maxFiles: number; maxFileBytes: number; maxTotalBytes: number; blockedExtensions: string[]; expiryDays: number[] }
export interface AppConfig {
  emailEnabled: boolean
  maxRecipientsPerRequest: number
  maxRecipientsPerShare: number
  limits?: AppConfigLimits
  /** Only the browser demo sets this: nothing is really sent. */
  simulated?: boolean
}
export interface EmailRequest { recipients: string[]; message?: string; senderName?: string; senderEmail?: string }
export interface EmailResult { email: string; sent: boolean; error?: string | null }
export interface EmailResponse { results: EmailResult[]; remaining: number }
export interface AuthStatus { passkeysEnabled: boolean; signedIn: boolean; hasPasskeys: boolean }
export interface PasskeySummary { id: string; label: string; createdAt: string; lastUsedAt: string | null; current: boolean }
/** A passkey ceremony's options and the device's answer, as JSON. Their shape belongs to the browser's WebAuthn API. */
export type WebAuthnJson = Record<string, unknown>
/** One live link in the owner's list, the same on every device. */
export interface ShareSummary {
  token: string
  createdAt: string
  expiresAt: string
  fileCount: number
  totalSize: number
  passwordProtected: boolean
  downloadLimit: number
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
    // X-Requested-With proves the request came from this app, not another site using the sign-in cookie.
    headers: { 'X-Requested-With': 'nitrodrp', ...(opts.body !== undefined ? { 'Content-Type': 'application/json' } : {}), ...opts.headers },
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

/** A signed-in device has no owner key for links made elsewhere; its sign-in cookie covers it. */
const owner = (key?: string): Record<string, string> => (key ? { 'X-Owner-Key': key } : {})

const realApi = {
  createShare: (
    apiKey: string | undefined,
    body: { files: { name: string; size: number; contentType: string }[]; expiresInDays: number; password?: string; downloadLimit?: number },
  ) => call<CreateShareResponse>('POST', '/shares', { body, headers: apiKey ? { 'X-Api-Key': apiKey } : {} }),

  presign: (token: string, fileId: string, ownerKey: string | undefined, partNumber: number) =>
    call<{ urls: { partNumber: number; url: string }[] }>('POST', `/shares/${token}/files/${fileId}/presign`, {
      body: { partNumbers: [partNumber] },
      headers: owner(ownerKey),
    }).then((r) => r.urls[0].url),

  completeFile: (token: string, fileId: string, ownerKey: string | undefined, parts: { partNumber: number; eTag: string }[]) =>
    call<void>('POST', `/shares/${token}/files/${fileId}/complete`, { body: { parts }, headers: owner(ownerKey) }),

  uploadStatus: (token: string, ownerKey: string | undefined) => call<UploadStatus>('GET', `/shares/${token}/upload`, { headers: owner(ownerKey) }),

  finalize: (token: string, ownerKey: string | undefined) => call<void>('POST', `/shares/${token}/finalize`, { headers: owner(ownerKey) }),
  deleteShare: (token: string, ownerKey: string | undefined) => call<void>('DELETE', `/shares/${token}`, { headers: owner(ownerKey) }),

  // --- who is signed in, and passkeys ---
  authStatus: () => call<AuthStatus>('GET', '/auth/status'),
  /** `accessKey` is only for the first device (or after losing every passkey): it proves you're the owner. */
  registerBegin: (accessKey?: string) =>
    call<{ flowId: string; options: WebAuthnJson }>('POST', '/auth/register/begin', { headers: accessKey ? { 'X-Api-Key': accessKey } : {} }),
  registerFinish: (flowId: string, label: string, credential: WebAuthnJson, accessKey?: string) =>
    call<PasskeySummary>('POST', '/auth/register/finish', { body: { flowId, label, credential }, headers: accessKey ? { 'X-Api-Key': accessKey } : {} }),
  loginBegin: () => call<{ flowId: string; options: WebAuthnJson }>('POST', '/auth/login/begin'),
  loginFinish: (flowId: string, credential: WebAuthnJson) => call<void>('POST', '/auth/login/finish', { body: { flowId, credential } }),
  logout: () => call<void>('POST', '/auth/logout'),
  passkeys: () => call<PasskeySummary[]>('GET', '/auth/passkeys'),
  removePasskey: (id: string) => call<void>('DELETE', `/auth/passkeys/${encodeURIComponent(id)}`),
  /** Every live link, newest first. Needs a sign-in (or the access key). */
  listMine: () => call<ShareSummary[]>('GET', '/shares'),

  getConfig: () => call<AppConfig>('GET', '/config'),
  sendEmail: (token: string, ownerKey: string | undefined, body: EmailRequest) =>
    call<EmailResponse>('POST', `/shares/${token}/email`, { body, headers: owner(ownerKey) }),

  getShare: (token: string) => call<ShareView>('GET', `/shares/${token}`),
  unlock: (token: string, password: string) => call<ShareView>('POST', `/shares/${token}/unlock`, { body: { password } }),
  zipLink: (token: string, password?: string): Promise<ZipLink> =>
    call<{ url: string }>('POST', `/shares/${token}/zip-link`, { body: { password } }),
  download: (token: string, password?: string, fileId?: string) =>
    call<{ files: DownloadLink[] }>('POST', `/shares/${token}/download`, { body: { password, fileId } }).then((r) => r.files),
}

export type Api = typeof realApi

export const api: Api = demo ? demo.api : realApi
