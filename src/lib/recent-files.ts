/**
 * 최근 열어본 문서 관리 유틸리티 (로컬 스토리지 기반, 최대 8개 유지)
 */

export interface RecentFileItem {
  fileName: string
  filePath: string
  pageCount: number
  openedAt: number
}

const STORAGE_KEY = 'pdf_recent_files'
const MAX_RECENT_FILES = 8

/** 최근 열어본 파일 목록 조회 */
export function getRecentFiles(): RecentFileItem[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw)
    if (Array.isArray(parsed)) {
      return parsed
    }
    return []
  } catch {
    return []
  }
}

/** 최근 파일 추가 (filePath 기준 중복 제거 및 최신 순 정렬, 최대 8개) */
export function addRecentFile(item: RecentFileItem): void {
  try {
    const list = getRecentFiles()
    const filtered = list.filter((f) => f.filePath !== item.filePath)
    filtered.unshift(item)
    const limited = filtered.slice(0, MAX_RECENT_FILES)
    localStorage.setItem(STORAGE_KEY, JSON.stringify(limited))
  } catch {
    /* localStorage 접근 제한 안전 폴백 */
  }
}

/** 특정 최근 파일 항목 삭제 */
export function removeRecentFile(filePath: string): void {
  try {
    const list = getRecentFiles()
    const filtered = list.filter((f) => f.filePath !== filePath)
    localStorage.setItem(STORAGE_KEY, JSON.stringify(filtered))
  } catch {
    /* localStorage 접근 제한 안전 폴백 */
  }
}

/** 최근 파일 목록 전체 비우기 */
export function clearRecentFiles(): void {
  try {
    localStorage.removeItem(STORAGE_KEY)
  } catch {
    /* localStorage 접근 제한 안전 폴백 */
  }
}
