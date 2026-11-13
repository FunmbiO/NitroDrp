import { useCallback, useEffect, useState } from 'react'
import { api, type NotificationDevice, type NotificationKind, type NotificationsView } from '../lib/api'
import { DEMO } from '../lib/config'
import { formatAgo } from '../lib/format'
import { currentSubscription, deviceIdFor, deviceLabel, needsInstall, pushPermission, pushSupported, subscribeThisDevice, unsubscribeThisDevice } from '../lib/push'
import { notifyChanged } from '../lib/unread'
import { PixelLoader } from '../loader/PixelLoader'
import { useMinimumLoading } from '../loader/useMinimumLoading'

const KIND_LABEL: Record<NotificationKind, string> = {
  downloaded: 'Someone downloads a link',
  limit_reached: "A link's downloads run out",
  expiring_soon: 'A link is about to expire',
  email_failed: "An email doesn't send",
}
const DEMO_DEVICE = 'demo-this-browser'

export function NotificationsPage() {
  const [view, setView] = useState<NotificationsView | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [thisDevice, setThisDevice] = useState<string | null>(null)
  const [message, setMessage] = useState('')
  const holdLoader = useMinimumLoading(view === null)

  const load = useCallback(async () => {
    try {
      const v = await api.notifications()
      setView(v)
      // Which of the listed devices is this one? In the demo there is only ever this browser.
      if (DEMO) setThisDevice(DEMO_DEVICE)
      else {
        const sub = await currentSubscription().catch(() => null)
        setThisDevice(sub ? await deviceIdFor(sub.endpoint) : null)
      }
      return v
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load your notifications.')
      return null
    }
  }, [])

  // Opening the page counts as reading: the numbers in this view stay highlighted, the bell clears.
  useEffect(() => {
    void load().then((v) => {
      if (v && v.unread > 0) void api.markNotificationsRead().then(notifyChanged, () => {})
    })
  }, [load])

  const run = async (fn: () => Promise<unknown>, done?: string) => {
    setBusy(true)
    setError('')
    setMessage('')
    try {
      await fn()
      if (done) setMessage(done)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  const clear = () => run(async () => {
    await api.clearNotifications()
    await load()
    notifyChanged()
  })

  const enable = () => run(async () => {
    if (!view) return
    if (DEMO) {
      await api.addDevice({ endpoint: DEMO_DEVICE, p256dh: 'demo', auth: 'demo', label: deviceLabel(navigator.userAgent), kinds: view.kinds })
    } else {
      if (!view.publicKey) throw new Error("Push isn't set up on this server.")
      const sub = await subscribeThisDevice(view.publicKey)
      await api.addDevice({ ...sub, label: deviceLabel(navigator.userAgent), kinds: view.kinds })
    }
    await load()
  }, DEMO ? 'Turned on in the demo. Nothing is really sent.' : 'Notifications are on for this device.')

  const remove = (d: NotificationDevice) => run(async () => {
    if (!confirm(`Stop notifications to "${d.label}"?`)) return
    await api.removeDevice(d.id)
    if (d.id === thisDevice && !DEMO) await unsubscribeThisDevice()
    await load()
  })

  const test = (d: NotificationDevice) => run(async () => {
    const r = await api.testDevice(d.id)
    if (r.gone) {
      await load()
      throw new Error('That device is no longer reachable, so it was removed. Turn notifications on again there.')
    }
    if (!r.sent) throw new Error("The push service didn't accept the message. Try again in a minute.")
  }, DEMO ? 'Test sent (simulated).' : 'Test sent. It should appear on that device in a few seconds.')

  // The box flips at once and is put back if the server says no, so it never feels slow.
  const toggleKind = (d: NotificationDevice, kind: NotificationKind) => {
    const next = d.kinds.includes(kind) ? d.kinds.filter((k) => k !== kind) : [...d.kinds, kind]
    if (next.length === 0) {
      setError('Pick at least one thing to be told about, or remove the device.')
      return
    }
    setError('')
    setView((v) => v && { ...v, devices: v.devices.map((x) => (x.id === d.id ? { ...x, kinds: next } : x)) })
    void api.setDeviceKinds(d.id, next).catch((e) => {
      setError(e instanceof Error ? e.message : 'Could not save that.')
      void load()
    })
  }

  const toggleDigest = (enabled: boolean) => {
    setError('')
    setView((v) => v && { ...v, digest: { ...v.digest, enabled } })
    void api.setDigest(enabled).catch((e) => {
      setError(e instanceof Error ? e.message : 'Could not save that.')
      void load()
    })
  }

  const deviceCard = () => {
    if (!view) return null
    const mine = view.devices.some((d) => d.id === thisDevice)
    if (!view.pushAvailable) {
      return <p className="muted" style={{ margin: 0 }}>Phone notifications aren't set up on this server yet. The list above always works. To get them on your phone, follow the steps in <code>docs/NOTIFICATIONS.md</code>.</p>
    }
    if (mine) return <p className="muted" style={{ margin: 0 }}>This device is on the list below.</p>
    if (!DEMO && needsInstall()) {
      return <p className="muted" style={{ margin: 0 }}>On an iPhone, notifications work once NitroDrp is on your Home Screen. In Safari tap Share, then <strong>Add to Home Screen</strong>, open it from there, and come back to this page.</p>
    }
    if (!DEMO && !pushSupported()) return <p className="muted" style={{ margin: 0 }}>This browser can't receive push notifications. Try Chrome, Edge, Firefox or Safari.</p>
    if (!DEMO && pushPermission() === 'denied') return <p className="muted" style={{ margin: 0 }}>Notifications are blocked for this site. Allow them in your browser's site settings, then reload this page.</p>
    return (
      <div className="stack" style={{ gap: 12 }}>
        <p className="muted" style={{ margin: 0 }}>Get a short message on this device when something happens, even when NitroDrp is closed. It never includes file names or email addresses.</p>
        <button type="button" className="btn" disabled={busy} onClick={() => void enable()}>Turn on notifications on this device</button>
      </div>
    )
  }

  return (
    <main className="page narrow">
      <h1 className="sm"><mark>Notifications</mark></h1>
      <p className="muted" style={{ margin: 0 }}>What's happened to your links. Messages never name your files or the people you sent them to.{DEMO && ' In this demo, everything is simulated in your browser.'}</p>
      {error && <p className="error" role="alert">{error}</p>}
      {message && <p className="muted" role="status" style={{ margin: 0, color: 'var(--lime)' }}>{message}</p>}

      <div className="card" aria-label="Recent activity">
        {view === null || holdLoader ? (
          <div style={{ padding: 24 }}><PixelLoader label="Loading notifications" /></div>
        ) : view.items.length === 0 ? (
          <div style={{ padding: 24 }}><p className="muted" style={{ margin: 0 }}>Nothing yet. You'll see a note here when someone downloads a link, a link runs out or is about to expire, or an email doesn't go through.</p></div>
        ) : (
          view.items.map((n) => (
            <div className={`note-row${n.unread ? ' unread' : ''}`} key={n.id} data-kind={n.kind}>
              <span className="note-dot" aria-hidden="true" />
              <div className="stack" style={{ gap: 2 }}>
                <strong>{n.title}</strong>
                <span className="muted">{n.body}</span>
                <span className="meta">{formatAgo(n.at)}</span>
              </div>
            </div>
          ))
        )}
      </div>
      {view && view.items.length > 0 && (
        <div className="actions">
          <button type="button" className="link-btn" disabled={busy} onClick={() => void clear()}>Clear all</button>
        </div>
      )}

      {view && (
        <div className="card roomy stack" style={{ gap: 18 }}>
          <h2 className="group-title" style={{ margin: 0 }}>On your phone and other devices</h2>
          {deviceCard()}
          {view.devices.length > 0 && (
            <div className="card" style={{ boxShadow: 'none' }}>
              {view.devices.map((d) => (
                <div className="device-row" key={d.id}>
                  <span><strong>{d.label}</strong>{d.id === thisDevice && <span className="tag">this device</span>}</span>
                  <span className="meta">{d.lastSentAt ? `Last message ${formatAgo(d.lastSentAt)}` : 'No messages sent yet'}</span>
                  <fieldset className="kinds" style={{ border: 0, padding: 0, margin: 0 }}>
                    <legend className="meta" style={{ padding: 0 }}>Tell me when…</legend>
                    {view.kinds.map((k) => (
                      <label className="check" key={k}>
                        <input type="checkbox" checked={d.kinds.includes(k)} onChange={() => toggleKind(d, k)} />
                        <span>{KIND_LABEL[k]}</span>
                      </label>
                    ))}
                  </fieldset>
                  <div className="row-actions">
                    <button type="button" className="link-btn" disabled={busy} onClick={() => void test(d)}>Send a test</button>
                    <button type="button" className="link-btn" disabled={busy} onClick={() => void remove(d)}>Remove</button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {view?.digest.available && (
        <div className="card roomy stack" style={{ gap: 14 }}>
          <h2 className="group-title" style={{ margin: 0 }}>Daily email summary</h2>
          <label className="check">
            <input type="checkbox" checked={view.digest.enabled} onChange={(e) => toggleDigest(e.target.checked)} />
            <span>Email me a short summary once a day{DEMO ? ' (simulated)' : ''}</span>
          </label>
          <p className="muted" style={{ margin: 0 }}>Only on days something happened. It has counts and a link to this page, never file names.</p>
        </div>
      )}
    </main>
  )
}
