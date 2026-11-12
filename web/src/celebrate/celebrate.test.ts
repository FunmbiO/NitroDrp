import { describe, expect, it } from 'vitest'
import { GLYPH_ADVANCE, textPixels, textWidth } from '../loader/font'
import { H, ROAD_Y, W } from '../loader/timeline'
import { ENVELOPE, F1, F1_BODY, F1_FRONT_WHEEL, F1_REAR_WHEEL, PALETTE, bigFlame, puff } from './f1'
import { celebrationMode } from './mode'
import {
  CAR_TOP, PUFF_LIFE, START_X, T_DONE, T_EXIT, T_GO, T_LAUNCH, T_LIGHT, carX, flameLength, frameCount, lights, puffs, sentLabel, shake,
  speedLinesOn, textBounce, textFrom, textVisible,
} from './timeline'

const rectangular = (rows: readonly string[]) => rows.every((r) => r.length === rows[0].length)
const validColours = (rows: readonly string[]) => rows.every((r) => [...r].every((c) => c === '.' || c in PALETTE))

describe('the F1 art', () => {
  it.each([['car', F1_BODY], ['rear wheel', F1_REAR_WHEEL], ['front wheel', F1_FRONT_WHEEL], ['envelope', ENVELOPE], ['flame', bigFlame(24, 1)], ['smoke', puff(6)]] as const)(
    'the %s is a clean rectangle that only uses palette colours',
    (_n, rows) => {
      expect(rows.length).toBeGreaterThan(0)
      expect(rectangular(rows)).toBe(true)
      expect(validColours(rows)).toBe(true)
      expect(rows.some((r) => /[^.]/.test(r))).toBe(true)
    },
  )

  it('is the size the scene is laid out for, and fits on the road', () => {
    expect([F1_BODY[0].length, F1_BODY.length]).toEqual([F1.w, F1.h])
    expect(CAR_TOP + F1.h + 1).toBe(ROAD_Y) // the wheels stand exactly on the road
    expect(F1.w).toBeLessThan(W / 2 + 10)
  })

  it('has a NOS bottle you can see: blue, with an orange label, on the car', () => {
    const used = new Set(F1_BODY.join(''))
    for (const ch of 'nNmo') expect(used.has(ch)).toBe(true)
    const blue = F1_BODY.join('').split('').filter((c) => 'nNm'.includes(c)).length
    expect(blue).toBeGreaterThan(40) // a good-sized bottle, not a speck
  })

  it('is the app\'s white and orange, with a pink front wing', () => {
    const used = new Set(F1_BODY.join(''))
    for (const ch of 'wop') expect(used.has(ch)).toBe(true)
  })

  it('the wheels sit inside the car', () => {
    expect(F1.rearWheel[0] - 5.5).toBeGreaterThanOrEqual(0)
    expect(F1.frontWheel[0] + 4.5).toBeLessThanOrEqual(F1.w)
    expect(F1_REAR_WHEEL.length).toBeGreaterThan(F1_FRONT_WHEEL.length) // an F1 car's rear tyres are the big ones
  })
})

describe('the timeline', () => {
  it('goes in order: lights, green, launch, gone, words, done', () => {
    const order = [0, ...T_LIGHT, T_GO, T_LAUNCH, T_EXIT, T_DONE]
    for (let i = 1; i < order.length; i++) expect(order[i]).toBeGreaterThan(order[i - 1])
    expect(textFrom).toBeGreaterThanOrEqual(T_EXIT) // the words wait until the car has gone
  })

  it('shows the three start lights one by one, then green', () => {
    expect(lights(0).lit).toBe(0)
    expect(lights(T_LIGHT[0] + 0.01).lit).toBe(1)
    expect(lights(T_LIGHT[2] + 0.01)).toEqual({ lit: 3, green: false })
    expect(lights(T_GO + 0.01)).toEqual({ lit: 3, green: true })
  })

  it('keeps the car on the grid until the launch, then only ever moves it forward, off the right edge', () => {
    for (let t = 0; t < T_LAUNCH; t += 0.05) expect(carX(t)).toBe(START_X)
    let last = -Infinity
    for (let t = 0; t <= T_DONE; t += 1 / frameCount) {
      expect(carX(t)).toBeGreaterThanOrEqual(last)
      last = carX(t)
    }
    expect(carX(T_EXIT + 0.01)).toBeGreaterThanOrEqual(W)
    expect(carX(T_EXIT - 0.15)).toBeLessThan(W)
  })

  it('accelerates: it covers more ground each moment than the one before', () => {
    const d = (a: number, b: number) => carX(b) - carX(a)
    expect(d(T_LAUNCH + 0.4, T_LAUNCH + 0.6)).toBeGreaterThan(d(T_LAUNCH + 0.1, T_LAUNCH + 0.3))
  })

  it('shudders on the grid, and only there', () => {
    expect([shake(0.1, 0), shake(0.1, 1)]).toEqual([0, 0])
    expect(new Set([shake(0.8, 0), shake(0.8, 1)])).toEqual(new Set([0, 1]))
    expect(shake(T_LAUNCH + 0.2, 0)).toBe(0)
  })

  it('lights the NOS flame just before the launch, burns it until the car is gone, then puts it out', () => {
    expect(flameLength(0.1)).toBe(0)
    expect(flameLength(T_LAUNCH - 0.1)).toBeGreaterThan(0)
    expect(flameLength(T_LAUNCH + 0.3)).toBeGreaterThan(flameLength(T_LAUNCH - 0.1))
    expect(flameLength(T_EXIT + 0.5)).toBe(0)
  })

  it('leaves a trail of smoke that is still there after the car has gone, and clears by the end', () => {
    expect(puffs(0.5)).toHaveLength(0)
    expect(puffs(T_EXIT).length).toBeGreaterThan(25)
    expect(puffs(T_EXIT + 0.6).length).toBeGreaterThan(15) // the trail hangs in the air
    expect(puffs(T_DONE).length).toBeLessThan(puffs(T_EXIT).length / 3) // thinning out
    expect(T_EXIT + PUFF_LIFE).toBeGreaterThanOrEqual(T_DONE - 0.5) // and almost gone by the last frame
  })

  it('puffs grow as they age, stay on the road side of the picture, and are always the same for the same moment', () => {
    const ps = puffs(T_EXIT)
    for (const p of ps) {
      expect(p.age).toBeGreaterThanOrEqual(0)
      expect(p.age).toBeLessThan(1)
      expect(p.r).toBeGreaterThanOrEqual(2)
      expect(p.r).toBeLessThanOrEqual(8)
      expect(p.y).toBeGreaterThan(0)
      expect(p.y).toBeLessThan(ROAD_Y)
    }
    expect(puffs(T_EXIT)).toEqual(puffs(T_EXIT))
    const oldest = ps.reduce((a, b) => (a.age > b.age ? a : b))
    const youngest = ps.reduce((a, b) => (a.age < b.age ? a : b))
    expect(oldest.r).toBeGreaterThan(youngest.r)
  })

  it('has speed lines only while the car is under way', () => {
    expect(speedLinesOn(T_LAUNCH - 0.1)).toBe(false)
    expect(speedLinesOn(T_LAUNCH + 0.3)).toBe(true)
    expect(speedLinesOn(T_EXIT + 0.1)).toBe(false)
  })

  it('shows the words after the car has gone, and keeps them up long enough to read', () => {
    expect(textVisible(textFrom - 0.01)).toBe(false)
    expect(textVisible(textFrom)).toBe(true)
    expect(T_DONE - textFrom).toBeGreaterThanOrEqual(1.5)
    expect(textBounce(textFrom + 0.02)).toBeGreaterThan(0) // lands from below...
    expect(textBounce(textFrom + 0.1)).toBeLessThan(0) // ...bounces up...
    expect(textBounce(textFrom + 0.5)).toBe(0) // ...and settles
  })

  it('is short enough not to be a chore', () => {
    expect(T_DONE).toBeLessThanOrEqual(4.2)
    expect(T_DONE).toBeGreaterThanOrEqual(3)
  })
})

describe('the words', () => {
  it('say EMAIL SENT when everyone got it, and how many otherwise', () => {
    expect(sentLabel(3, 3)).toBe('EMAIL SENT')
    expect(sentLabel(1, 1)).toBe('EMAIL SENT')
    expect(sentLabel(2, 3)).toBe('2 OF 3 SENT')
    expect(sentLabel(1, 10)).toBe('1 OF 10 SENT')
  })

  it('can be drawn with the font, and fit inside the picture at the size the scene picks', () => {
    const labels = ['EMAIL SENT']
    for (let total = 1; total <= 20; total++) for (let sent = 1; sent <= total; sent++) labels.push(sentLabel(sent, total))
    for (const label of labels) {
      for (const ch of label.replace(/ /g, '')) expect(textPixels(ch).length).toBeGreaterThan(0) // every character has a glyph
      const k = label.length * GLYPH_ADVANCE * 2 > 150 ? 1 : 2 // the rule the scene uses
      expect(textWidth(label) * k).toBeLessThanOrEqual(W - 12) // always inside the picture, with room either side
    }
  })

  it('the picture is 160 x 90, same as the rest of the pixel art', () => {
    expect([W, H]).toEqual([160, 90])
  })
})

describe('when it plays', () => {
  const base = { search: '', automated: false, reducedMotion: false }
  it('plays in full for people', () => expect(celebrationMode(base)).toBe('full'))
  it('shows a still picture under "reduce motion"', () => expect(celebrationMode({ ...base, reducedMotion: true })).toBe('still'))
  it('is skipped in automated browsers so tests stay quick, unless asked for', () => {
    expect(celebrationMode({ ...base, automated: true })).toBe('skip')
    expect(celebrationMode({ ...base, automated: true, search: '?celebrate=1' })).toBe('full')
    expect(celebrationMode({ ...base, automated: true, reducedMotion: true, search: '?celebrate=1' })).toBe('still')
  })
  it('ignores any other value', () => expect(celebrationMode({ ...base, automated: true, search: '?celebrate=yes' })).toBe('skip'))
})
