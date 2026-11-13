// End-to-end test of passkey sign-in in a real browser, against a real running API (not the demo).
// Chromium's virtual authenticator plays the part of Face ID / a fingerprint reader.
//
//   1. an S3 + DynamoDB emulator on :5000        (moto_server -p 5000, see docs/DEVELOPMENT.md)
//   2. the API on :8080 with passkeys on, for the Vite dev server's address:
//        NitroDrp__Auth__Passkeys=true NitroDrp__PublicBaseUrl=http://localhost:5173 NitroDrp__ApiKey=dev-access-key-change-me ... dotnet NitroDrp.Api.dll
//   3. the UI:  npm run dev                      (http://localhost:5173, proxies /api to :8080)
//   4. node e2e/passkeys.mjs
import { chromium } from 'playwright'

const BASE = (process.env.BASE_URL ?? 'http://localhost:5173').replace(/\/$/, '')
const ACCESS_KEY = process.env.API_KEY ?? 'dev-access-key-change-me'
const results = []
const ok = (pass, message) => {
  results.push(pass)
  console.log(`${pass ? 'PASS' : 'FAIL'} ${message}`)
}
const H = { 'X-Requested-With': 'nitrodrp' }

/** A browser profile with its own virtual authenticator: one "device". */
let deviceNumber = 0
async function device(browser) {
  // each device has its own address, so its own rate limit, as in real life (the test would otherwise share one)
  const context = await browser.newContext({ viewport: { width: 1000, height: 900 }, extraHTTPHeaders: { 'X-Client-IP': `10.30.0.${++deviceNumber}` } })
  const page = await context.newPage()
  const cdp = await context.newCDPSession(page)
  await cdp.send('WebAuthn.enable')
  const { authenticatorId } = await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true },
  })
  const errors = []
  page.on('pageerror', (e) => errors.push(e.message))
  page.on('dialog', (d) => d.accept())
  return { context, page, cdp, authenticatorId, errors }
}

/** Create, upload and finalize a one-file link through the API, as whoever's cookie the context holds. */
async function publish(context) {
  const created = await (await context.request.post(`${BASE}/api/shares`, { headers: H, data: { files: [{ name: 'e2e.txt', size: 5, contentType: 'text/plain' }], expiresInDays: 7 } })).json()
  const owner = { ...H, 'X-Owner-Key': created.ownerKey }
  const url = (await (await context.request.post(`${BASE}/api/shares/${created.token}/files/${created.files[0].id}/presign`, { headers: owner, data: { partNumbers: [1] } })).json()).urls[0].url
  const put = await context.request.put(url, { data: Buffer.from('hello'), headers: { 'Content-Type': 'application/octet-stream' } })
  const etag = put.headers().etag
  await context.request.post(`${BASE}/api/shares/${created.token}/files/${created.files[0].id}/complete`, { headers: owner, data: { parts: [{ partNumber: 1, eTag: etag }] } })
  const fin = await context.request.post(`${BASE}/api/shares/${created.token}/finalize`, { headers: owner })
  return { token: created.token, finalized: fin.status() === 204 }
}

let openLink
const browser = await chromium.launch()
try {
  // ---- device A: first time. The access key proves it's you, then a passkey is saved. ----
  const A = await device(browser)
  await A.page.goto(`${BASE}/owner`)
  await A.page.waitForSelector('text=Set up this device')
  ok(true, 'first visit asks to set up this device (no passkeys exist yet)')

  await A.page.fill('#si-key', 'not-the-key')
  await A.page.click('button:has-text("Set up this device")')
  await A.page.waitForSelector('[role=alert]')
  ok((await A.page.textContent('[role=alert]')).includes("isn't right"), 'a wrong access key is refused')

  await A.page.fill('#si-key', ACCESS_KEY)
  await A.page.fill('#si-name', 'Test laptop')
  await A.page.click('button:has-text("Set up this device")')
  await A.page.waitForSelector('text=Create link', { timeout: 15_000 })
  ok(true, 'the right access key saves a passkey and signs this device in')
  ok((await A.page.locator('#key').count()) === 0, 'the upload page no longer asks for the access key')
  ok(await A.page.locator('a.nav-link:has-text("Devices")').isVisible(), 'the Devices link appears once signed in')

  await A.page.reload()
  await A.page.waitForSelector('text=Create link')
  ok(true, 'still signed in after a reload (the cookie)')

  const cookies = await A.context.cookies()
  const session = cookies.find((c) => c.name === 'nd_session')
  ok(!!session && session.httpOnly && session.sameSite === 'Strict', 'the sign-in cookie is HttpOnly and SameSite=Strict')
  ok(!(await A.page.evaluate(() => document.cookie)).includes('nd_session'), 'page scripts cannot read the sign-in cookie')
  ok(!(await A.page.evaluate(() => JSON.stringify(localStorage))).includes(ACCESS_KEY), 'the access key was not stored in the browser')

  // ---- a link made on A shows in the list ----
  const link = await publish(A.context)
  ok(link.finalized, 'device A made a link with no access key at all')
  await A.page.goto(`${BASE}/recent`)
  await A.page.waitForSelector(`a[href="/created/${link.token}"]`)
  ok(true, 'the link is in the Recent list on A')

  // ---- device B: a different browser, holding a copy of the same passkey (like iCloud Keychain sync) ----
  const B = await device(browser)
  const { credentials } = await A.cdp.send('WebAuthn.getCredentials', { authenticatorId: A.authenticatorId })
  ok(credentials.length === 1, 'the authenticator on A holds one passkey')
  for (const credential of credentials) await B.cdp.send('WebAuthn.addCredential', { authenticatorId: B.authenticatorId, credential })

  await B.page.goto(`${BASE}/owner`)
  await B.page.waitForSelector('button:has-text("Sign in with a passkey")')
  ok(true, 'a fresh device offers "Sign in with a passkey"')
  await B.page.click('button:has-text("Sign in with a passkey")')
  await B.page.waitForSelector('text=Create link', { timeout: 15_000 })
  ok(true, 'device B signs in with the passkey alone (no access key typed)')

  await B.page.goto(`${BASE}/recent`)
  await B.page.waitForSelector(`a[href="/created/${link.token}"]`)
  ok(true, 'the same link appears on device B')

  await B.page.goto(`${BASE}/created/${link.token}`)
  await B.page.waitForSelector('text=Delete link now')
  ok(true, "B can open a link it didn't create")

  // B deletes it. It disappears for A too.
  await B.page.click('text=Delete link now')
  await B.page.waitForURL(`${BASE}/`)
  await A.page.goto(`${BASE}/recent`)
  await A.page.waitForSelector('text=Nothing here yet')
  ok(true, 'deleted on B, gone from A')

  // ---- an in-app loader that has appeared lingers a moment, even if the data arrives sooner ----
  const slowLink = await publish(A.context)
  openLink = await publish(A.context)
  const slow = await A.context.newPage()
  await slow.route(`**/api/shares/${slowLink.token}`, async (route) => {
    await new Promise((r) => setTimeout(r, 700)) // the link details arrive after 0.7 s
    await route.continue()
  })
  const began = Date.now()
  await slow.goto(`${BASE}/d/${slowLink.token}`, { waitUntil: 'commit' })
  await slow.waitForSelector('.pixel-loader')
  await slow.waitForTimeout(Math.max(0, 1250 - (Date.now() - began)))
  ok((await slow.locator('.pixel-loader').count()) === 1 && (await slow.locator('.dl-row').count()) === 0, 'a loader that has appeared stays up a moment, even though the data has already arrived')
  await slow.waitForSelector('.dl-row', { timeout: 8000 })
  ok((await slow.locator('.pixel-loader').count()) === 0, 'then the page appears')
  await slow.close()

  // ---- devices page ----
  await A.page.goto(`${BASE}/devices`)
  await A.page.waitForSelector('text=Test laptop')
  ok((await A.page.textContent('main')).includes('this device'), 'the Devices page lists the passkey and marks this device')
  await A.page.fill('#dv-name', 'Second')
  await A.page.click('button:has-text("Add a passkey on this device")')
  await A.page.waitForSelector('[role=alert]')
  ok((await A.page.textContent('[role=alert]')).includes('already has a passkey'), 'adding a duplicate passkey on the same device is explained, not an error page')

  // ---- removing the passkey signs out every device that used it ----
  await B.page.goto(`${BASE}/devices`)
  await B.page.waitForSelector('text=Test laptop')
  await B.page.click('button:has-text("Remove")')
  await B.page.waitForSelector('text=Hi, welcome', { timeout: 15_000 })
  ok(true, 'removing the passkey signs that device out (it falls back to the welcome page)')
  await A.page.goto(`${BASE}/`)
  await A.page.waitForSelector('text=Hi, welcome', { timeout: 15_000 })
  ok(true, 'and every other device that was using it, straight away')
  await A.page.goto(`${BASE}/owner`)
  await A.page.waitForSelector('text=Set up this device')
  const gone = await A.context.request.get(`${BASE}/api/shares`, { headers: H })
  ok(gone.status() === 401, 'the old cookie is refused by the API')

  // ---- the access key still works as the recovery path, and sign out works ----
  await A.page.fill('#si-key', ACCESS_KEY)
  await A.page.click('button:has-text("Set up this device")')
  await A.page.waitForSelector('text=Create link', { timeout: 15_000 })
  ok(true, 'with every passkey removed, the access key sets a device up again')
  await A.page.goto(`${BASE}/devices`)
  await A.page.click('button:has-text("Sign out of this device")')
  await A.page.waitForSelector('text=Hi, welcome', { timeout: 8000 })
  await A.page.goto(`${BASE}/owner`)
  await A.page.waitForSelector('button:has-text("Sign in with a passkey")')
  ok(true, 'signing out returns to the welcome page, and /owner offers the passkey sign-in again')

  // ---- a stranger sees a welcome page and nothing else ----
  const X = await browser.newContext({ extraHTTPHeaders: { 'X-Client-IP': '10.20.30.40' } }) // a stranger has their own address, so their own rate limit
  const xp = await X.newPage()
  for (const path of ['/', '/recent', '/devices', '/created/AbCdEfGh1234', '/anything/at/all']) {
    await xp.goto(`${BASE}${path}`)
    await xp.waitForSelector('text=Hi, welcome')
    const body = await xp.textContent('main')
    const clean = (await xp.locator('input[type=file]').count()) === 0 && !/Create link|Set up this device|Sign in with a passkey|Access key/.test(body)
    ok(clean, `a stranger at ${path} gets the welcome page: no app, no sign-in form`)
  }
  await xp.goto(`${BASE}/`)
  await xp.waitForSelector('text=Hi, welcome')
  ok((await xp.textContent('main')).includes('private file transfer app') && (await xp.getAttribute('a.btn', 'href')) === 'https://demo.jadedmedia.ca', 'it says whose app it is and links to the demo')
  ok(await xp.locator('.hero-scene canvas').isVisible(), 'with the racer')
  ok((await xp.locator('header .nav-link').count()) === 0, 'and no app menu')

  // a download link still works for anyone who has it, and so does pasting one
  await xp.goto(`${BASE}/d/${openLink.token}`)
  await xp.waitForSelector('.dl-row')
  ok(true, 'a download link opens for a stranger who was sent it')
  await xp.goto(`${BASE}/open`)
  await xp.waitForSelector('#open-link')
  ok(true, 'and the paste-a-link page is open to them too')

  // the owner's door is /owner. Strangers can look, but there's nothing they can do behind it
  await xp.goto(`${BASE}/`)
  await xp.click('text=Owner sign-in')
  await xp.waitForURL('**/owner')
  await xp.waitForSelector('text=Use the access key without a passkey')
  await xp.click('text=Use the access key without a passkey')
  await xp.waitForSelector('#ak-key')
  ok((await xp.locator('input[type=file]').count()) === 0 && (await xp.locator('text=Create link').count()) === 0, 'the access-key option asks for the key instead of opening the app')

  await xp.fill('#ak-key', 'not-the-access-key-at-all-123')
  await xp.click('button:has-text("Continue with the access key")')
  await xp.waitForSelector('.field-error')
  ok((await xp.textContent('.field-error')).includes("isn't right") && (await xp.locator('input[type=file]').count()) === 0, 'a wrong key is refused by the server, and the app stays shut')

  // tampering with the page's own storage doesn't open anything either
  await xp.evaluate(() => { sessionStorage.setItem('nitrodrp.accessKeyMode', '1'); localStorage.setItem('nitrodrp.accessKeyMode', '1'); localStorage.setItem('nitrodrp.apiKey', 'guess') })
  await xp.goto(`${BASE}/`)
  await xp.waitForSelector('text=Hi, welcome')
  ok((await xp.locator('input[type=file]').count()) === 0, 'setting flags by hand in the browser does not open it')

  // and the server refuses the same visitor, whatever the page does
  const anon = [
    await X.request.get(`${BASE}/api/shares`, { headers: H }),
    await X.request.post(`${BASE}/api/shares`, { headers: H, data: { files: [{ name: 'a.txt', size: 1 }], expiresInDays: 7 } }),
    await X.request.get(`${BASE}/api/auth/passkeys`, { headers: H }),
    await X.request.post(`${BASE}/api/auth/register/begin`, { headers: H }),
  ]
  ok(anon.every((r) => r.status() === 401), `the server answers a stranger with 401 on every one of those (${anon.map((r) => r.status()).join(', ')})`)

  await xp.goto(`${BASE}/owner`)
  await xp.click('text=Use the access key without a passkey')
  await xp.fill('#ak-key', ACCESS_KEY)
  await xp.click('button:has-text("Continue with the access key")')
  await xp.waitForSelector('text=Create link', { timeout: 15_000 })
  ok(true, 'the real access key still gets in, for browsers that can\'t use passkeys')
  await X.close()

  // ---- a stranger can't get into the app, whatever they click or tamper with ----
  const X = await browser.newContext({ extraHTTPHeaders: { 'X-Client-IP': '10.20.30.40' } }) // a stranger has their own address, so their own rate limit
  const xp = await X.newPage()
  await xp.goto(`${BASE}/`)
  await xp.waitForSelector('button:has-text("Sign in with a passkey")')
  await xp.click('text=Use the access key without a passkey')
  await xp.waitForSelector('#ak-key')
  ok((await xp.locator('input[type=file]').count()) === 0 && (await xp.locator('text=Create link').count()) === 0, 'the access-key button no longer opens the app: it asks for the key')

  await xp.fill('#ak-key', 'not-the-access-key-at-all-123')
  await xp.click('button:has-text("Continue with the access key")')
  await xp.waitForSelector('.field-error')
  ok((await xp.textContent('.field-error')).includes("isn't right") && (await xp.locator('input[type=file]').count()) === 0, 'a wrong key is refused by the server, and the app stays shut')

  // tampering with the page's own storage doesn't open it either
  await xp.evaluate(() => { sessionStorage.setItem('nitrodrp.accessKeyMode', '1'); localStorage.setItem('nitrodrp.accessKeyMode', '1'); localStorage.setItem('nitrodrp.apiKey', 'guess') })
  await xp.reload()
  await xp.waitForSelector('button:has-text("Sign in with a passkey")')
  ok((await xp.locator('input[type=file]').count()) === 0, 'setting flags by hand in the browser does not open it')

  // and the server refuses the same visitor, whatever the page does
  const anon = [
    await X.request.get(`${BASE}/api/shares`, { headers: H }),
    await X.request.post(`${BASE}/api/shares`, { headers: H, data: { files: [{ name: 'a.txt', size: 1 }], expiresInDays: 7 } }),
    await X.request.get(`${BASE}/api/auth/passkeys`, { headers: H }),
    await X.request.post(`${BASE}/api/auth/register/begin`, { headers: H }),
  ]
  ok(anon.every((r) => r.status() === 401), `the server answers a stranger with 401 on every one of those (${anon.map((r) => r.status()).join(', ')})`)

  await xp.click('text=Use the access key without a passkey')
  await xp.fill('#ak-key', ACCESS_KEY)
  await xp.click('button:has-text("Continue with the access key")')
  await xp.waitForSelector('text=Create link', { timeout: 15_000 })
  ok(true, 'the real access key still gets in, for browsers that can\'t use passkeys')
  await X.close()

  // ---- recipients never see any of this ----
  const R = await browser.newContext()
  const rp = await R.newPage()
  await rp.goto(`${BASE}/d/does-not-exist`)
  await rp.waitForSelector('text=gone, text=exist, h1', { timeout: 10_000 }).catch(() => {})
  ok(!(await rp.textContent('body')).includes('Set up this device'), 'a download link opens without any sign-in')

  const errors = [...A.errors, ...B.errors]
  ok(errors.length === 0, `no browser errors${errors.length ? ': ' + errors.join(' | ') : ''}`)
} finally {
  await browser.close()
}
const failed = results.filter((r) => !r).length
console.log(failed ? `\n${failed} check(s) failed.` : '\nAll checks passed.')
process.exit(failed ? 1 : 0)
