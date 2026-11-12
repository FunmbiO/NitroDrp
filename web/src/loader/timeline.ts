/**
 * The choreography of the loading screen, as pure functions of time: where the road is, where the car is, when it
 * grabs NOS, boosts, jumps, flips and drifts. Nothing here touches the page, so it can be tested, and the drawing
 * code just asks "what's happening at time t?". The loop repeats every LOOP seconds.
 */
export const W = 160
export const H = 90
export const ROAD_Y = 68 // top edge of the road
export const LOOP = 6.4
export const FPS = 20

/** The car sits here when cruising (top-left of its sprite). */
export const CAR_X = 34
export const CAR_W = 50
export const CAR_H = 17
export const CAR_Y = ROAD_Y - CAR_H
export const RAMP_W = 26
export const RAMP_H = 8

// --- the story, in seconds ---
export const T_PICKUP_1 = 1.5 // grabs the first NOS bottle
export const T_BOOST_IN = 0.15 // how quickly the boost kicks in
export const T_RAMP_START = 2.75 // the front wheels meet the ramp
export const AIR_SECONDS = 1.75
export const T_DRIFT_END = 5.9

/** How fast the world moves past, before the loop is normalised. Cruise, boost, then a drift. */
function rawSpeed(t: number) {
  const tLand = T_LAND_RAW
  if (t < T_PICKUP_1) return 60
  if (t < T_PICKUP_1 + T_BOOST_IN) return 60 + (150 - 60) * ((t - T_PICKUP_1) / T_BOOST_IN)
  if (t < tLand) return 150
  if (t < tLand + 0.4) return 150 - (150 - 75) * ((t - tLand) / 0.4)
  if (t < T_DRIFT_END) return 75
  return 75 - 15 * Math.min(1, (t - T_DRIFT_END) / 0.4)
}
// The landing time depends on the ramp, which depends on the scroll. This rough figure only shapes the speed curve.
const T_LAND_RAW = 2.9 + AIR_SECONDS

/** Cumulative distance the road has moved, sampled finely and scaled so one loop is exactly TOTAL_SCROLL pixels. */
export const TOTAL_SCROLL = 640 // a whole number of road tiles (20 px dashes, 160 px skyline), so the loop is seamless
const STEP = 1 / 480
const TABLE: number[] = (() => {
  const n = Math.ceil(LOOP / STEP) + 1
  const out = [0]
  for (let i = 1; i < n; i++) out.push(out[i - 1] + rawSpeed((i - 0.5) * STEP) * STEP)
  const k = TOTAL_SCROLL / out[Math.round(LOOP / STEP)]
  return out.map((v) => v * k)
})()

export function scroll(t: number) {
  const tt = Math.min(Math.max(t, 0), LOOP)
  const f = tt / STEP
  const i = Math.floor(f)
  return i >= TABLE.length - 1 ? TABLE[TABLE.length - 1] : TABLE[i] + (TABLE[i + 1] - TABLE[i]) * (f - i)
}

function timeAtScroll(distance: number) {
  let lo = 0
  let hi = LOOP
  for (let i = 0; i < 40; i++) {
    const mid = (lo + hi) / 2
    if (scroll(mid) < distance) lo = mid
    else hi = mid
  }
  return hi
}

/** The car leaves the end of the ramp when the road has moved one ramp-length since the wheels met it. */
export const T_LAUNCH = timeAtScroll(scroll(T_RAMP_START) + RAMP_W)
export const T_LAND = T_LAUNCH + AIR_SECONDS
export const T_APEX = T_LAUNCH + AIR_SECONDS / 2
export const T_FLIP_DONE = T_LAUNCH + AIR_SECONDS * 0.92
export const ARC = 36 // how high it jumps

export const clamp01 = (x: number) => Math.min(1, Math.max(0, x))
const easeInOut = (x: number) => 0.5 - 0.5 * Math.cos(Math.PI * clamp01(x))
const snap = (deg: number, step: number) => Math.round(deg / step) * step

export interface Pose {
  /** Top-left of the car sprite when upright. */
  x: number
  y: number
  /** Degrees, 0 is level, negative lifts the nose. Snapped so the pixels turn in clean steps. */
  angle: number
  boost: boolean
  airborne: boolean
  drifting: boolean
  /** The frame after landing: squashed one pixel. */
  squash: boolean
  /** 0 = on the road; otherwise how many pixels above it. */
  height: number
}

export function pose(t: number): Pose {
  const tick = Math.floor(t * FPS)
  const boostFrom = T_PICKUP_1
  const inBoost = t >= boostFrom && t < T_LAND
  // lean forward into the boost, and settle back after the landing
  const lean = Math.round(6 * (easeInOut((t - boostFrom) / 0.25) - easeInOut((t - T_LAND) / 0.5)))

  let y = CAR_Y
  let angle = 0
  let height = 0
  const onRamp = t >= T_RAMP_START && t < T_LAUNCH
  const airborne = t >= T_LAUNCH && t < T_LAND

  if (onRamp) {
    const p = clamp01((scroll(t) - scroll(T_RAMP_START)) / RAMP_W)
    height = RAMP_H * p
    angle = snap(-15 * p, 7.5)
  } else if (airborne) {
    const u = (t - T_LAUNCH) / AIR_SECONDS
    height = RAMP_H * (1 - u) + ARC * 4 * u * (1 - u)
    const flip = clamp01((u - 0.08) / 0.84)
    angle = snap(-15 * (1 - u) - 360 * easeInOut(flip), 15)
  } else if (t >= T_LAND && t < T_DRIFT_END) {
    // sliding: tail out, the nose lifted a little and wobbling
    const age = t - T_LAND
    const settle = 1 - clamp01((t - (T_DRIFT_END - 0.4)) / 0.4)
    angle = snap((-8 + 3 * Math.sin(age * 8)) * settle, 7.5)
  } else if (t < T_PICKUP_1 || t >= T_DRIFT_END) {
    y -= tick % 4 === 0 ? 1 : 0 // a slight bob while cruising
  }
  const squash = t >= T_LAND && t < T_LAND + 0.12
  if (squash) y += 1
  return {
    x: CAR_X + lean,
    y: y - Math.round(height),
    angle,
    boost: inBoost || airborne,
    airborne,
    drifting: t >= T_LAND + 0.05 && t < T_DRIFT_END,
    squash,
    height: Math.round(height),
  }
}

/** Screen x of the front wheel centre, which the ramp and the bottles are lined up against. */
export const frontWheelX = (t: number) => pose(t).x + 34 + 4.5

export interface Pickup {
  at: number
  /** On the ground ahead of the car, or hanging in the air above it. */
  kind: 'ground' | 'air'
}
export const PICKUPS: Pickup[] = [
  { at: T_PICKUP_1, kind: 'ground' },
  { at: T_APEX, kind: 'air' },
]

export interface Popup {
  text: string
  from: number
  to: number
}
export const POPUPS: Popup[] = [
  { text: 'NOS!', from: T_PICKUP_1, to: T_PICKUP_1 + 0.7 },
  { text: 'NOS!', from: T_APEX, to: T_APEX + 0.5 },
  { text: 'FLIP!', from: T_FLIP_DONE - 0.1, to: T_LAND + 0.3 },
  { text: 'DRIFT!', from: T_LAND + 0.45, to: T_DRIFT_END - 0.2 },
]

/** The loop time for a moment in wall-clock seconds. */
export const loopTime = (seconds: number) => ((seconds % LOOP) + LOOP) % LOOP
