/**
 * PDF.js worker 및 오프라인 CJK cMaps / standard_fonts 리소스를 public 폴더로 복사하는 스크립트
 * postinstall 단계에서 실행되어 인트라넷/오프라인 환경에서도 완전한 로컬 구동 보장
 */
import { copyFileSync, mkdirSync, existsSync, cpSync } from 'node:fs'
import { resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const root = resolve(__dirname, '..')

const workerSrc = resolve(root, 'node_modules/pdfjs-dist/build/pdf.worker.min.mjs')
const cmapsSrc = resolve(root, 'node_modules/pdfjs-dist/cmaps')
const fontsSrc = resolve(root, 'node_modules/pdfjs-dist/standard_fonts')

const destDir = resolve(root, 'public')
const workerDest = resolve(destDir, 'pdf.worker.min.mjs')
const cmapsDest = resolve(destDir, 'cmaps')
const fontsDest = resolve(destDir, 'standard_fonts')

if (!existsSync(workerSrc)) {
  console.warn(`[copy-pdf-worker] 소스가 없습니다 (아직 pdfjs-dist 미설치): ${workerSrc}`)
  process.exit(0)
}

mkdirSync(destDir, { recursive: true })
copyFileSync(workerSrc, workerDest)
console.log(`[copy-pdf-worker] 워커 복사 완료: ${workerDest}`)

// 오프라인 CJK 글꼴 맵(cMaps) 복사
if (existsSync(cmapsSrc)) {
  cpSync(cmapsSrc, cmapsDest, { recursive: true, force: true })
  console.log(`[copy-pdf-worker] cMaps 복사 완료: ${cmapsDest}`)
}

// 오프라인 표준 글꼴(standard_fonts) 복사
if (existsSync(fontsSrc)) {
  cpSync(fontsSrc, fontsDest, { recursive: true, force: true })
  console.log(`[copy-pdf-worker] standard_fonts 복사 완료: ${fontsDest}`)
}
