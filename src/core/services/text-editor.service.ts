/**
 * 본문 텍스트 직접 편집 서비스 (고도화 엔진)
 * - PDF.js getTextContent() 기반으로 페이지 내 모든 텍스트 블록의
 *   PDF 원본 좌표(tx, ty, width, height)와 뷰포트 좌표(vx, vy, vw, vh) 추출
 * - 2칸 이상의 연속 공백(/\s{2,}/)을 기준으로 개별 세그먼트로 분리하여 표 열 엉킴 방지
 * - 인접한 동일 베이스라인 낱글자 조각은 스마트 단어 블록으로 자동 병합 (Smart Run Merging)
 * - 디센더/어센더 베이스라인 정밀 보정 및 소수점 폰트 크기 보존
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

interface RawToken {
  str: string
  pdfX: number
  pdfY: number
  pdfWidth: number
  pdfHeight: number
  fontSize: number
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

    const rawTokens: RawToken[] = []

    for (const item of textContent.items) {
      if (!('str' in item)) continue
      const fullStr = item.str
      if (!fullStr || !fullStr.trim()) continue

      const transform = item.transform // [scaleX, skewY, skewX, scaleY, tx, ty]
      const tx = transform[4]
      const ty = transform[5] // baseline Y
      const totalWidth = item.width || 20
      const rawFontSize = Math.hypot(transform[0], transform[1]) || item.height || 12
      const fontSize = Number(rawFontSize.toFixed(1))
      const itemHeight = item.height || fontSize

      // 1) 2칸 이상의 연속 공백(/\s{2,}/)이 있으면 세그먼트로 분리 (표 열 간격 등)
      //    단일 공백으로 이어진 단어들은 하나의 구문으로 보존
      const segments: Array<{ text: string; startIdx: number }> = []
      const regex = /\S+(?:\s\S+)*/g
      let m: RegExpExecArray | null
      while ((m = regex.exec(fullStr)) !== null) {
        segments.push({ text: m[0], startIdx: m.index })
      }

      const totalChars = Math.max(1, fullStr.length)

      for (const seg of segments) {
        const segRatio = seg.startIdx / totalChars
        const segLenRatio = seg.text.length / totalChars
        const segPdfX = tx + totalWidth * segRatio
        const segPdfWidth = Math.max(8, totalWidth * segLenRatio)

        rawTokens.push({
          str: seg.text,
          pdfX: segPdfX,
          pdfY: ty,
          pdfWidth: segPdfWidth,
          pdfHeight: itemHeight,
          fontSize
        })
      }
    }

    // 2) 베이스라인 및 X 좌표 기준으로 정렬 (위쪽 줄 -> 아래쪽 줄, 왼쪽 -> 오른쪽)
    rawTokens.sort((a, b) => {
      const dy = b.pdfY - a.pdfY // PDF는 Y가 클수록 상단
      if (Math.abs(dy) > a.fontSize * 0.4) return dy
      return a.pdfX - b.pdfX
    })

    // 3) 같은 베이스라인에 위치한 인접 낱글자 조각 스마트 병합 (Smart Run Merging)
    const mergedTokens: RawToken[] = []
    let current: RawToken | null = null

    for (const token of rawTokens) {
      if (!current) {
        current = { ...token }
        continue
      }

      const isSameBaseline =
        Math.abs(current.pdfY - token.pdfY) <= current.fontSize * 0.25
      const gap = token.pdfX - (current.pdfX + current.pdfWidth)

      // 인접 글자 간격이 fontSize * 0.35 이하인 경우에만 같은 단어/구문으로 병합
      // (fontSize * 0.4 이상의 열 간격이나 분리된 셀은 절대 병합하지 않음)
      if (isSameBaseline && gap >= -2 && gap <= current.fontSize * 0.35) {
        const spaceChar = gap > current.fontSize * 0.12 ? ' ' : ''
        current.str += spaceChar + token.str
        current.pdfWidth = token.pdfX + token.pdfWidth - current.pdfX
        current.pdfHeight = Math.max(current.pdfHeight, token.pdfHeight)
      } else {
        mergedTokens.push(current)
        current = { ...token }
      }
    }
    if (current) {
      mergedTokens.push(current)
    }

    // 4) 베이스라인(Baseline) 정밀 보정 뷰포트 사각형 변환
    const items: EditableTextItem[] = []
    let counter = 0

    for (const tok of mergedTokens) {
      counter += 1
      const fs = tok.fontSize

      // ty는 베이스라인이므로 디센더(0.18)와 어센더(0.88) 반영
      const [vx1, vy1, vx2, vy2] = viewport.convertToViewportRectangle([
        tok.pdfX,
        tok.pdfY - fs * 0.18,
        tok.pdfX + tok.pdfWidth,
        tok.pdfY + fs * 0.88
      ])

      const vx = Math.min(vx1, vx2)
      const vy = Math.min(vy1, vy2)
      const vw = Math.max(Math.abs(vx2 - vx1), 8)
      const vh = Math.max(Math.abs(vy2 - vy1), fs)

      items.push({
        id: `txt_${virtualPage.sourcePageIndex}_${counter}`,
        str: tok.str,
        pdfX: tok.pdfX,
        pdfY: tok.pdfY,
        pdfWidth: tok.pdfWidth,
        pdfHeight: tok.pdfHeight,
        fontSize: fs,
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
