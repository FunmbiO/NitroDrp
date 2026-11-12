import { createScene } from './scene'
import { FPS, H, W } from './timeline'

/** The still frame shown to people who've asked their device for less motion: a bottle just ahead of the car. */
export const STILL_AT = 1.25

/**
 * Starts the animation on a canvas and returns a function that stops it.
 *
 * The clock is the page's own (time since it started loading), not "time since this loader appeared", so when the
 * first loader hands over to the next one the car carries on from where it was instead of starting again.
 */
export function startLoader(canvas: HTMLCanvasElement): () => void {
  const ctx = canvas.getContext('2d')
  if (!ctx) return () => {}
  canvas.width = W
  canvas.height = H
  const scene = createScene()
  const reduce = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches

  // Whole-number scaling only, so every art pixel stays a crisp square.
  const fit = () => {
    const room = Math.min(window.innerWidth - 48, 640)
    const scale = Math.max(1, Math.min(4, Math.floor(room / W)))
    canvas.style.width = `${W * scale}px`
    canvas.style.height = `${H * scale}px`
  }
  fit()
  window.addEventListener('resize', fit)

  if (reduce) {
    scene.draw(ctx, STILL_AT, Math.floor(STILL_AT * FPS))
    return () => window.removeEventListener('resize', fit)
  }

  let raf = 0
  let last = -1
  const frame = () => {
    const seconds = performance.now() / 1000
    const tick = Math.floor(seconds * FPS) // a retro 20 frames a second, which is also easy on the battery
    if (tick !== last) {
      last = tick
      scene.draw(ctx, seconds, tick)
    }
    raf = requestAnimationFrame(frame)
  }
  frame()
  return () => {
    cancelAnimationFrame(raf)
    window.removeEventListener('resize', fit)
  }
}
