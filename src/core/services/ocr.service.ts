/**
 * AI/광학 문자 인식(OCR) 서비스
 * - Tesseract.js (WebAssembly) 기반 한국어(kor) + 영어(eng) 고성능 인식
 * - 고해상도(2x DPI) 오프스크린 렌더링 및 스캔 화질 자동 보정(노이즈 제거/대비 강화)
 * - 하이브리드 모드: PDF 내장 텍스트 레이어 우선 추출 + 스캔 이미지 정밀 OCR 보완
 */
import { createWorker } from 'tesseract.js'
import { getPdfDoc } from '../../hooks/usePdfDocument'
import { ROOT_DOC_ID } from '../domain/document'
import type { VirtualPage } from '../domain/page'

export interface OcrWord {
  text: string
  confidence: number
  bbox: { x0: number; y0: number; x1: number; y1: number }
}

export interface OcrPageResult {
  pageNumber: number
  text: string
  confidence: number
  words?: OcrWord[]
  isEmbedded?: boolean
}

export interface OcrOptions {
  lang: 'kor+eng' | 'kor' | 'eng'
  autoEnhance?: boolean
  highDpi?: boolean
  hybrid?: boolean
  onProgress?: (percent: number, status: string) => void
}

/**
 * 스캔 문서 화질 자동 보정 (Adaptive Contrast Binarization)
 * - 노이즈 및 누런/회색 종이 배경을 순백색(#FFFFFF)으로 날리고
 * - 흐릿하거나 연한 한글 자모 획을 진한 검정색(#000000)으로 선명화
 */
export function enhanceScanImage(canvas: HTMLCanvasElement): void {
  const ctx = canvas.getContext('2d')
  if (!ctx) return
  const imgData = ctx.getImageData(0, 0, canvas.width, canvas.height)
  const data = imgData.data

  let minLum = 255
  let maxLum = 0
  const lums = new Uint8Array(data.length / 4)

  for (let i = 0, j = 0; i < data.length; i += 4, j++) {
    const gray = (data[i] * 299 + data[i + 1] * 587 + data[i + 2] * 114) / 1000
    lums[j] = gray
    if (gray < minLum) minLum = gray
    if (gray > maxLum) maxLum = gray
  }

  const range = maxLum - minLum || 1
  const bgThreshold = Math.min(230, maxLum - range * 0.15)
  const textThreshold = Math.max(70, minLum + range * 0.35)

  for (let i = 0, j = 0; i < data.length; i += 4, j++) {
    const gray = lums[j]
    let val: number
    if (gray >= bgThreshold) {
      val = 255
    } else if (gray <= textThreshold) {
      val = 0
    } else {
      val = Math.round(((gray - textThreshold) / (bgThreshold - textThreshold)) * 255)
    }
    data[i] = val
    data[i + 1] = val
    data[i + 2] = val
    data[i + 3] = 255
  }

  ctx.putImageData(imgData, 0, 0)
}

/**
 * 단일 가상 페이지의 텍스트 콘텐츠(내장 벡터 텍스트) 추출
 */
async function extractEmbeddedText(
  vPage: VirtualPage,
  pageIndex: number
): Promise<string> {
  const sourceDocId = vPage?.sourceDocId ?? ROOT_DOC_ID
  const sourcePageIndex = vPage?.sourcePageIndex ?? pageIndex
  const doc = getPdfDoc(sourceDocId)
  if (!doc) return ''

  try {
    const page = await doc.getPage(sourcePageIndex + 1)
    const content = await page.getTextContent()
    const items = content.items as Array<{ str?: string }>
    return items
      .map((item) => item.str ?? '')
      .join(' ')
      .replace(/\s+/g, ' ')
      .trim()
  } catch (err) {
    console.warn('내장 텍스트 추출 건너뜀:', err)
    return ''
  }
}

/**
 * 단일 가상 페이지를 오프스크린 캔버스에 고해상도 렌더링
 */
async function renderPageToCanvas(
  vPage: VirtualPage,
  pageIndex: number,
  scale: number
): Promise<HTMLCanvasElement> {
  const sourceDocId = vPage?.sourceDocId ?? ROOT_DOC_ID
  const sourcePageIndex = vPage?.sourcePageIndex ?? pageIndex
  const rotation = vPage?.rotation ?? 0

  const doc = getPdfDoc(sourceDocId)
  if (!doc) throw new Error('PDF 문서를 찾을 수 없습니다.')

  const page = await doc.getPage(sourcePageIndex + 1)
  const viewport = page.getViewport({ scale, rotation })

  const canvas = document.createElement('canvas')
  canvas.width = Math.floor(viewport.width)
  canvas.height = Math.floor(viewport.height)

  const ctx = canvas.getContext('2d', { alpha: false })
  if (!ctx) throw new Error('2D 컨텍스트 생성 실패')

  ctx.fillStyle = '#ffffff'
  ctx.fillRect(0, 0, canvas.width, canvas.height)

  await page.render({
    canvasContext: ctx,
    viewport,
    canvas
  } as Parameters<typeof page.render>[0]).promise

  return canvas
}

/**
 * 다중 페이지 OCR 실행 메인 함수
 */
export async function runOcrOnPages(
  pages: VirtualPage[],
  targetIndices: number[],
  options: OcrOptions
): Promise<OcrPageResult[]> {
  const {
    lang = 'kor+eng',
    autoEnhance = true,
    highDpi = true,
    hybrid = true,
    onProgress
  } = options

  const results: OcrPageResult[] = []
  const total = targetIndices.length
  if (total === 0) return results

  onProgress?.(5, 'OCR 엔진(Tesseract WebAssembly) 초기화 중...')

  let worker: Awaited<ReturnType<typeof createWorker>> | null = null

  try {
    // Tesseract 워커 생성 및 언어 모델 로드
    worker = await createWorker(lang, undefined, {
      logger: (m) => {
        if (m.status === 'recognizing text' && m.progress) {
          // 세부 진행률은 페이지 루프에서 종합 계산
        }
      }
    })

    const scale = highDpi ? 2.0 : 1.5

    for (let i = 0; i < total; i++) {
      const pageIndex = targetIndices[i]
      const vPage = pages[pageIndex]
      const pageNum = pageIndex + 1
      const basePct = Math.round((i / total) * 90) + 5

      // 1) 하이브리드 모드: 내장 텍스트 먼저 확인
      if (hybrid) {
        onProgress?.(basePct + 2, `P.${pageNum} 내장 텍스트 분석 중...`)
        const embeddedText = await extractEmbeddedText(vPage, pageIndex)
        // 공백 제외 40자 이상 유효한 내장 텍스트가 있으면 스캔 OCR 불필요/즉시 활용
        if (embeddedText.replace(/\s/g, '').length >= 40) {
          results.push({
            pageNumber: pageNum,
            text: embeddedText,
            confidence: 99,
            isEmbedded: true
          })
          continue
        }
      }

      // 2) 고해상도 렌더링
      onProgress?.(basePct + 4, `P.${pageNum} 고해상도 렌더링 중...`)
      const canvas = await renderPageToCanvas(vPage, pageIndex, scale)

      // 3) 스캔 이미지 자동 보정 (선택 시)
      if (autoEnhance) {
        onProgress?.(basePct + 6, `P.${pageNum} 스캔 이미지 노이즈 제거 및 대비 보정 중...`)
        enhanceScanImage(canvas)
      }

      // 4) Tesseract 문자 인식
      onProgress?.(basePct + 10, `P.${pageNum} 한국어/영어 문자 인식 중 (${lang})...`)
      const ret = await worker.recognize(canvas)

      const text = ret.data.text.trim()
      const confidence = Math.round(ret.data.confidence || 0)
      const rawWords = (ret.data as unknown as { words?: Array<{ text: string; confidence: number; bbox: { x0: number; y0: number; x1: number; y1: number } }> }).words || []
      const words: OcrWord[] = rawWords.map((w) => ({
        text: w.text,
        confidence: Math.round(w.confidence || 0),
        bbox: {
          x0: Math.round(w.bbox.x0 / scale),
          y0: Math.round(w.bbox.y0 / scale),
          x1: Math.round(w.bbox.x1 / scale),
          y1: Math.round(w.bbox.y1 / scale)
        }
      }))

      results.push({
        pageNumber: pageNum,
        text,
        confidence,
        words,
        isEmbedded: false
      })
    }

    onProgress?.(100, '문자 인식 완료!')
  } finally {
    if (worker) {
      try {
        await worker.terminate()
      } catch {
        /* noop */
      }
    }
  }

  return results
}
