import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, type AuthStatus } from './api'
import { setApiKey } from './storage'

interface AuthState {
  /** Null until the first answer arrives. */
  status: AuthStatus | null
  /** The server couldn't be reached to ask. */
  failed: boolean
  /** Signed in with a passkey (or running the demo, where everyone owns their own browser's links). */
  signedIn: boolean
  /**
   * Using the access key on this device instead of a passkey. Only ever true after the server has confirmed the key,
   * and never remembered: a reload asks again.
   */
  accessKeyMode: boolean
  /** Checks the key with the server. Resolves and switches the mode on if it's right; throws if it isn't. */
  enterWithAccessKey: (key: string) => Promise<void>
  refresh: () => Promise<void>
  signOut: () => Promise<void>
}

const Ctx = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus | null>(null)
  const [failed, setFailed] = useState(false)
  const [accessKeyMode, setMode] = useState(false)

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
      enterWithAccessKey: async (key: string) => {
        await api.verifyAccessKey(key.trim()) // throws unless the server says it's right
        setApiKey(key.trim()) // the upload page sends it with each link, as it always has in this mode
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
