import { useEffect, useRef } from 'react'
import { startLoader } from './start'

/**
 * The loading animation: a pixel racer grabbing NOS, boosting, jumping and drifting. It fades in after a quarter of
 * a second, so a quick load never flashes it. The words are real text for screen readers; the canvas is decoration.
 */
export function PixelLoader({ label = 'Loading' }: { label?: string }) {
  const canvas = useRef<HTMLCanvasElement>(null)
  useEffect(() => (canvas.current ? startLoader(canvas.current) : undefined), [])
  return (
    <div className="pixel-loader" role="status" aria-live="polite">
      <canvas ref={canvas} aria-hidden="true" />
      <p className="pixel-loader-text">
        {label}
        <span className="dots" aria-hidden="true" />
      </p>
    </div>
  )
}
