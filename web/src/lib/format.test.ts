import { describe, expect, it } from 'vitest'
import { formatAgo, formatBytes, formatExpiry } from './format'

describe('formatBytes', () => {
  it('formats common sizes like the design', () => {
    expect(formatBytes(86_000_000)).toBe('86 MB')
    expect(formatBytes(1_800_000_000)).toBe('1.8 GB')
    expect(formatBytes(4_200_000_000)).toBe('4.2 GB')
    expect(formatBytes(512)).toBe('512 B')
  })
})

describe('formatExpiry', () => {
  const now = new Date('2026-11-11T12:00:00Z')
  it('uses days for long windows and hours for short ones', () => {
    expect(formatExpiry('2026-11-18T12:00:00Z', now)).toBe('in 7 days')
    expect(formatExpiry('2026-11-18T11:59:00Z', now)).toBe('in 7 days') // a moment after creating a 7-day link
    expect(formatExpiry('2026-11-12T00:00:00Z', now)).toBe('in 12 hours')
    expect(formatExpiry('2026-11-11T11:00:00Z', now)).toBe('expired')
  })
})

describe('formatAgo', () => {
  const now = new Date('2026-11-11T12:00:00Z')
  const ago = (ms: number) => new Date(now.getTime() - ms).toISOString()
  it('speaks in the largest sensible unit', () => {
    expect(formatAgo(ago(10_000), now)).toBe('just now')
    expect(formatAgo(ago(60_000), now)).toBe('1 minute ago')
    expect(formatAgo(ago(5 * 60_000), now)).toBe('5 minutes ago')
    expect(formatAgo(ago(3_600_000), now)).toBe('1 hour ago')
    expect(formatAgo(ago(5 * 3_600_000), now)).toBe('5 hours ago')
    expect(formatAgo(ago(30 * 3_600_000), now)).toBe('yesterday')
    expect(formatAgo(ago(3 * 86_400_000), now)).toBe('3 days ago')
  })
  it('falls back to a date after a week, and never shows the future as negative', () => {
    expect(formatAgo(ago(10 * 86_400_000), now)).toMatch(/Nov|Oct/)
    expect(formatAgo(new Date(now.getTime() + 5000).toISOString(), now)).toBe('just now')
  })
})
