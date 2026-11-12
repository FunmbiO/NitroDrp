import { describe, expect, it } from 'vitest'
import { checkDownloadLimit, checkFiles, checkPassword, type UploadRules } from './validate'

describe('checkDownloadLimit', () => {
  it.each([[''], ['   ']])('treats %j as no limit', (t) => expect(checkDownloadLimit(t)).toEqual({ value: undefined, error: '' }))
  it.each([['1', 1], ['25', 25], [' 10000 ', 10000]])('accepts %j', (t, n) => expect(checkDownloadLimit(t as string)).toEqual({ value: n, error: '' }))
  it.each([['0'], ['-3'], ['10001'], ['2.5'], ['1e3'], ['+5'], ['abc'], ['5 downloads'], ['१२'], ['99999999999999999999'], ['0x10'], ['NaN'], ['Infinity']])('refuses %j', (t) => {
    const r = checkDownloadLimit(t)
    expect(r.value).toBeUndefined()
    expect(r.error).toContain('whole number')
  })
})

describe('checkPassword', () => {
  it('needs a value only when the option is on', () => {
    expect(checkPassword('', true)).toContain('Enter a password')
    expect(checkPassword('', false)).toBe('')
    expect(checkPassword('x', true)).toBe('')
  })
  it('stops at 256 characters', () => {
    expect(checkPassword('a'.repeat(256), true)).toBe('')
    expect(checkPassword('a'.repeat(257), true)).toContain('256')
  })
})

const rules: UploadRules = { maxFiles: 3, maxFileBytes: 1000, maxTotalBytes: 2500, blockedExtensions: ['exe', 'bat'], expiryDays: [1, 7] }

describe('checkFiles', () => {
  it('passes ordinary files', () => expect(checkFiles([{ name: 'a.txt', size: 10 }, { name: 'b', size: 1 }], rules)).toEqual({ perFile: ['', ''], general: '' }))
  it('flags an empty file, even with no rules loaded', () => expect(checkFiles([{ name: 'a.txt', size: 0 }], undefined).perFile[0]).toContain('empty'))
  it('flags a file that is too large', () => expect(checkFiles([{ name: 'a.zip', size: 1001 }], rules).perFile[0]).toContain('Too large'))
  it('flags blocked types in any letter case, and only by the final extension', () => {
    const r = checkFiles([{ name: 'a.EXE', size: 1 }, { name: 'b.exe.txt', size: 1 }, { name: 'exe', size: 1 }, { name: 'c.Bat', size: 1 }], { ...rules, maxFiles: 9 })
    expect(r.perFile.map((p) => p !== '')).toEqual([true, false, false, true])
  })
  it('flags too many files and too much in total', () => {
    expect(checkFiles(Array.from({ length: 4 }, (_, i) => ({ name: `f${i}`, size: 1 })), rules).general).toContain('at most 3')
    expect(checkFiles([{ name: 'a', size: 1000 }, { name: 'b', size: 1000 }, { name: 'c', size: 1000 }], rules).general).toContain('more than one link can carry')
  })
})
