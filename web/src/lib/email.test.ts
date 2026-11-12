import { describe, expect, it } from 'vitest'
import { isPlainAddress, parseRecipients } from './email'

describe('isPlainAddress', () => {
  it.each(['a@example.com', 'first.last+tag@mail.example.co.uk', 'x_y-z@sub.example.org'])('accepts %s', (a) => expect(isPlainAddress(a)).toBe(true))
  it.each(['', 'a', 'a@b', 'a@example.', '@example.com', 'a b@example.com', 'Bob <b@example.com>', 'a@b@example.com', 'a@example.com\nBcc: x@y.z', 'a@-example.com'])(
    'rejects %j',
    (a) => expect(isPlainAddress(a)).toBe(false),
  )
  it('rejects absurdly long addresses', () => expect(isPlainAddress(`${'a'.repeat(250)}@example.com`)).toBe(false))
})

describe('parseRecipients', () => {
  it('splits on commas, semicolons, spaces and new lines', () => {
    expect(parseRecipients('a@example.com, b@example.com;c@example.com\nd@example.com  e@example.com').valid).toHaveLength(5)
  })
  it('drops repeats regardless of case', () => {
    expect(parseRecipients('A@Example.com a@example.com').valid).toEqual(['A@Example.com'])
  })
  it('separates the ones that are not addresses so they can be shown', () => {
    expect(parseRecipients('a@example.com nope b@c')).toEqual({ valid: ['a@example.com'], invalid: ['nope', 'b@c'] })
  })
  it('is empty for blank input', () => expect(parseRecipients('  \n ,; ')).toEqual({ valid: [], invalid: [] }))
})
