import { describe, expect, it } from 'vitest'
import { buildStoredZip, crc32 } from './zip'

const bytes = (s: string) => new TextEncoder().encode(s)

describe('crc32', () => {
  it('matches published check values', () => {
    expect(crc32(bytes('123456789'))).toBe(0xcbf43926)
    expect(crc32(bytes('The quick brown fox jumps over the lazy dog'))).toBe(0x414fa339)
    expect(crc32(new Uint8Array(0))).toBe(0)
  })
})

/** Tiny reader so the test checks the archive structure, not just that we wrote something. */
function readZip(z: Uint8Array) {
  const v = new DataView(z.buffer, z.byteOffset, z.byteLength)
  const eocd = z.length - 22
  expect(v.getUint32(eocd, true)).toBe(0x06054b50)
  const count = v.getUint16(eocd + 10, true)
  let p = v.getUint32(eocd + 16, true) // central directory offset
  const out: { name: string; data: string; crcOk: boolean }[] = []
  for (let i = 0; i < count; i++) {
    expect(v.getUint32(p, true)).toBe(0x02014b50)
    const crc = v.getUint32(p + 16, true)
    const size = v.getUint32(p + 20, true)
    const nameLen = v.getUint16(p + 28, true)
    const localOff = v.getUint32(p + 42, true)
    const name = new TextDecoder().decode(z.subarray(p + 46, p + 46 + nameLen))
    expect(v.getUint32(localOff, true)).toBe(0x04034b50)
    const dataStart = localOff + 30 + v.getUint16(localOff + 26, true)
    const data = z.subarray(dataStart, dataStart + size)
    out.push({ name, data: new TextDecoder().decode(data), crcOk: crc32(data) === crc })
    p += 46 + nameLen
  }
  return out
}

describe('buildStoredZip', () => {
  it('round-trips names (including unicode) and contents with valid CRCs', () => {
    const zip = buildStoredZip([
      { name: 'a.txt', data: bytes('hello') },
      { name: 'Réception 1.pdf', data: bytes('second file') },
    ])
    expect(readZip(zip)).toEqual([
      { name: 'a.txt', data: 'hello', crcOk: true },
      { name: 'Réception 1.pdf', data: 'second file', crcOk: true },
    ])
  })
  it('produces a readable empty archive', () => {
    expect(readZip(buildStoredZip([]))).toEqual([])
  })
})
