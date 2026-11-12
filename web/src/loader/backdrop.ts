import { PALETTE } from './sprites'
import { H, ROAD_Y, W, clamp01 } from './timeline'

type Rows = readonly string[]
type Ctx = CanvasRenderingContext2D

const SKY = ['#08060c', '#0c0914', '#110b1e', '#170e29', '#1e1235', '#27163f', '#321a47']
const ROAD = '#1a1822'
const DASH = '#6b6880'

export function spriteCanvas(rows: Rows, palette: Record<string, string> = PALETTE): HTMLCanvasElement {
  const c = document.createElement('canvas')
  c.width = Math.max(...rows.map((r) => r.length))
  c.height = rows.length
  const g = c.getContext('2d')!
  rows.forEach((row, y) =>
    [...row].forEach((ch, x) => {
      if (ch === '.' || !palette[ch]) return
      g.fillStyle = palette[ch]
      g.fillRect(x, y, 1, 1)
    }),
  )
  return c
}

/** A small seeded random number generator, so the skyline is the same every time. */
export function rng(seed: number) {
  let a = seed
  return () => {
    a = (a + 0x6d2b79f5) | 0
    let t = Math.imul(a ^ (a >>> 15), 1 | a)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

function skyline(seed: number, base: string, lit: string[], minH: number, maxH: number, minW: number, maxW: number): HTMLCanvasElement {
  const c = document.createElement('canvas')
  c.width = W
  c.height = ROAD_Y
  const g = c.getContext('2d')!
  const r = rng(seed)
  let x = 0
  while (x < W) {
    const w = Math.min(W - x, Math.floor(minW + r() * (maxW - minW)))
    const h = Math.floor(minH + r() * (maxH - minH))
    g.fillStyle = base
    g.fillRect(x, ROAD_Y - h, w, h)
    if (r() < 0.3) g.fillRect(x + Math.floor(w / 2), ROAD_Y - h - 3, 1, 3) // an aerial
    for (let wy = ROAD_Y - h + 3; wy < ROAD_Y - 2; wy += 3)
      for (let wx = x + 1; wx < x + w - 1; wx += 3)
        if (r() < 0.07) {
          g.fillStyle = lit[Math.floor(r() * lit.length)]
          g.fillRect(wx, wy, 1, 1)
        }
    x += w + Math.floor(r() * 2)
  }
  return c
}

function background(): HTMLCanvasElement {
  const c = document.createElement('canvas')
  c.width = W
  c.height = H
  const g = c.getContext('2d')!
  // night sky in bands
  const band = Math.ceil(ROAD_Y / SKY.length)
  SKY.forEach((col, i) => {
    g.fillStyle = col
    g.fillRect(0, i * band, W, band + 1)
  })
  // stars
  const r = rng(7)
  for (let i = 0; i < 34; i++) {
    g.fillStyle = r() < 0.2 ? '#ff8ab8' : '#cfd3e8'
    g.fillRect(Math.floor(r() * W), Math.floor(r() * 40), 1, 1)
  }
  // a retro sun with stripes cut out of it, low behind the skyline
  const sx = 112
  const sy = 36
  const R = 21
  for (let y = sy - R; y <= sy + R; y++) {
    // stripes get thicker toward the bottom, like the classic synthwave sun
    const row = y - sy
    const cut = row > 2 && (row - 3) % 5 < Math.min(3, 1 + Math.floor(row / 7))
    if (cut) continue
    const dy = y + 0.5 - sy
    const half = Math.sqrt(Math.max(0, R * R - dy * dy))
    const k = clamp01((y - (sy - R)) / (R * 2))
    g.fillStyle = k < 0.35 ? '#ffd23f' : k < 0.6 ? '#ff8a1f' : k < 0.8 ? '#ff4b3a' : '#ff2e93'
    g.fillRect(Math.round(sx - half), y, Math.round(half * 2), 1)
  }
  // the road
  g.fillStyle = ROAD
  g.fillRect(0, ROAD_Y, W, H - ROAD_Y)
  g.fillStyle = '#ff2e93' // neon edge
  g.fillRect(0, ROAD_Y, W, 1)
  g.fillStyle = '#7a1747'
  g.fillRect(0, ROAD_Y + 1, W, 1)
  g.fillStyle = '#c6f432'
  g.fillRect(0, H - 2, W, 1)
  g.fillStyle = '#4e6b13'
  g.fillRect(0, H - 1, W, 1)
  return c
}


const px = (g: Ctx, color: string, x: number, y: number, w = 1, h = 1) => {
  g.fillStyle = color
  g.fillRect(Math.round(x), Math.round(y), w, h)
}

export interface Backdrop {
  /** Draws the night sky, sun, both skylines and the road, as they look when the road has moved `distance` pixels. */
  draw(g: Ctx, distance: number): void
}

/** The retro night scene shared by the loading screen and the "email sent" animation. */
export function createBackdrop(): Backdrop {
  const bg = background()
  const far = skyline(11, '#1a1230', ['#2c2352', '#3b2f6b'], 8, 20, 7, 13)
  const near = skyline(23, '#0e0a1a', ['#a3205f', '#a8742c', '#46568a'], 12, 28, 9, 16)
  return {
    draw(g, dist) {
      g.drawImage(bg, 0, 0)

    // two skylines, sliding at different speeds for depth. 640 px of road = 160 px of near city = 80 px of far city
    for (const [img, ratio] of [[far, 0.125], [near, 0.25]] as const) {
      const off = Math.floor((dist * ratio) % W)
      g.drawImage(img, -off, 0)
      g.drawImage(img, W - off, 0)
    }
    // the road, redrawn on top of the skylines' feet
    g.drawImage(bg, 0, ROAD_Y, W, H - ROAD_Y, 0, ROAD_Y, W, H - ROAD_Y)
    // kerb stripes and lane dashes, moving with the road
    const kerb = Math.floor(dist % 20)
    for (let x = -20 + kerb * -1; x < W; x += 20) px(g, '#f5f5f5', x + 20, ROAD_Y + 2, 10, 1)
    const dash = Math.floor(dist % 40)
    for (let x = -dash; x < W; x += 40) px(g, DASH, x, 80, 20, 1)

    },
  }
}
