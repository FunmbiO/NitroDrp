// Browser test of the demo build, the same site that runs at demo.jadedmedia.ca. Needs no backend: the demo
// runs its whole backend in the page. Also proves nothing is sent to any server.
//
//   npm run build:demo && npx vite preview --port 4174 &
//   npx playwright install chromium     # once
//   npm run e2e:demo                    # BASE_URL defaults to http://localhost:4174
import { chromium } from 'playwright'
import { mkdtempSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { randomBytes } from 'node:crypto'
import { createServer } from 'node:http'

const BASE = (process.env.BASE_URL ?? 'http://localhost:4174').replace(/\/$/, '')
const results = []
const ok = (pass, message) => {
  results.push(pass)
  console.log(`${pass ? 'PASS' : 'FAIL'} ${message}`)
}

/** Minimal reader for the store-only zips the demo builds: names and sizes from the central directory. */
function readZip(buf) {
  const eocd = buf.length - 22
  if (buf.readUInt32LE(eocd) !== 0x06054b50) throw new Error('not a zip')
  const count = buf.readUInt16LE(eocd + 10)
  let p = buf.readUInt32LE(eocd + 16)
  const entries = []
  for (let i = 0; i < count; i++) {
    if (buf.readUInt32LE(p) !== 0x02014b50) throw new Error('bad central directory')
    const nameLen = buf.readUInt16LE(p + 28)
    entries.push({ name: buf.toString('utf8', p + 46, p + 46 + nameLen), size: buf.readUInt32LE(p + 24) })
    p += 46 + nameLen
  }
  return entries
}

const dir = mkdtempSync(join(tmpdir(), 'nitrodrp-e2e-'))
const bigFile = join(dir, 'Photo-Set.zip')
const smallFile = join(dir, 'Contract.pdf')
writeFileSync(bigFile, randomBytes(3_000_000))
writeFileSync(smallFile, randomBytes(700_000))

const browser = await chromium.launch()
const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, acceptDownloads: true })
const page = await context.newPage()

const errors = []
const outbound = []
page.on('pageerror', (e) => errors.push(e.message))
page.on('console', (m) => m.type() === 'error' && !/fonts|favicon|Failed to load resource/.test(m.text()) && errors.push(m.text()))
context.on('request', (r) => {
  const u = new URL(r.url())
  const own = u.origin === new URL(BASE).origin
  const fonts = /(^|\.)(googleapis|gstatic)\.com$/.test(u.hostname)
  if ((own && u.pathname.startsWith('/api')) || (!own && !fonts && u.protocol.startsWith('http'))) outbound.push(`${r.method()} ${r.url()}`)
})

try {
  // sender: upload, password, limit
  await page.goto(`${BASE}/`)
  ok(await page.locator('.demo-banner').isVisible(), 'demo banner is shown')
  ok((await page.locator('#key').count()) === 0, 'no access key field in demo mode')
  await page.setInputFiles('input[type=file]', [bigFile, smallFile])
  await page.click('button:has-text("30 days")')
  await page.check('text=Require a password')
  await page.fill('#pw', 'secret1')
  await page.fill('#limit', '2')
  await page.click('button[type=submit]')
  await page.waitForURL('**/created/**', { timeout: 20_000 })
  const token = page.url().split('/').pop()
  ok(true, `link created: ${token}`)

  // recipient, in another tab of the same browser (the demo keeps its data in localStorage)
  const r = await context.newPage()
  await r.goto(`${BASE}/d/${token}`)
  await r.waitForSelector('#pw')
  await r.fill('#pw', 'wrong')
  await r.click('button:has-text("Unlock")')
  await r.waitForSelector('[role=alert]')
  ok((await r.textContent('[role=alert]')).includes("isn't right"), 'wrong password is rejected')
  await r.fill('#pw', 'secret1')
  await r.click('button:has-text("Unlock")')
  await r.waitForSelector('text=Download all as .zip')

  const [zip] = await Promise.all([r.waitForEvent('download'), r.click('button:has-text("Download all as .zip")')])
  const zipPath = join(dir, 'got.zip')
  await zip.saveAs(zipPath)
  const entries = readZip((await import('node:fs')).readFileSync(zipPath))
  ok(zip.suggestedFilename() === 'NitroDrp demo files.zip', `zip is named ${zip.suggestedFilename()}`)
  ok(entries.map((e) => e.name).join() === 'Photo-Set.zip,Contract.pdf', 'zip holds both files, in order')

  const [single] = await Promise.all([r.waitForEvent('download'), r.click('button[aria-label="Download Contract.pdf"]')])
  ok(single.suggestedFilename() === 'Contract.pdf', 'single-file download is named correctly')
  await r.waitForSelector('text=reached its download limit')
  ok(await r.locator('button:has-text("Download all as .zip")').isDisabled(), 'download limit of 2 is enforced')

  // sending the link by email, at creation and afterwards (simulated in the demo)
  const sender = await context.newPage()
  await sender.goto(`${BASE}/`)
  await sender.setInputFiles('input[type=file]', [smallFile])
  ok((await sender.locator('#up-to').count()) === 0, 'email fields are hidden until asked for')
  await sender.check('text=Also send the link by email')
  ok(await sender.locator('button[type=submit]').isDisabled(), 'cannot create the link while the email box is empty')
  await sender.fill('#up-to', 'friend@example.com, nope')
  ok((await sender.textContent('#up-hint')).includes('"nope" isn\'t a valid'), 'a bad address is called out as you type')
  ok(await sender.locator('button[type=submit]').isDisabled(), 'a bad address blocks creating the link')
  await sender.fill('#up-to', 'friend@example.com, Friend@Example.com, other@example.com')
  await sender.fill('#up-name', 'Amina')
  await sender.fill('#up-msg', 'Here are the files')
  await sender.click('button[type=submit]')
  await sender.waitForURL('**/created/**', { timeout: 20_000 })
  await sender.waitForSelector('.results li')
  ok((await sender.locator('.results li.ok').count()) === 2, 'created page lists the two unique recipients as sent')
  ok((await sender.textContent('.email-card')).includes('18 more people'), 'created page says how many emails are left')
  await sender.click('button:has-text("Email more people")')
  await sender.fill('#cr-to', 'third@example.com')
  await sender.click('button:has-text("Send to 1 person")')
  await sender.waitForSelector('.results li:nth-child(3)')
  ok((await sender.textContent('.email-card')).includes('17 more people'), 'emailing later from the created page works and counts down')
  await sender.click('button:has-text("Email more people")')
  ok((await sender.textContent('.email-card')).includes('Demo: no email is really sent'), 'demo says no email is really sent')

  // installable app: manifest, icons, and a service worker that lets it open offline
  const app = await context.newPage()
  await app.goto(`${BASE}/`)
  const manifestHref = await app.getAttribute('link[rel=manifest]', 'href')
  ok(manifestHref === '/manifest.webmanifest', 'page links the web app manifest')
  const mres = await context.request.get(`${BASE}${manifestHref}`)
  const manifest = await mres.json()
  ok(mres.headers()['content-type']?.includes('manifest') || mres.headers()['content-type']?.includes('json'), `manifest is served as ${mres.headers()['content-type']}`)
  ok(manifest.display === 'standalone' && manifest.start_url === '/' && manifest.name === 'NitroDrp', 'manifest: standalone, starts at /, named NitroDrp')
  const sizes = new Set(manifest.icons.map((i) => i.sizes))
  ok(sizes.has('192x192') && sizes.has('512x512') && manifest.icons.some((i) => i.purpose === 'maskable'), 'manifest has 192, 512 and a maskable icon')
  for (const src of [...manifest.icons.map((i) => i.src), await app.getAttribute('link[rel=apple-touch-icon]', 'href')]) {
    const r = await context.request.get(`${BASE}${src}`)
    ok(r.ok() && r.headers()['content-type'] === 'image/png', `icon loads: ${src}`)
  }
  await app.evaluate(() => navigator.serviceWorker.ready)
  await app.reload()
  ok(await app.evaluate(() => !!navigator.serviceWorker.controller), 'service worker is controlling the page')
  const cached = await app.evaluate(async () => {
    const urls = []
    for (const name of await caches.keys()) for (const r of await (await caches.open(name)).keys()) urls.push(new URL(r.url).pathname)
    return urls
  })
  ok(cached.includes('/') && cached.some((u) => u.startsWith('/assets/')), `app files are saved for offline (${cached.length} entries)`)
  ok(!cached.some((u) => u.startsWith('/api/')), 'nothing under /api is ever saved')
  const cacheNames = await app.evaluate(() => caches.keys())
  ok(cacheNames.some((n) => n.endsWith('-v2')) && !cacheNames.some((n) => n.endsWith('-v1')), `the saved copy is the new version, so phones drop the old logo (${cacheNames.join(', ')})`)
  const fav = await (await context.request.get(`${BASE}/favicon.svg`)).text()
  ok(fav.includes('#1f5cff') && fav.includes('#ff5a1f') && fav.includes('crispEdges'), 'the favicon is the new bottle logo')
  ok((await app.locator('.brand svg rect').count()) > 30 && (await app.getAttribute('.brand', 'aria-label')) === 'NitroDrp, home', 'the header shows the logo, with an accessible name')
  ok((await app.textContent('.wordmark')) === 'NITRODRP', 'and the NITRO + DRP wordmark')
  await context.setOffline(true)
  await app.goto(`${BASE}/recent`)
  await app.waitForSelector('.brand', { timeout: 10_000 })
  ok(await app.locator('.offline-banner').isVisible(), 'offline: the app still opens and says you are offline')
  await context.setOffline(false)
  await app.waitForSelector('.offline-banner', { state: 'detached', timeout: 10_000 })
  ok(true, 'back online: the offline notice goes away')
  await app.close()

  // "Open a link": paste a link, land on its download page
  const opener = await context.newPage()
  await opener.goto(`${BASE}/`)
  await opener.click('a.nav-link:has-text("Open a link")')
  await opener.waitForSelector('#open-link')
  ok(opener.url().endsWith('/open'), 'the menu has an "Open a link" page')
  await opener.fill('#open-link', 'not a link')
  await opener.click('button:has-text("Open link")')
  await opener.waitForSelector('[role=alert]')
  ok((await opener.textContent('[role=alert]')).includes("doesn't look like"), 'junk is explained, not opened')
  await opener.fill('#open-link', `Here you go: ${BASE}/d/demo1234abcd, thanks!`)
  await opener.click('button:has-text("Open link")')
  await opener.waitForURL('**/d/demo1234abcd')
  await opener.waitForSelector('text=Download all as .zip')
  ok(true, 'a link inside a message opens its download page')
  await opener.goto(`${BASE}/open`)
  await opener.evaluate(() => {
    const i = document.getElementById('open-link')
    i.focus()
    const dt = new DataTransfer()
    dt.setData('text', 'https://drop.jadedmedia.ca/d/locked123456')
    i.dispatchEvent(new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true }))
  })
  await opener.waitForURL('**/d/locked123456')
  ok(true, 'pasting a link goes straight to it, with no extra tap')
  await opener.goto(`${BASE}/open`)
  await opener.fill('#open-link', 'expired00000')
  await opener.press('#open-link', 'Enter')
  await opener.waitForURL('**/d/expired00000')
  ok(true, 'a bare code and the Enter key work too')
  await opener.close()

  // validation: every text box says what's wrong, right where it is, before anything is sent
  const v = await context.newPage()
  const dialogs = []
  v.on('dialog', (d) => { dialogs.push(d.message()); d.dismiss() })
  await v.goto(`${BASE}/`)
  const pick = (name, text = 'hello') => v.setInputFiles('input[type=file]', [{ name, mimeType: 'text/plain', buffer: Buffer.from(text) }])
  const submit = v.locator('button[type=submit]:has-text("Create link")')

  await pick('setup.exe')
  await v.waitForSelector('.field-error')
  ok((await v.textContent('.field-error')).includes(".exe files can't be sent"), 'a blocked file type is flagged on the file itself')
  ok(await submit.isDisabled(), 'and blocks creating the link')
  await v.click('button[aria-label="Remove setup.exe"]')
  await pick('empty.txt', '')
  await v.waitForSelector('.field-error')
  ok((await v.textContent('.field-error')).includes('empty'), 'an empty file is flagged')
  await v.click('button[aria-label="Remove empty.txt"]')
  await pick('ok.txt')
  ok((await v.locator('.field-error').count()) === 0 && (await submit.isEnabled()), 'a normal file is fine')

  for (const bad of ['abc', '0', '-3', '2.5', '1e3', '10001', '5 downloads']) {
    await v.fill('#limit', bad)
    ok((await v.textContent('#limit-err')).includes('whole number') && (await submit.isDisabled()), `download limit "${bad}" is refused with a reason`)
  }
  await v.fill('#limit', '5')
  ok((await v.locator('#limit-err').count()) === 0 && (await submit.isEnabled()), 'download limit "5" is accepted')
  await v.fill('#limit', '')

  await v.check('text=Require a password')
  await v.focus('#pw'); await v.press('#pw', 'Tab')
  ok((await v.textContent('#pw-err')).includes('Enter a password') && (await submit.isDisabled()), 'an empty password is refused when the option is on')
  await v.fill('#pw', 'x'.repeat(400))
  ok((await v.inputValue('#pw')).length === 256, 'the password box stops at 256 characters')
  await v.uncheck('text=Require a password')

  await v.check('text=Also send the link by email')
  ok((await v.getAttribute('#up-to', 'maxlength')) === '2000' && (await v.getAttribute('#up-msg', 'maxlength')) === '1000' && (await v.getAttribute('#up-name', 'maxlength')) === '60' && (await v.getAttribute('#up-from', 'maxlength')) === '254', 'the email boxes have length limits')
  for (const bad of ['plainaddress', 'a@b', 'Bob <b@x.com>', 'a@example.com,@example.com', 'a b@example.com']) {
    await v.fill('#up-to', bad)
    ok((await v.textContent('#up-hint')).includes("isn't a valid") && (await submit.isDisabled()), `recipient "${bad}" is refused`)
  }
  await v.fill('#up-to', 'good@example.com')
  await v.fill('#up-from', 'not an email')
  ok((await v.textContent('#up-hint')).includes('Your email address'), 'a bad reply-to address is refused')
  await v.fill('#up-from', '')
  await v.fill('#up-msg', 'x'.repeat(30))
  ok((await v.textContent('main')).includes('30 / 1000'), 'the message shows how much room is left')
  await v.uncheck('text=Also send the link by email')

  // hostile text is shown as text and never runs
  await v.click('button[aria-label="Remove ok.txt"]')
  const evilName = '"><img src=x onerror="window.__xss=1">.txt'
  await pick(evilName)
  await v.check('text=Also send the link by email')
  await v.fill('#up-to', 'friend@example.com')
  await v.fill('#up-name', '<script>window.__xss=2</script>')
  await v.fill('#up-msg', '<img src=x onerror="window.__xss=3"> <script>window.__xss=4</script>')
  await v.click('button[type=submit]')
  await v.waitForURL('**/created/**', { timeout: 20_000 })
  await v.waitForSelector('.results li')
  const evilToken = v.url().split('/').pop()
  ok((await v.evaluate(() => window.__xss)) === undefined && dialogs.length === 0, 'the created page runs none of the hostile text')
  await v.goto(`${BASE}/recent`)
  await v.waitForSelector(`a[href="/created/${evilToken}"]`)
  await v.goto(`${BASE}/d/${evilToken}`)
  await v.waitForSelector('.dl-row .name')
  ok((await v.textContent('.dl-row .name')) === evilName, 'the download page shows the file name literally, as text')
  ok((await v.locator('.dl-row img, main img[src="x"]').count()) === 0 && (await v.evaluate(() => window.__xss)) === undefined && dialogs.length === 0, 'and nothing in it runs')

  // other boxes
  await v.goto(`${BASE}/open`)
  ok((await v.getAttribute('#open-link', 'maxlength')) === '2048', 'the open-link box has a length limit')
  await v.fill('#open-link', '<script>alert(1)</script>')
  await v.press('#open-link', 'Enter')
  ok((await v.textContent('[role=alert]')).includes("doesn't look like") && v.url().endsWith('/open'), 'script text in the open-link box is refused, not opened')
  await v.goto(`${BASE}/d/locked123456`)
  await v.waitForSelector('#pw')
  ok((await v.getAttribute('#pw', 'maxlength')) === '256', 'the unlock password box has a length limit')
  await v.fill('#pw', "' OR '1'='1")
  await v.click('button:has-text("Unlock")')
  await v.waitForSelector('[role=alert]')
  ok((await v.textContent('[role=alert]')).includes("isn't right"), 'an injection-style password is just a wrong password')
  await v.close()

  // loading screen: while the app's main script is still downloading, the page shows the animation, not a blank
  const bootSnap = (page) => page.evaluate(() => {
    const c = document.querySelector('#boot canvas')
    const d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data
    const colours = new Set()
    let hash = 0
    for (let i = 0; i < d.length; i += 4) {
      colours.add((d[i] << 16) | (d[i + 1] << 8) | d[i + 2])
      hash = (hash * 31 + d[i] + d[i + 1] * 7 + d[i + 2] * 13) | 0
    }
    return { colours: colours.size, hash }
  })
  const slowLoad = async (options) => {
    const ctx = await browser.newContext({ viewport: { width: 900, height: 700 }, ...options })
    const pg = await ctx.newPage()
    const errs = []
    pg.on('pageerror', (e) => errs.push(e.message))
    await pg.route('**/assets/index-*.js', async (route) => {
      await new Promise((r) => setTimeout(r, 2500))
      await route.continue()
    })
    await pg.goto(`${BASE}/`, { waitUntil: 'commit' })
    return { ctx, pg, errs }
  }

  const live = await slowLoad({})
  await live.pg.waitForSelector('#boot canvas', { state: 'visible' })
  await live.pg.waitForTimeout(500)
  const a1 = await bootSnap(live.pg)
  await live.pg.waitForTimeout(430)
  const a2 = await bootSnap(live.pg)
  ok(a1.colours > 25, `before the app has loaded, the page already shows a drawn scene (${a1.colours} colours, not a blank page)`)
  ok(a1.hash !== a2.hash, 'and it is animating')
  ok((await live.pg.textContent('#boot')).toLowerCase().includes('loading'), 'with the word "Loading" for screen readers')
  ok(await live.pg.evaluate(() => getComputedStyle(document.body).backgroundColor === 'rgb(10, 10, 10)'), 'the background is dark from the first frame (no white flash)')
  await live.pg.waitForSelector('text=Create link', { timeout: 20_000 })
  ok((await live.pg.locator('#boot').count()) === 0, 'once the app has loaded, the loading screen is gone')
  ok(live.errs.length === 0, `no errors from the loading screen${live.errs.length ? ': ' + live.errs.join(' | ') : ''}`)
  await live.ctx.close()

  const calm = await slowLoad({ reducedMotion: 'reduce' })
  await calm.pg.waitForSelector('#boot canvas', { state: 'visible' })
  await calm.pg.waitForTimeout(500)
  const c1 = await bootSnap(calm.pg)
  await calm.pg.waitForTimeout(430)
  const c2 = await bootSnap(calm.pg)
  ok(c1.colours > 25 && c1.hash === c2.hash, 'with "reduce motion" on, it shows a still picture instead of animating')
  await calm.ctx.close()

  // the splash waits for the animation even when the app is ready sooner (automated browsers skip it unless asked)
  const lingerCtx = await browser.newContext({ viewport: { width: 900, height: 700 } })
  const lp = await lingerCtx.newPage()
  const began = Date.now()
  await lp.goto(`${BASE}/?splash=2200`)
  await lp.waitForSelector('#boot canvas')
  await lp.waitForTimeout(900)
  ok((await lp.locator('#boot').count()) === 1 && !(await lp.locator('#root').isVisible()), 'the app is ready underneath, but the animation is still showing (the app waits its turn)')
  await lp.waitForSelector('#boot', { state: 'detached', timeout: 10_000 })
  const waited = Date.now() - began
  ok(waited >= 2000 && waited < 4500, `the animation stayed up for about ${(waited / 1000).toFixed(1)} s before the app appeared`)
  await lp.waitForSelector('text=Create link')
  ok(await lp.locator('#root').isVisible(), 'then the app appears')
  await lingerCtx.close()

  const quick = await browser.newContext({ viewport: { width: 900, height: 700 } })
  const qp = await quick.newPage()
  const qStart = Date.now()
  await qp.goto(`${BASE}/`)
  await qp.waitForSelector('text=Create link')
  ok(Date.now() - qStart < 2500, 'automated browsers are not held up by the splash')
  await quick.close()

  // built-in example links
  await r.goto(`${BASE}/d/demo1234abcd`)
  await r.waitForSelector('text=Download all as .zip')
  ok((await r.textContent('main')).includes('NitroDrp demo sent you'), 'example link works')
  await r.goto(`${BASE}/d/expired00000`)
  await r.waitForSelector('text=gone')
  ok(true, 'expired example shows the "gone" page')
  ok(await r.locator('a[href="/open"]').isVisible(), 'the gone page offers to try a different link')

  // branding footer: text, link and logo on a page recipients see
  await r.goto(`${BASE}/d/demo1234abcd`)
  await r.waitForSelector('footer.site-footer')
  ok((await r.textContent('footer p')) === 'Powered by jadedmedia.ca, used for personal and customer files', 'footer says who powers it and what for')
  ok((await r.getAttribute('footer p a', 'href')) === 'https://jadedmedia.ca', 'footer links to jadedmedia.ca')
  ok(await r.evaluate(() => { const i = document.querySelector('footer img'); return i.complete && i.naturalWidth > 0 }), 'footer logo loads')
  const short = await context.newPage()
  await short.goto(`${BASE}/d/expired00000`)
  await short.waitForSelector('footer.site-footer')
  ok(await short.evaluate(() => document.querySelector('footer').getBoundingClientRect().bottom === innerHeight), 'footer sits at the bottom of a short page')

  // the demo must never talk to a server
  ok(outbound.length === 0, `no API or third-party requests${outbound.length ? ': ' + outbound.join(', ') : ''}`)
  ok(errors.length === 0, `no browser errors${errors.length ? ': ' + errors.join(' | ') : ''}`)

  // embedding: a page on another origin hosts the demo in an iframe and resizes it from the posted height.
  // (Served from a local port: Chromium refuses to let a non-local page frame localhost.)
  const hostServer = createServer((_, res) => {
    res.setHeader('Content-Type', 'text/html')
    res.end(`<iframe id="nd" src="${BASE}/?embed=1" style="width:100%;border:0;height:300px"></iframe>
<script>addEventListener('message',e=>{if(e.data&&e.data.type==='nitrodrp:height')document.getElementById('nd').style.height=e.data.height+'px'})</script>`)
  })
  await new Promise((resolve) => hostServer.listen(0, '127.0.0.1', resolve))
  const host = await context.newPage()
  await host.goto(`http://localhost:${hostServer.address().port}/`)
  const frame = host.frameLocator('#nd')
  await frame.locator('h1').waitFor()
  await host.waitForTimeout(500)
  const before = await host.evaluate(() => document.getElementById('nd').getBoundingClientRect().height)
  ok((await frame.locator('header.header').count()) === 0, 'embed hides the site header')
  ok(before !== 300, `embedded iframe resized itself (${Math.round(before)}px)`)
  await frame.locator('input[type=file]').setInputFiles(Array(9).fill(smallFile))
  await host.waitForTimeout(500)
  const after = await host.evaluate(() => document.getElementById('nd').getBoundingClientRect().height)
  ok(after > before, `iframe grows with its content (${Math.round(before)} -> ${Math.round(after)}px)`)
  // ...and shrinks back when the content does (the footer's full-height layout must not pin it open)
  for (let i = 0; i < 9; i++) await frame.locator('button[aria-label^="Remove"]').first().click()
  await host.waitForTimeout(500)
  const shrunk = await host.evaluate(() => document.getElementById('nd').getBoundingClientRect().height)
  ok(shrunk < after - 100, `iframe shrinks when content is removed (${Math.round(after)} -> ${Math.round(shrunk)}px)`)
  hostServer.close()
} catch (err) {
  ok(false, `unexpected error: ${err.message}`)
} finally {
  await browser.close()
}

const failed = results.filter((r) => !r).length
console.log(failed ? `\n${failed} check(s) failed.` : '\nAll checks passed.')
process.exit(failed ? 1 : 0)
