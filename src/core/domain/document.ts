/**
 * 문서(Document) 및 가상 매니페스트 도메인 모델
 */
import type { VirtualPage } from './page'

export const ROOT_DOC_ID = 'root_doc'

export interface SourceDocument {
  id: string
  fileName: string
  filePath: string | null
  bytes: Uint8Array
  pageCount: number
}

export interface VirtualDocumentManifest {
  /** 현재 문서 표시명 */
  fileName: string
  /** 원본 파일 경로 (null 이면 저장 필요) */
  filePath: string | null
  /** 가상 페이지 목록 (화면 표시 및 최종 출력 순서) */
  pages: VirtualPage[]
  /** 참조하는 원본 PDF 소스들 (외부 삽입 PDF 포함) */
  sources: Map<string, SourceDocument>
  /** 변경 여부 */
  isDirty: boolean
}

/** 새 가상 문서 매니페스트 생성 */
export function createDocumentManifest(
  fileName: string,
  filePath: string | null,
  bytes: Uint8Array,
  pageCount: number,
  initialRotations?: number[]
): VirtualDocumentManifest {
  const rootSource: SourceDocument = {
    id: ROOT_DOC_ID,
    fileName,
    filePath,
    bytes,
    pageCount
  }

  const sources = new Map<string, SourceDocument>()
  sources.set(ROOT_DOC_ID, rootSource)

  const pages: VirtualPage[] = Array.from({ length: pageCount }, (_, i) => ({
    id: `p_${ROOT_DOC_ID}_${i}`,
    sourceDocId: ROOT_DOC_ID,
    sourcePageIndex: i,
    rotation: initialRotations?.[i] ?? 0
  }))

  return {
    fileName,
    filePath,
    pages,
    sources,
    isDirty: false
  }
}
