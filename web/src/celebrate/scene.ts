import { createBackdrop, rng, spriteCanvas } from '../loader/backdrop'
import { GLYPH_ADVANCE, textPixels, textWidth } from '../loader/font'
import { H, ROAD_Y, W } from '../loader/timeline'
import { ENVELOPE, F1, F1_BODY, F1_FRONT_WHEEL, F1_REAR_WHEEL, PALETTE, bigFlame, puff } from './f1'
import { CAR_TOP, carX, flameLength, lights, puffs, shake, speedLinesOn, textBounce, textVisible } from './timeline'

type Ctx = CanvasRenderingContext2D

export interface EmailScene {
  /** Draws the frame for time `t` seconds into the animation. `label` is the words ("EMAIL SENT"). */
  draw(g: Ctx, t: number, tick: number, label: string): void
}

export function createEmailScene(): EmailScene {
  const backdrop = createBackdrop()
  const body = spriteCanvas(F1_BODY, PALETTE)
  const rear = spriteCanvas(F1_REAR_WHEEL, PALETTE)
  const front = spriteCanvas(F1_FRONT_WHEEL, PALETTE)
  const envelope = spriteCanvas(ENVELOPE, PALETTE)
  const flames = [0, 1, 2].map((w) => (len: number) => spriteCanvas(bigFlame(len, w), PALETTE))
  const flameCache = new Map<string, HTMLCanvasElement>()
  const flame = (len: number, frame: number) => {
    const key = `${len}-${frame}`
    let c = flameCache.get(key)
    if (!c) flameCache.set(key, (c = flames[frame](len)))
    return c
  }
  const puffCache = new Map<number, HTMLCanvasElement>()
  const puffSprite = (r: number) => {
    let c = puffCache.get(r)
    if (!c) puffCache.set(r, (c = spriteCanvas(puff(r), PALETTE)))
    return c
  }

  const px = (g: Ctx, color: string, x: number, y: number, w = 1, h = 1) => {
    g.fillStyle = color
    g.fillRect(Math.round(x), Math.round(y), w, h)
  }

  /** Words in big pixels, with a dark edge so they read on anything. */
  function bigText(g: Ctx, s: string, cx: number, y: number, k: number, color: string) {
    const w = textWidth(s) * k
    const x0 = Math.round(cx - w / 2)
    const pts = textPixels(s)
    g.fillStyle = '#14101a'
    for (const [dx, dy] of pts) for (const [ox, oy] of [[k, 0], [-k, 0], [0, k], [0, -k], [k, k], [-k, -k], [k, -k], [-k, k]]) g.fillRect(x0 + dx * k + ox, y + dy * k + oy, k, k)
    g.fillStyle = color
    for (const [dx, dy] of pts) g.fillRect(x0 + dx * k, y + dy * k, k, k)
  }

  function startLights(g: Ctx, t: number) {
    const { lit, green } = lights(t)
    px(g, '#14101a', 62, 3, 36, 13)
    px(g, '#5a5a66', 62, 3, 36, 1)
    for (let i = 0; i < 3; i++) {
      const on = green ? true : i < lit
      const colour = green ? '#c6f432' : on ? '#ff2e1f' : '#3a1218'
      const cx = 69 + i * 11
      g.fillStyle = colour
      g.fillRect(cx - 3, 7, 7, 5)
      g.fillRect(cx - 2, 6, 5, 7)
      if (on) px(g, '#ffffff', cx - 1, 8, 1, 1)
    }
  }

  function draw(g: Ctx, t: number, tick: number, label: string) {
    g.imageSmoothingEnabled = false
    g.clearRect(0, 0, W, H)
    backdrop.draw(g, 0)

    // the start line and the lights
    for (let i = 0; i < 6; i++) px(g, i % 2 ? '#14101a' : '#f5f5f5', 3, ROAD_Y + 2 + i * 3, 2, 3)
    startLights(g, t)

    // smoke: soft puffs that grow and thin out, and stay where the car left them
    for (const p of puffs(t)) {
      if (p.age > 0.7 && (Math.round(p.x) + Math.round(p.y) + tick) % 3 === 0) continue // thins out by flickering away
      g.drawImage(puffSprite(p.r), Math.round(p.x - p.r), Math.round(p.y - p.r))
    }

    // speed lines, streaking back from the car
    if (speedLinesOn(t)) {
      const r = rng(41)
      const x = carX(t)
      for (let i = 0; i < 10; i++) {
        const y = 24 + Math.floor(r() * 44)
        const len = 14 + Math.floor(r() * 26)
        const lx = x - 6 - len - Math.floor(r() * 24) - ((tick * 5 + i * 13) % 14)
        if (y > CAR_TOP && y < ROAD_Y) continue
        px(g, i % 3 === 0 ? '#ff2e93' : '#f5f5f5', lx, y, len, 1)
      }
    }

    // the car: shaking on the grid, then gone
    const x = carX(t)
    if (x < W) {
      const y = CAR_TOP + shake(t, tick)
      const fl = flameLength(t)
      if (fl > 0) g.drawImage(flame(fl, tick % 3), x + F1.exhaust[0] - 33, y + F1.exhaust[1] - 4)
      g.drawImage(body, x, y)
      g.drawImage(rear, Math.round(x + F1.rearWheel[0] - 5.5), Math.round(y + F1.rearWheel[1] - 5.5 + 1))
      g.drawImage(front, Math.round(x + F1.frontWheel[0] - 4.5), Math.round(y + F1.frontWheel[1] - 4.5 + 1))
    }

    // the words, popping in with the envelope
    if (textVisible(t)) {
      const drop = textBounce(t)
      const k = label.length * GLYPH_ADVANCE * 2 > 150 ? 1 : 2 // as big as fits
      const bob = Math.round(Math.sin(t * 9) * 1.5)
      g.drawImage(envelope, Math.round(W / 2 - 8), 20 + bob + drop)
      bigText(g, label, W / 2, 40 + drop, k, '#c6f432')
      // a little shine
      if (tick % 8 < 3) px(g, '#ffffff', 99, 24 + bob, 1, 3)
    }
  }

  return { draw }
}
