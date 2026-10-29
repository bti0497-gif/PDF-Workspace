const assert = require('node:assert/strict')
const fs = require('node:fs')
const ts = require('typescript')
const { PDFDocument, degrees } = require('pdf-lib')
require.extensions['.ts'] = (module, filename) => {
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  module._compile(output, filename)
}
const { buildMergePlanPdf } = require('../src/components/modals/merge-plan.ts')
;(async () => {
  const a = await PDFDocument.create()
  a.addPage([101, 301]); a.addPage([102, 302])
  const b = await PDFDocument.create()
  b.addPage([201, 401]).setRotation(degrees(90)); b.addPage([202, 402])
  const aBytes = await a.save(); const bBytes = await b.save()
  const beforeA = Buffer.from(aBytes); const beforeB = Buffer.from(bBytes)
  const sources = new Map([['a', aBytes], ['b', bBytes]])
  const result = await buildMergePlanPdf(sources, [
    { sourceId: 'b', index: 0, included: true },
    { sourceId: 'a', index: 1, included: true },
    { sourceId: 'a', index: 0, included: false },
    { sourceId: 'b', index: 1, included: true },
  ])
  const reopened = await PDFDocument.load(result)
  assert.equal(reopened.getPageCount(), 3)
  assert.deepEqual(reopened.getPages().map(p => p.getWidth()), [201, 102, 202])
  assert.equal(reopened.getPage(0).getRotation().angle, 90)
  assert.deepEqual(Buffer.from(aBytes), beforeA)
  assert.deepEqual(Buffer.from(bBytes), beforeB)
  await assert.rejects(buildMergePlanPdf(sources, []), /한 쪽/)
  await assert.rejects(buildMergePlanPdf(sources, [{ sourceId: 'a', index: 9, included: true }]), /범위/)
  await assert.rejects(buildMergePlanPdf(sources, [{ sourceId: 'missing', index: 0, included: true }]), /원본/)
  console.log('PASS: merge order, excluded pages, page count, rotation, immutable originals, empty/out-of-range/missing-source rejection (8 checks)')
})().catch(err => { console.error(err); process.exitCode = 1 })
