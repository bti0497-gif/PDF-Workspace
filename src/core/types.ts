/**
 * 도메인 타입 (MVVM Model 계층이 사용)
 *
 * 메인/렌더러 공용 타입은 electron/shared/types.ts 에 정의되어 있고,
 * 여기서는 렌더러 도메인(Service/ViewModel) 전용 타입을 추가로 정의한다.
 */
export type {
  Annotation,
  AnnotationType,
  MenuEvent,
  OpenFileResult,
  RotationDirection
} from '../../electron/shared/types'

import type { Annotation } from '../../electron/shared/types'

// ─────────────────────────────────────────────────────────────
// 문서 메타데이터 (렌더러 도메인 전용)
// ─────────────────────────────────────────────────────────────

/** 로드된 문서의 메타 정보 - ViewModel 상태의 일부 */
export interface DocumentMeta {
  /** 원본 PDF 바이트 (편집/저장용) */
  bytes: Uint8Array
  /** 파일 경로 (null = 새 문서/저장 필요) */
  filePath: string | null
  /** 표시 파일명 */
  fileName: string
  /** 전체 페이지 수 */
  pageCount: number
  /** 페이지별 회전 각도 (도 단위) */
  rotations: number[]
}

// ─────────────────────────────────────────────────────────────
// 주석 관련 값 객체
// ─────────────────────────────────────────────────────────────

/** 주석 색상 후보 (도구 팔레트) */
export const ANNOTATION_COLORS = {
  highlight: '#ffeb3b',
  rectangle: '#e5534b',
  textbox: '#1e1e1e',
  drawing: '#0e6fd6'
} as const

/** 도구 종류 (선택 + 주석 4종) */
export type ToolType = 'select' | Annotation['type']

/** 테마 */
export type ThemeMode = 'light' | 'dark'

/** 줌 제한 (비즈니스 규칙) */
export const ZOOM_LIMITS = { MIN: 0.25, MAX: 5 } as const

/** 줌 단계 (버튼 클릭 시 증감폭) */
export const ZOOM_STEP = 0.25
