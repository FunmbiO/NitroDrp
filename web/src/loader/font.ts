/** A 5 x 5 pixel font, just the letters the loading screen needs. "X" is a lit pixel. */
const GLYPHS: Record<string, string[]> = {
  N: ['X...X', 'XX..X', 'X.X.X', 'X..XX', 'X...X'],
  O: ['.XXX.', 'X...X', 'X...X', 'X...X', '.XXX.'],
  S: ['.XXXX', 'X....', '.XXX.', '....X', 'XXXX.'],
  F: ['XXXXX', 'X....', 'XXXX.', 'X....', 'X....'],
  L: ['X....', 'X....', 'X....', 'X....', 'XXXXX'],
  I: ['XXXXX', '..X..', '..X..', '..X..', 'XXXXX'],
  P: ['XXXX.', 'X...X', 'XXXX.', 'X....', 'X....'],
  D: ['XXXX.', 'X...X', 'X...X', 'X...X', 'XXXX.'],
  R: ['XXXX.', 'X...X', 'XXXX.', 'X..X.', 'X...X'],
  T: ['XXXXX', '..X..', '..X..', '..X..', '..X..'],
  '!': ['..X..', '..X..', '..X..', '.....', '..X..'],
  X: ['X...X', '.X.X.', '..X..', '.X.X.', 'X...X'],
  '2': ['.XXX.', 'X...X', '..XX.', '.X...', 'XXXXX'],
  E: ['XXXXX', 'X....', 'XXXX.', 'X....', 'XXXXX'],
  M: ['X...X', 'XX.XX', 'X.X.X', 'X...X', 'X...X'],
  A: ['.XXX.', 'X...X', 'XXXXX', 'X...X', 'X...X'],
  '0': ['.XXX.', 'X..XX', 'X.X.X', 'XX..X', '.XXX.'],
  '1': ['..X..', '.XX..', '..X..', '..X..', '.XXX.'],
  '3': ['XXXX.', '....X', '.XXX.', '....X', 'XXXX.'],
  '4': ['X..X.', 'X..X.', 'XXXXX', '...X.', '...X.'],
  '5': ['XXXXX', 'X....', 'XXXX.', '....X', 'XXXX.'],
  '6': ['.XXX.', 'X....', 'XXXX.', 'X...X', '.XXX.'],
  '7': ['XXXXX', '....X', '...X.', '..X..', '..X..'],
  '8': ['.XXX.', 'X...X', '.XXX.', 'X...X', '.XXX.'],
  '9': ['.XXX.', 'X...X', '.XXXX', '....X', '.XXX.'],
  ' ': ['.....', '.....', '.....', '.....', '.....'],
}

export const GLYPH_W = 5
export const GLYPH_ADVANCE = 6

/** The lit pixels of `text`, as [x, y] offsets from its top-left. Unknown characters are skipped. */
export function textPixels(text: string): [number, number][] {
  const out: [number, number][] = []
  let x = 0
  for (const ch of text.toUpperCase()) {
    const g = GLYPHS[ch]
    if (g) g.forEach((row, y) => [...row].forEach((c, i) => c === 'X' && out.push([x + i, y])))
    x += GLYPH_ADVANCE
  }
  return out
}

export const textWidth = (text: string) => Math.max(0, text.length * GLYPH_ADVANCE - 1)
