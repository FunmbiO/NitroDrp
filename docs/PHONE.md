# Using NitroDrp as a phone app

NitroDrp is an installable web app (a PWA). Added to the home screen, it opens full screen with its own icon, like a
normal app, with no app store and nothing to update by hand.

## Install

Open your site (for example `https://drop.example.com`) in the phone's browser first.

| Phone | Steps |
|---|---|
| **iPhone / iPad** (use Safari) | Share button, **Add to Home Screen**, **Add** |
| **Android** (Chrome) | Menu (three dots), **Install app** (or **Add to Home screen**), **Install** |

The demo site is installable the same way.

## Good to know

- **Enter the access key once.** An installed app keeps its own storage, separate from the browser, so the first
  time you open it, type the access key again. It remembers it after that. Recent links start empty for the same
  reason.
- **Keep the app open while uploading.** A phone may pause a page that's in the background. If an upload stops,
  reopen the app and press **Resume upload**: finished parts are never sent again.
- **Offline:** the app opens without a connection and says you're offline. Creating and opening links, uploads
  and downloads need one. Nothing private is saved on the phone by the app's offline copy (see below).
- **Updates:** when a new version is deployed, the app picks it up the next time it opens (sometimes the second
  time). To force it, close the app fully and reopen it.
- **Remove it:** press and hold the icon, then remove it like any app.

## What the offline copy contains

Only the app's own files: the page, scripts, styles and icons (`web/public/sw.js`). It never stores anything from
`/api/`, so link details, passwords, file names and upload plans always come live from the server. Downloads and
uploads go to storage sites, not through the copy.

## For developers

- Files: `web/public/manifest.webmanifest`, `web/public/sw.js`, the icons in `web/public/icons/`, and the tags in
  `web/index.html`. The service worker only registers in production builds, never in `npm run dev` and never inside
  an embed.
- Regenerate the icons (the orange arrow from the favicon): `python3 dev/make-icons.py` (needs Pillow).
- If a release needs every phone to drop its saved copy, change `VERSION` in `sw.js`.
- `/sw.js` and the manifest are served with `Cache-Control: no-cache` (`web/nginx.conf`, `web/vercel.json`) so
  updates always reach phones.
- `npm run e2e:demo` checks the manifest, the icons, offline opening, and that nothing under `/api` is saved.
- Not included: sharing files *into* the app from the phone's share sheet (Android only; possible later).
