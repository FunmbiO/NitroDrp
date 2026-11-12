// Takes the screenshots used in the README from the demo build.
//   npm run build:demo && npx vite preview --port 4174 &
//   npm run screenshots          # writes ../docs/screenshots/*.png
import { chromium } from 'playwright'
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { randomBytes } from 'node:crypto'

const BASE = (process.env.BASE_URL ?? 'http://localhost:4174').replace(/\/$/, '')
const PUBLIC_ORIGIN = process.env.PUBLIC_ORIGIN ?? 'https://demo.jadedmedia.ca'
const OUT = resolve(import.meta.dirname, '../../docs/screenshots')
mkdirSync(OUT, { recursive: true })

const dir = mkdtempSync(join(tmpdir(), 'nitrodrp-shots-'))
const files = ['Smith-Wedding-Gallery.zip', 'Ceremony-Highlights.mp4', 'Album-Proofs.pdf'].map((name, i) => {
  const path = join(dir, name)
  writeFileSync(path, randomBytes([2_400_000, 1_800_000, 86_000][i]))
  return path
})

const browser = await chromium.launch()
const desktop = await browser.newContext({ viewport: { width: 1200, height: 900 }, deviceScaleFactor: 2 })
const page = await desktop.newPage()

// 1. upload screen with files chosen
await page.goto(`${BASE}/`)
await page.setInputFiles('input[type=file]', files)
await page.waitForSelector('text=Album-Proofs.pdf')
await page.screenshot({ path: join(OUT, '1-upload.png') })

// 2. link created
await page.click('button[type=submit]')
await page.waitForURL('**/created/**')
await page.waitForSelector('text=Copy link')
// The link is built from wherever the build is being served (localhost here). Show the real address instead.
await page.evaluate((origin) => {
  const input = document.getElementById('link')
  input.value = input.value.replace(location.origin, origin)
}, PUBLIC_ORIGIN)
await page.screenshot({ path: join(OUT, '2-link-created.png') })

// 3. what the recipient sees
const recipient = await desktop.newPage()
await recipient.goto(`${BASE}/d/demo1234abcd`)
await recipient.waitForSelector('text=Download all as .zip')
await recipient.screenshot({ path: join(OUT, '3-download.png') })

// 4. the same page on a phone
const phone = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2 })
const mobile = await phone.newPage()
await mobile.goto(`${BASE}/d/demo1234abcd`)
await mobile.waitForSelector('text=Download all as .zip')
await mobile.screenshot({ path: join(OUT, '4-mobile.png') })

await browser.close()
console.log(`Wrote 4 screenshots to ${OUT}`)
