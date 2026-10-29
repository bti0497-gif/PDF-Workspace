/**
 * pdf-lib 래퍼 - 페이지 조작, 합치기/분할, 주석 저장
 * 모든 함수는 부수효과가 없는 순수 변환 (입력 -> 새로운 PDF 바이트)
 */
import { PDFDocument, degrees, rgb, StandardFonts } from 'pdf-lib'
import fontkit from '@pdf-lib/fontkit'
import type { Annotation } from '../../electron/shared/types'

/** 커스텀 폰트 임베딩을 위해 fontkit 등록 */
export function ensureFontkit(doc: PDFDocument): void {
  try {
    doc.registerFontkit(fontkit)
  } catch {
    /* 이미 등록된 경우 무시 */
  }
}

/** number[] -> Uint8Array */
function toBytes(data: number[] | Uint8Array): Uint8Array {
  return data instanceof Uint8Array ? data : new Uint8Array(data)
}

/** #RRGGBB -> pdf-lib rgb */
function hexToRgb(hex: string): ReturnType<typeof rgb> {
  const clean = hex.replace('#', '')
  const r = parseInt(clean.substring(0, 2), 16) / 255
  const g = parseInt(clean.substring(2, 4), 16) / 255
  const b = parseInt(clean.substring(4, 6), 16) / 255
  return rgb(r || 0, g || 0, b || 0)
}

// ─────────────────────────────────────────────────────────────
// 페이지 조작
// ─────────────────────────────────────────────────────────────

/** 페이지 회전 (90도 단위, 시계 방향/반시계) */
export async function rotatePages(
  data: number[] | Uint8Array,
  pageIndices: number[],
  direction: 'left' | 'right'
): Promise<Uint8Array> {
  const doc = await PDFDocument.load(toBytes(data))
  const pages = doc.getPages()
  const delta = direction === 'right' ? 90 : -90
  for (const idx of pageIndices) {
    const page = pages[idx]
    if (page) {
      const current = page.getRotation().angle
      page.setRotation(degrees((current + delta + 360) % 360))
    }
  }
  return doc.save()
}

/** 페이지 삭제 */
export async function deletePages(
  data: number[] | Uint8Array,
  pageIndices: number[]
): Promise<Uint8Array> {
  const doc = await PDFDocument.load(toBytes(data))
  // 인덱스를 역순으로 정렬해 안전하게 삭제
  const sorted = [...new Set(pageIndices)].sort((a, b) => b - a)
  for (const idx of sorted) {
    doc.removePage(idx)
  }
  return doc.save()
}

/** 페이지 추출 - 지정 페이지들만 새 PDF로 */
export async function extractPages(
  data: number[] | Uint8Array,
  pageIndices: number[]
): Promise<Uint8Array> {
  const src = await PDFDocument.load(toBytes(data))
  const dst = await PDFDocument.create()
  const valid = pageIndices.filter((i) => i >= 0 && i < src.getPageCount())
  const copied = await dst.copyPages(src, valid)
  copied.forEach((p) => dst.addPage(p))
  return dst.save()
}

/**
 * 페이지 순서 변경
 * @param fromIndices 원본 페이지 인덱스 (현재 순서)
 * @returns 재배열된 새 PDF
 */
export async function reorderPages(
  data: number[] | Uint8Array,
  newOrder: number[]
): Promise<Uint8Array> {
  const src = await PDFDocument.load(toBytes(data))
  const dst = await PDFDocument.create()
  const copied = await dst.copyPages(src, newOrder)
  copied.forEach((p) => dst.addPage(p))
  return dst.save()
}

// ─────────────────────────────────────────────────────────────
// 합치기 / 분할
// ─────────────────────────────────────────────────────────────

/** 여러 PDF(데이터 배열)를 순서대로 하나로 합침 */
export async function mergePdfs(files: (number[] | Uint8Array)[]): Promise<Uint8Array> {
  const dst = await PDFDocument.create()
  for (const file of files) {
    const src = await PDFDocument.load(toBytes(file))
    const copied = await dst.copyPages(src, src.getPageIndices())
    copied.forEach((p) => dst.addPage(p))
  }
  return dst.save()
}

/**
 * PDF 분할 - 페이지 범위 배열에 따라 여러 PDF 생성
 * @param ranges 예: [[0,2],[3,5]] -> 2개 파일
 */
export async function splitPdf(
  data: number[] | Uint8Array,
  ranges: [number, number][]
): Promise<Uint8Array[]> {
  const src = await PDFDocument.load(toBytes(data))
  const results: Uint8Array[] = []
  for (const [start, end] of ranges) {
    const dst = await PDFDocument.create()
    const indices: number[] = []
    for (let i = start; i <= end && i < src.getPageCount(); i++) {
      if (i >= 0) indices.push(i)
    }
    const copied = await dst.copyPages(src, indices)
    copied.forEach((p) => dst.addPage(p))
    results.push(await dst.save())
  }
  return results
}

// ─────────────────────────────────────────────────────────────
// 주석 저장
// ─────────────────────────────────────────────────────────────

/**
 * 주석을 PDF 에 합성
 * 화면 좌표계(원점 좌상단, y 아래로 증가) -> PDF 좌표계(원점 좌하단, y 위로 증가)
 * 변환은 호출측에서 이미 PDF 좌표계로 변환했다고 가정 (Annotation.y 는 PDF 좌표)
 *
 * @param customFont 커스텀 폰트 바이트(한글 지원). 없으면 Helvetica(영문만)
 */
export async function applyAnnotations(
  data: number[] | Uint8Array,
  annotations: Annotation[],
  customFont?: Uint8Array
): Promise<Uint8Array> {
  const doc = await PDFDocument.load(toBytes(data))
  ensureFontkit(doc)
  // 한글을 위해 커스텀 폰트(NanumGothic 등) 사용, 없으면 Helvetica
  const font = customFont
    ? await doc.embedFont(customFont, { subset: true })
    : await doc.embedFont(StandardFonts.Helvetica)
  const pages = doc.getPages()

  for (const ann of annotations) {
    const page = pages[ann.pageIndex]
    if (!page) continue
    const color = hexToRgb(ann.color)
    const { width: pw, height: ph } = page.getSize()

    switch (ann.type) {
      case 'highlight': {
        page.drawRectangle({
          x: ann.x,
          y: ph - (ann.y ?? 0) - (ann.height ?? 0),
          width: ann.width,
          height: ann.height,
          color,
          opacity: 0.35
        })
        break
      }
      case 'rectangle': {
        page.drawRectangle({
          x: ann.x,
          y: ph - (ann.y ?? 0) - (ann.height ?? 0),
          width: ann.width,
          height: ann.height,
          borderColor: color,
          borderWidth: ann.strokeWidth ?? 2,
          opacity: 0
        })
        break
      }
      case 'textbox': {
        const fontSize = 14
        const text = (ann.text ?? '').trim()
        if (text) {
          // pdf-lib 텍스트는 기준점이 텍스트 기준선 -> 상자 상단에서 폰트 높이만큼 내림
          page.drawText(text, {
            x: ann.x + 4,
            y: ph - (ann.y ?? 0) - fontSize,
            size: fontSize,
            font,
            color,
            maxWidth: ann.width - 8
          })
        }
        // 테두리
        page.drawRectangle({
          x: ann.x,
          y: ph - (ann.y ?? 0) - (ann.height ?? 0),
          width: ann.width,
          height: ann.height,
          borderColor: color,
          borderWidth: 1,
          opacity: 0
        })
        break
      }
      case 'drawing': {
        const pts = ann.points ?? []
        for (let i = 1; i < pts.length; i++) {
          const a = pts[i - 1]
          const b = pts[i]
          page.drawLine({
            start: { x: a.x, y: ph - a.y },
            end: { x: b.x, y: ph - b.y },
            color,
            thickness: ann.strokeWidth ?? 2
          })
        }
        break
      }
      case 'stamp': {
        if (ann.imageDataUrl) {
          try {
            const base64Data = ann.imageDataUrl.split(',')[1]
            if (base64Data) {
              const binStr = atob(base64Data)
              const imgBytes = new Uint8Array(binStr.length)
              for (let b = 0; b < binStr.length; b++) {
                imgBytes[b] = binStr.charCodeAt(b)
              }
              const isJpg =
                ann.imageDataUrl.startsWith('data:image/jpeg') ||
                ann.imageDataUrl.startsWith('data:image/jpg')
              const embeddedImg = isJpg
                ? await doc.embedJpg(imgBytes)
                : await doc.embedPng(imgBytes)
              const w = ann.width ?? 120
              const h = ann.height ?? 60
              page.drawImage(embeddedImg, {
                x: ann.x,
                y: ph - ann.y - h,
                width: w,
                height: h,
                opacity: ann.opacity ?? 1
              })
            }
          } catch (err) {
            console.warn('스탬프/이미지 주석 저장 실패:', err)
          }
        }
        break
      }
      default:
          throw new Error('지원하지 않는 주석이 있어 저장을 중단했습니다.')
    }
    // ph 사용 경고 방지용 (참조 유지)
    void pw
  }

  return doc.save()
}

// ─────────────────────────────────────────────────────────────
// 메타데이터
// ─────────────────────────────────────────────────────────────

/** PDF 페이지 수 반환 */
export async function getPageCount(data: number[] | Uint8Array): Promise<number> {
  const doc = await PDFDocument.load(toBytes(data))
  return doc.getPageCount()
}
