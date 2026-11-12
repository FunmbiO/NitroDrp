import { describe, expect, it } from 'vitest'
import { parseLink } from './link'

describe('parseLink', () => {
  it.each([
    ['https://drop.jadedmedia.ca/d/AbC123xyz789', 'AbC123xyz789'],
    ['  https://drop.jadedmedia.ca/d/AbC123xyz789  ', 'AbC123xyz789'],
    ['https://drop.jadedmedia.ca/d/AbC123xyz789/', 'AbC123xyz789'],
    ['https://drop.jadedmedia.ca/d/AbC123xyz789?utm=1#top', 'AbC123xyz789'],
    ['http://localhost:5173/d/demo1234abcd', 'demo1234abcd'],
    ['https://demo.jadedmedia.ca/d/locked123456', 'locked123456'],
    ['/d/AbC123xyz789', 'AbC123xyz789'],
    ['d/AbC123xyz789', 'AbC123xyz789'],
    ['AbC123xyz789', 'AbC123xyz789'],
    ['<https://drop.jadedmedia.ca/d/AbC123xyz789>', 'AbC123xyz789'],
    ['[my files](https://drop.jadedmedia.ca/d/AbC123xyz789)', 'AbC123xyz789'],
    ['Here you go: https://drop.jadedmedia.ca/d/AbC123xyz789, it expires Friday.', 'AbC123xyz789'],
    ['"AbC123xyz789".', 'AbC123xyz789'],
    ['https://drop.jadedmedia.ca/d/AbC123xyz789\n', 'AbC123xyz789'],
  ])('finds the token in %j', (input, token) => expect(parseLink(input)).toBe(token))

  it.each([
    [''],
    ['   '],
    ['hello'],
    ['hello world'],
    ['https://example.com/'],
    ['https://example.com/about'],
    ['https://drop.jadedmedia.ca/d/'],
    ['https://drop.jadedmedia.ca/d/short'],
    ['https://drop.jadedmedia.ca/recent'],
    ['not a link at all, just a sentence.'],
    ['AbC123xyz789 and more words'],
  ])('finds nothing in %j', (input) => expect(parseLink(input)).toBeNull())

  it('does not treat a path trick as a token', () => {
    expect(parseLink('/d/../../etc/passwd')).toBeNull()
    expect(parseLink('/d/AbC123xyz789/../x')).toBe('AbC123xyz789') // the token part is still just the token
  })

  it('ignores absurdly long input without hanging', () => {
    expect(parseLink('a'.repeat(100_000))).toBeNull()
    expect(parseLink('/d/' + 'a'.repeat(100_000))).toBeNull()
  })
})
