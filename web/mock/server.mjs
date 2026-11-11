// Dev-only stand-in for the NitroDrp API + S3, so the UI can be run and demoed without AWS or Docker.
//   node mock/server.mjs        (listens on :8080, which vite proxies /api and /mock-storage to)
// Everything is in memory. Uploaded bytes are discarded; downloads serve zero-filled files capped at
// MOCK_CAP bytes so a "4.2 GB" demo file doesn't really stream 4.2 GB. Not for production.
import http from 'node:http'
import crypto from 'node:crypto'
import zlib from 'node:zlib'

const PORT = Number(process.env.PORT ?? 8080)
const PART_SIZE = 16 * 1024 * 1024
const MOCK_CAP = 2 * 1024 * 1024
const DAY = 86_400_000
const shares = new Map()
const rand = (n) => crypto.randomBytes(n).toString('base64url').slice(0, n)
const partCount = (size) => Math.ceil(size / PART_SIZE)

function seed(token, files, opts = {}) {
  shares.set(token, {
    token, ownerKey: 'seed-owner', status: 'Active', senderName: 'Jaded Media',
    expiresAt: Date.now() + (opts.days ?? 7) * DAY, password: opts.password ?? null,
    limit: opts.limit ?? 0, count: 0,
    files: files.map(([name, size], i) => ({ id: `f${i + 1}`, name, size, status: 'Complete', parts: new Map() })),
  })
}
seed('demo1234abcd', [['Smith-Wedding-Gallery.zip', 4_200_000_000], ['Ceremony-Highlights.mp4', 1_800_000_000], ['Album-Proofs.pdf', 86_000_000]])
seed('locked123456', [['Contract-Signed.pdf', 2_400_000], ['Invoice-0042.pdf', 310_000]], { password: 'nitro' })
seed('expired00000', [['Old-Gallery.zip', 1_000_000]], { days: -1 })

const json = (res, status, body) => { res.writeHead(status, { 'content-type': 'application/json' }); res.end(JSON.stringify(body)) }
const fail = (res, status, code, message) => json(res, status, { code, message })
const readJson = (req) => new Promise((resolve) => { let b = ''; req.on('data', (c) => (b += c)); req.on('end', () => { try { resolve(JSON.parse(b || '{}')) } catch { resolve({}) } }) })
const view = (s, unlocked) => ({
  token: s.token, senderName: s.senderName, expiresAt: new Date(s.expiresAt).toISOString(), passwordRequired: !!s.password,
  downloadsRemaining: s.limit ? Math.max(0, s.limit - s.count) : null, totalSize: s.files.reduce((a, f) => a + f.size, 0), fileCount: s.files.length,
  files: !s.password || unlocked ? s.files.map(({ id, name, size }) => ({ id, name, size })) : null,
})
const uploadStatus = (s) => ({
  token: s.token, expiresAt: new Date(s.expiresAt).toISOString(), partSize: PART_SIZE, finalized: s.status === 'Active',
  files: s.files.map((f) => ({ id: f.id, name: f.name, size: f.size, partCount: partCount(f.size), complete: f.status === 'Complete',
    parts: [...f.parts].map(([partNumber, eTag]) => ({ partNumber, eTag })) })),
})

function active(res, token) {
  const s = shares.get(token)
  if (!s || s.status !== 'Active') return fail(res, 404, 'not_found', "This link doesn't exist."), null
  if (Date.now() >= s.expiresAt) return fail(res, 410, 'expired', 'This link has expired.'), null
  if (s.limit && s.count >= s.limit) return fail(res, 410, 'limit_reached', 'This link has reached its download limit.'), null
  return s
}
function checkPassword(res, s, password) {
  if (!s.password) return true
  if (!password) return fail(res, 401, 'password_required', 'This link needs a password.'), false
  if (password !== s.password) return fail(res, 401, 'wrong_password', "That password isn't right."), false
  return true
}
function owned(req, res, token) {
  const s = shares.get(token)
  if (!s) return fail(res, 404, 'not_found', "This link doesn't exist."), null
  if (req.headers['x-owner-key'] !== s.ownerKey) return fail(res, 403, 'bad_owner_key', "You can't change this link."), null
  return s
}

function zeroStream(res, size) {
  const chunk = Buffer.alloc(64 * 1024)
  let left = Math.min(size, MOCK_CAP)
  const pump = () => { while (left > 0) { const n = Math.min(left, chunk.length); left -= n; if (!res.write(chunk.subarray(0, n))) return res.once('drain', pump) } res.end() }
  pump()
}

/** Tiny store-only zip writer for the demo (real one is ZipStreamer in the API). */
function buildZip(files) {
  const parts = []; const central = []; let offset = 0
  for (const f of files) {
    const name = Buffer.from(f.name); const size = Math.min(f.size, MOCK_CAP)
    let crc = 0; const z = Buffer.alloc(64 * 1024); for (let l = size; l > 0; l -= z.length) crc = zlib.crc32(z.subarray(0, Math.min(l, z.length)), crc)
    const lh = Buffer.alloc(30); lh.writeUInt32LE(0x04034b50, 0); lh.writeUInt16LE(20, 4); lh.writeUInt16LE(0x0800, 6)
    lh.writeUInt32LE(crc, 14); lh.writeUInt32LE(size, 18); lh.writeUInt32LE(size, 22); lh.writeUInt16LE(name.length, 26)
    parts.push(lh, name); parts.push({ zeros: size })
    const ch = Buffer.alloc(46); ch.writeUInt32LE(0x02014b50, 0); ch.writeUInt16LE(20, 4); ch.writeUInt16LE(20, 6); ch.writeUInt16LE(0x0800, 8)
    ch.writeUInt32LE(crc, 16); ch.writeUInt32LE(size, 20); ch.writeUInt32LE(size, 24); ch.writeUInt16LE(name.length, 28); ch.writeUInt32LE(offset, 42)
    central.push(ch, name); offset += 30 + name.length + size
  }
  const cd = Buffer.concat(central); const eocd = Buffer.alloc(22)
  eocd.writeUInt32LE(0x06054b50, 0); eocd.writeUInt16LE(files.length, 8); eocd.writeUInt16LE(files.length, 10); eocd.writeUInt32LE(cd.length, 12); eocd.writeUInt32LE(offset, 16)
  return [...parts, cd, eocd]
}

http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://x'); const p = url.pathname; const m = req.method
  let g
  try {
    if (p === '/api/health') return json(res, 200, { status: 'ok' })

    if (m === 'PUT' && (g = p.match(/^\/mock-storage\/([^/]+)\/([^/]+)\/(\d+)$/))) {
      const s = shares.get(g[1]); const f = s?.files.find((x) => x.id === g[2])
      req.resume(); await new Promise((r) => req.on('end', r))
      if (!f) return fail(res, 404, 'not_found', 'No such upload.')
      const etag = `"mock-${g[3]}"`; f.parts.set(Number(g[3]), etag)
      res.writeHead(200, { etag }); return res.end()
    }
    if (m === 'GET' && (g = p.match(/^\/mock-storage\/dl\/([^/]+)\/([^/]+)$/))) {
      const f = shares.get(g[1])?.files.find((x) => x.id === g[2]); if (!f) return fail(res, 404, 'not_found', 'No such file.')
      res.writeHead(200, { 'content-type': 'application/octet-stream', 'content-disposition': `attachment; filename*=UTF-8''${encodeURIComponent(f.name)}` })
      return zeroStream(res, f.size)
    }

    if (m === 'POST' && p === '/api/shares') {
      if (!req.headers['x-api-key']) return fail(res, 401, 'bad_api_key', 'Wrong access key.')
      const b = await readJson(req)
      if (![1, 7, 30].includes(b.expiresInDays)) return fail(res, 400, 'bad_expiry', 'Choose one of the allowed expiry times.')
      if (!b.files?.length) return fail(res, 400, 'no_files', 'Add at least one file.')
      const bad = b.files.find((f) => /\.(exe|bat|cmd|scr|msi|dll)$/i.test(f.name)); if (bad) return fail(res, 400, 'blocked_type', `'.${bad.name.split('.').pop()}' files can't be sent.`)
      const token = rand(12); const ownerKey = rand(43)
      const s = { token, ownerKey, status: 'Pending', senderName: 'Jaded Media', expiresAt: Date.now() + b.expiresInDays * DAY, password: b.password || null, limit: b.downloadLimit || 0, count: 0,
        files: b.files.map((f, i) => ({ id: `f${i + 1}`, name: f.name, size: f.size, status: 'Uploading', parts: new Map() })) }
      shares.set(token, s)
      return json(res, 201, { token, ownerKey, expiresAt: new Date(s.expiresAt).toISOString(), partSize: PART_SIZE, files: s.files.map((f) => ({ id: f.id, name: f.name, size: f.size, partCount: partCount(f.size) })) })
    }

    if ((g = p.match(/^\/api\/shares\/([^/]+)(?:\/(.*))?$/))) {
      const [, token, rest = ''] = g
      if (m === 'GET' && rest === 'upload') { const s = owned(req, res, token); return s && json(res, 200, uploadStatus(s)) }
      if (m === 'POST' && (g = rest.match(/^files\/([^/]+)\/presign$/))) {
        const s = owned(req, res, token); if (!s) return; const b = await readJson(req)
        return json(res, 200, { urls: b.partNumbers.map((n) => ({ partNumber: n, url: `/mock-storage/${token}/${g[1]}/${n}` })) })
      }
      if (m === 'POST' && (g = rest.match(/^files\/([^/]+)\/complete$/))) {
        const s = owned(req, res, token); if (!s) return; const f = s.files.find((x) => x.id === g[1]); if (f) f.status = 'Complete'; res.writeHead(204); return res.end()
      }
      if (m === 'POST' && rest === 'finalize') { const s = owned(req, res, token); if (!s) return; s.status = 'Active'; res.writeHead(204); return res.end() }
      if (m === 'DELETE' && rest === '') { const s = owned(req, res, token); if (!s) return; shares.delete(token); res.writeHead(204); return res.end() }

      if (m === 'GET' && rest === '') { const s = active(res, token); return s && json(res, 200, view(s, false)) }
      if (m === 'POST' && rest === 'unlock') { const s = active(res, token); if (!s) return; const b = await readJson(req); return checkPassword(res, s, b.password) && json(res, 200, view(s, true)) }
      if (m === 'POST' && rest === 'download') {
        const s = active(res, token); if (!s) return; const b = await readJson(req); if (!checkPassword(res, s, b.password)) return
        const targets = b.fileId ? s.files.filter((f) => f.id === b.fileId) : s.files; if (!targets.length) return fail(res, 404, 'file_not_found', "That file isn't part of this link.")
        s.count++; return json(res, 200, { files: targets.map((f) => ({ id: f.id, name: f.name, size: f.size, url: `/mock-storage/dl/${token}/${f.id}` })) })
      }
      if (m === 'POST' && rest === 'zip-link') {
        const s = active(res, token); if (!s) return; const b = await readJson(req); if (!checkPassword(res, s, b.password)) return
        s.count++; return json(res, 200, { url: `/api/shares/${token}/zip?exp=${Date.now() + 900_000}&sig=mock` })
      }
      if (m === 'GET' && rest === 'zip') {
        const s = shares.get(token); if (!s || s.status !== 'Active') return fail(res, 404, 'not_found', "This link doesn't exist.")
        res.writeHead(200, { 'content-type': 'application/zip', 'content-disposition': `attachment; filename*=UTF-8''${encodeURIComponent(s.senderName + ' files.zip')}` })
        for (const part of buildZip(s.files)) { if (part.zeros !== undefined) { let l = part.zeros; const z = Buffer.alloc(64 * 1024); while (l > 0) { const n = Math.min(l, z.length); res.write(z.subarray(0, n)); l -= n } } else res.write(part) }
        return res.end()
      }
    }
    fail(res, 404, 'not_found', 'Not found.')
  } catch (e) { console.error(e); fail(res, 500, 'internal_error', 'Something went wrong.') }
}).listen(PORT, () => console.log(`mock API on :${PORT}\n  demo link:      /d/demo1234abcd\n  password link:  /d/locked123456  (password: nitro)\n  expired link:   /d/expired00000`))
