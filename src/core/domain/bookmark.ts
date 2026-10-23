/**
 * 북마크(목차/Outline) 도메인 모델
 */

export interface BookmarkNode {
  /** 식별용 ID */
  id: string
  /** 목차 제목 */
  title: string
  /** 목적지 페이지 번호 (0-based, 파싱 실패 시 null) */
  targetPageIndex: number | null
  /** 목적지 원시 데이터 (PDF.js dest) */
  dest?: unknown
  /** 펼침/접힘 상태 */
  isExpanded?: boolean
  /** 하위 목차 목록 */
  children: BookmarkNode[]
}

let _bmCounter = 0
export function generateBookmarkId(): string {
  _bmCounter += 1
  return `bm_${Date.now().toString(36)}_${_bmCounter}`
}
