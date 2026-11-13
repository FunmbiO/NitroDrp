export function formatBytes(n: number): string {
  if (n < 1000) return `${n} B`
  const units = ['KB', 'MB', 'GB', 'TB']
  let v = n / 1000
  let i = 0
  while (v >= 1000 && i < units.length - 1) {
    v /= 1000
    i++
  }
  const rounded = v >= 100 ? Math.round(v) : Math.round(v * 10) / 10
  return `${rounded} ${units[i]}`
}

export function formatExpiry(expiresAt: string | Date, now = new Date()): string {
  const ms = new Date(expiresAt).getTime() - now.getTime()
  if (ms <= 0) return 'expired'
  const hours = Math.floor(ms / 3_600_000)
  if (hours < 1) return 'in under an hour'
  if (hours < 48) return `in ${hours} hour${hours === 1 ? '' : 's'}`
  return `in ${Math.ceil(ms / 86_400_000)} days` // round up: a link made a moment ago with a 7-day expiry is "7 days", not 6
}

/** "just now", "5 minutes ago", "3 hours ago", "yesterday", or a date once it's older than a week. */
export function formatAgo(iso: string | Date, now = new Date()): string {
  const ms = now.getTime() - new Date(iso).getTime()
  if (ms < 60_000) return 'just now'
  const minutes = Math.floor(ms / 60_000)
  if (minutes < 60) return `${minutes} minute${minutes === 1 ? '' : 's'} ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} hour${hours === 1 ? '' : 's'} ago`
  const days = Math.floor(hours / 24)
  if (days === 1) return 'yesterday'
  if (days < 7) return `${days} days ago`
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
}
