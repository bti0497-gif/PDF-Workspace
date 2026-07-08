/**
 * PDF.js worker 파일을 public 폴더로 복사하는 스크립트
 * postinstall 단계에서 실행되어 pdf.worker.min.mjs 를 사용 가능하게 함
 */
import { copyFileSync, mkdirSync, existsSync } from 'node:fs'
import { resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const root = resolve(__dirname, '..')

const src = resolve(root, 'node_modules/pdfjs-dist/build/pdf.worker.min.mjs')
const destDir = resolve(root, 'public')
const dest = resolve(destDir, 'pdf.worker.min.mjs')

if (!existsSync(src)) {
  console.warn(`[copy-pdf-worker] 소스가 없습니다 (아직 pdfjs-dist 미설치): ${src}`)
  process.exit(0)
}

mkdirSync(destDir, { recursive: true })
copyFileSync(src, dest)
console.log(`[copy-pdf-worker] 복사 완료: ${dest}`)
