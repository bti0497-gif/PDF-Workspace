/**
 * 가상 페이지(VirtualPage) 도메인 모델 및 순수 함수
 *
 * 포토샵의 비파괴 프록시 편집 원칙:
 * - 원본 PDF 바이트는 불변으로 유지
 * - 페이지의 순서, 회전, 크롭, 조판 규격 일치화 등은 가상 매니페스트(VirtualPage[]) 배열만 조작 (0.001ms 소요)
 */

export interface CropMargins {
  top: number
  bottom: number
  left: number
  right: number
}

export type PageFitMode = 'letterbox' | 'fill' | 'stretch'

export interface NormalizedPageConfig {
  targetWidth: number
  targetHeight: number
  fitMode: PageFitMode
  autoOrientation: boolean
}

export interface PageFormat {
  id: string
  name: string
  width: number // pt (72 pt = 1 inch)
  height: number // pt
}

/** 워터마크 설정 */
export interface WatermarkConfig {
  text: string
  fontSize: number      // e.g. 48
  color: string         // hex e.g. '#e5534b'
  opacity: number       // 0.05 ~ 0.9, e.g. 0.25
  rotation: number      // degrees e.g. -45, 0, 45
  tiled?: boolean       // true = 3x3 tiled grid, false = center
}

/** 페이지 번호(Header/Footer) 설정 */
export interface PageNumberConfig {
  text: string          // formatted text for this page, e.g. "- 1 -" or "1 / 10"
  position: 'top-left' | 'top-center' | 'top-right' | 'bottom-left' | 'bottom-center' | 'bottom-right'
  fontSize: number      // e.g. 11
  color: string         // hex e.g. '#333333'
  margin: number        // pt from edge, e.g. 24
}

/** 본문 텍스트 인라인 교체 모델 */
export interface TextReplacement {
  id: string
  pageIndex: number
  /** PDF 내부 좌표 (원점: 페이지 좌하단) */
  pdfX: number
  pdfY: number
  pdfWidth: number
  pdfHeight: number
  /** 화면 뷰포트 좌표 (scale: 1 기준) */
  vx: number
  vy: number
  vw: number
  vh: number
  /** 원본 텍스트 및 수정된 새 텍스트 */
  originalText: string
  newText: string
  fontSize: number
  color: string
  backgroundColor?: string
  /** 고도화: 원본 아이템 ID, 원본 폭, 정렬 */
  originalItemId?: string
  align?: 'left' | 'center' | 'right'
  origVw?: number
  origPdfWidth?: number
  /** Explicit consent: flatten only this edited page to remove old searchable text. */
  safeRasterize?: boolean
}

export const STANDARD_PAGE_FORMATS: PageFormat[] = [
  { id: 'a4', name: 'A4 (210 × 297 mm)', width: 595.28, height: 841.89 },
  { id: 'a3', name: 'A3 (297 × 420 mm)', width: 841.89, height: 1190.55 },
  { id: 'b5', name: 'B5 (176 × 250 mm)', width: 498.9, height: 708.66 },
  { id: 'letter', name: 'Letter (8.5 × 11 in)', width: 612.0, height: 792.0 },
  { id: 'legal', name: 'Legal (8.5 × 14 in)', width: 612.0, height: 1008.0 }
]

export interface VirtualPage {
  /** 고유 ID (순서가 바뀌어도 DOM/Canvas 노드 식별 유지) */
  id: string
  /** 소스 문서 식별자 (단일 문서일 때 'root_doc', 외부 삽입 문서일 때 고유 id) */
  sourceDocId: string
  /** 원본 문서 내 0-based 페이지 인덱스 */
  sourcePageIndex: number
  /** 누적 회전 각도 (0, 90, 180, 270) */
  rotation: number
  /** 가로/세로 포인트 크기 (선택적 메타데이터) */
  width?: number
  height?: number
  /** 조판 규격 정규화 설정 */
  normalizedConfig?: NormalizedPageConfig
  /** 여백 크롭 설정 */
  cropMargins?: CropMargins
  /** 본문 텍스트 인라인 교체 목록 */
  textReplacements?: TextReplacement[]
  /** 워터마크 설정 */
  watermarkConfig?: WatermarkConfig
  /** 페이지 번호 설정 */
  pageNumberConfig?: PageNumberConfig
}

let _idCounter = 0
export function generatePageId(prefix = 'p'): string {
  _idCounter += 1
  return `${prefix}_${Date.now().toString(36)}_${_idCounter}`
}

/** 단일 문서로부터 초기 가상 페이지 목록 생성 */
export function createInitialVirtualPages(
  sourceDocId: string,
  pageCount: number,
  initialRotations?: number[],
  dimensions?: { width: number; height: number }
): VirtualPage[] {
  const defaultWidth = dimensions?.width ?? 595.28
  const defaultHeight = dimensions?.height ?? 841.89

  return Array.from({ length: pageCount }, (_, i) => ({
    id: generatePageId(`p${i}`),
    sourceDocId,
    sourcePageIndex: i,
    rotation: initialRotations?.[i] ?? 0,
    width: defaultWidth,
    height: defaultHeight
  }))
}

/** 페이지 순서 재배열 (순수 함수) */
export function reorderVirtualPages(
  pages: VirtualPage[],
  fromIndex: number,
  toIndex: number
): VirtualPage[] {
  if (
    fromIndex === toIndex ||
    fromIndex < 0 ||
    fromIndex >= pages.length ||
    toIndex < 0 ||
    toIndex >= pages.length
  ) {
    return pages
  }
  const next = [...pages]
  const [removed] = next.splice(fromIndex, 1)
  next.splice(toIndex, 0, removed)
  return next
}

/** 여러 페이지 순서 일괄 재배열 (순수 함수) */
export function reorderMultipleVirtualPages(
  pages: VirtualPage[],
  fromIndices: number[],
  targetIndex: number
): { pages: VirtualPage[]; newSelectedIndices: number[] } {
  if (fromIndices.length === 0 || pages.length === 0) {
    return { pages, newSelectedIndices: fromIndices }
  }
  const validFromIndices = [...new Set(fromIndices)]
    .filter((idx) => idx >= 0 && idx < pages.length)
    .sort((a, b) => a - b)
  if (validFromIndices.length === 0) {
    return { pages, newSelectedIndices: [] }
  }

  const fromSet = new Set(validFromIndices)
  const movingPages = validFromIndices.map((idx) => pages[idx])
  const removedBeforeTarget = validFromIndices.filter((idx) => idx < targetIndex).length
  const insertionIndex = Math.max(
    0,
    Math.min(pages.length - validFromIndices.length, targetIndex - removedBeforeTarget)
  )

  const remaining = pages.filter((_, idx) => !fromSet.has(idx))
  const next = [
    ...remaining.slice(0, insertionIndex),
    ...movingPages,
    ...remaining.slice(insertionIndex)
  ]
  const newSelectedIndices = movingPages.map((_, offset) => insertionIndex + offset)
  return { pages: next, newSelectedIndices }
}

/** 페이지 회전 (순수 함수) */
export function rotateVirtualPages(
  pages: VirtualPage[],
  targetIndices: number[],
  direction: 'left' | 'right' | '180'
): VirtualPage[] {
  const delta = direction === '180' ? 180 : direction === 'right' ? 90 : -90
  const indexSet = new Set(targetIndices)
  return pages.map((page, idx) => {
    if (!indexSet.has(idx)) return page
    const nextRotation = (((page.rotation + delta) % 360) + 360) % 360
    return { ...page, rotation: nextRotation }
  })
}

/** 페이지 삭제 (순수 함수) */
export function deleteVirtualPages(
  pages: VirtualPage[],
  targetIndices: number[]
): VirtualPage[] {
  const indexSet = new Set(targetIndices)
  return pages.filter((_, idx) => !indexSet.has(idx))
}

/** 페이지 복제 (순수 함수) */
export function duplicateVirtualPages(
  pages: VirtualPage[],
  targetIndices: number[]
): VirtualPage[] {
  const next: VirtualPage[] = []
  const indexSet = new Set(targetIndices)
  for (let i = 0; i < pages.length; i++) {
    const page = pages[i]
    next.push(page)
    if (indexSet.has(i)) {
      next.push({
        ...page,
        id: generatePageId('dup')
      })
    }
  }
  return next
}

/** 특정 위치에 새 가상 페이지들 삽입 (순수 함수) */
export function insertVirtualPagesAt(
  pages: VirtualPage[],
  newPages: VirtualPage[],
  insertIndex: number
): VirtualPage[] {
  const next = [...pages]
  const safeIndex = Math.max(0, Math.min(next.length, insertIndex))
  next.splice(safeIndex, 0, ...newPages)
  return next
}

/** 조판 규격 일치화 (순수 함수) */
export function normalizeVirtualPages(
  pages: VirtualPage[],
  targetIndices: number[],
  config: NormalizedPageConfig
): VirtualPage[] {
  const indexSet = new Set(targetIndices)
  return pages.map((page, idx) => {
    if (!indexSet.has(idx)) return page

    let finalW = config.targetWidth
    let finalH = config.targetHeight

    // 원본 페이지 가로/세로 방향 감지
    const curW = page.width ?? 595.28
    const curH = page.height ?? 841.89
    const isSrcLandscape = curW > curH
    const isTargetLandscape = finalW > finalH

    if (config.autoOrientation && isSrcLandscape !== isTargetLandscape) {
      ;[finalW, finalH] = [finalH, finalW]
    }

    return {
      ...page,
      width: finalW,
      height: finalH,
      normalizedConfig: {
        ...config,
        targetWidth: finalW,
        targetHeight: finalH
      }
    }
  })
}

/** 여백 자르기 (순수 함수) */
export function cropVirtualPages(
  pages: VirtualPage[],
  targetIndices: number[],
  margins: CropMargins
): VirtualPage[] {
  const indexSet = new Set(targetIndices)
  return pages.map((page, idx) => {
    if (!indexSet.has(idx)) return page
    // Margins are absolute relative to the uncropped page, not cumulative.
    const old = page.cropMargins
    const curW = (page.width ?? 595.28) + (old ? old.left + old.right : 0)
    const curH = (page.height ?? 841.89) + (old ? old.top + old.bottom : 0)
    if (Object.values(margins).some(v => !Number.isFinite(v) || v < 0) ||
        curW - margins.left - margins.right < 10 || curH - margins.top - margins.bottom < 10) {
      throw new Error(`${idx + 1}페이지의 자르기 영역은 가로·세로 10pt 이상이어야 합니다.`)
    }
    const newW = curW - margins.left - margins.right
    const newH = curH - margins.top - margins.bottom

    return {
      ...page,
      width: newW,
      height: newH,
      cropMargins: margins
    }
  })
}

/** 본문 텍스트 교체 (비파괴 순수 함수) */
export function replaceTextOnVirtualPage(
  pages: VirtualPage[],
  pageIndex: number,
  replacement: TextReplacement
): VirtualPage[] {
  return pages.map((page, idx) => {
    if (idx !== pageIndex) return page
    const existing = page.textReplacements ?? []
    const filtered = existing.filter((r) => r.id !== replacement.id)
    return {
      ...page,
      textReplacements: [...filtered, replacement]
    }
  })
}

/** 본문 텍스트 교체 항목 삭제/복원 (비파괴 순수 함수) */
export function removeTextReplacementOnVirtualPage(
  pages: VirtualPage[],
  pageIndex: number,
  replacementId: string
): VirtualPage[] {
  return pages.map((page, idx) => {
    if (idx !== pageIndex) return page
    const existing = page.textReplacements ?? []
    return {
      ...page,
      textReplacements: existing.filter((r) => r.id !== replacementId)
    }
  })
}

/** 워터마크 적용/제거 (순수 함수) */
export function applyWatermarkToVirtualPages(
  pages: VirtualPage[],
  targetIndices: number[],
  config: WatermarkConfig | undefined
): VirtualPage[] {
  const indexSet = new Set(targetIndices)
  return pages.map((page, idx) => {
    if (!indexSet.has(idx)) return page
    return {
      ...page,
      watermarkConfig: config ? { ...config } : undefined
    }
  })
}

/** 페이지 번호 매기기 적용/제거 (순수 함수) */
export function applyPageNumberToVirtualPages(
  pages: VirtualPage[],
  targetIndices: number[],
  formatFn: (
    pageOrderIdx: number,
    totalSelected: number,
    globalIdx: number,
    totalPages: number
  ) => PageNumberConfig | undefined
): VirtualPage[] {
  const sortedTargets = [...new Set(targetIndices)]
    .filter((idx) => idx >= 0 && idx < pages.length)
    .sort((a, b) => a - b)
  const targetMap = new Map<number, number>()
  sortedTargets.forEach((globalIdx, orderIdx) => {
    targetMap.set(globalIdx, orderIdx)
  })

  return pages.map((page, idx) => {
    if (!targetMap.has(idx)) return page
    const orderIdx = targetMap.get(idx)!
    const config = formatFn(orderIdx, sortedTargets.length, idx, pages.length)
    return {
      ...page,
      pageNumberConfig: config ? { ...config } : undefined
    }
  })
}
