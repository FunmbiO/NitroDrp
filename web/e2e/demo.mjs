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

  // built-in example links
  await r.goto(`${BASE}/d/demo1234abcd`)
  await r.waitForSelector('text=Download all as .zip')
  ok((await r.textContent('main')).includes('NitroDrp demo sent you'), 'example link works')
  await r.goto(`${BASE}/d/expired00000`)
  await r.waitForSelector('text=gone')
  ok(true, 'expired example shows the "gone" page')

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
  hostServer.close()
} catch (err) {
  ok(false, `unexpected error: ${err.message}`)
} finally {
  await browser.close()
}

const failed = results.filter((r) => !r).length
console.log(failed ? `\n${failed} check(s) failed.` : '\nAll checks passed.')
process.exit(failed ? 1 : 0)
