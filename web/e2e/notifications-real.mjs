// End-to-end test of notifications in a real browser against the real API (S3 and DynamoDB emulated by moto).
// Start the stack like the header of passkeys.mjs, with push on:
//   NitroDrp__Notifications__Push__PublicKey / __PrivateKey (from deploy/make-vapid.sh) and __Subject=mailto:you@example.com
//   PUSH_PUBLIC_KEY=<the same public key> npm run e2e:notifications-real
//
// A real browser can't get a push subscription without its maker's push service, so what a push looks like on the
// device is covered by the unit tests (src/sw.test.ts), and the server's sending by the API tests (RFC 8291 vector).
import { chromium } from 'playwright'
import { generateKeyPairSync, randomBytes } from 'node:crypto'

const BASE = (process.env.BASE_URL ?? 'http://localhost:5173').replace(/\/$/, '')
const ACCESS_KEY = process.env.API_KEY ?? 'dev-access-key-change-me'
const PUSH_PUBLIC_KEY = process.env.PUSH_PUBLIC_KEY
const results = []
const ok = (pass, message) => {
  results.push(pass)
  console.log(`${pass ? 'PASS' : 'FAIL'} ${message}`)
}
const H = { 'X-Requested-With': 'nitrodrp' }
const b64url = (buf) => Buffer.from(buf).toString('base64url')

let n = 0
async function device(browser) {
  const context = await browser.newContext({ viewport: { width: 1000, height: 900 }, extraHTTPHeaders: { 'X-Client-IP': `10.40.0.${++n}` } })
  const page = await context.newPage()
  const cdp = await context.newCDPSession(page)
  await cdp.send('WebAuthn.enable')
  await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true },
  })
  const errors = []
  page.on('pageerror', (e) => errors.push(e.message))
  page.on('dialog', (d) => d.accept())
  return { context, page, cdp, errors }
}

async function publish(context, downloadLimit) {
  const created = await (await context.request.post(`${BASE}/api/shares`, { headers: H, data: { files: [{ name: 'secret-name.txt', size: 5, contentType: 'text/plain' }], expiresInDays: 7, downloadLimit } })).json()
  const owner = { ...H, 'X-Owner-Key': created.ownerKey }
  const url = (await (await context.request.post(`${BASE}/api/shares/${created.token}/files/${created.files[0].id}/presign`, { headers: owner, data: { partNumbers: [1] } })).json()).urls[0].url
  const put = await context.request.put(url, { data: Buffer.from('hello'), headers: { 'Content-Type': 'application/octet-stream' } })
  await context.request.post(`${BASE}/api/shares/${created.token}/files/${created.files[0].id}/complete`, { headers: owner, data: { parts: [{ partNumber: 1, eTag: put.headers().etag }] } })
  await context.request.post(`${BASE}/api/shares/${created.token}/finalize`, { headers: owner })
  return created.token
}

const until = async (fn, ms = 8000) => {
  const end = Date.now() + ms
  while (Date.now() < end) {
    const v = await fn()
    if (v) return v
    await new Promise((r) => setTimeout(r, 150))
  }
  return null
}

const browser = await chromium.launch()
try {
  const A = await device(browser)
  await A.page.goto(`${BASE}/owner`)
  await A.page.fill('#si-key', ACCESS_KEY)
  await A.page.fill('#si-name', 'Test laptop')
  await A.page.click('button:has-text("Set up this device")')
  await A.page.waitForSelector('text=Create link', { timeout: 15_000 })
  ok(await A.page.locator('a.bell').isVisible(), 'a signed-in device has the bell in the header')
  ok((await A.page.locator('a.bell .badge').count()) === 0, 'nothing is unread yet, so there is no badge')

  // ---- the API is private ----
  const stranger = await browser.newContext({ extraHTTPHeaders: { 'X-Client-IP': '10.40.9.9' } })
  for (const path of ['/api/notifications', '/api/notifications/unread']) {
    ok((await stranger.request.get(`${BASE}${path}`)).status() === 401, `a stranger gets 401 from GET ${path}`)
  }
  ok((await stranger.request.post(`${BASE}/api/notifications/devices`, { headers: H, data: {} })).status() === 401, 'a stranger cannot add a device')
  ok((await stranger.request.get(`${BASE}/api/notifications`, { headers: { 'X-Api-Key': 'wrong-key-wrong-key-123' } })).status() === 401, 'a wrong access key is refused too')

  const view0 = await (await A.context.request.get(`${BASE}/api/notifications`)).json()
  ok(view0.pushAvailable === true && (!PUSH_PUBLIC_KEY || view0.publicKey === PUSH_PUBLIC_KEY), 'the server offers push and the public key the browser needs')
  ok(view0.digest.available === false, 'the daily summary is off when email is not set up')

  // ---- a download tells the owner, through DynamoDB and the background worker ----
  const token = await publish(A.context, 1)
  const files = await (await stranger.request.post(`${BASE}/api/shares/${token}/download`, { headers: H, data: {} })).json()
  ok(Array.isArray(files.files), 'a stranger downloads the link')
  const kinds = await until(async () => {
    const v = await (await A.context.request.get(`${BASE}/api/notifications`)).json()
    return v.items.length >= 2 ? v.items.map((i) => i.kind).sort() : null
  })
  ok(JSON.stringify(kinds) === '["downloaded","limit_reached"]', `the feed has the download and the limit (${kinds})`)
  const second = await stranger.request.post(`${BASE}/api/shares/${token}/download`, { headers: H, data: {} })
  ok(second.status() === 410, 'the link refuses a second download')
  await new Promise((r) => setTimeout(r, 600))
  ok((await (await A.context.request.get(`${BASE}/api/notifications`)).json()).items.length === 2, 'a refused download tells nobody')

  // ---- the page ----
  await A.page.goto(`${BASE}/`)
  await A.page.waitForSelector('a.bell .badge')
  ok((await A.page.textContent('a.bell .badge')).trim() === '2', 'the bell shows 2 unread')
  await A.page.click('a.bell')
  await A.page.waitForSelector('.note-row')
  const text = await A.page.textContent('main')
  ok(text.includes('Link downloaded') && text.includes('Download limit reached'), 'the page lists both')
  ok(!text.includes('secret-name') && !text.includes(token), 'neither the file name nor the link appears')
  await A.page.waitForFunction(() => !document.querySelector('a.bell .badge'))
  ok(true, 'opening the page clears the badge')
  ok((await (await A.context.request.get(`${BASE}/api/notifications/unread`)).json()).unread === 0, 'and the server agrees')

  // ---- devices ----
  const { publicKey } = generateKeyPairSync('ec', { namedCurve: 'prime256v1' })
  const jwk = publicKey.export({ format: 'jwk' })
  const p256dh = b64url(Buffer.concat([Buffer.from([4]), Buffer.from(jwk.x, 'base64url'), Buffer.from(jwk.y, 'base64url')]))
  const sub = { endpoint: 'https://fcm.googleapis.com/fcm/send/e2e-test-device', p256dh, auth: b64url(randomBytes(16)), label: 'Pixel (test)', kinds: ['downloaded', 'limit_reached'] }
  ok((await A.context.request.post(`${BASE}/api/notifications/devices`, { headers: H, data: { ...sub, endpoint: 'https://169.254.169.254/latest/meta-data' } })).status() === 400, 'an internal address is refused as a push endpoint')
  ok((await A.context.request.post(`${BASE}/api/notifications/devices`, { headers: H, data: { ...sub, endpoint: 'https://evil.example.com/x' } })).status() === 400, 'so is any other site')
  const added = await A.context.request.post(`${BASE}/api/notifications/devices`, { headers: H, data: sub })
  ok(added.status() === 200, 'a real-looking subscription is accepted')
  await A.page.reload()
  await A.page.waitForSelector('.device-row')
  ok((await A.page.locator('.device-row input:checked').count()) === 2, 'the page shows the device with the two kinds it asked for')
  await A.page.getByLabel('A link is about to expire').check()
  await A.page.waitForFunction(() => document.querySelectorAll('.device-row input:checked').length === 3)
  const after = await (await A.context.request.get(`${BASE}/api/notifications`)).json()
  ok(after.devices[0].kinds.length === 3, 'a kind turned on in the page is saved on the server')
  ok(!(JSON.stringify(after)).includes(sub.auth) && !JSON.stringify(after).includes(sub.p256dh), "the device's keys are never sent back to the browser")
  await A.page.getByRole('button', { name: /^remove$/i }).click()
  await A.page.waitForFunction(() => !document.querySelector('.device-row'))
  ok((await (await A.context.request.get(`${BASE}/api/notifications`)).json()).devices.length === 0, 'removing a device removes it on the server')

  // ---- clearing ----
  await A.page.getByRole('button', { name: /clear all/i }).click()
  await A.page.waitForFunction(() => !document.querySelector('.note-row'))
  ok((await (await A.context.request.get(`${BASE}/api/notifications`)).json()).items.length === 0, 'clear all empties the feed on the server')

  // ---- the service worker loads and takes a push without trouble ----
  // (What it shows, and where a tap goes, is proven by src/sw.test.ts: a headless browser won't display notifications.)
  await A.page.goto(`${BASE}/`)
  await A.page.evaluate(async () => {
    await navigator.serviceWorker.register('/sw.js')
    await navigator.serviceWorker.ready
  })
  const state = await until(async () => ((await A.page.evaluate(async () => (await navigator.serviceWorker.ready).active?.state)) === 'activated' ? 'activated' : null))
  ok(state === 'activated', 'the service worker, with its push handlers, registers and activates')
  ok(A.errors.length === 0, `no errors in the page${A.errors.length ? ': ' + A.errors.join(' | ') : ''}`)
} catch (err) {
  ok(false, `unexpected error: ${err.message}\n${err.stack}`)
} finally {
  await browser.close()
}

const failed = results.filter((r) => !r).length
console.log(failed ? `\n${failed} check(s) failed.` : '\nAll checks passed.')
process.exit(failed ? 1 : 0)
