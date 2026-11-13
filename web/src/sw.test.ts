import { describe, expect, it } from 'vitest'
import source from '../public/sw.js?raw'

/**
 * Runs public/sw.js as the browser would (with a stand-in for the worker's `self`) and checks what it does with a
 * push and with a tap on the notification. The real service worker can't be observed showing a notification in a
 * headless browser, so this is where that logic is proven.
 */
type Handler = (event: Record<string, unknown>) => void
function load() {
  const handlers: Record<string, Handler> = {}
  const shown: { title: string; options: Record<string, unknown> }[] = []
  const opened: string[] = []
  const focused: { url: string; navigatedTo?: string }[] = []
  let windows: { url: string; focus: () => Promise<unknown> }[] = []
  const self = {
    location: { origin: 'https://drop.example.com' },
    addEventListener: (type: string, fn: Handler) => void (handlers[type] = fn),
    skipWaiting: () => Promise.resolve(),
    registration: { showNotification: (title: string, options: Record<string, unknown>) => { shown.push({ title, options }); return Promise.resolve() } },
    clients: {
      claim: () => Promise.resolve(),
      matchAll: () => Promise.resolve(windows),
      openWindow: (url: string) => { opened.push(url); return Promise.resolve() },
    },
  }
  new Function('self', 'URL', 'caches', 'fetch', 'Response', source)(self, URL, {}, () => {}, Response)
  const settle = async (fn: Handler, event: Record<string, unknown>) => {
    const waits: Promise<unknown>[] = []
    fn({ ...event, waitUntil: (p: Promise<unknown>) => waits.push(p) })
    await Promise.all(waits)
  }
  return {
    shown, opened, focused,
    setWindows: (urls: string[]) =>
      void (windows = urls.map((url) => {
        const entry = { url, focus: () => { focused.push({ url }); return Promise.resolve({ navigate: (to: string) => void (focused[focused.length - 1].navigatedTo = to) }) } }
        return entry
      })),
    push: (data: unknown) => settle(handlers.push, { data: data === undefined ? null : { json: () => (typeof data === 'string' ? JSON.parse(data) : data) } }),
    click: () => settle(handlers.notificationclick, { notification: { close: () => {} } }),
  }
}

describe('service worker: push', () => {
  it('shows the title, text and kind from the message', async () => {
    const sw = load()
    await sw.push({ title: 'Link downloaded', body: 'Someone downloaded one of your links.', tag: 'downloaded' })
    expect(sw.shown).toHaveLength(1)
    expect(sw.shown[0].title).toBe('Link downloaded')
    expect(sw.shown[0].options).toMatchObject({ body: 'Someone downloaded one of your links.', tag: 'downloaded', icon: '/icons/icon-192.png', data: { url: '/notifications' } })
  })

  it('always points at the notifications page, whatever address the message carries', async () => {
    const sw = load()
    await sw.push({ title: 'x', body: 'y', url: 'https://evil.example.com/steal' })
    expect(sw.shown[0].options.data).toEqual({ url: '/notifications' })
  })

  it('shows a plain default for a message with no data, or that is not JSON', async () => {
    const sw = load()
    await sw.push(undefined)
    await sw.push('not json {')
    expect(sw.shown.map((s) => s.title)).toEqual(['NitroDrp', 'NitroDrp'])
    expect(sw.shown[0].options.body).toBe('Something happened.')
  })

  it('limits how much text a message can put on screen, and ignores wrong types', async () => {
    const sw = load()
    await sw.push({ title: 'T'.repeat(500), body: 'B'.repeat(5000), tag: { not: 'a string' } })
    expect(String(sw.shown[0].title).length).toBe(80)
    expect(String(sw.shown[0].options.body).length).toBe(200)
    expect(sw.shown[0].options.tag).toBe('nitrodrp')
  })
})

describe('service worker: tapping a notification', () => {
  it('opens the notifications page when the app is closed', async () => {
    const sw = load()
    await sw.click()
    expect(sw.opened).toEqual(['https://drop.example.com/notifications'])
  })

  it('brings an open window of this app forward and sends it to the page', async () => {
    const sw = load()
    sw.setWindows(['https://drop.example.com/recent'])
    await sw.click()
    expect(sw.opened).toEqual([])
    expect(sw.focused).toEqual([{ url: 'https://drop.example.com/recent', navigatedTo: 'https://drop.example.com/notifications' }])
  })

  it("never takes over a window of some other site", async () => {
    const sw = load()
    sw.setWindows(['https://elsewhere.example.net/'])
    await sw.click()
    expect(sw.focused).toEqual([])
    expect(sw.opened).toEqual(['https://drop.example.com/notifications'])
  })
})
