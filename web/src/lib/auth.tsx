import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, type AuthStatus } from './api'

interface AuthState {
  /** Null until the first answer arrives. */
  status: AuthStatus | null
  /** The server couldn't be reached to ask. */
  failed: boolean
  /** Signed in with a passkey (or running the demo, where everyone owns their own browser's links). */
  signedIn: boolean
  /** The person chose to use the access key on this device instead of a passkey. */
  accessKeyMode: boolean
  useAccessKey: () => void
  refresh: () => Promise<void>
  signOut: () => Promise<void>
}

const Ctx = createContext<AuthState | null>(null)
const KEY_MODE = 'nitrodrp.accessKeyMode'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus | null>(null)
  const [failed, setFailed] = useState(false)
  const [accessKeyMode, setMode] = useState(() => {
    try {
      return sessionStorage.getItem(KEY_MODE) === '1'
    } catch {
      return false
    }
  })

  const refresh = useCallback(async () => {
    try {
      setStatus(await api.authStatus())
      setFailed(false)
    } catch {
      setFailed(true)
    }
  }, [])
  useEffect(() => {
    void refresh()
  }, [refresh])

  const value = useMemo<AuthState>(
    () => ({
      status,
      failed,
      signedIn: !!status?.signedIn,
      accessKeyMode,
      useAccessKey: () => {
        try {
          sessionStorage.setItem(KEY_MODE, '1')
        } catch {
          /* fine: it just lasts until the next reload */
        }
        setMode(true)
      },
      refresh,
      signOut: async () => {
        await api.logout().catch(() => {})
        await refresh()
      },
    }),
    [status, failed, accessKeyMode, refresh],
  )
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

export function useAuth(): AuthState {
  const v = useContext(Ctx)
  if (!v) throw new Error('useAuth needs <AuthProvider>')
  return v
}
