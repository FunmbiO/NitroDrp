import { Link } from 'react-router-dom'
import { DEMO_URL, OWNER_NAME } from '../lib/config'
import { HeroScene } from '../loader/HeroScene'

/**
 * What everyone sees at the site's address unless they are the owner and signed in. There is nothing to sign in to for
 * anyone else, so there's no sign-in form here: just a friendly note and the demo. (The owner's sign-in is at /owner,
 * which isn't linked from anywhere prominent. That is tidiness, not security: the server refuses strangers anyway.)
 */
export function WelcomePage() {
  return (
    <main className="page narrow">
      <div className="card roomy stack welcome">
        <HeroScene always />
        <h1 className="sm">Hi, <mark>welcome</mark>!</h1>
        <p className="welcome-lede">
          It looks like you've found {OWNER_NAME}'s private file transfer app. It's for personal use, so there's nothing to
          sign in to here.
        </p>
        <p className="muted" style={{ margin: 0 }}>
          Curious how it works? Try the demo. Nothing you pick leaves your browser.
        </p>
        <a className="btn big" href={DEMO_URL}>See how it works (demo)</a>
        <p className="hint">
          Were you sent a download link? Open it from the message you got, or <Link to="/open">paste it here</Link>.
        </p>
      </div>
      <p className="owner-link">
        <Link to="/owner">Owner sign-in</Link>
      </p>
    </main>
  )
}
