import { describe, expect, it } from 'vitest'
import { bytesToUrlBase64, deviceIdFor, deviceLabel, urlBase64ToBytes } from './push'

describe('push helpers', () => {
  it('converts a VAPID key both ways', () => {
    const key = 'BEl62iUYgUivxIkv69yViEuiBIa-ib37J8xYjEzGhKcr_dHzDaN6dDKdn0m1QeFvCs-fBvXp4rvI_s8i7EAhG0I'
    const bytes = urlBase64ToBytes(key)
    expect(bytes.length).toBe(65)
    expect(bytes[0]).toBe(4)
    expect(bytesToUrlBase64(bytes)).toBe(key)
  })

  it('handles keys of every padding length', () => {
    for (const n of [1, 2, 3, 15, 16, 17, 64, 65]) {
      const bytes = Uint8Array.from({ length: n }, (_, i) => (i * 37 + 11) % 256)
      expect(Array.from(urlBase64ToBytes(bytesToUrlBase64(bytes)))).toEqual(Array.from(bytes))
    }
  })

  it('names devices from the browser string', () => {
    expect(deviceLabel('Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)')).toBe('iPhone')
    expect(deviceLabel('Mozilla/5.0 (Linux; Android 14; Pixel 8) Mobile Safari')).toBe('Android phone')
    expect(deviceLabel('Mozilla/5.0 (Linux; Android 14; SM-X900) Safari')).toBe('Android tablet')
    expect(deviceLabel('Mozilla/5.0 (Windows NT 10.0; Win64; x64)')).toBe('Windows PC')
    expect(deviceLabel('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)')).toBe('Mac')
    expect(deviceLabel('???')).toBe('This device')
  })

  it('gives a device the same id the server does', async () => {
    // The API test PushPolicyTests computes the same value from the same endpoint.
    const id = await deviceIdFor('https://fcm.googleapis.com/x')
    expect(id).toMatch(/^[0-9a-f]{32}$/)
    expect(await deviceIdFor('https://fcm.googleapis.com/x')).toBe(id)
    expect(await deviceIdFor('https://fcm.googleapis.com/y')).not.toBe(id)
  })
})
