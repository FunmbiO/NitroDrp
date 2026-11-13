# Notifications

NitroDrp can tell you when something happens to one of your links, in three ways. Each one is separate, and only the
first needs nothing set up.

| | What it is | Set up |
|---|---|---|
| **The bell** | A list inside the app, with a badge for what's new. | Nothing. It's on. |
| **Phone notifications** | A short message on your phone or laptop, even when NitroDrp is closed. | Make a key pair once (5 minutes). |
| **Daily email summary** | One email a day with counts, only on days something happened. | An address, and email already set up ([Email](EMAIL.md)). |

**What you're told about** (you choose, per device, for phone notifications):

- **Someone downloads a link.**
- **A link runs out of downloads** (you set a limit and it was used up).
- **A link is about to expire** (within 24 hours; you're told once per link).
- **An email with a link didn't send** (the email service refused an address).

**What a message never contains:** file names, the link, the password, or the people you emailed. A message says things
like "Someone downloaded one of your links." Phone messages travel through Apple's or Google's servers, so they are
deliberately generic. Open the app for anything more.

---

## Step by step

### Part 1: The bell (2 minutes)

1. Get the latest NitroDrp onto your server. On the server, in the NitroDrp folder:

   ```bash
   git pull
   docker compose --profile public up -d --build
   ```

   (Use whichever profile you normally start it with, `public` for Caddy, `tunnel` for Cloudflare.)

2. Open your site and sign in as usual.
3. Look in the top right of the header: there's a **bell** next to *Recent links* and *Open a link*.
4. Tap the bell. This is the notifications page. Nothing yet? That's normal. The next time someone downloads a link you'll see a
   red number on the bell.
5. Try it: make a link with a **download limit of 1**, open it in a private window, and download. Back in your app the
   bell now shows **2**: "Link downloaded" and "Download limit reached". Opening the page marks them read, so the number goes away.

The list keeps 30 days. **Clear all** empties it.

### Part 2: Notifications on your phone (10 minutes, once)

You need the site on **https** with passkeys on (this is already true if you followed [the Lightsail guide](LIGHTSAIL.md)). Phones only allow notifications from secure sites.

**A. Make the key pair on your server.** It's what lets your server prove to Apple and Google that the messages are really from it:

```bash
./deploy/make-vapid.sh
```

It prints two lines:

```
PUSH_PUBLIC_KEY=B...
PUSH_PRIVATE_KEY=...
```

**B. Put them in `.env`.** Open `.env` in an editor and find the commented-out *notifications* block. Remove the `#` from
the lines and paste the two values, then add a contact for the push services (your email as `mailto:`, or your site address):

```
PUSH_PUBLIC_KEY=B...
PUSH_PRIVATE_KEY=...
PUSH_SUBJECT=mailto:you@example.com
```

The private key is a secret, like `API_KEY`. Never put it in the app, in chat, or in git. (`.env` is already ignored by git.)

> Prefer questions to editing? `./deploy/setup-env.sh` asks about all of this, but it writes a **new** `.env` with a **new access key**. Only use it on a fresh install.

**C. Restart the app so it picks them up:**

```bash
docker compose --profile public up -d --build
```

If the keys are wrong, the app refuses to start and says so in `docker compose logs api`. Check the notifications page: the *"On your phone and other devices"* card now offers a button instead of saying push isn't set up.

**D. On your phone.**

- **iPhone or iPad** (iOS 16.4 or newer): notifications only work for apps on the Home Screen.
  1. Open your NitroDrp address in **Safari**.
  2. Tap **Share**, then **Add to Home Screen**, then **Add**.
  3. Open NitroDrp **from the new icon** (not from Safari). Sign in with Face ID.
- **Android**: open the address in Chrome and sign in. Installing it to the Home Screen is optional.
- **A laptop**: Chrome, Edge, Firefox and Safari all work. Just open the site and sign in.

**E. Turn it on for that device.**

1. Tap the **bell**.
2. In the *"On your phone and other devices"* card, tap **Turn on notifications on this device**.
3. Your phone asks *"NitroDrp would like to send you notifications"*. Tap **Allow**.
4. The device appears in the list, marked **this device**.

**F. Choose what you hear about.** Under the device, tick the things you want. All four are on to start. Untick the ones that would be noise.

**G. Send a test.** Tap **Send a test**. A message saying *"Notifications are working on this device"* should arrive within a few seconds.
Now do the real thing from Part 1, step 5, and your phone will buzz.

Do the same on each phone or laptop you want notified: every device has its own entry, its own choices and its own **Remove**.

### Part 3: The daily email summary (optional)

This needs email to be on already ([Email](EMAIL.md)). Add where it should go, to `.env`:

```
DIGEST_EMAIL_TO=you@example.com
DIGEST_HOUR_UTC=13
```

Restart (`docker compose --profile public up -d --build`), open the bell page, and tick **Email me a short summary once a day**. It goes out
at that hour (UTC; 13 is about 8 or 9 in the morning in Toronto), and **only if something happened** in the last 24 hours. It lists counts, not names.
If you only want the app's own list and phone messages, leave `DIGEST_EMAIL_TO` empty and the checkbox won't appear.

---

## Settings

All optional, in `.env` (see `.env.example`):

| Setting | What it does |
|---|---|
| `PUSH_PUBLIC_KEY`, `PUSH_PRIVATE_KEY` | The key pair from `./deploy/make-vapid.sh`. Both, or neither (no phone notifications). |
| `PUSH_SUBJECT` | A contact for the push services: `mailto:you@example.com` or `https://your.site`. Required with the keys. |
| `DIGEST_EMAIL_TO` | Where the daily summary goes. Needs email set up. |
| `DIGEST_HOUR_UTC` | Hour of the day (0 to 23, UTC) it's sent. Default 13. |

Behind those, the server also has `NitroDrp__Notifications__ExpiringHours` (default 24), `RetentionDays` (30) and `MaxDevices` (10) if you ever want them different.

## When something doesn't work

| What you see | What to do |
|---|---|
| The card says *"Phone notifications aren't set up on this server yet."* | Part 2, B and C: the keys aren't in `.env`, or you haven't restarted. |
| The app won't start after adding keys | `docker compose logs api` says which part is wrong: a key pair that doesn't match (make a new one), or a missing `PUSH_SUBJECT`. |
| On iPhone there's no button, only a note about Home Screen | You're in Safari. Add to Home Screen and open it from the icon (Part 2, D). |
| *"Notifications are blocked for this site"* | You tapped *Don't allow* once. iPhone: Settings, Notifications, NitroDrp. Android: long-press the icon, Site settings. Desktop: the padlock in the address bar. Then reload. |
| The test says the device *"is no longer reachable, so it was removed"* | The phone's subscription expired (reinstalling the app or clearing site data does this). Turn it on again on that phone. |
| The test says the push service *"didn't accept the message"* | Usually temporary. Try again in a minute. If it keeps happening, check the server can reach the internet (`docker compose logs api`). |
| The test works but real events don't arrive | Check the device's ticks: that kind may be off. Also check the bell: if the event is listed there, the server saw it and the phone's settings (Focus modes, battery saver) are holding the message. |
| Nothing shows in the bell for a download | The list refreshes every minute and when you open the page. Reload it. |
| You made a **new** key pair | Every device has to turn notifications on again: the old subscriptions were tied to the old key. Remove each device on the page first, then add it back. |

## Privacy and security

- **Only you can see or change any of it.** Every notification route needs your passkey sign-in (or the access key), like creating a link does. Strangers get a plain "sign in" refusal, and the test suite checks every route.
- **Messages are generic** and carry nothing about your files or recipients (see above). The daily email likewise.
- **Messages are encrypted** to the device (RFC 8291) and signed with your server's key (RFC 8292). Apple's and Google's push servers carry them but can't read them.
- **The server won't call arbitrary addresses.** A device is registered by the address the browser gives (at Apple's, Google's, Mozilla's or Microsoft's push service). The server accepts only those, over https, so a registration can never make it contact an internal address or another site.
- **A removed device is gone** from the server immediately. A phone that uninstalled the app is forgotten the next time a push to it is refused.
- **Keys:** the private key stays on your server. The public key is meant to be public.
- **No extra library:** the encryption uses .NET's own cryptography, and is checked against the example in the standard itself.

## How it works

1. When something happens (a download, an email that failed), the API records a small event in DynamoDB (the same table as everything else; it deletes itself after 30 days) and puts it on a queue, so a download never waits.
2. A background worker sends each new event to the devices that asked for that kind.
3. Every five minutes the same worker checks for links ending soon (once per link) and, at the chosen hour, sends the daily summary.
4. The phone's **service worker** (`web/public/sw.js`) receives the message and shows it. Tapping it opens the notifications page.

Tests: the rules and the sending are in `api/tests` (`NotificationServiceTests`, `WebPushTransportTests`, `NotificationApiTests`), the service worker in `web/src/sw.test.ts`,
and the whole thing in a real browser with `npm run e2e:notifications-real` and `npm run e2e:notifications` (see [Development](DEVELOPMENT.md)).
