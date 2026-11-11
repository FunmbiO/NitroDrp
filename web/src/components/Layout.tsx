import { Link, Outlet, useLocation } from 'react-router-dom'
import { LogoMark } from './Icons'

export function Layout() {
  const { pathname } = useLocation()
  const isRecipient = pathname.startsWith('/d/')
  return (
    <>
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
      <Outlet />
    </>
  )
}
