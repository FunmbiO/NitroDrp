// Browser test of the notifications page in the demo build (no backend needed: the demo simulates it).
//
//   npm run build:demo && npx vite preview --port 4174 &
//   npm run e2e:notifications           # BASE_URL defaults to http://localhost:4174
import { chromium } from 'playwright'
import { mkdtempSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { randomBytes } from 'node:crypto'

const BASE = (process.env.BASE_URL ?? 'http://localhost:4174').replace(/\/$/, '')
const SHOT = process.env.SHOT_DIR
const results = []
const ok = (pass, message) => {
  results.push(pass)
  console.log(`${pass ? 'PASS' : 'FAIL'} ${message}`)
}

const dir = mkdtempSync(join(tmpdir(), 'nitrodrp-notes-'))
const file = join(dir, 'Contract.pdf')
writeFileSync(file, randomBytes(50_000))

const browser = await chromium.launch()
const context = await browser.newContext({ viewport: { width: 1000, height: 900 }, acceptDownloads: true })
const page = await context.newPage()
const errors = []
page.on('pageerror', (e) => errors.push(e.message))
page.on('console', (m) => m.type() === 'error' && !/fonts|favicon|Failed to load resource/.test(m.text()) && errors.push(m.text()))

const badge = async () => ((await page.locator('.bell .badge').count()) ? (await page.locator('.bell .badge').textContent()).trim() : '')

try {
  await page.goto(`${BASE}/`)
  await page.waitForSelector('.bell')
  ok((await badge()) === '2', 'the bell shows the 2 example notifications as unread')

  await page.click('.bell')
  await page.waitForSelector('.note-row')
  ok((await page.locator('.note-row').count()) === 2, 'the notifications page lists them')
  ok((await page.locator('.note-row.unread').count()) === 2, 'both are highlighted as new on this visit')
  await page.waitForFunction(() => !document.querySelector('.bell .badge'))
  ok((await badge()) === '', 'opening the page marks them read: the badge is gone')
  ok(!(await page.textContent('main')).match(/\.(pdf|zip|mp4)/), 'no file names appear on the page')
  if (SHOT) await page.screenshot({ path: join(SHOT, 'notifications-desktop.png') })

  // Make a link with a limit of one, download it: the owner is told twice.
  await page.goto(`${BASE}/`)
  await page.locator('input[type=file]').setInputFiles(file)
  await page.fill('#limit', '1')
  await page.getByRole('button', { name: /^(create|get a|upload)/i }).first().click()
  await page.waitForURL(/\/created\//, { timeout: 30000 })
  const token = new URL(page.url()).pathname.split('/').pop()

  const dl = await context.newPage()
  await dl.goto(`${BASE}/d/${token}`)
  await dl.getByRole('button', { name: /download/i }).first().click()
  await dl.waitForTimeout(800)
  await dl.close()

  await page.goto(`${BASE}/`)
  await page.waitForSelector('.bell')
  const n = await badge()
  ok(n === '2', `a download tells the owner (bell shows ${n || 'nothing'}): the download and the limit being reached`)
  await page.click('.bell')
  await page.waitForSelector('.note-row')
  const titles = await page.locator('.note-row strong').allTextContents()
  ok(titles.includes('Download limit reached') && titles.includes('Link downloaded'), `the feed has them: ${titles.join(', ')}`)

  // Turn on notifications on this "device" (simulated), choose kinds, test, remove.
  await page.getByRole('button', { name: /turn on notifications/i }).click()
  await page.waitForSelector('.device-row')
  ok((await page.locator('.device-row .tag').count()) === 1, 'the device is listed and marked as this device')
  ok((await page.locator('.device-row input[type=checkbox]:checked').count()) === 4, 'all four kinds are on by default')
  await page.getByLabel('A link is about to expire').uncheck()
  await page.waitForFunction(() => document.querySelectorAll('.device-row input:checked').length === 3)
  ok(true, 'a kind can be turned off')
  await page.getByRole('button', { name: /send a test/i }).click()
  await page.waitForSelector('[role=status]')
  ok(/Test sent/.test(await page.textContent('[role=status]')), 'a test message can be sent')
  await page.getByLabel(/email me a short summary/i).check()
  await page.waitForFunction(() => document.querySelector('input[type=checkbox][disabled]') === null)
  ok(await page.getByLabel(/email me a short summary/i).isChecked(), 'the daily summary can be switched on')
  if (SHOT) await page.screenshot({ path: join(SHOT, 'notifications-devices.png'), fullPage: true })

  // survives a reload
  await page.reload()
  await page.waitForSelector('.device-row')
  ok((await page.locator('.device-row input:checked').count()) === 3 && (await page.getByLabel(/email me a short summary/i).isChecked()), 'choices are remembered after a reload')

  page.once('dialog', (d) => d.accept())
  await page.getByRole('button', { name: /^remove$/i }).click()
  await page.waitForFunction(() => !document.querySelector('.device-row'))
  ok(true, 'the device can be removed')

  await page.getByRole('button', { name: /clear all/i }).click()
  await page.waitForFunction(() => !document.querySelector('.note-row'))
  ok(/Nothing yet/.test(await page.textContent('main')), 'the list can be cleared')

  // phone width
  await page.setViewportSize({ width: 375, height: 800 })
  await page.reload()
  await page.waitForSelector('h1')
  ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), 'no sideways scrolling on a phone')
  if (SHOT) await page.screenshot({ path: join(SHOT, 'notifications-phone.png'), fullPage: true })
  ok(errors.length === 0, `no errors in the page${errors.length ? ': ' + errors.join(' | ') : ''}`)
} catch (err) {
  ok(false, `unexpected error: ${err.message}`)
} finally {
  await browser.close()
}

const failed = results.filter((r) => !r).length
console.log(failed ? `\n${failed} check(s) failed.` : '\nAll checks passed.')
process.exit(failed ? 1 : 0)
