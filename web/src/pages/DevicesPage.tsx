import { useCallback, useEffect, useState } from 'react'
import { api, ApiError, type PasskeySummary } from '../lib/api'
import { useAuth } from '../lib/auth'
import { PixelLoader } from '../loader/PixelLoader'
import { createPasskey, passkeyErrorMessage, passkeysSupported } from '../lib/passkeys'

const when = (iso: string | null) => (iso ? new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' }) : 'never')

export function DevicesPage() {
  const { refresh, signOut } = useAuth()
  const [keys, setKeys] = useState<PasskeySummary[] | null>(null)
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    try {
      setKeys(await api.passkeys())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load your devices.')
    }
  }, [])
  useEffect(() => {
    void load()
  }, [load])

  const add = async () => {
    setBusy(true)
    setError('')
    try {
      const begun = await api.registerBegin()
      await api.registerFinish(begun.flowId, name.trim() || 'My device', await createPasskey(begun.options))
      setName('')
      await load()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : passkeyErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  const remove = async (k: PasskeySummary) => {
    const warning = k.current ? 'This is the device you are using. Removing it signs you out here. Continue?' : `Remove "${k.label}"? It will be signed out.`
    if (!confirm(warning)) return
    try {
      await api.removePasskey(k.id)
      if (k.current) await refresh()
      else await load()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not remove it.')
    }
  }

  return (
    <main className="page narrow">
      <h1 className="sm">Your <mark>devices</mark></h1>
      <p className="muted" style={{ margin: 0 }}>Each device you sign in on has a passkey. Remove one and that device is signed out within a minute.</p>
      {error && <p className="error" role="alert">{error}</p>}

      <div className="card">
        {keys === null ? (
          <div className="pad"><PixelLoader label="Loading your devices" /></div>
        ) : (
          keys.map((k) => (
            <div className="recent-row" key={k.id}>
              <div className="stack" style={{ gap: 2 }}>
                <span><strong>{k.label}</strong>{k.current && <span className="tag">this device</span>}</span>
                <span className="meta">Added {when(k.createdAt)} · last used {when(k.lastUsedAt)}</span>
              </div>
              <button type="button" className="link-btn" onClick={() => void remove(k)}>Remove</button>
            </div>
          ))
        )}
      </div>

      {passkeysSupported() && (
        <div className="card roomy stack" style={{ gap: 14 }}>
          <h2 className="group-title" style={{ margin: 0 }}>Add this device</h2>
          <p className="muted" style={{ margin: 0 }}>Already signed in on another device? Passkeys from iCloud Keychain or Google Password Manager arrive on your other devices on their own. Add one here for a device that doesn't share yours.</p>
          <div className="field">
            <label htmlFor="dv-name">Name</label>
            <input id="dv-name" className="input" maxLength={40} placeholder="e.g. Work laptop" disabled={busy} value={name} onChange={(e) => setName(e.target.value)} />
          </div>
          <button type="button" className="btn" disabled={busy} onClick={() => void add()}>{busy ? 'Waiting for your device…' : 'Add a passkey on this device'}</button>
        </div>
      )}

      <div className="actions">
        <button type="button" className="link-btn" onClick={() => void signOut()}>Sign out of this device</button>
      </div>
    </main>
  )
}
