/**
 * How long the first loading screen stays up. The animation takes a moment to get going (the first NOS bottle is
 * grabbed at 1.5 s, the ramp comes at about 2.8 s), so when the app is ready sooner than that, the screen waits.
 * Measured from when the page started loading, not from when the screen appeared: a slow load gets no extra wait.
 *
 * This is the one number to change if it feels too short or too long.
 */
export const SPLASH_MS = 3000

export interface SplashContext {
  /** The page's address query string, e.g. "?splash=500". */
  search: string
  /** True in automated browsers (tests), which shouldn't wait. */
  automated: boolean
  /** Shown inside another site's frame: don't hold up the host page. */
  embedded: boolean
  /** The person asked their device for less motion, so there's nothing worth waiting for. */
  reducedMotion: boolean
}

export function splashMs({ search, automated, embedded, reducedMotion }: SplashContext): number {
  const forced = new URLSearchParams(search).get('splash') // a way to try other lengths, and for tests
  if (forced !== null && /^\d{1,5}$/.test(forced)) return Number(forced)
  if (automated || embedded || reducedMotion) return 0
  return SPLASH_MS
}

/** Milliseconds still to wait, given how long the page has been loading. Never negative. */
export const splashRemaining = (total: number, sinceStart: number) => Math.max(0, total - sinceStart)

export function currentSplashMs(embedded: boolean): number {
  return splashMs({
    search: location.search,
    automated: navigator.webdriver === true,
    embedded,
    reducedMotion: typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches,
  })
}
