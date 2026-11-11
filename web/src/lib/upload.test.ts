import { describe, expect, it, vi } from 'vitest'
import { uploadParts } from './upload'

const blob = (n: number) => new Blob([new Uint8Array(n)])

describe('uploadParts', () => {
  it('uploads every part once and returns them in order', async () => {
    const seen: number[] = []
    const parts = await uploadParts(blob(25), 10, 3, {
      presign: async (n) => `url-${n}`,
      putPart: async (url, body, onProgress) => {
        seen.push(body.size)
        onProgress(body.size)
        return `"etag-${url}"`
      },
      onProgress: () => {},
      sleep: async () => {},
    })
    expect(parts.map((p) => p.partNumber)).toEqual([1, 2, 3])
    expect(seen.sort((a, b) => a - b)).toEqual([5, 10, 10]) // last part is the remainder
  })

  it('retries a failing part with a fresh url and still finishes', async () => {
    const presign = vi.fn(async (n: number) => `url-${n}`)
    let failed = false
    const parts = await uploadParts(blob(20), 10, 2, {
      presign,
      putPart: async (url) => {
        if (url === 'url-2' && !failed) {
          failed = true
          throw new Error('boom')
        }
        return 'etag'
      },
      onProgress: () => {},
      sleep: async () => {},
    })
    expect(parts).toHaveLength(2)
    expect(presign.mock.calls.filter(([n]) => n === 2)).toHaveLength(2)
  })

  it('gives up after maxAttempts', async () => {
    await expect(
      uploadParts(blob(10), 10, 1, {
        presign: async () => 'u',
        putPart: async () => {
          throw new Error('nope')
        },
        onProgress: () => {},
        sleep: async () => {},
        maxAttempts: 3,
      }),
    ).rejects.toThrow('nope')
  })

  it('reports progress that ends at the file size', async () => {
    let last = 0
    await uploadParts(blob(30), 10, 3, {
      presign: async () => 'u',
      putPart: async (_u, body, onProgress) => {
        onProgress(body.size / 2)
        return 'e'
      },
      onProgress: (b) => (last = b),
      sleep: async () => {},
    })
    expect(last).toBe(30)
  })
})
