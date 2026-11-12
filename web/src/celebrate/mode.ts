export type CelebrationMode = 'full' | 'still' | 'skip'

export interface ModeContext {
  search: string
  /** An automated browser (tests): don't hold things up unless asked to. */
  automated: boolean
  reducedMotion: boolean
}

/**
 * How to show the "email sent" moment:
 * - full: the whole animation.
 * - still: one picture with the words, for people who asked their device for less motion.
 * - skip: nothing. Automated browsers, so tests stay quick; `?celebrate=1` on the address turns it back on.
 */
export function celebrationMode({ search, automated, reducedMotion }: ModeContext): CelebrationMode {
  const forced = new URLSearchParams(search).get('celebrate') === '1'
  if (automated && !forced) return 'skip'
  return reducedMotion ? 'still' : 'full'
}

export const currentCelebrationMode = (): CelebrationMode =>
  celebrationMode({
    search: location.search,
    automated: navigator.webdriver === true,
    reducedMotion: typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches,
  })
