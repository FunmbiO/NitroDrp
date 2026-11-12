import { Outlet } from 'react-router-dom'
import { useAuth } from '../lib/auth'
import { SignInPage } from '../pages/SignInPage'
import { PixelLoader } from '../loader/PixelLoader'

/** The sender's pages. Recipients' download links are never behind this. */
export function RequireSignIn() {
  const { status, failed, signedIn, accessKeyMode, refresh } = useAuth()

  if (!status) {
    return (
      <main className="page narrow">
        {failed ? (
          <div className="card roomy stack">
            <h1 className="sm">Can't reach the server</h1>
            <p className="muted" style={{ margin: 0 }}>Check your connection, then try again.</p>
            <button type="button" className="btn" onClick={() => void refresh()}>Try again</button>
          </div>
        ) : (
          <PixelLoader />
        )}
      </main>
    )
  }
  // Passkeys are off on this server: the old way (the page asks for the access key) is the only way.
  if (!status.passkeysEnabled || signedIn || accessKeyMode) return <Outlet />
  return <SignInPage />
}
