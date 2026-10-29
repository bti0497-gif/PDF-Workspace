/**
 * 메인 프로세스와 렌더러 프로세스가 공유하는 타입 정의
 */

/** 파일 열기 결과 */
export interface OpenFileResult {
  filePath: string
  fileName: string
  /** 파일의 Uint8Array (pdfjs-dist / pdf-lib 에 전달) */
  data: number[] | Uint8Array
}

/** 페이지 회전 방향 */
export type RotationDirection = 'left' | 'right'

/** 페이지 작업 명령 (회전/삭제/재정렬) */
export interface PageOperation {
  type: 'rotate-left' | 'rotate-right' | 'delete' | 'reorder' | 'insert'
  pageIndices?: number[]
  targetIndex?: number
  /** insert 시 외부 PDF 데이터 */
  sourceData?: number[]
}

/** 주석 종류 */
export type AnnotationType =
  | 'highlight'
  | 'rectangle'
  | 'textbox'
  | 'drawing'
  | 'stamp'
  | 'redaction'

/** 단일 주석 (PDF 좌표계, 원점 좌하단) */
export interface Annotation {
  id: string
  type: AnnotationType
  pageIndex: number
  /** PDF 좌표계 (포인트 단위), 원점 = 페이지 좌하단 */
  x: number
  y: number
  width: number
  height: number
  color: string
  strokeWidth?: number
  opacity?: number
  fontSize?: number
  /** 자유 그리기인 경우 경로 점들 */
  points?: { x: number; y: number }[]
  /** 텍스트 박스인 경우 내용 */
  text?: string
  /** 스탬프/서명/이미지인 경우 Base64 Data URL (data:image/png;base64,... 등) */
  imageDataUrl?: string
}

/** 합치기/분할에 사용할 파일 항목 */
export interface FileEntry {
  filePath: string
  fileName: string
}

/** IPC 채널 이름 집합 (오타 방지용) */
export const IPC = {
  // 파일
  OPEN_FILE: 'dialog:openFile',
  SAVE_FILE: 'dialog:saveFile',
  READ_FILE: 'file:read',
  WRITE_FILE: 'file:write',
  OPEN_MULTIPLE: 'dialog:openMultiple',
  SAVE_PDF_BYTES: 'file:savePdfBytes',
  SHOW_IN_FOLDER: 'shell:showInFolder',
  // 메뉴 이벤트 (메인 -> 렌더러)
  MENU_EVENT: 'menu:event',
  // 다이얼로그
  MERGE_DIALOG: 'dialog:merge',
  SPLIT_DIALOG: 'dialog:split'
} as const

export type MenuEvent =
  | 'file:open'
  | 'file:save'
  | 'file:saveAs'
  | 'file:print'
  | 'file:close'
  | 'edit:undo'
  | 'edit:redo'
  | 'edit:deleteAnnotation'
  | 'tool:highlight'
  | 'tool:rectangle'
  | 'tool:textbox'
  | 'tool:drawing'
  | 'tool:select'
  | 'tool:merge'
  | 'tool:split'
  | 'tool:normalize'
  | 'tool:crop'
  | 'view:zoomIn'
  | 'view:zoomOut'
  | 'view:zoomReset'
  | 'view:fitWidth'
  | 'view:fitPage'
  | 'view:toggleThumbnails'
  | 'view:toggleTheme'
  | 'page:rotateLeft'
  | 'page:rotateRight'
  | 'page:delete'
  | 'page:extract'
  | 'page:previous'
  | 'page:next'
  | 'help:about'
  | 'help:shortcuts'

/** 업데이트 확인 결과 */
export interface UpdateCheckResult {
  status: 'dev' | 'available' | 'not-available' | 'downloading' | 'downloaded' | 'error' | string
  version?: string
  message?: string
  percent?: number
}

