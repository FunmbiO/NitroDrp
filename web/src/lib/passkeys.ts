import type { WebAuthnJson } from './api'

/**
 * Passkeys through the browser's own WebAuthn API. The server speaks JSON and the browser can read that JSON
 * directly (PublicKeyCredential.parse...FromJSON), so no library is needed. Any modern iPhone, Android, Mac or PC
 * browser has it; the page must be https (or localhost).
 */
export const passkeysSupported = () =>
  typeof window !== 'undefined' &&
  window.isSecureContext &&
  typeof PublicKeyCredential !== 'undefined' &&
  typeof PublicKeyCredential.parseCreationOptionsFromJSON === 'function'

/** The server's options carry some fields set to null; the browser's parser wants them absent. */
function withoutNulls(v: unknown): unknown {
  if (Array.isArray(v)) return v.map(withoutNulls)
  if (v && typeof v === 'object')
    return Object.fromEntries(Object.entries(v).filter(([, x]) => x !== null && x !== undefined).map(([k, x]) => [k, withoutNulls(x)]))
  return v
}

/** Asks the device to make a new passkey (Face ID, fingerprint, PIN). Returns what the server needs to save it. */
export async function createPasskey(options: WebAuthnJson): Promise<WebAuthnJson> {
  const publicKey = PublicKeyCredential.parseCreationOptionsFromJSON(withoutNulls(options) as never)
  const credential = (await navigator.credentials.create({ publicKey })) as PublicKeyCredential | null
  if (!credential) throw new Error('No passkey was created.')
  return credential.toJSON() as unknown as WebAuthnJson
}

/** Asks the device to sign in with a passkey it already holds for this site. */
export async function getPasskey(options: WebAuthnJson): Promise<WebAuthnJson> {
  const publicKey = PublicKeyCredential.parseRequestOptionsFromJSON(withoutNulls(options) as never)
  const credential = (await navigator.credentials.get({ publicKey })) as PublicKeyCredential | null
  if (!credential) throw new Error('No passkey was chosen.')
  return credential.toJSON() as unknown as WebAuthnJson
}

/** Plain-English text for what can go wrong in the browser's passkey prompt. */
export function passkeyErrorMessage(e: unknown): string {
  const name = e instanceof DOMException ? e.name : ''
  if (name === 'NotAllowedError') return 'Cancelled, or it took too long. Try again.'
  if (name === 'InvalidStateError') return 'This device already has a passkey for NitroDrp.'
  if (name === 'NotSupportedError' || name === 'SecurityError') return "This browser or address can't use passkeys."
  return e instanceof Error ? e.message : 'Something went wrong.'
}
