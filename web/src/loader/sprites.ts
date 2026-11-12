/**
 * Pixel art for the loading screen, built from a few drawing helpers so the shapes stay clean. A sprite is a list of
 * text rows: one character is one pixel and "." is empty. The palette is the app's own colours (orange, pink, lime)
 * plus the blue of a real NOS bottle, so the bottles stand out against everything else.
 */
export const PALETTE: Record<string, string> = {
  k: '#14101a', // outline
  o: '#ff5a1f', // body orange
  O: '#ff8a4c', // highlight
  d: '#c23d0a', // shade
  p: '#ff2e93', // pink accent
  w: '#f5f5f5', // white
  g: '#a3a3a3',
  G: '#5a5a66',
  b: '#1a2038', // window
  B: '#6f86c9', // window glint
  y: '#ffe14d', // headlight
  r: '#e5202e', // taillight
  s: '#e4e7ee', // wheel silver
  S: '#8b93a6', // wheel shade
  t: '#2a2833', // tyre
  l: '#c6f432', // lime
  n: '#1f5cff', // NOS blue
  N: '#7aa2ff', // NOS blue light
  m: '#0b2a9e', // NOS blue dark
  c: '#ffd23f', // flame yellow
  f: '#ff8a1f', // flame orange
  e: '#ff4b1f', // flame red
}

export class Grid {
  readonly px: string[][]
  constructor(readonly w: number, readonly h: number) {
    this.px = Array.from({ length: h }, () => Array<string>(w).fill('.'))
  }
  set(x: number, y: number, c: string) {
    if (x >= 0 && y >= 0 && x < this.w && y < this.h) this.px[y][x] = c
  }
  get(x: number, y: number) {
    return x >= 0 && y >= 0 && x < this.w && y < this.h ? this.px[y][x] : '.'
  }
  rect(x: number, y: number, w: number, h: number, c: string) {
    for (let j = 0; j < h; j++) for (let i = 0; i < w; i++) this.set(x + i, y + j, c)
  }
  /** Fill a polygon (even-odd, sampled at pixel centres). */
  poly(pts: [number, number][], c: string) {
    for (let y = 0; y < this.h; y++)
      for (let x = 0; x < this.w; x++) if (inside(x + 0.5, y + 0.5, pts)) this.set(x, y, c)
  }
  circle(cx: number, cy: number, r: number, c: string) {
    for (let y = 0; y < this.h; y++)
      for (let x = 0; x < this.w; x++) if ((x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r) this.set(x, y, c)
  }
  /** One-pixel outline around everything drawn so far, on the empty pixels that touch it. */
  outline(c: string) {
    const add: [number, number][] = []
    for (let y = 0; y < this.h; y++)
      for (let x = 0; x < this.w; x++)
        if (this.get(x, y) === '.' && ([[1, 0], [-1, 0], [0, 1], [0, -1]] as const).some(([dx, dy]) => this.get(x + dx, y + dy) !== '.' && this.get(x + dx, y + dy) !== c))
          add.push([x, y])
    for (const [x, y] of add) this.set(x, y, c)
  }
  rows() {
    return this.px.map((r) => r.join(''))
  }
}

function inside(x: number, y: number, p: [number, number][]) {
  let c = false
  for (let i = 0, j = p.length - 1; i < p.length; j = i++) {
    const [xi, yi] = p[i]
    const [xj, yj] = p[j]
    if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) c = !c
  }
  return c
}

/** The racer, facing right: a low coupe with a big rear wing. The wheels are separate so they can spin. */
function car() {
  const g = new Grid(50, 17)
  // body: trunk, cabin, hood, nose
  g.poly([[1, 13], [0, 11], [0, 7], [2, 6], [13, 6], [16, 2], [28, 2], [37, 6], [45, 7], [49, 9], [49, 12], [47, 13]], 'o')
  // rear wing: plate, uprights
  g.rect(1, 0, 11, 2, 'p')
  g.rect(1, 2, 1, 4, 'k')
  g.rect(9, 2, 1, 4, 'k')
  g.rect(1, 0, 11, 1, 'p')
  g.rect(3, 1, 8, 1, 'd')
  // windows
  g.poly([[15, 6], [17.5, 3], [27, 3], [33, 6]], 'b')
  g.rect(22, 3, 1, 3, 'o') // B pillar
  g.set(18, 4, 'B')
  g.set(19, 4, 'B')
  g.set(20, 3, 'B')
  // top light and shade on the body
  g.rect(14, 6, 24, 1, 'O')
  g.rect(38, 7, 8, 1, 'O')
  // racing stripe: pink over white, front to back
  g.rect(3, 9, 42, 1, 'p')
  g.rect(3, 10, 42, 1, 'w')
  // lower body shade and sill
  g.rect(1, 12, 46, 1, 'd')
  g.rect(3, 13, 43, 1, 'k')
  // door line and handle
  g.rect(22, 7, 1, 5, 'd')
  g.set(24, 8, 'k')
  g.set(25, 8, 'k')
  // lights
  g.rect(48, 8, 1, 2, 'y')
  g.rect(47, 8, 1, 1, 'y')
  g.rect(0, 8, 1, 2, 'r')
  // exhaust
  g.rect(0, 12, 2, 1, 'G')
  // wheel arches (dark, wheels sit in them)
  for (const cx of [9.5, 38.5]) {
    for (let y = 0; y < 14; y++) for (let x = 0; x < 50; x++) if ((x + 0.5 - cx) ** 2 + (y + 0.5 - 12.5) ** 2 <= 5.3 ** 2) g.set(x, y, 'k')
  }
  g.outline('k')
  return g.rows()
}
export const CAR = car()
/** Where the wheel centres are on the car, in car pixels. */
export const WHEEL_AT = { rear: [5, 8], front: [34, 8] } as const

function wheel(offsetDeg: number) {
  const g = new Grid(9, 9)
  g.circle(4.5, 4.5, 4.5, 't')
  g.circle(4.5, 4.5, 3.2, 's')
  g.circle(4.5, 4.5, 1.2, 'G')
  for (let i = 0; i < 5; i++) {
    const a = ((offsetDeg + i * 72) * Math.PI) / 180
    for (let r = 1; r < 3.4; r += 0.5) g.set(Math.floor(4.5 + Math.cos(a) * r), Math.floor(4.5 + Math.sin(a) * r), 'S')
  }
  g.outline('k')
  return g.rows().map((r) => r.slice(0, 9))
}
export const WHEEL_A = wheel(0)
export const WHEEL_B = wheel(36)

/** A NOS bottle: blue cylinder with a rounded shoulder, silver valve, big orange label. 11 wide, 24 tall. */
function bottle() {
  const g = new Grid(11, 24)
  g.rect(3, 1, 5, 2, 'S') // valve
  g.rect(4, 0, 3, 1, 'w')
  g.rect(4, 3, 3, 2, 'G') // neck
  g.poly([[3, 5], [8, 5], [10, 8], [10, 22], [8, 24], [3, 24], [1, 22], [1, 8]], 'n') // cylinder
  g.rect(2, 7, 1, 14, 'N') // light edge
  g.rect(8, 9, 2, 13, 'm') // dark edge
  g.rect(2, 22, 7, 1, 'm')
  g.rect(2, 10, 7, 9, 'o') // label
  g.rect(2, 10, 7, 1, 'O')
  g.rect(2, 18, 7, 1, 'd')
  // a white "N" on the label
  g.rect(3, 12, 1, 5, 'w'); g.rect(7, 12, 1, 5, 'w'); g.set(4, 13, 'w'); g.set(5, 14, 'w'); g.set(6, 15, 'w')
  g.outline('k')
  return g.rows()
}
export const BOTTLE = bottle()

/** Exhaust flame, three frames, pointing left (it trails behind the car). 16 x 7. */
function flame(len: number, wobble: number) {
  const g = new Grid(16, 7)
  const mid = 3
  for (let i = 0; i < len; i++) {
    const half = Math.max(0, Math.round(2.6 - (i / len) * 2.6 + (i % 2 ? wobble : 0)))
    const x = 15 - i
    for (let y = -half; y <= half; y++) g.set(x, mid + y, i < len * 0.35 ? 'c' : i < len * 0.7 ? 'f' : 'e')
    if (i < len * 0.3) g.set(x, mid, 'w')
  }
  return g.rows()
}
export const FLAME_A = flame(15, 0)
export const FLAME_B = flame(12, 1)
export const FLAME_C = flame(16, 0)

export const SPARKLE = ['..w..', '..w..', 'wwwww', '..w..', '..w..']
/** A small cloud puff for smoke and dust. */
export const PUFF = ['.ggg.', 'ggggg', 'ggggg', '.ggg.']

/** A small bottle for the HUD (5 x 11), so the player can see how much NOS is collected. */
function miniBottle(lit: boolean) {
  const g = new Grid(5, 11)
  g.rect(2, 0, 1, 1, lit ? 'w' : 'G')
  g.rect(1, 1, 3, 1, lit ? 'S' : 'G')
  g.poly([[1, 2], [4, 2], [5, 4], [5, 10], [4, 11], [1, 11], [0, 10], [0, 4]], lit ? 'n' : 't')
  if (lit) {
    g.rect(1, 3, 1, 6, 'N')
    g.rect(1, 5, 3, 3, 'o')
    g.set(2, 6, 'w')
  }
  g.outline('k')
  return g.rows().map((r) => r.slice(0, 5))
}
export const MINI_LIT = miniBottle(true)
export const MINI_OFF = miniBottle(false)

/** A launch ramp, 26 x 8: a wedge in the app's orange with a hazard stripe. */
function ramp() {
  const g = new Grid(26, 8)
  g.poly([[0, 8], [25, 0], [26, 0], [26, 8]], 'd')
  g.poly([[0, 8], [25, 1], [26, 1], [26, 8]], 'o')
  g.rect(0, 7, 26, 1, 'd')
  for (let i = 0; i < 4; i++) g.rect(4 + i * 6, 5 - i * 1, 2, 2, 'y')
  g.outline('k')
  return g.rows().map((r) => r.slice(0, 26))
}
export const RAMP = ramp()
