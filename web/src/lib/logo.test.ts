import { describe, expect, it } from 'vitest'
import logo from '../assets/logo.json'
import { LOGO_SIZE, logoRects } from './logo'

describe('the logo source', () => {
  it('is a 24 x 24 grid that only uses colours it defines', () => {
    expect(LOGO_SIZE).toBe(24)
    expect(logo.rows).toHaveLength(24)
    for (const row of logo.rows) {
      expect(row).toHaveLength(24)
      for (const ch of row) expect(ch === '.' || ch in logo.palette).toBe(true)
    }
  })

  it('has a rounded plate: the four corners are transparent and the middle is filled', () => {
    const rows = logo.rows
    for (const [x, y] of [[0, 0], [23, 0], [0, 23], [23, 23]]) expect(rows[y][x]).toBe('.')
    expect(rows[12][1]).toBe('P')
    expect(rows[12][22]).toBe('P')
  })

  it('is a blue NOS bottle with a white label and an orange arrow, on an orange plate', () => {
    const used = new Set(logo.rows.join(''))
    for (const ch of 'PnNmwok') expect(used.has(ch)).toBe(true) // plate, three blues, label, arrow, outline
    expect(logo.palette.P).toBe('#ff5a1f') // the app's orange
    expect(logo.palette.o).toBe(logo.palette.P) // the arrow is the plate's orange, kept apart only so the bottle can be used alone
    expect(logo.palette.n.toLowerCase()).toMatch(/^#[0-9a-f]{6}$/)
  })

  it('the arrow points up: it is widest above its shaft', () => {
    const arrow = logo.rows.map((r) => [...r].filter((c) => c === 'o').length).filter((n) => n > 0)
    expect(Math.max(...arrow.slice(0, 4))).toBeGreaterThan(arrow[arrow.length - 1])
    expect(arrow[0]).toBeLessThan(arrow[3])
  })
})

describe('logoRects', () => {
  it('covers every drawn pixel exactly once, and nothing else', () => {
    const covered = new Set<string>()
    for (const r of logoRects()) for (let i = 0; i < r.w; i++) {
      const key = `${r.x + i},${r.y}`
      expect(covered.has(key)).toBe(false)
      covered.add(key)
    }
    const drawn = logo.rows.flatMap((row, y) => [...row].flatMap((c, x) => (c === '.' ? [] : [`${x},${y}`])))
    expect([...covered].sort()).toEqual(drawn.sort())
  })

  it('paints each pixel in its own palette colour', () => {
    const colours = new Map<string, string>()
    for (const r of logoRects()) for (let i = 0; i < r.w; i++) colours.set(`${r.x + i},${r.y}`, r.fill)
    logo.rows.forEach((row, y) => [...row].forEach((c, x) => c !== '.' && expect(colours.get(`${x},${y}`)).toBe((logo.palette as Record<string, string>)[c])))
  })

  it('merges runs, so it is far fewer rectangles than pixels', () => {
    expect(logoRects().length).toBeLessThan(200)
  })
})
