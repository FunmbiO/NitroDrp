# Signing in with a passkey

Optional. A passkey replaces typing the access key on every device. You sign in with **Face ID, a fingerprint, or
your device PIN**, and the app behaves as **one app on all your devices**: the same "Recent links" list everywhere,
and any signed-in device can email, open or delete any of your links.

It is also safer than a stored key. A passkey never leaves the device and can't be phished (it only works on the
real site's address). The sign-in itself is an HttpOnly cookie that page scripts can't read, where the old access
key sat in the browser's storage.

## How it works for you

1. **First device:** open the site. It says "Set up this device". Type the access key **once** and give the device
   a name. The device creates a passkey (Face ID, fingerprint or PIN) and you're signed in.
2. **Your other devices:** if you use **iCloud Keychain** (Apple) or **Google Password Manager** (Android, Chrome),
   the passkey is already there. Open the site and tap **Sign in with a passkey**. Otherwise, on the new device
   choose "New device, or no passkey here? Set it up", type the access key once, and it gets its own passkey.
3. **Every day:** the app stays signed in for **90 days, renewed each time you use it**. If you go 90 days without
   opening it, you tap **Sign in with a passkey** again.

The **Devices** page (in the top menu once you're signed in) lists your passkeys, adds one for the device in your
hand, removes any of them, and signs this device out.

## The access key doesn't go away

It's the owner's master secret and recovery key:

- It's how you enrol the first device, and a new device that doesn't share your passkeys.
- If you lose every device, type it to set up a new one.
- Scripts such as `dev/smoke.py` can still create links with it.
- **Changing `API_KEY` signs every device out** (the sign-in cookie is signed with a key derived from it). Do that
  if you think a device was stolen. Devices then sign in again with their passkeys.
- A browser that can't use passkeys (or anyone who prefers it) has "Use the access key without a passkey" on the
  sign-in screen. It asks for the key and **checks it with the server first**: nothing opens until the server says
  it's right, and it isn't remembered, so a reload asks again. Then the upload page works the old way and sends the
  key with each link.

## Turn it on

Passkeys are tied to the site's address, so `PUBLIC_BASE_URL` must be your real `https` address:

```
PASSKEYS_ENABLED=true
PUBLIC_BASE_URL=https://drop.jadedmedia.ca
```

`./deploy/setup-env.sh` asks for both (answer **y**). Then rebuild: `docker compose --profile public up -d --build`.
The API refuses to start if passkeys are on and `PUBLIC_BASE_URL` isn't https, so a typo can't half-work.

No new AWS setup is needed: passkeys are stored in the same DynamoDB table. The IAM policy already allows it.

> **Changing the domain breaks existing passkeys.** A passkey belongs to one address. If you move the site to a new
> domain, devices need setting up again with the access key.

## Removing a device

On the Devices page press **Remove**. That device is signed out within a minute (usually at once), and its passkey
can no longer sign in. Removing the passkey you're using signs you out here.

## What it protects, and what it doesn't

- **Creating links, and listing, emailing and deleting them, needs the owner:** signed in, or holding the access
  key (creating, listing) or the link's own owner key (a link's owner actions). Recipients never sign in: a
  download link works for anyone who has it, as before.
- Everything that changes something also needs the `X-Requested-With: nitrodrp` header, and the cookie is
  `SameSite=Strict`, so another website can't make your browser act for you.
- Sign-in attempts are rate limited (10 a minute per visitor), and each one is single use and expires in five minutes.
- Anyone who has **both** your unlocked device and its Face ID/PIN is you, as with any passkey. Remove a lost
  device from the Devices page on another one.

## For developers

- Code: `api/src/NitroDrp.Api/Auth/` (passkeys via the open-source `Fido2` library, signed sessions) and
  `Endpoints/AuthEndpoints.cs`; web: `web/src/lib/auth.tsx`, `passkeys.ts`, `pages/SignInPage.tsx`, `DevicesPage.tsx`.
- Storage: `pk = "AUTH"`, `sk = "CRED#<id>"` for passkeys, and `pk = "OWNER"`, `sk = "S#<created>#<token>"` for the
  list of links (these rows expire with the link).
- Tests: the API tests include a software authenticator that signs real WebAuthn responses, so the server's actual
  verification runs, including forged signatures and a passkey made for a different site. `npm run e2e:passkeys`
  drives a real browser with a virtual authenticator against a real API (CI runs it).
- Local: in `docker-compose.dev.yml` passkeys are on for `http://localhost:8080`. For the Vite dev server
  (`http://localhost:5173`), set `NitroDrp__PublicBaseUrl=http://localhost:5173` or add it to
  `NitroDrp__Auth__ExtraOrigins__0`.
- Settings: `NitroDrp:Auth:Passkeys`, `SessionDays` (90), `MaxPasskeys` (20), `RpName`, `ExtraOrigins`.
