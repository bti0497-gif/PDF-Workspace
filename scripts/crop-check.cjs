const { buildSync } = require('esbuild')
const fs = require('node:fs'), assert = require('node:assert/strict'), path = require('node:path')
const temp = path.resolve('test-output/crop-check.cjs')
buildSync({ entryPoints: ['src/components/crop-geometry.ts'], outfile: temp, bundle: true, platform: 'node', format: 'cjs' })
try {
  const { displayMargins, pdfMargins, validCrop } = require(temp)
  const m = { top: 10, right: 20, bottom: 30, left: 40 }
  for (const r of [0, 90, 180, 270, 360, -90]) assert.deepEqual(pdfMargins(displayMargins(m, r), r), m)
  assert(validCrop(m, 595, 842)); assert(!validCrop({ ...m, left: 1000 }, 595, 842))
  console.log('PASS: crop rotation round trips and invalid size rejection')
} finally { fs.rmSync(temp, { force: true }) }
