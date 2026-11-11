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

  it('skips parts that were already uploaded and still returns the full, ordered list', async () => {
    const putCalls: number[] = []
    let last = 0
    const parts = await uploadParts(blob(25), 10, 3, {
      presign: async (n) => `url-${n}`,
      putPart: async (url) => {
        putCalls.push(Number(url.split('-')[1]))
        return 'new'
      },
      alreadyUploaded: [
        { partNumber: 1, eTag: 'old1' },
        { partNumber: 3, eTag: 'old3' },
      ],
      onProgress: (b) => (last = b),
      sleep: async () => {},
    })
    expect(putCalls).toEqual([2])
    expect(parts).toEqual([
      { partNumber: 1, eTag: 'old1' },
      { partNumber: 2, eTag: 'new' },
      { partNumber: 3, eTag: 'old3' },
    ])
    expect(last).toBe(25) // part 3 is the 5-byte remainder
  })

  it('reports already-uploaded bytes straight away so a resumed bar starts where it left off', async () => {
    const seen: number[] = []
    await uploadParts(blob(30), 10, 3, {
      presign: async () => 'u',
      putPart: async () => 'e',
      alreadyUploaded: [{ partNumber: 1, eTag: 'a' }, { partNumber: 2, eTag: 'b' }],
      onProgress: (b) => seen.push(b),
      sleep: async () => {},
    })
    expect(seen[0]).toBe(20)
  })

  it('ignores listed parts outside the file range', async () => {
    const parts = await uploadParts(blob(10), 10, 1, {
      presign: async () => 'u',
      putPart: async () => 'e',
      alreadyUploaded: [{ partNumber: 9, eTag: 'junk' }],
      onProgress: () => {},
      sleep: async () => {},
    })
    expect(parts).toEqual([{ partNumber: 1, eTag: 'e' }])
  })
})
