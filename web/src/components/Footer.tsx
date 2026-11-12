import logo from '../assets/jadedmedia-logo.png'

const SITE = 'https://jadedmedia.ca'

/** Credit line on every page: who runs this instance, and what it's used for. */
export function Footer() {
  return (
    <footer className="site-footer">
      {/* The logo repeats the text link beside it, so it's hidden from keyboards and screen readers. */}
      <a className="footer-logo" href={SITE} target="_blank" rel="noopener" tabIndex={-1} aria-hidden="true">
        <img src={logo} alt="" width="56" height="28" />
      </a>
      <p>
        Powered by{' '}
        <a href={SITE} target="_blank" rel="noopener">
          jadedmedia.ca
        </a>
        , used for personal and customer files
      </p>
    </footer>
  )
}
