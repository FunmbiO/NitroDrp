import { Link, Outlet, useLocation } from 'react-router-dom'
import { DEMO, EMBED } from '../lib/config'
import { useEmbedAutoHeight } from '../lib/embed'
import { useOnline } from '../lib/online'
import { useAuth } from '../lib/auth'
import { Footer } from './Footer'
import { LogoMark } from './Icons'

export function Layout() {
  const { pathname } = useLocation()
  useEmbedAutoHeight()
  const online = useOnline()
  const { signedIn, status } = useAuth()
  const isRecipient = pathname.startsWith('/d/')
  return (
    <>
      {!online && !EMBED && (
        <div className="offline-banner" role="status">
          You're offline. Links and downloads need a connection. If an upload stops, press Resume upload when you're back.
        </div>
      )}
      {DEMO && (
        <div className="demo-banner" role="note">
          <strong>Demo mode</strong> · nothing is uploaded. Your files never leave this page, and links only work in this browser.
        </div>
      )}
      {!EMBED && (
        <header className="header">
          <Link to="/" className="brand" aria-label="NitroDrp, home">
            <LogoMark />
            <span className="wordmark" aria-hidden="true">NITRO<mark>DRP</mark></span>
          </Link>
          {!isRecipient && (signedIn || !status?.passkeysEnabled) && (
            <nav className="nav">
              <Link to="/recent" className="nav-link">
                Recent links
              </Link>
              <Link to="/open" className="nav-link">
                Open a link
              </Link>
              {signedIn && status?.passkeysEnabled && (
                <Link to="/devices" className="nav-link">
                  Devices
                </Link>
              )}
            </nav>
          )}
        </header>
      )}
      <Outlet />
      <Footer />
    </>
  )
}
