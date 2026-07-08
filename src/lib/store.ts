/**
 * 전역 애플리케이션 상태 (Zustand)
 */
import { create } from 'zustand'
import type { Annotation, AnnotationType } from '../../electron/shared/types'

/** 현재 로드된 문서의 메타 정보 */
export interface DocumentMeta {
  /** 원본 PDF 바이트 (편집/저장용, 상태 보존) */
  bytes: Uint8Array
  /** 파일 경로 (null 이면 새 문서/저장 필요) */
  filePath: string | null
  /** 표시할 파일명 */
  fileName: string
  /** 전체 페이지 수 */
  pageCount: number
  /** 페이지 회전 상태 (도 단위, pdfjs 가 렌더링에 반영) */
  rotations: number[]
}

export type ToolType = 'select' | AnnotationType

export type ThemeMode = 'light' | 'dark'

interface AppState {
  // ── 문서 ──
  document: DocumentMeta | null
  isDirty: boolean
  isLoading: boolean

  // ── 보기 ──
  currentPage: number
  zoom: number
  thumbnailsVisible: boolean
  theme: ThemeMode

  // ── 도구 ──
  activeTool: ToolType
  toolColor: string

  // ── 주석 ──
  annotations: Annotation[]
  selectedAnnotationId: string | null

  // ── 액션 ──
  setDocument: (doc: DocumentMeta | null) => void
  setDirty: (dirty: boolean) => void
  setLoading: (loading: boolean) => void
  setCurrentPage: (page: number) => void
  setZoom: (zoom: number) => void
  zoomIn: () => void
  zoomOut: () => void
  resetZoom: () => void
  toggleThumbnails: () => void
  toggleTheme: () => void
  setActiveTool: (tool: ToolType) => void
  setToolColor: (color: string) => void
  addAnnotation: (ann: Annotation) => void
  updateAnnotation: (id: string, patch: Partial<Annotation>) => void
  removeAnnotation: (id: string) => void
  clearAnnotations: () => void
  selectAnnotation: (id: string | null) => void
  /** 문서의 모든 페이지 회전 갱신 */
  setRotations: (rotations: number[]) => void
  rotatePages: (indices: number[], direction: 'left' | 'right') => void
}

const MIN_ZOOM = 0.25
const MAX_ZOOM = 5

export const useStore = create<AppState>((set) => ({
  document: null,
  isDirty: false,
  isLoading: false,

  currentPage: 0,
  zoom: 1,
  thumbnailsVisible: true,
  theme: 'dark',

  activeTool: 'select',
  toolColor: '#ffeb3b',

  annotations: [],
  selectedAnnotationId: null,

  setDocument: (doc) =>
    set({
      document: doc,
      currentPage: 0,
      zoom: 1,
      annotations: [],
      selectedAnnotationId: null,
      isDirty: false
    }),

  setDirty: (dirty) => set({ isDirty: dirty }),
  setLoading: (loading) => set({ isLoading: loading }),

  setCurrentPage: (page) => set({ currentPage: page }),

  setZoom: (zoom) => set({ zoom: Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, zoom)) }),
  zoomIn: () => set((s) => ({ zoom: Math.min(MAX_ZOOM, +(s.zoom + 0.25).toFixed(2)) })),
  zoomOut: () => set((s) => ({ zoom: Math.max(MIN_ZOOM, +(s.zoom - 0.25).toFixed(2)) })),
  resetZoom: () => set({ zoom: 1 }),

  toggleThumbnails: () => set((s) => ({ thumbnailsVisible: !s.thumbnailsVisible })),
  toggleTheme: () => set((s) => ({ theme: s.theme === 'dark' ? 'light' : 'dark' })),

  setActiveTool: (tool) => set({ activeTool: tool, selectedAnnotationId: null }),
  setToolColor: (color) => set({ toolColor: color }),

  addAnnotation: (ann) =>
    set((s) => ({
      annotations: [...s.annotations, ann],
      isDirty: true
    })),

  updateAnnotation: (id, patch) =>
    set((s) => ({
      annotations: s.annotations.map((a) => (a.id === id ? { ...a, ...patch } : a)),
      isDirty: true
    })),

  removeAnnotation: (id) =>
    set((s) => ({
      annotations: s.annotations.filter((a) => a.id !== id),
      selectedAnnotationId:
        s.selectedAnnotationId === id ? null : s.selectedAnnotationId,
      isDirty: true
    })),

  clearAnnotations: () => set({ annotations: [], selectedAnnotationId: null }),

  selectAnnotation: (id) => set({ selectedAnnotationId: id }),

  setRotations: (rotations) =>
    set((s) =>
      s.document ? { document: { ...s.document, rotations } } : {}
    ),

  rotatePages: (indices, direction) =>
    set((s) => {
      if (!s.document) return {}
      const delta = direction === 'right' ? 90 : -90
      const rotations = [...s.document.rotations]
      for (const idx of indices) {
        rotations[idx] = (((rotations[idx] ?? 0) + delta) % 360 + 360) % 360
      }
      return { document: { ...s.document, rotations }, isDirty: true }
    })
}))
