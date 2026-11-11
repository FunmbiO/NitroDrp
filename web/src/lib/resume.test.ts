import { describe, expect, it } from 'vitest'
import { matchFiles } from './resume'

const f = (name: string, size = 10, lastModified = 1) => ({ name, size, lastModified })

describe('matchFiles', () => {
  it('maps expected files to chosen ones regardless of pick order', () => {
    expect(matchFiles([f('a'), f('b')], [f('b'), f('a')])).toEqual([1, 0])
  })
  it('refuses when anything differs: size, modified time, extras or missing files', () => {
    expect(matchFiles([f('a')], [f('a', 11)])).toBeNull()
    expect(matchFiles([f('a')], [f('a', 10, 2)])).toBeNull()
    expect(matchFiles([f('a')], [f('a'), f('b')])).toBeNull()
    expect(matchFiles([f('a'), f('b')], [f('a')])).toBeNull()
    expect(matchFiles([], [])).toBeNull()
  })
  it('pairs identical duplicates one-to-one', () => {
    expect(matchFiles([f('a'), f('a')], [f('a'), f('a')])).toEqual([0, 1])
  })
})
