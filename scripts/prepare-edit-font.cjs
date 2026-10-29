/** Build a browser-compatible derivative of the bundled font, retaining outlines/metrics.
 * Legacy VTT and hint-program tables rejected by Chromium are omitted; original is untouched.
 */
const fs = require('node:fs')
const path = require('node:path')
const root = path.resolve(__dirname, '..')
const b = fs.readFileSync(path.join(root, 'assets/fonts/NanumGothic.ttf'))
const keep = new Set(['head', 'hhea', 'maxp', 'OS/2', 'hmtx', 'cmap', 'loca', 'glyf', 'name', 'post', 'gasp'])
const tables = []
for (let i = 0; i < b.readUInt16BE(4); i++) {
  const at = 12 + i * 16, tag = b.toString('ascii', at, at + 4)
  const offset = b.readUInt32BE(at + 8), length = b.readUInt32BE(at + 12)
  if (keep.has(tag)) { const bytes = Buffer.from(b.subarray(offset, offset + length)); if (tag === 'head') bytes.writeUInt32BE(0, 8); tables.push({ tag, bytes }) }
}
function checksum(bytes) {
  let value = 0
  for (let i = 0; i < bytes.length; i += 4) {
    let word = 0
    for (let j = 0; j < 4; j++) word = (word * 256 + (bytes[i + j] || 0)) >>> 0
    value = (value + word) >>> 0
  }
  return value
}
let offset = 12 + tables.length * 16
const out = Buffer.alloc(offset + tables.reduce((n, t) => n + Math.ceil(t.bytes.length / 4) * 4, 0))
out.writeUInt32BE(0x10000, 0); out.writeUInt16BE(tables.length, 4)
const exponent = Math.floor(Math.log2(tables.length))
out.writeUInt16BE(2 ** exponent * 16, 6); out.writeUInt16BE(exponent, 8); out.writeUInt16BE(tables.length * 16 - 2 ** exponent * 16, 10)
let head
for (let i = 0; i < tables.length; i++) {
  const t = tables[i], at = 12 + i * 16
  out.write(t.tag, at, 4, 'ascii'); out.writeUInt32BE(checksum(t.bytes), at + 4); out.writeUInt32BE(offset, at + 8); out.writeUInt32BE(t.bytes.length, at + 12)
  t.bytes.copy(out, offset); if (t.tag === 'head') head = offset
  offset += Math.ceil(t.bytes.length / 4) * 4
}
if (head === undefined) throw new Error('Missing font head table')
out.writeUInt32BE((0xB1B0AFBA - checksum(out)) >>> 0, head + 8)
for (const folder of ['assets/fonts', 'public/fonts']) {
  fs.mkdirSync(path.join(root, folder), { recursive: true })
  fs.writeFileSync(path.join(root, folder, 'NanumGothic-Edit.ttf'), out)
}
console.log('Prepared compatible edit font:', out.length, 'bytes')
