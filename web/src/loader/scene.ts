import { createBackdrop, rng, spriteCanvas } from './backdrop'
import { textPixels, textWidth } from './font'
import {
  BOTTLE, CAR, FLAME_A, FLAME_B, FLAME_C, MINI_LIT, MINI_OFF, PUFF, RAMP, SPARKLE, WHEEL_A, WHEEL_AT, WHEEL_B,
} from './sprites'
import {
  CAR_H, CAR_W, CAR_X, FPS, H, LOOP, PICKUPS, POPUPS, RAMP_H, RAMP_W, ROAD_Y, T_APEX, T_LAND, T_RAMP_START, W,
  frontWheelX, loopTime, pose, scroll,
} from './timeline'

type Ctx = CanvasRenderingContext2D

export interface Scene {
  /** Draws the frame for loop time `t` (seconds). `tick` is the frame number, used for the little flicker animations. */
  draw(ctx: Ctx, t: number, tick: number): void
}

export function createScene(): Scene {
  const backdrop = createBackdrop()
  const car = spriteCanvas(CAR)
  const wheelA = spriteCanvas(WHEEL_A)
  const wheelB = spriteCanvas(WHEEL_B)
  const flames = [spriteCanvas(FLAME_A), spriteCanvas(FLAME_B), spriteCanvas(FLAME_C)]
  const bottle = spriteCanvas(BOTTLE)
  const miniLit = spriteCanvas(MINI_LIT)
  const miniOff = spriteCanvas(MINI_OFF)
  const ramp = spriteCanvas(RAMP)
  const sparkle = spriteCanvas(SPARKLE)
  const puff = spriteCanvas(PUFF)

  // the car, wheels and flame composed once per frame, so the whole thing can be turned as one
  const LAYER_W = 70
  const LAYER_H = 24
  const layer = document.createElement('canvas')
  layer.width = LAYER_W
  layer.height = LAYER_H
  const lg = layer.getContext('2d')!
  const OX = 18 // where the car sits inside the layer
  const OY = 4

  function composeCar(boost: boolean, tick: number, drifting: boolean) {
    lg.clearRect(0, 0, LAYER_W, LAYER_H)
    if (boost) lg.drawImage(flames[tick % 3], OX - 16, OY + 9)
    lg.drawImage(car, OX, OY)
    const wf = (tick + (drifting ? 1 : 0)) % 2 === 0 ? wheelA : wheelB
    const wf2 = (tick + (drifting ? 1 : 0)) % 2 === 0 ? wheelB : wheelA
    lg.drawImage(wf, OX + WHEEL_AT.rear[0], OY + WHEEL_AT.rear[1])
    lg.drawImage(wf2, OX + WHEEL_AT.front[0], OY + WHEEL_AT.front[1])
  }

  const px = (g: Ctx, color: string, x: number, y: number, w = 1, h = 1) => {
    g.fillStyle = color
    g.fillRect(Math.round(x), Math.round(y), w, h)
  }

  function text(g: Ctx, s: string, x: number, y: number, color: string) {
    const pts = textPixels(s)
    g.fillStyle = '#14101a' // a dark edge so it reads on any background
    for (const [dx, dy] of pts) for (const [ox, oy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) g.fillRect(x + dx + ox, y + dy + oy, 1, 1)
    g.fillStyle = color
    for (const [dx, dy] of pts) g.fillRect(x + dx, y + dy, 1, 1)
  }

  /** Where a ground bottle is on screen: it rides the road toward the car. */
  const groundBottleX = (t: number) => frontWheelX(PICKUPS[0].at) + 8 + (scroll(PICKUPS[0].at) - scroll(t))
  /** The bottle hanging in the air is lined up with the car at the top of the jump. */
  const airBottleX = (t: number) => pose(T_APEX).x + 20 + (scroll(T_APEX) - scroll(t))

  function draw(g: Ctx, tRaw: number, tick: number) {
    const t = loopTime(tRaw)
    const p = pose(t)
    const dist = scroll(t)
    g.imageSmoothingEnabled = false
    g.clearRect(0, 0, W, H)
    backdrop.draw(g, dist)

    // the launch ramp, riding the road toward the car
    const rampX = frontWheelX(T_RAMP_START) - 4.5 + 4.5 + (scroll(T_RAMP_START) - dist)
    if (rampX < W + 4 && rampX > -RAMP_W - 4 && t < T_LAND + 0.6) g.drawImage(ramp, Math.round(rampX), ROAD_Y - RAMP_H + 1)

    // a shadow under the car, smaller the higher it is
    const shadowW = Math.max(14, 44 - p.height)
    px(g, '#0a0810', CAR_X + 25 - shadowW / 2 + (p.x - CAR_X), ROAD_Y + 1, shadowW, 2)

    // NOS bottles waiting to be grabbed. They hover, glow and glint so they are impossible to miss.
    const bob = Math.round(Math.sin(t * 7) * 1.5)
    const first = PICKUPS[0]
    if (t >= 0.2 && t < first.at) {
      const bx = Math.round(groundBottleX(t))
      drawBottle(g, bx, ROAD_Y - 26 + bob, tick)
    }
    const second = PICKUPS[1]
    if (t >= T_RAMP_START + 0.1 && t < second.at) {
      drawBottle(g, Math.round(airBottleX(t)), pose(T_APEX).y + CAR_H / 2 - 12 + bob, tick)
    }

    // speed lines while boosting
    if (p.boost) {
      const r = rng(99)
      for (let i = 0; i < 12; i++) {
        const y = 12 + Math.floor(r() * 54)
        const len = 12 + Math.floor(r() * 22)
        const sp = 3 + r() * 2
        const x = W - ((dist * sp + i * 41) % (W + 40)) + 20
        if (y > p.y - 4 && y < p.y + CAR_H && x > p.x - 4 && x < p.x + CAR_W + 4) continue // not through the car
        px(g, i % 3 === 0 ? '#ff2e93' : '#f5f5f5', x, y, len, 1)
      }
    }

    // smoke from the rear tyre while drifting, dust and sparks on landing
    if (p.drifting || t < T_LAND + 1.2) {
      for (let i = 0; i < 16; i++) {
        const born = T_LAND + 0.05 + i * 0.06
        const age = t - born
        if (age < 0 || age > 0.7 || born > 5.9) continue
        const rear = CAR_X + 14 + (p.x - CAR_X)
        const x = rear - age * 52
        const y = ROAD_Y - 4 - age * 14 - (i % 3)
        const size = age < 0.3 ? 5 : age < 0.5 ? 4 : 3
        px(g, i % 2 ? '#8b93a6' : '#c9cedb', x, y, size, size - 1)
      }
    }
    if (t >= T_LAND && t < T_LAND + 0.55) {
      const age = t - T_LAND
      const r = rng(5)
      for (let i = 0; i < 12; i++) {
        const a = Math.PI + (r() - 0.1) * Math.PI * 0.9
        const v = 20 + r() * 38
        const sx = CAR_X + 6 + 12 + Math.cos(a) * v * age * -1
        const sy = ROAD_Y - 2 + Math.sin(a) * v * age + 60 * age * age
        px(g, i % 3 === 0 ? '#ffe14d' : '#ff8a1f', sx, sy)
      }
    }

    // the car itself: all one piece, turned in clean pixel steps
    composeCar(p.boost, tick, p.drifting)
    g.save()
    g.translate(Math.round(p.x + CAR_W / 2), Math.round(p.y + 8.5))
    g.rotate((p.angle * Math.PI) / 180)
    g.drawImage(layer, -OX - CAR_W / 2, -OY - 8.5)
    g.restore()

    // sparkles when a bottle is collected
    for (const pk of PICKUPS) {
      const age = t - pk.at
      if (age >= 0 && age < 0.45) {
        const cx = pose(pk.at).x + (pk.kind === 'air' ? 24 : 46)
        const cy = (pk.kind === 'air' ? pose(pk.at).y + 8 : ROAD_Y - 14)
        for (let i = 0; i < 6; i++) {
          const a = (i / 6) * Math.PI * 2 + age * 3
          const d = 4 + age * 40
          g.drawImage(sparkle, Math.round(cx + Math.cos(a) * d) - 2, Math.round(cy + Math.sin(a) * d) - 2)
        }
      }
    }

    // words that pop up above the action
    for (const pop of POPUPS) {
      if (t < pop.from || t >= pop.to) continue
      const age = t - pop.from
      const blink = Math.floor(age * 10) % 4 === 3 && age > 0.35
      if (blink) continue
      const w = textWidth(pop.text)
      const y = Math.max(8, Math.round(pose(pop.from).y - 12 - age * 10))
      const shift = pop.from === T_APEX ? 34 : 0 // the mid-air bonus pops up beside the car, not on it
      text(g, pop.text, Math.round(p.x + CAR_W / 2 - w / 2 + shift), Math.min(y, 54), pop.text === 'FLIP!' ? '#c6f432' : pop.text === 'DRIFT!' ? '#ff2e93' : '#ffd23f')
    }

    // HUD: how much NOS has been collected this lap
    text(g, 'NOS', 4, 4, '#c6f432')
    const got = [t >= PICKUPS[0].at, t >= PICKUPS[1].at]
    got.forEach((on, i) => g.drawImage(on ? miniLit : miniOff, 4 + i * 8, 11))
    void puff
    void FPS
    void LOOP
  }

  function drawBottle(g: Ctx, x: number, y: number, tick: number) {
    // a pulsing halo of dots, then the bottle, then twinkles
    const pulse = tick % 6 < 3
    g.fillStyle = pulse ? '#7aa2ff' : '#1f5cff'
    for (let i = 0; i < 10; i++) {
      const a = (i / 10) * Math.PI * 2
      g.fillRect(Math.round(x + 5 + Math.cos(a) * 11), Math.round(y + 12 + Math.sin(a) * 15), 1, 1)
    }
    g.drawImage(bottle, x, y)
    if (tick % 5 < 3) g.drawImage(sparkle, x - 4, y + 2)
    if (tick % 7 > 3) g.drawImage(sparkle, x + 10, y + 9)
  }

  return { draw: (g, t, tick) => draw(g, t, tick) }
}
