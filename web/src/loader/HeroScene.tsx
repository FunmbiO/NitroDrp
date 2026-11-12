import { useEffect, useRef } from 'react'
import { startLoader } from './start'

/** The page header's art is only worth its space on a wide screen. */
const WIDE = '(min-width: 900px)'

/**
 * The pixel racer, running its loop in a frame at the top right of the upload page, in the space beside the headline.
 * Decoration only: hidden from screen readers, not drawn at all on narrow screens (so phones don't spend battery on
 * it), and a still picture for anyone who has asked for less motion.
 */
export function HeroScene() {
  const canvas = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const el = canvas.current
    if (!el || typeof matchMedia !== 'function') return
    const wide = matchMedia(WIDE)
    let stop: (() => void) | undefined
    const sync = () => {
      if (wide.matches && !stop) stop = startLoader(el, { scale: 2 })
      if (!wide.matches && stop) {
        stop()
        stop = undefined
      }
    }
    sync()
    wide.addEventListener('change', sync)
    return () => {
      wide.removeEventListener('change', sync)
      stop?.()
    }
  }, [])

  return (
    <div className="hero-scene" aria-hidden="true">
      <canvas ref={canvas} />
    </div>
  )
}
