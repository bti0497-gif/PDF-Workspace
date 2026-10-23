/**
 * PDF 텍스트 검색 서비스
 * - PDF.js getTextContent() 기반 문서 전체 텍스트 검색
 * - convertToViewportRectangle 을 통해 회전을 반영한 정확한 뷰포트 좌표 계산
 */
import { getPdfDoc } from '../../hooks/usePdfDocument'
import type { VirtualPage } from '../domain/page'
import type { SearchMatch } from '../../lib/store'

export async function searchInDocument(
  pages: VirtualPage[],
  query: string
): Promise<SearchMatch[]> {
  const cleanQuery = query.trim().toLowerCase()
  if (!cleanQuery || pages.length === 0) return []

  const matches: SearchMatch[] = []

  for (let pageIdx = 0; pageIdx < pages.length; pageIdx++) {
    const vPage = pages[pageIdx]
    const doc = getPdfDoc(vPage.sourceDocId)
    if (!doc) continue

    try {
      const page = await doc.getPage(vPage.sourcePageIndex + 1)
      const viewport = page.getViewport({ scale: 1, rotation: vPage.rotation })
      const textContent = await page.getTextContent()

      for (const item of textContent.items) {
        if (!('str' in item)) continue
        const str = item.str
        if (!str) continue

        const lowerStr = str.toLowerCase()
        let matchIndex = lowerStr.indexOf(cleanQuery)

        while (matchIndex !== -1) {
          const transform = item.transform // [scaleX, skewY, skewX, scaleY, tx, ty]
          const tx = transform[4]
          const ty = transform[5]
          const itemWidth = item.width || 20
          const itemHeight = item.height || 12

          // 검색어가 문자열 내 일부인 경우 위치/폭 비례 계산
          const charWidth = itemWidth / str.length
          const matchX = tx + matchIndex * charWidth
          const matchW = Math.max(charWidth * cleanQuery.length, 6)

          // 회전이 반영된 뷰포트 사각형 변환 (scale: 1 기준)
          const [vx1, vy1, vx2, vy2] = viewport.convertToViewportRectangle([
            matchX,
            ty,
            matchX + matchW,
            ty + itemHeight
          ])

          const x = Math.min(vx1, vx2)
          const y = Math.min(vy1, vy2)
          const width = Math.max(Math.abs(vx2 - vx1), 6)
          const height = Math.max(Math.abs(vy2 - vy1), itemHeight)

          matches.push({
            pageIndex: pageIdx,
            text: str.substring(matchIndex, matchIndex + cleanQuery.length),
            x,
            y,
            width,
            height
          })

          matchIndex = lowerStr.indexOf(cleanQuery, matchIndex + cleanQuery.length)
        }
      }
    } catch (err) {
      console.error(`페이지 ${pageIdx + 1} 검색 실패:`, err)
    }
  }

  return matches
}
