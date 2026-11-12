import logo from '../assets/logo.json'

export interface LogoRect {
  x: number
  y: number
  w: number
  fill: string
}

export const LOGO_SIZE = logo.rows.length
const palette: Record<string, string> = logo.palette

/** The logo as the fewest rectangles: runs of equal pixels along each row are merged into one. */
export function logoRects(): LogoRect[] {
  const out: LogoRect[] = []
  logo.rows.forEach((row, y) => {
    let x = 0
    while (x < row.length) {
      const ch = row[x]
      if (ch === '.') {
        x++
        continue
      }
      const start = x
      while (x < row.length && row[x] === ch) x++
      out.push({ x: start, y, w: x - start, fill: palette[ch] })
    }
  })
  return out
}
