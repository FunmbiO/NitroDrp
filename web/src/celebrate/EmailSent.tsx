import { useEffect, useRef } from 'react'
import { H, W } from '../loader/timeline'
import { currentCelebrationMode } from './mode'
import { createEmailScene } from './scene'
import { FPS, T_DONE, sentLabel } from './timeline'

/** The picture shown to people who asked for less motion: the car has gone and the words are up. */
const STILL_AT = T_DONE - 0.6
const STILL_HOLD_MS = 1500

interface Props {
  /** How many addresses the email went to, out of how many were tried. */
  sent: number
  total: number
  /** Called once, when the animation ends or is skipped. */
  onDone: () => void
}

/**
 * Covers the page for a few seconds when an email has gone out: an F1 car on NOS launches, speeds away leaving a trail
 * of smoke, and the words "EMAIL SENT" appear. Click, tap or press any key to skip it.
 */
export function EmailSent({ sent, total, onDone }: Props) {
  const canvas = useRef<HTMLCanvasElement>(null)
  const done = useRef(onDone)
  done.current = onDone
  const mode = useRef(currentCelebrationMode()).current
  const finished = useRef(false)
  const finish = useRef(() => {
    if (finished.current) return
    finished.current = true
    done.current()
  }).current
  const label = sentLabel(sent, total)

  useEffect(() => {
    if (mode === 'skip') {
      finish()
      return
    }
    const el = canvas.current
    const g = el?.getContext('2d')
    if (!el || !g) {
      finish()
      return
    }
    el.width = W
    el.height = H
    const scene = createEmailScene()

    // whole-number scaling, as large as the screen allows, so every art pixel stays a crisp square
    const fit = () => {
      const scale = Math.max(2, Math.min(6, Math.floor(Math.min((window.innerWidth * 0.94) / W, (window.innerHeight * 0.8) / H))))
      el.style.width = `${W * scale}px`
      el.style.height = `${H * scale}px`
    }
    fit()
    window.addEventListener('resize', fit)
    const skip = () => finish()
    window.addEventListener('keydown', skip)

    let raf = 0
    let timer: ReturnType<typeof setTimeout> | undefined
    if (mode === 'still') {
      scene.draw(g, STILL_AT, Math.floor(STILL_AT * FPS), label)
      timer = setTimeout(finish, STILL_HOLD_MS)
    } else {
      const start = performance.now()
      let last = -1
      const frame = () => {
        const t = (performance.now() - start) / 1000
        const tick = Math.floor(t * FPS)
        if (tick !== last) {
          last = tick
          scene.draw(g, Math.min(t, T_DONE), tick, label)
        }
        if (t >= T_DONE) finish()
        else raf = requestAnimationFrame(frame)
      }
      frame()
    }
    return () => {
      cancelAnimationFrame(raf)
      if (timer) clearTimeout(timer)
      window.removeEventListener('resize', fit)
      window.removeEventListener('keydown', skip)
    }
  }, [mode, label, finish])

  if (mode === 'skip') return null
  return (
    <div className="email-sent" role="status" aria-live="polite" onClick={() => finish()}>
      <canvas ref={canvas} aria-hidden="true" />
      <p className="visually-hidden">{sent >= total ? 'Email sent' : `Sent to ${sent} of ${total} addresses`}</p>
      <p className="email-sent-hint" aria-hidden="true">Press any key or tap to skip</p>
    </div>
  )
}
