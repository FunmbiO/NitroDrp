/**
 * Push notifications on this device: the browser side. The browser makes a subscription (a private address at its
 * maker's push service); the server is told about it and uses it to send short, generic messages.
 */

export interface DeviceSubscription { endpoint: string; p256dh: string; auth: string }

/** Can this browser do push at all? (On an iPhone only once the app is added to the Home Screen.) */
export function pushSupported(): boolean {
  return typeof navigator !== 'undefined' && 'serviceWorker' in navigator && typeof window !== 'undefined' && 'PushManager' in window && 'Notification' in window
}

/** An iPhone or iPad where push needs the app installed first: in Safari, not yet on the Home Screen. */
export function needsInstall(): boolean {
  if (typeof navigator === 'undefined') return false
  const ios = /iPhone|iPad|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1)
  if (!ios) return false
  const standalone = (navigator as unknown as { standalone?: boolean }).standalone === true || matchMedia('(display-mode: standalone)').matches
  return !standalone
}

/** The browser wants the server's key as bytes, but it travels as base64url text. */
export function urlBase64ToBytes(b64: string): Uint8Array<ArrayBuffer> {
  const pad = '='.repeat((4 - (b64.length % 4)) % 4)
  const raw = atob((b64 + pad).replace(/-/g, '+').replace(/_/g, '/'))
  const out = new Uint8Array(new ArrayBuffer(raw.length))
  for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i)
  return out
}

export function bytesToUrlBase64(buf: ArrayBuffer | Uint8Array): string {
  const bytes = buf instanceof Uint8Array ? buf : new Uint8Array(buf)
  let s = ''
  for (const b of bytes) s += String.fromCharCode(b)
  return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

/** A friendly default name for the device list, from what the browser says it is. */
export function deviceLabel(ua: string): string {
  const where = /iPhone/.test(ua) ? 'iPhone' : /iPad/.test(ua) ? 'iPad' : /Android/.test(ua) ? (/Mobile/.test(ua) ? 'Android phone' : 'Android tablet')
    : /Windows/.test(ua) ? 'Windows PC' : /Macintosh|Mac OS X/.test(ua) ? 'Mac' : /Linux|CrOS/.test(ua) ? 'Linux PC' : 'This device'
  return where
}

export type PushPermission = 'granted' | 'denied' | 'default' | 'unsupported'

export function pushPermission(): PushPermission {
  return pushSupported() ? Notification.permission : 'unsupported'
}

function toSubscription(sub: PushSubscription): DeviceSubscription {
  const j = sub.toJSON()
  if (!j.endpoint || !j.keys?.p256dh || !j.keys?.auth) throw new Error('The browser gave back an incomplete subscription.')
  return { endpoint: j.endpoint, p256dh: j.keys.p256dh, auth: j.keys.auth }
}

/** This browser's current subscription, or null if it has none. */
export async function currentSubscription(): Promise<DeviceSubscription | null> {
  if (!pushSupported()) return null
  const reg = await navigator.serviceWorker.getRegistration()
  const sub = await reg?.pushManager.getSubscription()
  return sub ? toSubscription(sub) : null
}

/** Asks permission (the browser shows its own prompt) and subscribes. Throws a plain message when it can't. */
export async function subscribeThisDevice(publicKey: string): Promise<DeviceSubscription> {
  if (!pushSupported()) throw new Error("This browser can't receive push notifications.")
  const permission = await Notification.requestPermission()
  if (permission !== 'granted') throw new Error('Notifications are blocked for this site. Allow them in your browser settings, then try again.')
  const reg = await navigator.serviceWorker.ready
  const existing = await reg.pushManager.getSubscription()
  const sub = existing ?? (await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: urlBase64ToBytes(publicKey) }))
  return toSubscription(sub)
}

/** Stops this browser receiving pushes (the server is told separately). */
export async function unsubscribeThisDevice(): Promise<void> {
  if (!pushSupported()) return
  const reg = await navigator.serviceWorker.getRegistration()
  const sub = await reg?.pushManager.getSubscription()
  await sub?.unsubscribe()
}

/** The id the server gives a device: the first 32 hex characters of the SHA-256 of its endpoint. Lets the page spot "this device" in the list. */
export async function deviceIdFor(endpoint: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(endpoint))
  return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, '0')).join('').slice(0, 32)
}
