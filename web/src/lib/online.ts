import { useSyncExternalStore } from 'react'

const subscribe = (notify: () => void) => {
  addEventListener('online', notify)
  addEventListener('offline', notify)
  return () => {
    removeEventListener('online', notify)
    removeEventListener('offline', notify)
  }
}

/** True while the device has a connection. (It can be wrong on a captive Wi-Fi, so it only drives a notice.) */
export const useOnline = () => useSyncExternalStore(subscribe, () => navigator.onLine, () => true)
