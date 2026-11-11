import { describe, expect, it } from 'vitest'
import { formatBytes, formatExpiry } from './format'

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
