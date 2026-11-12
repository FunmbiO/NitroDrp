import { Link, Outlet, useLocation } from 'react-router-dom'
import { DEMO, EMBED } from '../lib/config'
import { useEmbedAutoHeight } from '../lib/embed'
import { useOnline } from '../lib/online'
import { Footer } from './Footer'
import { LogoMark } from './Icons'

export function Layout() {
  const { pathname } = useLocation()
  useEmbedAutoHeight()
  const online = useOnline()
  const isRecipient = pathname.startsWith('/d/')
  return (
    <>
      {!online && !EMBED && (
        <div className="offline-banner" role="status">
          You're offline. Links and downloads need a connection. Uploads pause and resume when you're back.
        </div>
      )}
      {DEMO && (
        <div className="demo-banner" role="note">
          <strong>Demo mode</strong> · nothing is uploaded. Your files never leave this page, and links only work in this browser.
        </div>
      )}
      {!EMBED && (
        <header className="header">
          <Link to="/" className="brand">
            <LogoMark />
            NitroDrp
          </Link>
          {!isRecipient && (
            <Link to="/recent" className="nav-link">
              Recent links
            </Link>
          )}
        </header>
      )}
      <Outlet />
      <Footer />
    </>
  )
}
