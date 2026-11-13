import { useCallback, useEffect, useState } from 'react'
import { api } from './api'

const CHANGED = 'nitrodrp:notifications-changed'

/** Tells the bell to recount (the notifications page calls this after marking things read or clearing them). */
export const notifyChanged = () => window.dispatchEvent(new Event(CHANGED))

/** How many unread notifications there are, for the bell. Rechecks every minute while the page is visible, and when told to. */
export function useUnread(enabled: boolean): number {
  const [unread, setUnread] = useState(0)
  const check = useCallback(() => {
    if (!enabled || document.visibilityState === 'hidden') return
    api.unreadCount().then(setUnread, () => {
      /* offline or signed out: keep the last number */
    })
  }, [enabled])

  useEffect(() => {
    check()
    const timer = setInterval(check, 60_000)
    addEventListener(CHANGED, check)
    document.addEventListener('visibilitychange', check)
    return () => {
      clearInterval(timer)
      removeEventListener(CHANGED, check)
      document.removeEventListener('visibilitychange', check)
    }
  }, [check])
  return enabled ? unread : 0
}
