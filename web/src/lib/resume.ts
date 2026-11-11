/** Enough to recognise "the same file" after a reload. Browsers can't keep a File handle, so the user re-picks it. */
export interface FileSig {
  name: string
  size: number
  lastModified: number
}

export const sigOf = (f: { name: string; size: number; lastModified: number }): FileSig => ({
  name: f.name,
  size: f.size,
  lastModified: f.lastModified,
})

const same = (a: FileSig, b: FileSig) => a.name === b.name && a.size === b.size && a.lastModified === b.lastModified

/**
 * For each expected file (in order), the index of the chosen file that matches it.
 * Returns null unless the chosen set is exactly the expected set, so we never resume into the wrong share.
 * Identical duplicates are paired off one-to-one.
 */
export function matchFiles(expected: FileSig[], chosen: FileSig[]): number[] | null {
  if (expected.length === 0 || expected.length !== chosen.length) return null
  const used = new Set<number>()
  const result: number[] = []
  for (const e of expected) {
    const i = chosen.findIndex((c, idx) => !used.has(idx) && same(e, c))
    if (i === -1) return null
    used.add(i)
    result.push(i)
  }
  return result
}
