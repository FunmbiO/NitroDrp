// NitroDrp service worker. It does two small things so the installed app opens fast and shows something
// without a connection: it keeps a copy of the app's own files, and nothing else.
//
// It NEVER touches /api/ (links, passwords, upload plans), storage or download URLs, or any other site's
// requests. Those always go straight to the network, so nothing private is ever stored by the app.
//
// To force every phone to drop its saved copy after a release that needs it, bump VERSION.
const VERSION = 'v1'
const SHELL = `nitrodrp-shell-${VERSION}` // the page itself
const ASSETS = `nitrodrp-assets-${VERSION}` // the hashed script/style files and icons

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(SHELL)
      .then((cache) => cache.addAll(['/', '/favicon.svg', '/icons/icon-192.png']))
      .then(() => self.skipWaiting()),
  )
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== SHELL && k !== ASSETS).map((k) => caches.delete(k))))
      .then(() => self.clients.claim()),
  )
})

const isOwnAsset = (url) => url.pathname.startsWith('/assets/') || url.pathname.startsWith('/icons/') || url.pathname === '/favicon.svg'

self.addEventListener('fetch', (event) => {
  const req = event.request
  if (req.method !== 'GET') return
  const url = new URL(req.url)
  if (url.origin !== self.location.origin) return // S3, CloudFront, fonts: not ours to cache
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/mock-storage/')) return // always live

  // Opening the app or any page in it: use the network, fall back to the saved page when offline.
  // Every route is served by the same index.html, so one saved copy covers them all.
  if (req.mode === 'navigate') {
    event.respondWith(
      fetch(req)
        .then((res) => {
          if (res.ok) {
            const copy = res.clone()
            event.waitUntil(caches.open(SHELL).then((c) => c.put('/', copy)))
          }
          return res
        })
        .catch(() => caches.match('/').then((hit) => hit || Response.error())),
    )
    return
  }

  // Files whose names change whenever their contents do: a saved copy is always right.
  if (isOwnAsset(url)) {
    event.respondWith(
      caches.match(req).then(
        (hit) =>
          hit ||
          fetch(req).then((res) => {
            if (res.ok) {
              const copy = res.clone()
              event.waitUntil(caches.open(ASSETS).then((c) => c.put(req, copy)))
            }
            return res
          }),
      ),
    )
  }
})
