/**
 * 본문 텍스트 직접 편집 서비스
 * - PDF.js getTextContent() 기반으로 페이지 내 모든 텍스트 블록의
 *   PDF 원본 좌표(tx, ty, width, height)와 뷰포트 좌표(vx, vy, vw, vh) 추출
 * - 메모리 캐싱으로 0ms 즉시 응답
 */
import { getPdfDoc } from '../../hooks/usePdfDocument'
import type { VirtualPage } from '../domain/page'

export interface EditableTextItem {
  id: string
  str: string
  pdfX: number
  pdfY: number
  pdfWidth: number
  pdfHeight: number
  fontSize: number
  vx: number
  vy: number
  vw: number
  vh: number
}

// 텍스트 아이템 캐시 (페이지별)
const textItemsCache = new Map<string, EditableTextItem[]>()

export function clearTextEditorCache(): void {
  textItemsCache.clear()
}

/** 특정 가상 페이지의 편집 가능한 텍스트 블록 목록 추출 */
export async function getEditableTextItems(
  virtualPage: VirtualPage
): Promise<EditableTextItem[]> {
  const cacheKey = `${virtualPage.sourceDocId}_${virtualPage.sourcePageIndex}_${virtualPage.rotation}`
  if (textItemsCache.has(cacheKey)) {
    return textItemsCache.get(cacheKey)!
  }

  const doc = getPdfDoc(virtualPage.sourceDocId)
  if (!doc) return []

  try {
    const page = await doc.getPage(virtualPage.sourcePageIndex + 1)
    const viewport = page.getViewport({ scale: 1, rotation: virtualPage.rotation })
    const textContent = await page.getTextContent()

    const items: EditableTextItem[] = []
    let counter = 0

    for (const item of textContent.items) {
      if (!('str' in item)) continue
      const str = item.str
      if (!str || !str.trim()) continue

      const transform = item.transform // [scaleX, skewY, skewX, scaleY, tx, ty]
      const tx = transform[4]
      const ty = transform[5]
      const itemWidth = item.width || 20
      const fontSize = Math.hypot(transform[0], transform[1]) || item.height || 12
      const itemHeight = item.height || fontSize

      const [vx1, vy1, vx2, vy2] = viewport.convertToViewportRectangle([
        tx,
        ty,
        tx + itemWidth,
        ty + itemHeight
      ])

      const vx = Math.min(vx1, vx2)
      const vy = Math.min(vy1, vy2)
      const vw = Math.max(Math.abs(vx2 - vx1), 8)
      const vh = Math.max(Math.abs(vy2 - vy1), fontSize)

      counter += 1
      items.push({
        id: `txt_${virtualPage.sourcePageIndex}_${counter}`,
        str,
        pdfX: tx,
        pdfY: ty,
        pdfWidth: itemWidth,
        pdfHeight: itemHeight,
        fontSize: Math.round(fontSize),
        vx,
        vy,
        vw,
        vh
      })
    }

    textItemsCache.set(cacheKey, items)
    return items
  } catch (err) {
    console.error('getEditableTextItems error:', err)
    return []
  }
}
