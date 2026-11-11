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
