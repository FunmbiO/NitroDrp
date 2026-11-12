import { H, ROAD_Y, W } from '../loader/timeline'
import { F1 } from './f1'

/**
 * The "email sent" moment, as pure functions of time: three start lights, a launch on NOS, the car speeding off to the
 * right and leaving a trail of smoke, then the words. Nothing here touches the page, so it can be tested.
 */
export const FPS = 20

export const T_LIGHT = [0.25, 0.5, 0.75] // the three red lights come on, one by one
export const T_GO = 1.05 // all green
export const T_LAUNCH = 1.1 // NOS: the car goes
export const T_TEXT_BASE = 2.05 // the words appear once the car has gone
export const T_DONE = 3.9 // and the animation hands over to the next page

/** The car's left edge at the start, and how hard it accelerates (pixels, seconds). */
export const START_X = 8
const V0 = 25
const ACCEL = 285

/** When the car has completely left the picture on the right. */
export const T_EXIT = (() => {
  // START_X + V0 t + A t^2 / 2 = W  (its left edge reaches the right edge of the picture)
  const c = START_X - W
  const t = (-V0 + Math.sqrt(V0 * V0 - 2 * ACCEL * c)) / ACCEL
  return T_LAUNCH + t
})()

/** Top of the car sprite, so its wheels stand on the road. */
export const CAR_TOP = ROAD_Y - (F1.h + 1)

export function carX(t: number): number {
  if (t < T_LAUNCH) return START_X
  const s = t - T_LAUNCH
  return Math.round(START_X + V0 * s + 0.5 * ACCEL * s * s)
}

export interface Lights {
  /** How many of the three are lit. */
  lit: number
  green: boolean
}
export const lights = (t: number): Lights => ({ lit: T_LIGHT.filter((x) => t >= x).length, green: t >= T_GO })

/** The engine shuddering on the grid, before the launch. */
export function shake(t: number, tick: number): number {
  return t >= 0.3 && t < T_LAUNCH ? (tick % 2 === 0 ? 1 : 0) : 0
}

/** Length in pixels of the NOS flame: none, then a spurt just before the launch, then the full burn. */
export function flameLength(t: number): number {
  if (t < T_LAUNCH - 0.28 || t >= T_EXIT + 0.1) return 0
  if (t < T_LAUNCH) return 8 + Math.round(((t - (T_LAUNCH - 0.28)) / 0.28) * 6)
  return Math.min(30, 20 + Math.round((t - T_LAUNCH) * 20))
}

export const PUFF_LIFE = 1.9
export const MAX_PUFF = 11
const PUFF_EVERY = 0.02

export interface Puff {
  x: number
  y: number
  r: number
  /** 0 when it has just been made, 1 when it has gone. */
  age: number
}

/** Every puff of smoke alive at time t. They stay where the car left them, so together they make the trail. */
export function puffs(t: number): Puff[] {
  const out: Puff[] = []
  const first = T_LAUNCH - 0.22
  for (let i = 0; ; i++) {
    const born = first + i * PUFF_EVERY
    if (born > T_EXIT || born > t) break
    const life = (t - born) / PUFF_LIFE
    if (life >= 1) continue
    const wobble = ((i * 7919) % 5) - 2 // a deterministic bit of randomness
    const x = carX(born) + 8 - life * 10 + wobble
    const y = ROAD_Y - 5 - life * 16 + (wobble % 2)
    // the first puffs, from the spinning tyres at the launch, are bigger: a burnout cloud
    const burnout = born < T_LAUNCH + 0.3 ? 2 : 0
    out.push({ x, y, r: Math.min(MAX_PUFF, 3 + burnout + Math.floor(life * 8)), age: life })
  }
  return out
}

/** Speed lines streaking back from the car once it is under way. */
export const speedLinesOn = (t: number) => t >= T_LAUNCH + 0.05 && t < T_EXIT

/** The words: when they appear, and a little bounce as they land (pixels down, then up, then at rest). */
export const textFrom = T_TEXT_BASE
export const textVisible = (t: number) => t >= textFrom
export function textBounce(t: number): number {
  const age = t - textFrom
  if (age < 0) return 0
  return age < 0.08 ? 6 : age < 0.16 ? -3 : 0
}

/** What the words say. Everyone sent: "EMAIL SENT". Otherwise how many got it. */
export function sentLabel(sent: number, total: number): string {
  return sent >= total ? 'EMAIL SENT' : `${sent} OF ${total} SENT`
}

export const frameCount = Math.ceil(T_DONE * FPS)
export const SCENE = { W, H }
