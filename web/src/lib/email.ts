/** Same rule as the server: plain `name@domain.tld`, nothing else (no "Name <addr>", no lists, no control characters). */
const PLAIN = /^[^\s@<>(),;:"[\]\\]+@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)+$/

export const isPlainAddress = (s: string) => s.length <= 254 && PLAIN.test(s)

export interface ParsedRecipients {
  valid: string[]
  invalid: string[]
}

/** Splits what someone typed or pasted (commas, semicolons, spaces, new lines), drops repeats in any letter case. */
export function parseRecipients(text: string): ParsedRecipients {
  const seen = new Set<string>()
  const valid: string[] = []
  const invalid: string[] = []
  for (const raw of text.split(/[\s,;]+/)) {
    const s = raw.trim()
    if (!s) continue
    const key = s.toLowerCase()
    if (seen.has(key)) continue
    seen.add(key)
    ;(isPlainAddress(s) ? valid : invalid).push(s)
  }
  return { valid, invalid }
}

export const MAX_MESSAGE = 1000
