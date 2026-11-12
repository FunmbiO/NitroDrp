import { Grid, PALETTE as BASE } from '../loader/sprites'

/** The loading screen's colours, plus the few an F1 car needs. */
export const PALETTE: Record<string, string> = {
  ...BASE,
  h: '#ffd23f', // helmet
  v: '#4a5f9a', // visor
  g: '#9aa3b5', // grey metal
  q: '#e7e9f0', // light body shade
}

/** Where things are on the car, in car pixels (the sprite is CAR_W wide, CAR_H tall). */
export const F1 = { w: 74, h: 20, rearWheel: [15, 14.5], frontWheel: [60, 15.5], exhaust: [2, 12] } as const

/** A Formula 1 car facing right, in the app's colours: white and orange, a pink front wing, a NOS bottle on the spine. */
function f1Body() {
  const g = new Grid(F1.w, F1.h)
  // floor and diffuser
  g.rect(8, 16, 54, 1, 'k')
  g.poly([[3, 17], [10, 15], [10, 17]], 'G')
  // engine cover and sidepod: white, with an orange stripe
  g.poly([[3, 14], [3, 11], [9, 10], [18, 8.5], [30, 8], [44, 9], [58, 12], [70, 14], [70, 16], [8, 16]], 'w')
  g.poly([[18, 12], [40, 11], [58, 13.5], [58, 14.5], [18, 14.5]], 'o')
  g.rect(8, 15, 52, 1, 'd')
  g.rect(4, 12, 6, 1, 'q')
  // the nose cone: long, orange tip
  g.poly([[44, 9], [60, 11.5], [72, 14], [72, 15], [44, 15]], 'w')
  g.poly([[60, 12], [72, 14], [72, 15], [62, 15]], 'o')
  // airbox over the driver, and the halo
  g.poly([[24, 8], [26, 4], [34, 4], [34, 8]], 'o')
  g.rect(26, 4, 8, 1, 'O')
  g.rect(36, 3, 1, 6, 'k') // halo front strut
  g.rect(28, 3, 9, 1, 'k') // halo bar
  // the driver
  g.circle(31.5, 7, 2.3, 'h')
  g.rect(32, 6, 3, 2, 'v')
  // the NOS bottle strapped along the engine cover: unmistakable
  g.rect(6, 4, 16, 5, 'n')
  g.rect(7, 4, 14, 1, 'N')
  g.rect(18, 5, 4, 4, 'm')
  g.rect(10, 5, 5, 4, 'o') // label
  g.rect(11, 6, 1, 2, 'w'); g.rect(13, 6, 1, 2, 'w'); g.set(12, 6, 'w')
  g.rect(3, 5, 3, 3, 'g') // valve
  g.rect(4, 4, 1, 1, 'w')
  g.rect(14, 9, 1, 2, 'g') // strap
  g.rect(19, 9, 1, 2, 'g')
  // rear wing: plate, pillar and endplate
  g.rect(0, 2, 14, 2, 'o')
  g.rect(0, 1, 14, 1, 'O')
  g.rect(0, 1, 2, 10, 'w')
  g.rect(0, 5, 2, 2, 'o')
  g.rect(8, 4, 1, 6, 'k')
  // front wing: pink, with an endplate
  g.rect(62, 17, 12, 1, 'p')
  g.rect(70, 12, 2, 6, 'w')
  g.rect(70, 15, 2, 1, 'p')
  // sidepod intake shadow
  g.rect(40, 10, 8, 1, 'd')
  g.outline('k')
  return g.rows()
}
export const F1_BODY = f1Body()

function wheel(r: number) {
  const size = Math.ceil(r * 2)
  const g = new Grid(size, size)
  const c = size / 2
  g.circle(c, c, r, 't')
  g.circle(c, c, r * 0.62, 'w')
  g.circle(c, c, r * 0.36, 'S')
  g.circle(c, c, r * 0.14, 'k')
  g.outline('k')
  return g.rows().map((row) => row.slice(0, size))
}
export const F1_REAR_WHEEL = wheel(5.5)
export const F1_FRONT_WHEEL = wheel(4.5)

/** The NOS exhaust flame, pointing left, `len` long. */
export function bigFlame(len: number, wobble: number) {
  const w = 34
  const g = new Grid(w, 9)
  const mid = 4
  for (let i = 0; i < len; i++) {
    const half = Math.max(0, Math.round(3.4 - (i / len) * 3.4 + (i % 3 === 0 ? wobble : 0)))
    const x = w - 1 - i
    for (let y = -half; y <= half; y++) g.set(x, mid + y, i < len * 0.3 ? 'c' : i < len * 0.65 ? 'f' : 'e')
    if (i < len * 0.28) g.set(x, mid, 'w')
  }
  return g.rows()
}

/** An envelope, to ride along with "EMAIL SENT". 16 x 11. */
function envelope() {
  const g = new Grid(16, 11)
  g.rect(0, 0, 16, 11, 'w')
  g.rect(0, 10, 16, 1, 'S')
  g.poly([[0, 0], [16, 0], [8, 6.5]], 'q')
  for (let i = 0; i < 8; i++) { g.set(i, Math.round(i * 0.8), 'g'); g.set(15 - i, Math.round(i * 0.8), 'g') }
  g.rect(6, 7, 4, 4, 'p') // a pink seal
  g.outline('k')
  return g.rows()
}
export const ENVELOPE = envelope()

/** A little cloud of smoke, soft-edged (a checkerboard at the rim). `r` is the radius in pixels. */
export function puff(r: number) {
  const size = r * 2 + 2
  const g = new Grid(size, size)
  const c = size / 2
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const d = Math.hypot(x + 0.5 - c, y + 0.5 - c)
    if (d <= r - 1) g.set(x, y, 'q')
    else if (d <= r && (x + y) % 2 === 0) g.set(x, y, 'g')
  }
  return g.rows()
}
