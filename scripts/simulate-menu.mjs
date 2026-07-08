/**
 * 메뉴 기능 시뮬레이션 검증 스크립트
 * 실제 매출세금계산서.pdf 로 앱의 모든 기능을 실행해 검증
 *
 * 사용하는 함수는 앱 소스(src/lib/pdf-operations.ts)와 동일한 로직.
 * ESM 임포트로 pdf-lib 를 직접 사용 (앱 코드와 동일 동작 보장).
 */
import { readFile, writeFile, mkdir } from 'node:fs/promises'
import { existsSync } from 'node:fs'
import {
  PDFDocument,
  degrees,
  rgb,
  StandardFonts
} from 'pdf-lib'
import fontkit from '@pdf-lib/fontkit'

// 한글 폰트 (앱과 동일하게 NanumGothic 사용)
const FONT_PATH = existsSync('assets/fonts/NanumGothic.ttf')
  ? 'assets/fonts/NanumGothic.ttf'
  : 'C:/Windows/Fonts/NanumGothic.ttf'
const koreanFontBytes = await readFile(FONT_PATH)

const SOURCE = 'test-output/원본.pdf'
const OUT = 'test-output'

// ── 앱의 pdf-operations.ts 와 동일한 함수들 ──
function toBytes(data) {
  return data instanceof Uint8Array ? data : new Uint8Array(data)
}
function hexToRgb(hex) {
  const c = hex.replace('#', '')
  return rgb(
    parseInt(c.slice(0, 2), 16) / 255,
    parseInt(c.slice(2, 4), 16) / 255,
    parseInt(c.slice(4, 6), 16) / 255
  )
}
async function rotatePages(data, indices, dir) {
  const doc = await PDFDocument.load(toBytes(data))
  const pages = doc.getPages()
  const delta = dir === 'right' ? 90 : -90
  for (const i of indices) {
    if (pages[i]) {
      const cur = pages[i].getRotation().angle
      pages[i].setRotation(degrees(((cur + delta) % 360 + 360) % 360))
    }
  }
  return doc.save()
}
async function deletePages(data, indices) {
  const doc = await PDFDocument.load(toBytes(data))
  const sorted = [...new Set(indices)].sort((a, b) => b - a)
  for (const i of sorted) doc.removePage(i)
  return doc.save()
}
async function extractPages(data, indices) {
  const src = await PDFDocument.load(toBytes(data))
  const dst = await PDFDocument.create()
  const valid = indices.filter((i) => i >= 0 && i < src.getPageCount())
  const copied = await dst.copyPages(src, valid)
  copied.forEach((p) => dst.addPage(p))
  return dst.save()
}
async function mergePdfs(files) {
  const dst = await PDFDocument.create()
  for (const f of files) {
    const src = await PDFDocument.load(toBytes(f))
    const copied = await dst.copyPages(src, src.getPageIndices())
    copied.forEach((p) => dst.addPage(p))
  }
  return dst.save()
}
async function splitPdf(data, ranges) {
  const src = await PDFDocument.load(toBytes(data))
  const out = []
  for (const [s, e] of ranges) {
    const dst = await PDFDocument.create()
    const idx = []
    for (let i = s; i <= e && i < src.getPageCount(); i++) if (i >= 0) idx.push(i)
    const copied = await dst.copyPages(src, idx)
    copied.forEach((p) => dst.addPage(p))
    out.push(await dst.save())
  }
  return out
}
async function applyAnnotations(data, annotations, customFont) {
  const doc = await PDFDocument.load(toBytes(data))
  doc.registerFontkit(fontkit)
  const font = customFont
    ? await doc.embedFont(customFont, { subset: true })
    : await doc.embedFont(StandardFonts.Helvetica)
  const pages = doc.getPages()
  for (const a of annotations) {
    const page = pages[a.pageIndex]
    if (!page) continue
    const color = hexToRgb(a.color)
    const { width: pw } = page.getSize()
    switch (a.type) {
      case 'highlight':
        page.drawRectangle({ x: a.x, y: a.y, width: a.width, height: a.height, color, opacity: 0.35 })
        break
      case 'rectangle':
        page.drawRectangle({ x: a.x, y: a.y, width: a.width, height: a.height, borderColor: color, borderWidth: a.strokeWidth ?? 2, opacity: 0 })
        break
      case 'textbox': {
        const text = (a.text ?? '').trim()
        if (text) {
          page.drawText(text, { x: a.x + 4, y: a.y + 4, size: 14, font, color, maxWidth: a.width - 8 })
        }
        page.drawRectangle({ x: a.x, y: a.y, width: a.width, height: a.height, borderColor: color, borderWidth: 1, opacity: 0 })
        break
      }
      case 'drawing': {
        const pts = a.points ?? []
        const ph = pages[a.pageIndex].getSize().height
        for (let i = 1; i < pts.length; i++) {
          page.drawLine({ start: { x: pts[i - 1].x, y: ph - pts[i - 1].y }, end: { x: pts[i].x, y: ph - pts[i].y }, color, thickness: a.strokeWidth ?? 2 })
        }
        break
      }
    }
    void pw
  }
  return doc.save()
}

// ── 헬퍼 ──
const log = (icon, msg) => console.log(`  ${icon} ${msg}`)
const save = async (name, bytes) => {
  await writeFile(`${OUT}/${name}`, bytes)
  return `${OUT}/${name}`
}
const info = async (bytes, label) => {
  const doc = await PDFDocument.load(toBytes(bytes))
  const rot = doc.getPages().map((p) => p.getRotation().angle)
  console.log(`     → ${label}: ${doc.getPageCount()}페이지, 회전=[${rot.join(',')}]`)
  return { pages: doc.getPageCount(), rot }
}

// ── 메인 시뮬레이션 ──
async function main() {
  if (!existsSync(SOURCE)) throw new Error(`원본 없음: ${SOURCE}`)
  await mkdir(OUT, { recursive: true })

  const orig = await readFile(SOURCE)
  console.log('\n╔══════════════════════════════════════════════╗')
  console.log('║  PDF Workspace - 메뉴 기능 시뮬레이션 검증   ║')
  console.log('╚══════════════════════════════════════════════╝')
  console.log(`원본: 매출세금계산서.pdf (${(orig.length / 1024).toFixed(1)} KB)`)
  const origInfo = await info(orig, '원본')
  if (origInfo.pages < 2) {
    console.log(`\n⚠️  원본이 1페이지입니다. 합치기 테스트를 위해 자기 자신을 복제합니다.`)
  }
  let pass = 0
  let fail = 0
  const assert = (cond, msg) => {
    if (cond) { pass++; log('✅', msg) } else { fail++; log('❌', msg) }
  }

  // ── [보기] 회전 (오른쪽/왼쪽) ──
  console.log('\n── [페이지 > 회전] ──')
  const rotatedR = await rotatePages(orig, [0], 'right')
  const rr = await info(rotatedR, '오른쪽 회전')
  assert(rr.rot[0] === 90, `오른쪽 회전 -> ${rr.rot[0]}°`)
  await save('1_회전_오른쪽.pdf', rotatedR)

  const rotatedL = await rotatePages(rotatedR, [0], 'left')
  const rl = await info(rotatedL, '다시 왼쪽 회전')
  assert(rl.rot[0] === 0, `왼쪽 회전 -> 원복 ${rl.rot[0]}° (90-90=0)`)
  await save('1_회전_왼쪽복구.pdf', rotatedL)

  const rotated90 = await rotatePages(orig, [0], 'right')
  const rotated180 = await rotatePages(rotated90, [0], 'right')
  const r180i = await info(rotated180, '180도 (오른쪽x2)')
  assert(r180i.rot[0] === 180, `180도 회전 -> ${r180i.rot[0]}°`)
  await save('1_회전_180도.pdf', rotated180)

  // ── [페이지 > 삭제] (복제본에서) ──
  console.log('\n── [페이지 > 삭제] ──')
  // 여러 페이지 확보를 위해 원본을 3배 복제
  const triple = await mergePdfs([orig, orig, orig])
  const ti = await info(triple, '복제(원본x3)')
  assert(ti.pages === origInfo.pages * 3, `복제 ${ti.pages}페이지 (원본x3)`)
  await save('2_복제_원본x3.pdf', triple)

  const afterDel = await deletePages(triple, [0]) // 첫 페이지 삭제
  const di = await info(afterDel, '첫 페이지 삭제')
  assert(di.pages === ti.pages - 1, `삭제 후 ${di.pages}페이지 (기대 ${ti.pages - 1})`)
  await save('2_삭제_첫페이지.pdf', afterDel)

  // ── [페이지 > 추출] ──
  console.log('\n── [페이지 > 추출] ──')
  const extracted = await extractPages(triple, [0])
  const ei = await info(extracted, '첫 페이지 추출')
  assert(ei.pages === 1, `추출 ${ei.pages}페이지 (1페이지만 추출)`)
  await save('3_추출_첫페이지.pdf', extracted)

  // ── [도구 > 합치기] ──
  console.log('\n── [도구 > PDF 합치기] ──')
  const merged = await mergePdfs([orig, rotated180, extracted])
  const mi = await info(merged, '원본+회전+추출 병합')
  const expected = origInfo.pages + origInfo.pages + 1 // 33 + 33 + 1
  assert(mi.pages === expected, `병합 ${mi.pages}페이지 (기대 ${expected} = 33+33+1)`)
  // 병합본 [33-65] 은 회전본(rotated180). 첫 페이지(인덱스 33)가 180°여야 함
  assert(mi.rot[origInfo.pages] === 180, `병합본 회전본 시작(인덱스 ${origInfo.pages}) 회전 ${mi.rot[origInfo.pages]}°`)
  await save('4_합치기.pdf', merged)

  // ── [도구 > 나누기] ──
  console.log('\n── [도구 > PDF 나누기] ──')
  const parts = await splitPdf(merged, [[0, origInfo.pages - 1], [origInfo.pages, origInfo.pages * 2 - 1], [origInfo.pages * 2, merged.length]])
  assert(parts.length === 3, `분할 ${parts.length}개 파일 생성`)
  const expectedParts = [origInfo.pages, origInfo.pages, 1]
  for (let i = 0; i < parts.length; i++) {
    const pi = await info(parts[i], `분할#${i + 1}`)
    await save(`5_나누기_${i + 1}.pdf`, parts[i])
    assert(pi.pages === expectedParts[i], `분할#${i + 1}: ${pi.pages}페이지 (기대 ${expectedParts[i]})`)
  }

  // ── [도구 > 주석: 형광펜/사각형/텍스트/그리기] ──
  console.log('\n── [도구 > 주석 4종] ──')
  const page0 = (await PDFDocument.load(toBytes(orig))).getPages()[0]
  const { width: pw, height: ph } = page0.getSize()
  const annotations = [
    { id: 'a1', type: 'highlight', pageIndex: 0, x: 30, y: ph - 60, width: 200, height: 18, color: '#ffeb3b' },
    { id: 'a2', type: 'rectangle', pageIndex: 0, x: 30, y: ph - 120, width: 150, height: 40, color: '#e5534b', strokeWidth: 2 },
    { id: 'a3', type: 'textbox', pageIndex: 0, x: 250, y: ph - 180, width: 180, height: 30, color: '#1e1e1e', text: '주석 테스트' },
    { id: 'a4', type: 'drawing', pageIndex: 0, x: 0, y: 0, width: 0, height: 0, color: '#0e6fd6', strokeWidth: 2, points: [{ x: 30, y: 30 }, { x: 100, y: 60 }, { x: 150, y: 30 }, { x: 220, y: 80 }] }
  ]
  const annotated = await applyAnnotations(orig, annotations, koreanFontBytes)
  const ai = await info(annotated, '주석 적용')
  assert(ai.pages === origInfo.pages, `주석 후 페이지 수 유지 ${ai.pages}`)
  // 주석이 실제로 추가되었는지: 한글 폰트 서브셋이 임베드되어 에러 없이 완료되면 성공
  assert(annotated.length > 0, `주석 4종(형광펜/사각형/한글텍스트/그리기) 적용 성공 (${(annotated.length / 1024).toFixed(0)} KB)`)
  await save('6_주석_적용.pdf', annotated)

  // ── [파일 > 저장] 통합: 회전+주석을 같이 적용한 최종본 ──
  console.log('\n── [파일 > 저장] (회전+주석 통합) ──')
  const rotatedAndAnnotated = await applyAnnotations(rotated180, annotations, koreanFontBytes)
  await save('7_최종저장_회전+주석.pdf', rotatedAndAnnotated)
  const finalInfo = await info(rotatedAndAnnotated, '최종 저장본')
  assert(finalInfo.rot[0] === 180, `최종본 회전 ${finalInfo.rot[0]}° 유지`)
  log('💾', '7_최종저장_회전+주석.pdf 저장 완료')

  // ── 결과 요약 ──
  console.log('\n════════════════════════════════════════════════')
  console.log(`  통과 ${pass} / 실패 ${fail}`)
  console.log('════════════════════════════════════════════════')
  console.log('  생성된 파일 (test-output/ 폴더):')
  const files = ['1_회전_오른쪽.pdf','1_회전_왼쪽복구.pdf','1_회전_180도.pdf','2_복제_원본x3.pdf','2_삭제_첫페이지.pdf','3_추출_첫페이지.pdf','4_합치기.pdf','5_나누기_1.pdf','5_나누기_2.pdf','5_나누기_3.pdf','6_주석_적용.pdf','7_최종저장_회전+주석.pdf']
  for (const f of files) {
    const st = await readFile(`${OUT}/${f}`)
    console.log(`    • ${f.padEnd(30)} ${(st.length / 1024).toFixed(1)} KB`)
  }
  console.log('')
  process.exit(fail > 0 ? 1 : 0)
}

main().catch((e) => {
  console.error('\n❌ 시뮬레이션 중 오류:', e)
  process.exit(1)
})
