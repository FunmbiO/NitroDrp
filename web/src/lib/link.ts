const BARE_TOKEN = /^[A-Za-z0-9_-]{8,64}$/

/**
 * Finds the link's token in whatever was pasted: a full link (from any address, with or without a trailing slash,
 * query or #), a path like /d/abc..., a bare token, or a link sitting inside a message. Null when there isn't one.
 */
export function parseLink(input: string): string | null {
  const s = input.trim()
  if (!s) return null
  const inPath = s.match(/(?<![A-Za-z0-9_-])d\/([A-Za-z0-9_-]{8,64})(?![A-Za-z0-9_-])/)
  if (inPath) return inPath[1]
  // A lone token, possibly wrapped in quotes, brackets or trailing punctuation from a chat message.
  const bare = s.replace(/^[<("'`]+|[>)"'`.,;:!?]+$/g, '')
  return BARE_TOKEN.test(bare) ? bare : null
}
