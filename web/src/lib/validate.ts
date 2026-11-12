import { formatBytes } from './format'

/** Same ceilings the server enforces. The server is the real gate; these just say so before anything is sent. */
export const MAX_PASSWORD = 256
export const MAX_SECRET = 256 // the access key
export const MAX_DOWNLOADS = 10_000
export const MAX_RECIPIENT_TEXT = 2000
export const MAX_LINK_TEXT = 2048

export interface UploadRules {
  maxFiles: number
  maxFileBytes: number
  maxTotalBytes: number
  blockedExtensions: string[]
  expiryDays: number[]
}

export interface DownloadLimitCheck {
  /** The number to send, or undefined for "no limit". */
  value: number | undefined
  error: string
}

/** Empty means no limit. Otherwise a whole number from 1 to 10,000: no letters, signs, decimals or exponents. */
export function checkDownloadLimit(text: string): DownloadLimitCheck {
  const t = text.trim()
  if (t === '') return { value: undefined, error: '' }
  if (!/^\d+$/.test(t)) return { value: undefined, error: `Enter a whole number from 1 to ${MAX_DOWNLOADS.toLocaleString()}, or leave it empty for no limit.` }
  const n = Number(t)
  if (n < 1 || n > MAX_DOWNLOADS) return { value: undefined, error: `Enter a whole number from 1 to ${MAX_DOWNLOADS.toLocaleString()}, or leave it empty for no limit.` }
  return { value: n, error: '' }
}

export function checkPassword(text: string, required: boolean): string {
  if (text.length > MAX_PASSWORD) return `Use ${MAX_PASSWORD} characters or fewer.`
  if (required && text.length === 0) return 'Enter a password, or turn the option off.'
  return ''
}

export interface FileChecks {
  /** One entry per file: a problem to show on that file, or '' when it's fine. */
  perFile: string[]
  /** A problem with the set as a whole (too many files, too much in total), or ''. */
  general: string
}

export function checkFiles(files: { name: string; size: number }[], rules: UploadRules | undefined): FileChecks {
  const perFile = files.map((f) => {
    if (f.size <= 0) return 'This file is empty.'
    if (!rules) return ''
    if (f.size > rules.maxFileBytes) return `Too large. The limit is ${formatBytes(rules.maxFileBytes)} per file.`
    const dot = f.name.lastIndexOf('.')
    const ext = dot >= 0 ? f.name.slice(dot + 1).trim().toLowerCase() : ''
    if (ext && rules.blockedExtensions.some((b) => b.toLowerCase() === ext)) return `.${ext} files can't be sent.`
    return ''
  })
  let general = ''
  if (rules && files.length > rules.maxFiles) general = `A link can hold at most ${rules.maxFiles} files.`
  else if (rules && files.reduce((n, f) => n + f.size, 0) > rules.maxTotalBytes) general = `That's more than one link can carry (${formatBytes(rules.maxTotalBytes)} in total).`
  return { perFile, general }
}
