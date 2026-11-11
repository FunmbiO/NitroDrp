import { beforeEach, describe, expect, it } from 'vitest'
import { ApiError } from './errors'
import { createDemoBackend, type DemoState, type DemoStore } from './demoApi'

const memoryStore = (): DemoStore & { state: DemoState | null } => {
  const s = { state: null as DemoState | null, load: () => s.state, save: (x: DemoState) => void (s.state = JSON.parse(JSON.stringify(x))) }
  return s
}

let clock = 0
let store: ReturnType<typeof memoryStore>
const make = () => createDemoBackend(store, { now: () => clock, uploadMs: () => 0, makeUrl: (b) => `blob:${b.length}` })

const code = async (p: Promise<unknown>) => {
  try {
    await p
    return 'ok'
  } catch (e) {
    return e instanceof ApiError ? e.code : String(e)
  }
}

beforeEach(() => {
  clock = 1_800_000_000_000
  store = memoryStore()
})

/** Same sender flow as the real UI: create, upload each part, complete, finalize. */
async function publish(b: ReturnType<typeof make>, o: { size?: number; password?: string; limit?: number; files?: { name: string; size: number }[] } = {}) {
  const files = o.files ?? [{ name: 'a.zip', size: o.size ?? 40_000_000 }]
  const c = await b.api.createShare('k', { files: files.map((f) => ({ ...f, contentType: 'x' })), expiresInDays: 7, password: o.password, downloadLimit: o.limit })
  for (const f of c.files) {
    const parts = []
    for (let n = 1; n <= f.partCount; n++) {
      const url = await b.api.presign(c.token, f.id, c.ownerKey, n)
      parts.push({ partNumber: n, eTag: await b.putPart(url, new Blob([new Uint8Array(1)]), () => {}) })
    }
    await b.api.completeFile(c.token, f.id, c.ownerKey, parts)
  }
  await b.api.finalize(c.token, c.ownerKey)
  return c
}

describe('demo backend', () => {
  it('ships evergreen examples, including an expired one', async () => {
    const { api } = make()
    expect((await api.getShare('demo1234abcd')).fileCount).toBe(3)
    expect(await code(api.getShare('expired00000'))).toBe('expired')
    expect((await api.getShare('locked123456')).files).toBeNull() // names hidden until unlocked
    expect((await api.unlock('locked123456', 'nitro')).files).toHaveLength(2)
  })

  it('runs the full sender flow and serves the link', async () => {
    const b = make()
    const c = await publish(b, { size: 40_000_000 })
    expect(c.files[0].partCount).toBe(3)
    const v = await b.api.getShare(c.token)
    expect(v.totalSize).toBe(40_000_000)
    expect((await b.api.download(c.token)).length).toBe(1)
  })

  it('keeps shares between loads (another tab) via the store', async () => {
    const c = await publish(make())
    expect((await make().api.getShare(c.token)).fileCount).toBe(1)
  })

  it('rejects unfinished, bad and blocked uploads like the server', async () => {
    const { api } = make()
    const c = await api.createShare('k', { files: [{ name: 'a.zip', size: 20_000_000, contentType: 'x' }], expiresInDays: 7 })
    expect(await code(api.finalize(c.token, c.ownerKey))).toBe('incomplete')
    expect(await code(api.getShare(c.token))).toBe('not_found') // pending links are invisible
    expect(await code(api.completeFile(c.token, 'f1', c.ownerKey, [{ partNumber: 1, eTag: 'e' }]))).toBe('bad_parts')
    expect(await code(api.presign(c.token, 'f1', 'wrong', 1))).toBe('bad_owner_key')
    expect(await code(api.createShare('k', { files: [{ name: 'x.EXE', size: 5, contentType: 'x' }], expiresInDays: 7 }))).toBe('blocked_type')
    expect(await code(api.createShare('k', { files: [{ name: 'x', size: 5, contentType: 'x' }], expiresInDays: 3 }))).toBe('bad_expiry')
  })

  it('enforces passwords, download limits and expiry', async () => {
    const b = make()
    const pw = await publish(b, { password: 'hunter2' })
    expect(await code(b.api.download(pw.token))).toBe('password_required')
    expect(await code(b.api.zipLink(pw.token, 'nope'))).toBe('wrong_password')
    expect(await code(b.api.zipLink(pw.token, 'hunter2'))).toBe('ok')

    const lim = await publish(b, { limit: 1 })
    expect(await code(b.api.download(lim.token))).toBe('ok')
    expect(await code(b.api.download(lim.token))).toBe('limit_reached')
    expect((await publish(b, { limit: 2 }).then((c) => b.api.getShare(c.token))).downloadsRemaining).toBe(2)

    const soon = await publish(b)
    clock += 8 * 86_400_000
    expect(await code(b.api.getShare(soon.token))).toBe('expired')
  })

  it('resumes: reports parts already uploaded and lets the rest finish', async () => {
    const b = make()
    const c = await b.api.createShare('k', { files: [{ name: 'big.zip', size: 40_000_000, contentType: 'x' }], expiresInDays: 7 })
    await b.putPart(await b.api.presign(c.token, 'f1', c.ownerKey, 1), new Blob([new Uint8Array(1)]), () => {})
    await b.putPart(await b.api.presign(c.token, 'f1', c.ownerKey, 2), new Blob([new Uint8Array(1)]), () => {})

    const status = await b.api.uploadStatus(c.token, c.ownerKey)
    expect(status.files[0].parts.map((p) => p.partNumber).sort()).toEqual(['1', '2'].map(Number))
    expect(status.finalized).toBe(false)
  })

  it('deleting removes the link', async () => {
    const b = make()
    const c = await publish(b)
    await b.api.deleteShare(c.token, c.ownerKey)
    expect(await code(b.api.getShare(c.token))).toBe('not_found')
  })

  it('zip links carry a file name and count as one download', async () => {
    const b = make()
    const c = await publish(b, { limit: 3, files: [{ name: 'a.txt', size: 5 }, { name: 'b.txt', size: 6 }] })
    const z = await b.api.zipLink(c.token)
    expect(z.fileName).toBe('NitroDrp demo files.zip')
    expect((await b.api.getShare(c.token)).downloadsRemaining).toBe(2)
  })
})
