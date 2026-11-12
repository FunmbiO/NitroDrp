import { describe, expect, it } from 'vitest'
import { GLYPH_ADVANCE, textPixels, textWidth } from './font'
import { SPLASH_MS, splashMs, splashRemaining } from './splash'
import {
  BOTTLE, CAR, FLAME_A, FLAME_B, FLAME_C, MINI_LIT, MINI_OFF, PALETTE, PUFF, RAMP, SPARKLE, WHEEL_A, WHEEL_AT, WHEEL_B,
} from './sprites'
import {
  ARC, CAR_H, CAR_W, CAR_X, FPS, H, LOOP, POPUPS, PICKUPS, ROAD_Y, TOTAL_SCROLL, T_APEX, T_DRIFT_END, T_FLIP_DONE, T_LAND, T_LAUNCH,
  T_PICKUP_1, T_RAMP_START, W, loopTime, pose, scroll,
} from './timeline'

const SPRITES = { CAR, WHEEL_A, WHEEL_B, BOTTLE, FLAME_A, FLAME_B, FLAME_C, SPARKLE, PUFF, MINI_LIT, MINI_OFF, RAMP }

describe('the pixel art', () => {
  it.each(Object.entries(SPRITES))('%s is a clean rectangle that only uses palette colours', (_name, rows) => {
    expect(rows.length).toBeGreaterThan(0)
    const width = rows[0].length
    for (const row of rows) {
      expect(row.length).toBe(width)
      for (const ch of row) expect(ch === '.' || ch in PALETTE).toBe(true)
    }
    expect(rows.some((r) => /[^.]/.test(r))).toBe(true) // something is actually drawn
  })

  it('the sprites are the sizes the scene is laid out for', () => {
    expect([CAR[0].length, CAR.length]).toEqual([CAR_W, CAR_H])
    expect([WHEEL_A[0].length, WHEEL_A.length]).toEqual([9, 9])
    expect([BOTTLE[0].length, BOTTLE.length]).toEqual([11, 24])
    expect([RAMP[0].length, RAMP.length]).toEqual([26, 8])
  })

  it('the wheels sit inside the car and reach below its body', () => {
    for (const [x, y] of Object.values(WHEEL_AT)) {
      expect(x).toBeGreaterThanOrEqual(0)
      expect(x + 9).toBeLessThanOrEqual(CAR_W)
      expect(y + 9).toBeLessThanOrEqual(CAR_H)
    }
  })

  it('the bottle is taller than the car, so it cannot be missed', () => {
    expect(BOTTLE.length).toBeGreaterThan(CAR_H)
  })

  it('the bottle is blue with an orange label', () => {
    const used = new Set(BOTTLE.join(''))
    for (const ch of 'nNmo') expect(used.has(ch)).toBe(true)
  })

  it('the two wheel frames differ, so the wheels seem to turn', () => {
    expect(WHEEL_A.join('')).not.toBe(WHEEL_B.join(''))
  })
})

describe('the font', () => {
  it('has every letter the popups use', () => {
    for (const p of POPUPS) expect(textPixels(p.text).length).toBeGreaterThan(p.text.replace(/ /g, '').length * 3)
  })
  it('skips characters it does not know, and measures text', () => {
    expect(textPixels('~~')).toEqual([])
    expect(textWidth('NOS')).toBe(3 * GLYPH_ADVANCE - 1)
    expect(textWidth('')).toBe(0)
  })
})

describe('the timeline', () => {
  it('tells its story in order, inside one loop', () => {
    const order = [0, T_PICKUP_1, T_RAMP_START, T_LAUNCH, T_APEX, T_FLIP_DONE, T_LAND, T_DRIFT_END, LOOP]
    for (let i = 1; i < order.length; i++) expect(order[i]).toBeGreaterThan(order[i - 1])
  })

  it('moves the road forward, never back, and exactly 640 px per loop so it repeats without a jump', () => {
    expect(scroll(0)).toBe(0)
    expect(scroll(LOOP)).toBeCloseTo(TOTAL_SCROLL, 6)
    let last = -1
    for (let t = 0; t <= LOOP; t += 0.01) {
      const s = scroll(t)
      expect(s).toBeGreaterThanOrEqual(last)
      last = s
    }
    expect(TOTAL_SCROLL % 160).toBe(0) // the skylines and road dashes tile
  })

  it('starts and ends the loop in the same pose, so the loop is seamless', () => {
    const a = pose(0)
    const b = pose(LOOP - 1 / FPS)
    expect([a.x, a.angle, a.boost, a.airborne]).toEqual([b.x, b.angle, b.boost, b.airborne])
    expect(Math.abs(a.y - b.y)).toBeLessThanOrEqual(1) // the idle bob
  })

  it('boosts from the first bottle until the landing, and only then', () => {
    expect(pose(T_PICKUP_1 - 0.05).boost).toBe(false)
    expect(pose(T_PICKUP_1 + 0.05).boost).toBe(true)
    expect(pose(T_APEX).boost).toBe(true)
    expect(pose(T_LAND + 0.05).boost).toBe(false)
  })

  it('jumps off the ramp, reaches its height at the top, and lands on the road', () => {
    expect(pose(T_LAUNCH - 0.001).airborne).toBe(false)
    expect(pose(T_LAUNCH + 0.01).airborne).toBe(true)
    expect(pose(T_APEX).height).toBeGreaterThanOrEqual(ARC)
    expect(pose(T_LAND - 0.001).airborne).toBe(true)
    expect(pose(T_LAND + 0.2).height).toBe(0)
    let max = 0
    for (let t = T_LAUNCH; t < T_LAND; t += 1 / FPS) max = Math.max(max, pose(t).height)
    expect(max).toBeLessThanOrEqual(ARC + 12)
  })

  it('does a full front flip, and ends the jump level', () => {
    expect(pose(T_APEX).angle).toBeLessThanOrEqual(-150)
    const nearLanding = pose(T_LAND - 1 / FPS).angle
    expect(Math.abs(Math.abs(nearLanding) - 360)).toBeLessThanOrEqual(30) // a whole turn, give or take one 15 degree step
    expect(Math.abs(pose(T_LAND + 0.3).angle)).toBeLessThanOrEqual(15)
  })

  it('only turns in clean steps, so the pixels stay crisp', () => {
    for (let t = 0; t < LOOP; t += 1 / FPS) expect(pose(t).angle % 7.5).toBeCloseTo(0, 6)
  })

  it('stays on screen the whole time, with the whole car inside the picture', () => {
    for (let t = 0; t < LOOP; t += 1 / FPS) {
      const p = pose(t)
      expect(p.x).toBeGreaterThanOrEqual(CAR_X - 1)
      expect(p.x + CAR_W).toBeLessThan(W)
      expect(p.y).toBeGreaterThanOrEqual(0)
      expect(p.y + CAR_H).toBeLessThanOrEqual(H)
    }
  })

  it('keeps the whole car in the picture even while it is turned over', () => {
    for (let t = 0; t < LOOP; t += 1 / FPS) {
      const p = pose(t)
      const a = (p.angle * Math.PI) / 180
      // the half-width and half-height of the car's outline once turned by `a`
      const hx = Math.abs(Math.cos(a)) * (CAR_W / 2) + Math.abs(Math.sin(a)) * (CAR_H / 2)
      const hy = Math.abs(Math.sin(a)) * (CAR_W / 2) + Math.abs(Math.cos(a)) * (CAR_H / 2)
      const cx = p.x + CAR_W / 2
      const cy = p.y + 8.5
      expect(cx - hx).toBeGreaterThanOrEqual(0)
      expect(cx + hx).toBeLessThanOrEqual(W)
      expect(cy - hy).toBeGreaterThanOrEqual(0)
      expect(cy + hy).toBeLessThanOrEqual(ROAD_Y + 6) // a tilted wheel may dip a pixel or two into the road surface
    }
  })

  it('squashes briefly on landing and drifts afterwards', () => {
    expect(pose(T_LAND + 0.02).squash).toBe(true)
    expect(pose(T_LAND + 0.3).squash).toBe(false)
    expect(pose(T_LAND + 0.5).drifting).toBe(true)
    expect(pose(T_DRIFT_END + 0.1).drifting).toBe(false)
  })

  it('has two NOS pickups, one on the road and one in the air, both inside the loop', () => {
    expect(PICKUPS.map((p) => p.kind)).toEqual(['ground', 'air'])
    for (const p of PICKUPS) expect(p.at).toBeGreaterThan(0), expect(p.at).toBeLessThan(LOOP)
  })

  it('shows its popups in order and inside the loop', () => {
    for (const p of POPUPS) {
      expect(p.to).toBeGreaterThan(p.from)
      expect(p.from).toBeGreaterThanOrEqual(0)
      expect(p.to).toBeLessThanOrEqual(LOOP + 0.5)
    }
  })

  it('keeps the car above the road line while cruising', () => {
    expect(pose(0.5).y + CAR_H).toBeLessThanOrEqual(ROAD_Y)
  })

  it('wraps time into the loop, including negative time', () => {
    expect(loopTime(LOOP + 1)).toBeCloseTo(1, 6)
    expect(loopTime(-1)).toBeCloseTo(LOOP - 1, 6)
    expect(loopTime(0)).toBe(0)
  })
})

describe('how long the first loading screen stays up', () => {
  const normal = { search: '', automated: false, embedded: false, reducedMotion: false }

  it('waits the full splash time for an ordinary visit', () => {
    expect(splashMs(normal)).toBe(SPLASH_MS)
  })

  it('long enough to see the first NOS grab and the boost, and not absurdly long', () => {
    expect(SPLASH_MS).toBeGreaterThanOrEqual(2200)
    expect(SPLASH_MS).toBeLessThanOrEqual(4000)
    expect(SPLASH_MS / 1000).toBeGreaterThan(T_PICKUP_1 + 0.5)
  })

  it('does not hold up tests, embedded pages, or people who asked for less motion', () => {
    expect(splashMs({ ...normal, automated: true })).toBe(0)
    expect(splashMs({ ...normal, embedded: true })).toBe(0)
    expect(splashMs({ ...normal, reducedMotion: true })).toBe(0)
  })

  it('can be set from the address, for trying other lengths', () => {
    expect(splashMs({ ...normal, search: '?splash=1500' })).toBe(1500)
    expect(splashMs({ ...normal, search: '?x=1&splash=0' })).toBe(0)
    expect(splashMs({ ...normal, automated: true, search: '?splash=2000' })).toBe(2000) // an explicit ask wins
  })

  it('ignores junk in that setting', () => {
    for (const bad of ['?splash=abc', '?splash=-5', '?splash=1e9', '?splash=', '?splash=999999', '?splash=2.5'])
      expect(splashMs({ ...normal, search: bad })).toBe(SPLASH_MS)
  })

  it('only waits for what is left: a slow load gets no extra delay', () => {
    expect(splashRemaining(3000, 400)).toBe(2600)
    expect(splashRemaining(3000, 3000)).toBe(0)
    expect(splashRemaining(3000, 9000)).toBe(0)
    expect(splashRemaining(0, 100)).toBe(0)
  })
})

