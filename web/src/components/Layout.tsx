import { Link, Outlet, useLocation } from 'react-router-dom'
import { DEMO, EMBED } from '../lib/config'
import { useEmbedAutoHeight } from '../lib/embed'
import { LogoMark } from './Icons'

export function Layout() {
  const { pathname } = useLocation()
  useEmbedAutoHeight()
  const isRecipient = pathname.startsWith('/d/')
  return (
    <>
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
    </>
  )
}
