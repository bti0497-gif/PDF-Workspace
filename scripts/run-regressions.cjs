const fs = require('node:fs')
const { spawnSync } = require('node:child_process')
fs.mkdirSync('test-output', { recursive: true })
const scripts = ['prepare-edit-font.cjs', 'annotation-tools-regression.cjs', 'options-regression.cjs', 'document-close-regression.cjs', 'crop-check.cjs', 'merge-plan-check.cjs', 'test-bookmarks.mjs', 'text-edit-regression.cjs', 'update-controller-regression.cjs']
for (const script of scripts) {
  const result = spawnSync(process.execPath, ['scripts/' + script], { stdio: 'inherit' })
  if (result.status !== 0) process.exit(result.status ?? 1)
}
console.log('PASS: complete source regression suite (native installation is a separate acceptance test).')
