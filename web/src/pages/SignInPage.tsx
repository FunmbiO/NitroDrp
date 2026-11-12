import { useState } from 'react'
import { api, ApiError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { MAX_SECRET } from '../lib/validate'
import { createPasskey, getPasskey, passkeyErrorMessage, passkeysSupported } from '../lib/passkeys'

/** A sensible default name for the passkey, so the device list reads "Amina's iPhone"-style, not "Device 3". */
function guessDeviceName() {
  const ua = navigator.userAgent
  if (/iPhone/.test(ua)) return 'iPhone'
  if (/iPad/.test(ua)) return 'iPad'
  if (/Android/.test(ua)) return 'Android phone'
  if (/Macintosh/.test(ua)) return 'Mac'
  if (/Windows/.test(ua)) return 'Windows PC'
  if (/Linux/.test(ua)) return 'Linux PC'
  return 'My device'
}

export function SignInPage() {
  const { status, refresh, useAccessKey } = useAuth()
  const supported = passkeysSupported()
  const hasPasskeys = !!status?.hasPasskeys
  const [showSetup, setShowSetup] = useState(!hasPasskeys)
  const [accessKey, setAccessKey] = useState('')
  const [name, setName] = useState(guessDeviceName)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const run = async (job: () => Promise<void>) => {
    setBusy(true)
    setError('')
    try {
      await job()
      await refresh()
    } catch (e) {
      setError(e instanceof ApiError ? (e.code === 'sign_in_required' ? "That access key isn't right." : e.message) : passkeyErrorMessage(e))
      setBusy(false)
    }
  }

  const signIn = () =>
    run(async () => {
      const begun = await api.loginBegin()
      await api.loginFinish(begun.flowId, await getPasskey(begun.options))
    })

  const setUp = () =>
    run(async () => {
      const key = accessKey.trim()
      const begun = await api.registerBegin(key)
      await api.registerFinish(begun.flowId, name.trim() || guessDeviceName(), await createPasskey(begun.options), key)
    })

  return (
    <main className="page narrow">
      <div className="card roomy stack">
        <h1 className="sm">{showSetup ? <>Set up <mark>this device</mark></> : <>Welcome <mark>back</mark></>}</h1>

        {!supported && (
          <p className="error" role="alert">
            This browser can't use passkeys here. Open the site with https in an up-to-date Safari, Chrome, Edge or Firefox, or use the access key below.
          </p>
        )}

        {supported && hasPasskeys && !showSetup && (
          <>
            <p className="muted" style={{ margin: 0 }}>Use Face ID, your fingerprint or your device PIN. No access key needed.</p>
            <button type="button" className="btn big" disabled={busy} onClick={signIn}>{busy ? 'Waiting for your device…' : 'Sign in with a passkey'}</button>
            <button type="button" className="link-btn" onClick={() => setShowSetup(true)}>New device, or no passkey here? Set it up</button>
          </>
        )}

        {supported && showSetup && (
          <form
            className="stack"
            onSubmit={(e) => {
              e.preventDefault()
              void setUp()
            }}
          >
            <p className="muted" style={{ margin: 0 }}>
              Type the access key <strong>once</strong>, and this device saves a passkey. After that you sign in with Face ID or your fingerprint. If you use iCloud Keychain or Google Password Manager, your other devices get it too.
            </p>
            <div className="field">
              <label htmlFor="si-key">Access key</label>
              <input id="si-key" className="input" type="password" autoComplete="current-password" maxLength={MAX_SECRET} disabled={busy} value={accessKey} onChange={(e) => setAccessKey(e.target.value)} />
            </div>
            <div className="field">
              <label htmlFor="si-name">Name this device</label>
              <input id="si-name" className="input" maxLength={40} disabled={busy} value={name} onChange={(e) => setName(e.target.value)} />
            </div>
            <button type="submit" className="btn big" disabled={busy || accessKey.trim().length === 0}>{busy ? 'Waiting for your device…' : 'Set up this device'}</button>
            {hasPasskeys && <button type="button" className="link-btn" onClick={() => setShowSetup(false)}>Back to sign in</button>}
          </form>
        )}

        {error && <p className="error" role="alert">{error}</p>}

        <button type="button" className="link-btn muted" onClick={useAccessKey}>Use the access key without a passkey</button>
      </div>
    </main>
  )
}
