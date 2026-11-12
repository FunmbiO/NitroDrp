import { Navigate } from 'react-router-dom'
import { useAuth } from '../lib/auth'
import { PixelLoader } from '../loader/PixelLoader'
import { SignInPage } from './SignInPage'

/** The owner's way in. Not linked from the welcome page's main content; strangers can't do anything here either. */
export function OwnerPage() {
  const { status, signedIn, accessKeyMode } = useAuth()
  if (!status) return <main className="page narrow"><PixelLoader /></main>
  // already in, or passkeys are off on this server (the old way has no separate sign-in): carry on to the app
  if (signedIn || accessKeyMode || !status.passkeysEnabled) return <Navigate to="/" replace />
  return <SignInPage />
}
