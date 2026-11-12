// Records the walkthrough GIF used at the top of the README, from the demo build.
//   npm run build:demo && npx vite preview --port 4174 &
//   npm run record               # writes ../docs/screenshots/walkthrough.gif   (needs ffmpeg)
import { chromium } from 'playwright'
import { execFileSync } from 'node:child_process'
import { mkdtempSync, mkdirSync, writeFileSync, renameSync, statSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { randomBytes } from 'node:crypto'

const BASE = (process.env.BASE_URL ?? 'http://localhost:4174').replace(/\/$/, '')
const PUBLIC_ORIGIN = process.env.PUBLIC_ORIGIN ?? 'https://demo.jadedmedia.ca'
const OUT = resolve(import.meta.dirname, '../../docs/screenshots/walkthrough.gif')
const work = mkdtempSync(join(tmpdir(), 'nitrodrp-rec-'))
const sizes = [2_400_000, 1_800_000, 86_000]
const files = ['Smith-Wedding-Gallery.zip', 'Ceremony-Highlights.mp4', 'Album-Proofs.pdf'].map((name, i) => {
  const path = join(work, name)
  writeFileSync(path, randomBytes(sizes[i]))
  return path
})

const size = { width: 1160, height: 880 }
const browser = await chromium.launch()
const context = await browser.newContext({ viewport: size, deviceScaleFactor: 1, recordVideo: { dir: work, size } })
const page = await context.newPage()
const pause = (ms) => page.waitForTimeout(ms)

// 1. pick files, set it up
await page.goto(`${BASE}/`)
await pause(1500)
await page.setInputFiles('input[type=file]', files)
await pause(1600)
await page.click('button:has-text("30 days")')
await pause(700)
await page.check('text=Require a password')
await page.type('#pw', 'sunny-days-2026', { delay: 70 })
await pause(500)
await page.type('#limit', '5', { delay: 150 })
await pause(500)
await page.check('text=Also send the link by email')
await page.type('#up-to', 'client@example.com', { delay: 55 })
await page.type('#up-name', 'Amina', { delay: 80 })
await page.type('#up-msg', 'Here are the final photos. The password is on its way separately.', { delay: 28 })
await pause(1200)

// 2. create the link
await page.click('button[type=submit]')
await page.waitForURL('**/created/**', { timeout: 30_000 })
await page.waitForSelector('.results li')
// The page was served from localhost: show the real address instead, as in the README screenshots.
await page.evaluate((origin) => {
  const input = document.getElementById('link')
  input.value = input.value.replace(location.origin, origin)
}, PUBLIC_ORIGIN)
await pause(3600)
const link = await page.inputValue('#link')

// 3. open it the way a recipient would, by pasting it
await page.click('a.nav-link:has-text("Open a link")')
await pause(900)
await page.type('#open-link', link, { delay: 12 })
await pause(700)
await page.press('#open-link', 'Enter')
await page.waitForSelector('#pw')
await pause(1300)
await page.type('#pw', 'sunny-days-2026', { delay: 70 })
await pause(500)
await page.click('button:has-text("Unlock")')
await page.waitForSelector('.dl-row')
await pause(3200)

await context.close() // finishes the video file
const video = await page.video().path()
await browser.close()

// webm -> small GIF: 10 fps, 760 px wide, one shared 96-colour palette
mkdirSync(resolve(OUT, '..'), { recursive: true })
const filter = 'fps=10,scale=760:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=96:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle'
execFileSync('ffmpeg', ['-y', '-loglevel', 'error', '-i', video, '-vf', filter, '-loop', '0', join(work, 'out.gif')])
renameSync(join(work, 'out.gif'), OUT)
console.log(`Wrote ${OUT} (${(statSync(OUT).size / 1024 / 1024).toFixed(1)} MB)`)
