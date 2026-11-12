import { useEffect, useState } from 'react'

/**
 * For in-app loading screens. Once the loader has actually appeared (after `showAfter` ms of loading), keeps it up for
 * at least `minVisible` ms, even if the data arrives sooner, so it never flashes for a split second.
 *
 * Returns true while the loader should still be shown *after* loading has finished. Show it when
 * `loading || holding`.
 */
export function useMinimumLoading(loading: boolean, showAfter = 300, minVisible = 1400): boolean {
  const [holding, setHolding] = useState(false)
  const [shownAt, setShownAt] = useState<number | null>(null)

  // note the moment the loader becomes visible
  useEffect(() => {
    if (!loading || shownAt !== null) return
    const id = setTimeout(() => setShownAt(performance.now()), showAfter)
    return () => clearTimeout(id)
  }, [loading, shownAt, showAfter])

  // when loading ends, keep it up until it has been visible long enough
  useEffect(() => {
    if (loading) return
    if (shownAt === null) return
    const left = minVisible - (performance.now() - shownAt)
    if (left <= 0) {
      setShownAt(null)
      return
    }
    setHolding(true)
    const id = setTimeout(() => {
      setHolding(false)
      setShownAt(null)
    }, left)
    return () => clearTimeout(id)
  }, [loading, shownAt, minVisible])

  return holding
}
