/**
 * 전역 애플리케이션 상태 (Zustand)
 *
 * MVVM ViewModel & 비파괴 프록시 매니페스트 통합:
 * - pages: 가상 페이지 목록 (초경량 조작, 0ms 렌더링)
 * - sources: 원본 PDF 바이트 소스 맵 (불변 보존)
 * - bookmarks: 계층형 목차 트리
 * - history: 가상 매니페스트 기반 Undo/Redo 초경량 스택
 */
import { create } from 'zustand'
import type { Annotation, AnnotationType } from '../../electron/shared/types'
import type { VirtualPage, WatermarkConfig, PageNumberConfig } from '../core/domain/page'
import {
  createInitialVirtualPages,
  reorderVirtualPages,
  reorderMultipleVirtualPages,
  rotateVirtualPages,
  deleteVirtualPages,
  duplicateVirtualPages,
  insertVirtualPagesAt,
  normalizeVirtualPages,
  cropVirtualPages,
  replaceTextOnVirtualPage,
  removeTextReplacementOnVirtualPage,
  applyWatermarkToVirtualPages,
  applyPageNumberToVirtualPages
} from '../core/domain/page'
import type {
  NormalizedPageConfig,
  CropMargins,
  TextReplacement
} from '../core/domain/page'
import type { SourceDocument } from '../core/domain/document'
import { ROOT_DOC_ID } from '../core/domain/document'
import { generateBookmarkId, type BookmarkNode } from '../core/domain/bookmark'

/** 하위 호환성을 위한 현재 로드된 문서 메타 정보 */
export interface DocumentMeta {
  bytes: Uint8Array
  filePath: string | null
  fileName: string
  pageCount: number
  rotations: number[]
}

export type ToolType = 'select' | AnnotationType | 'text-edit'
export type ThemeMode = 'light' | 'dark'
export type ViewMode = 'single' | 'continuous' | 'two-page'
export type SidebarTab = 'thumbnails' | 'bookmarks' | 'annotations'

/** 문서 메타데이터 (PDF Info) */
export interface DocumentMetadata {
  title?: string
  author?: string
  subject?: string
  keywords?: string
  creator?: string
}

/** 검색 매칭 항목 정보 */
export interface SearchMatch {
  pageIndex: number
  text: string
  x: number
  y: number
  width: number
  height: number
}

export interface LoadingProgress {
  visible: boolean
  percent: number
  title: string
  detail?: string
}

interface HistoryState {
  past: VirtualPage[][]
  future: VirtualPage[][]
}

// ── 로컬 스토리지 환경설정 안전 헬퍼 ──
function loadPreference<T>(key: string, defaultValue: T): T {
  try {
    const val = localStorage.getItem(key)
    if (val === null) return defaultValue
    return JSON.parse(val) as T
  } catch {
    return defaultValue
  }
}

function savePreference<T>(key: string, value: T): void {
  try {
    localStorage.setItem(key, JSON.stringify(value))
  } catch {
    /* localStorage 접근 제한 환경 안전 폴백 */
  }
}

interface AppState {
  // ── 문서 & 가상 매니페스트 ──
  document: DocumentMeta | null
  pages: VirtualPage[]
  sources: Map<string, SourceDocument>
  isDirty: boolean
  isLoading: boolean
  loadingProgress: LoadingProgress | null

  // ── 사이드바 & 목차 & UI 설정 ──
  sidebarTab: SidebarTab
  bookmarks: BookmarkNode[]
  thumbnailSize: number
  sidebarWidth: number
  selectionColor: string

  // ── 보기 & 선택 ──
  currentPage: number
  selectedPageIndices: number[]
  selectionAnchorIndex: number | null
  viewMode: ViewMode
  zoom: number
  thumbnailsVisible: boolean
  theme: ThemeMode

  // ── 도구 ──
  activeTool: ToolType
  toolColor: string
  toolStrokeWidth: number
  toolOpacity: number
  toolFontSize: number

  // ── 주석 ──
  annotations: Annotation[]
  selectedAnnotationId: string | null
  copiedAnnotation: Annotation | null

  // ── 검색 ──
  searchQuery: string
  searchMatches: SearchMatch[]
  activeMatchIndex: number
  searchBarVisible: boolean

  // ── 히스토리 ──
  history: HistoryState

  // ── 문서 메타데이터 (속성) ──
  metadata?: DocumentMetadata

  // ── 액션 ──
  setDocumentMetadata: (meta: DocumentMetadata) => void
  setDocument: (doc: DocumentMeta | null, dimensions?: { width: number; height: number }) => void
  setDirty: (dirty: boolean) => void
  setLoading: (loading: boolean) => void
  setLoadingProgress: (progress: LoadingProgress | null) => void
  setSidebarTab: (tab: SidebarTab) => void
  setBookmarks: (bookmarks: BookmarkNode[]) => void
  addBookmark: (title: string, pageIndex: number) => void
  removeBookmark: (id: string) => void
  setThumbnailSize: (size: number) => void
  setSidebarWidth: (width: number) => void
  setSelectionColor: (color: string) => void

  setCurrentPage: (page: number) => void
  setViewMode: (mode: ViewMode) => void
  selectPage: (index: number, mode?: 'single' | 'toggle' | 'range') => void
  selectAllPages: () => void
  clearPageSelection: () => void
  selectPagesByPredicate: (mode: 'all' | 'none' | 'even' | 'odd' | 'invert' | number[]) => void

  setZoom: (zoom: number) => void
  zoomIn: () => void
  zoomOut: () => void
  resetZoom: () => void

  toggleThumbnails: () => void
  toggleTheme: () => void

  setActiveTool: (tool: ToolType) => void
  setToolColor: (color: string) => void
  setToolStrokeWidth: (w: number) => void
  setToolOpacity: (o: number) => void
  setToolFontSize: (s: number) => void

  addAnnotation: (ann: Annotation) => void
  updateAnnotation: (id: string, patch: Partial<Annotation>) => void
  removeAnnotation: (id: string) => void
  clearAnnotations: () => void
  selectAnnotation: (id: string | null) => void
  copySelectedAnnotation: () => void
  pasteAnnotation: () => void

  // ── 검색 액션 ──
  openSearchBar: () => void
  closeSearchBar: () => void
  setSearchQuery: (query: string) => void
  setSearchMatches: (matches: SearchMatch[]) => void
  nextSearchMatch: () => void
  prevSearchMatch: () => void

  // ── 본문 텍스트 인라인 수정 ──
  addTextReplacement: (replacement: TextReplacement) => void
  removeTextReplacement: (pageIndex: number, replacementId: string) => void

  // ── 가상 페이지 조작 (비파괴, 0ms) ──
  reorderPages: (fromIndex: number, toIndex: number) => void
  reorderMultiplePages: (fromIndices: number[], targetIndex: number) => void
  rotatePages: (indices: number[], direction: 'left' | 'right' | '180') => void
  deletePages: (indices: number[]) => void
  duplicatePages: (indices: number[]) => void
  normalizePages: (indices: number[], config: NormalizedPageConfig) => void
  cropPages: (indices: number[], margins: CropMargins) => void
  applyWatermark: (indices: number[], config: WatermarkConfig | undefined) => void
  applyPageNumbering: (
    indices: number[],
    options: {
      format: 'number' | 'dash' | 'slash' | 'pageOf'
      startNumber: number
      position: PageNumberConfig['position']
      fontSize: number
      color: string
      margin: number
    } | null
  ) => void
  insertPagesFromDocument: (
    sourceId: string,
    fileName: string,
    bytes: Uint8Array,
    pageCount: number,
    insertIndex: number,
    dimensions?: { width: number; height: number }
  ) => void
  insertPdfPagesAt: (
    sourceId: string,
    fileName: string,
    bytes: Uint8Array,
    pageCount: number,
    insertIndex: number,
    dimensions?: { width: number; height: number }
  ) => void

  undo: () => void
  redo: () => void
}

const MIN_ZOOM = 0.25
const MAX_ZOOM = 5

export const useStore = create<AppState>((set, get) => ({
  document: null,
  pages: [],
  sources: new Map(),
  isDirty: false,
  isLoading: false,
  loadingProgress: null,

  sidebarTab: 'thumbnails',
  bookmarks: [],
  thumbnailSize: Math.max(96, Math.min(260, Number(loadPreference('douzone_pdf_thumb_size', 156)) || 156)),
  sidebarWidth: Math.max(170, Math.min(640, Number(loadPreference('douzone_pdf_sidebar_width', 220)) || 220)),
  selectionColor: loadPreference('douzone_pdf_selection_color', '#10b981') || '#10b981',

  currentPage: 0,
  selectedPageIndices: [0],
  selectionAnchorIndex: 0,
  viewMode: 'continuous',
  zoom: 1,
  thumbnailsVisible: true,
  theme: 'dark',

  activeTool: 'select',
  toolColor: '#ffeb3b',
  toolStrokeWidth: 2,
  toolOpacity: 1,
  toolFontSize: 14,

  annotations: [],
  selectedAnnotationId: null,
  copiedAnnotation: null,

  searchQuery: '',
  searchMatches: [],
  activeMatchIndex: -1,
  searchBarVisible: false,

  history: { past: [], future: [] },
  metadata: undefined,

  setDocumentMetadata: (meta) => set({ metadata: meta, isDirty: true }),

  setDocument: (doc, dimensions) => {
    if (!doc) {
      set({
        document: null,
        pages: [],
        sources: new Map(),
        bookmarks: [],
        currentPage: 0,
        selectedPageIndices: [],
        selectionAnchorIndex: null,
        annotations: [],
        selectedAnnotationId: null,
        isDirty: false,
        history: { past: [], future: [] },
        metadata: undefined
      })
      return
    }

    const pages = createInitialVirtualPages(ROOT_DOC_ID, doc.pageCount, doc.rotations, dimensions)
    const sources = new Map<string, SourceDocument>()
    sources.set(ROOT_DOC_ID, {
      id: ROOT_DOC_ID,
      fileName: doc.fileName,
      filePath: doc.filePath,
      bytes: doc.bytes,
      pageCount: doc.pageCount
    })

    set({
      document: doc,
      pages,
      sources,
      currentPage: 0,
      selectedPageIndices: [0],
      selectionAnchorIndex: 0,
      zoom: 1,
      annotations: [],
      selectedAnnotationId: null,
      isDirty: false,
      metadata: undefined,
      history: { past: [], future: [] }
    })
  },

  setDirty: (dirty) => set({ isDirty: dirty }),
  setLoading: (loading) => set({ isLoading: loading }),
  setLoadingProgress: (progress) => set({ loadingProgress: progress }),
  setSidebarTab: (tab) => set({ sidebarTab: tab }),
  setBookmarks: (bookmarks) => set({ bookmarks }),
  addBookmark: (title: string, pageIndex: number) => {
    const newBm: BookmarkNode = {
      id: generateBookmarkId(),
      title: title.trim() || `${pageIndex + 1} 페이지 책갈피`,
      targetPageIndex: pageIndex,
      children: []
    }
    set((state) => ({
      bookmarks: [...state.bookmarks, newBm],
      isDirty: true
    }))
  },
  removeBookmark: (id: string) => {
    const filterTree = (nodes: BookmarkNode[]): BookmarkNode[] => {
      return nodes
        .filter((n) => n.id !== id)
        .map((n) => ({
          ...n,
          children: n.children ? filterTree(n.children) : []
        }))
    }
    set((state) => ({
      bookmarks: filterTree(state.bookmarks),
      isDirty: true
    }))
  },
  setThumbnailSize: (size) => {
    const clamped = Math.max(96, Math.min(260, Math.round(size)))
    savePreference('douzone_pdf_thumb_size', clamped)
    set({ thumbnailSize: clamped })
  },
  setSidebarWidth: (width) => {
    const clamped = Math.max(170, Math.min(640, Math.round(width)))
    savePreference('douzone_pdf_sidebar_width', clamped)
    set({ sidebarWidth: clamped })
  },
  setSelectionColor: (color) => {
    savePreference('douzone_pdf_selection_color', color)
    set({ selectionColor: color })
  },

  setCurrentPage: (page) => {
    const { pages, selectedPageIndices } = get()
    const safePage = Math.max(0, Math.min(pages.length - 1, page))
    if (selectedPageIndices.length <= 1) {
      set({
        currentPage: safePage,
        selectedPageIndices: [safePage],
        selectionAnchorIndex: safePage
      })
    } else {
      set({ currentPage: safePage })
    }
  },

  setViewMode: (mode) => set({ viewMode: mode }),

  selectPage: (index, mode = 'single') => {
    const { selectedPageIndices, pages, selectionAnchorIndex, currentPage } = get()
    if (index < 0 || index >= pages.length) return

    let nextSelected: number[]
    let nextAnchor: number | null = selectionAnchorIndex

    if (mode === 'single') {
      nextSelected = [index]
      nextAnchor = index
      set({
        selectedPageIndices: nextSelected,
        selectionAnchorIndex: nextAnchor,
        currentPage: index
      })
    } else if (mode === 'toggle') {
      if (selectedPageIndices.includes(index)) {
        nextSelected = selectedPageIndices.filter((i) => i !== index)
      } else {
        nextSelected = [...selectedPageIndices, index].sort((a, b) => a - b)
      }
      nextAnchor = index
      const nextCurrent =
        nextSelected.length > 0
          ? nextSelected.includes(currentPage)
            ? currentPage
            : nextSelected[nextSelected.length - 1]
          : index
      set({
        selectedPageIndices: nextSelected,
        selectionAnchorIndex: nextAnchor,
        currentPage: nextCurrent
      })
    } else {
      // range (Shift+클릭) - anchor 기준 대체 범위 선택
      const anchor = selectionAnchorIndex ?? selectedPageIndices[0] ?? currentPage
      const start = Math.min(anchor, index)
      const end = Math.max(anchor, index)
      nextSelected = []
      for (let i = start; i <= end; i++) {
        nextSelected.push(i)
      }
      set({
        selectedPageIndices: nextSelected,
        selectionAnchorIndex: anchor,
        currentPage: index
      })
    }
  },

  selectAllPages: () => {
    const { pages } = get()
    set({
      selectedPageIndices: pages.map((_, i) => i),
      selectionAnchorIndex: 0
    })
  },

  clearPageSelection: () => {
    const { currentPage } = get()
    set({
      selectedPageIndices: [currentPage],
      selectionAnchorIndex: currentPage
    })
  },

  selectPagesByPredicate: (mode) => {
    const { pages, selectedPageIndices, currentPage } = get()
    if (pages.length === 0) return

    let nextSelected: number[]
    if (mode === 'all') {
      nextSelected = pages.map((_, i) => i)
    } else if (mode === 'none') {
      nextSelected = []
    } else if (mode === 'even') {
      // 짝수 페이지 번호 (2, 4, 6... -> 0-based 인덱스 1, 3, 5...)
      nextSelected = pages.map((_, i) => i).filter((idx) => (idx + 1) % 2 === 0)
    } else if (mode === 'odd') {
      // 홀수 페이지 번호 (1, 3, 5... -> 0-based 인덱스 0, 2, 4...)
      nextSelected = pages.map((_, i) => i).filter((idx) => (idx + 1) % 2 === 1)
    } else if (mode === 'invert') {
      const setIdx = new Set(selectedPageIndices)
      nextSelected = pages.map((_, i) => i).filter((idx) => !setIdx.has(idx))
    } else if (Array.isArray(mode)) {
      const valid = new Set(mode.filter((i) => i >= 0 && i < pages.length))
      nextSelected = Array.from(valid).sort((a, b) => a - b)
    } else {
      return
    }

    const anchor = nextSelected.length > 0 ? nextSelected[0] : null
    let nextCurrent = currentPage
    if (nextSelected.length > 0 && !nextSelected.includes(currentPage)) {
      nextCurrent = nextSelected[0]
    }

    set({
      selectedPageIndices: nextSelected,
      selectionAnchorIndex: anchor,
      currentPage: nextCurrent
    })
  },

  setZoom: (zoom) => set({ zoom: Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, zoom)) }),
  zoomIn: () => set((s) => ({ zoom: Math.min(MAX_ZOOM, +(s.zoom + 0.25).toFixed(2)) })),
  zoomOut: () => set((s) => ({ zoom: Math.max(MIN_ZOOM, +(s.zoom - 0.25).toFixed(2)) })),
  resetZoom: () => set({ zoom: 1 }),

  toggleThumbnails: () => set((s) => ({ thumbnailsVisible: !s.thumbnailsVisible })),
  toggleTheme: () => set((s) => ({ theme: s.theme === 'dark' ? 'light' : 'dark' })),

  setActiveTool: (tool) => set({ activeTool: tool, selectedAnnotationId: null }),
  setToolColor: (color) => {
    const { selectedAnnotationId, updateAnnotation } = get()
    set({ toolColor: color })
    if (selectedAnnotationId) {
      updateAnnotation(selectedAnnotationId, { color })
    }
  },
  setToolStrokeWidth: (w) => {
    const strokeWidth = Math.max(1, Math.min(24, w))
    const { selectedAnnotationId, updateAnnotation } = get()
    set({ toolStrokeWidth: strokeWidth })
    if (selectedAnnotationId) {
      updateAnnotation(selectedAnnotationId, { strokeWidth })
    }
  },
  setToolOpacity: (o) => {
    const opacity = Math.max(0.05, Math.min(1, o))
    const { selectedAnnotationId, updateAnnotation } = get()
    set({ toolOpacity: opacity })
    if (selectedAnnotationId) {
      updateAnnotation(selectedAnnotationId, { opacity })
    }
  },
  setToolFontSize: (s) => {
    const fontSize = Math.max(8, Math.min(72, s))
    const { selectedAnnotationId, updateAnnotation } = get()
    set({ toolFontSize: fontSize })
    if (selectedAnnotationId) {
      updateAnnotation(selectedAnnotationId, { fontSize })
    }
  },

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
      selectedAnnotationId: s.selectedAnnotationId === id ? null : s.selectedAnnotationId,
      isDirty: true
    })),

  clearAnnotations: () => set({ annotations: [], selectedAnnotationId: null }),
  selectAnnotation: (id) => set({ selectedAnnotationId: id }),

  copySelectedAnnotation: () => {
    const { annotations, selectedAnnotationId } = get()
    const ann = annotations.find((a) => a.id === selectedAnnotationId)
    if (ann) set({ copiedAnnotation: { ...ann } })
  },

  pasteAnnotation: () => {
    const { copiedAnnotation, currentPage, zoom } = get()
    if (!copiedAnnotation) return
    const offset = 15 / zoom
    const newAnn: Annotation = {
      ...copiedAnnotation,
      id: `ann-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
      pageIndex: currentPage,
      x: copiedAnnotation.x + offset,
      y: copiedAnnotation.y + offset
    }
    set((s) => ({
      annotations: [...s.annotations, newAnn],
      selectedAnnotationId: newAnn.id,
      isDirty: true
    }))
  },

  openSearchBar: () => set({ searchBarVisible: true }),
  closeSearchBar: () =>
    set({
      searchBarVisible: false,
      searchQuery: '',
      searchMatches: [],
      activeMatchIndex: -1
    }),
  setSearchQuery: (query) => set({ searchQuery: query }),
  setSearchMatches: (matches) =>
    set({
      searchMatches: matches,
      activeMatchIndex: matches.length > 0 ? 0 : -1
    }),
  nextSearchMatch: () => {
    const { searchMatches, activeMatchIndex } = get()
    if (searchMatches.length === 0) return
    const nextIdx = (activeMatchIndex + 1) % searchMatches.length
    const match = searchMatches[nextIdx]
    set({ activeMatchIndex: nextIdx, currentPage: match.pageIndex })
  },
  prevSearchMatch: () => {
    const { searchMatches, activeMatchIndex } = get()
    if (searchMatches.length === 0) return
    const prevIdx = (activeMatchIndex - 1 + searchMatches.length) % searchMatches.length
    const match = searchMatches[prevIdx]
    set({ activeMatchIndex: prevIdx, currentPage: match.pageIndex })
  },

  // ── 가상 페이지 조작 (비파괴, 0ms) ──
  reorderPages: (fromIndex, toIndex) => {
    const { selectedPageIndices } = get()
    if (selectedPageIndices.includes(fromIndex) && selectedPageIndices.length > 1) {
      get().reorderMultiplePages(selectedPageIndices, toIndex)
      return
    }

    const { pages, document, history, currentPage, annotations } = get()
    if (fromIndex === toIndex || !document) return

    const nextPages = reorderVirtualPages(pages, fromIndex, toIndex)
    if (nextPages === pages) return

    const oldToNew = new Map<number, number>()
    for (let oldIdx = 0; oldIdx < pages.length; oldIdx++) {
      const oldId = pages[oldIdx].id
      const newIdx = nextPages.findIndex((p) => p.id === oldId)
      if (newIdx !== -1) oldToNew.set(oldIdx, newIdx)
    }

    const nextAnnotations = annotations.map((ann) => ({
      ...ann,
      pageIndex: oldToNew.get(ann.pageIndex) ?? ann.pageIndex
    }))

    const nextCurrent = oldToNew.get(currentPage) ?? toIndex

    set({
      pages: nextPages,
      currentPage: nextCurrent,
      selectedPageIndices: [nextCurrent],
      selectionAnchorIndex: nextCurrent,
      annotations: nextAnnotations,
      document: {
        ...document,
        rotations: nextPages.map((p) => p.rotation)
      },
      isDirty: true,
      history: {
        past: [...history.past, pages],
        future: []
      }
    })
  },

  reorderMultiplePages: (fromIndices, targetIndex) => {
    const { pages, document, history, currentPage, annotations } = get()
    if (fromIndices.length === 0 || !document) return

    const { pages: nextPages, newSelectedIndices } = reorderMultipleVirtualPages(
      pages,
      fromIndices,
      targetIndex
    )
    if (nextPages === pages) return

    const oldToNew = new Map<number, number>()
    for (let oldIdx = 0; oldIdx < pages.length; oldIdx++) {
      const oldId = pages[oldIdx].id
      const newIdx = nextPages.findIndex((p) => p.id === oldId)
      if (newIdx !== -1) oldToNew.set(oldIdx, newIdx)
    }

    const nextAnnotations = annotations.map((ann) => ({
      ...ann,
      pageIndex: oldToNew.get(ann.pageIndex) ?? ann.pageIndex
    }))

    const nextCurrent =
      newSelectedIndices.length > 0
        ? newSelectedIndices[0]
        : (oldToNew.get(currentPage) ?? currentPage)

    set({
      pages: nextPages,
      currentPage: nextCurrent,
      selectedPageIndices: newSelectedIndices,
      selectionAnchorIndex: nextCurrent,
      annotations: nextAnnotations,
      document: {
        ...document,
        rotations: nextPages.map((p) => p.rotation)
      },
      isDirty: true,
      history: {
        past: [...history.past, pages],
        future: []
      }
    })
  },

  rotatePages: (indices, direction) => {
    const { pages, document, history } = get()
    if (!document || indices.length === 0) return

    const nextPages = rotateVirtualPages(pages, indices, direction)
    set({
      pages: nextPages,
      document: {
        ...document,
        rotations: nextPages.map((p) => p.rotation)
      },
      isDirty: true,
      history: {
        past: [...history.past, pages],
        future: []
      }
    })
  },

  deletePages: (indices) => {
    const { pages, document, history, currentPage, annotations } = get()
    if (!document || pages.length <= indices.length || indices.length === 0) return // 전체 삭제 방지

    const indexSet = new Set(indices)
    const nextPages = deleteVirtualPages(pages, indices)
    const nextCurrent = Math.max(0, Math.min(currentPage, nextPages.length - 1))

    // 주석 정리: 삭제된 페이지 주석 제거 및 인덱스 시프트
    const sortedIndices = [...indexSet].sort((a, b) => a - b)
    const nextAnnotations = annotations
      .filter((ann) => !indexSet.has(ann.pageIndex))
      .map((ann) => {
        const deletedBefore = sortedIndices.filter((i) => i < ann.pageIndex).length
        return {
          ...ann,
          pageIndex: ann.pageIndex - deletedBefore
        }
      })

    set({
      pages: nextPages,
      currentPage: nextCurrent,
      selectedPageIndices: [nextCurrent],
      selectionAnchorIndex: nextCurrent,
      annotations: nextAnnotations,
      document: {
        ...document,
        pageCount: nextPages.length,
        rotations: nextPages.map((p) => p.rotation)
      },
      isDirty: true,
      history: {
        past: [...history.past, pages],
        future: []
      }
    })
  },

  duplicatePages: (indices) => {
    const { pages, document, history } = get()
    if (!document || indices.length === 0) return

    const nextPages = duplicateVirtualPages(pages, indices)
    set({
      pages: nextPages,
      document: {
        ...document,
        pageCount: nextPages.length,
        rotations: nextPages.map((p) => p.rotation)
      },
      isDirty: true,
      history: {
        past: [...history.past, pages],
        future: []
      }
    })
  },

  normalizePages: (indices, config) => {
    const { pages, history, document } = get()
    if (!document || indices.length === 0) return

    const nextPages = normalizeVirtualPages(pages, indices, config)
    set({
      pages: nextPages,
      isDirty: true,
      history: {
        past: [...history.past, pages],
        future: []
      }
    })
  },

  cropPages: (indices, margins) => {
    const { pages, history, document } = get()
    if (!document || indices.length === 0) return

    const nextPages = cropVirtualPages(pages, indices, margins)
    set({
      pages: nextPages,
      isDirty: true,
      history: {
        past: [...history.past, pages],
        future: []
      }
    })
  },

  applyWatermark: (indices, config) => {
    const { pages, history, document } = get()
    if (!document || indices.length === 0) return

    const nextPages = applyWatermarkToVirtualPages(pages, indices, config)
    set({
      pages: nextPages,
      isDirty: true,
      history: {
        past: [...history.past, pages],
        future: []
      }
    })
  },

  applyPageNumbering: (indices, options) => {
    const { pages, history, document } = get()
    if (!document || indices.length === 0) return

    let nextPages: VirtualPage[]
    if (!options) {
      nextPages = applyPageNumberToVirtualPages(pages, indices, () => undefined)
    } else {
      nextPages = applyPageNumberToVirtualPages(
        pages,
        indices,
        (pageOrderIdx, totalSelected) => {
          const currentNum = options.startNumber + pageOrderIdx
          let text = `${currentNum}`
          if (options.format === 'dash') {
            text = `- ${currentNum} -`
          } else if (options.format === 'slash') {
            text = `${currentNum} / ${options.startNumber + totalSelected - 1}`
          } else if (options.format === 'pageOf') {
            text = `Page ${currentNum} of ${options.startNumber + totalSelected - 1}`
          }

          return {
            text,
            position: options.position,
            fontSize: options.fontSize,
            color: options.color,
            margin: options.margin
          }
        }
      )
    }

    set({
      pages: nextPages,
      isDirty: true,
      history: {
        past: [...history.past, pages],
        future: []
      }
    })
  },

  addTextReplacement: (replacement) => {
    const { pages, history, document } = get()
    if (!document) return

    const nextPages = replaceTextOnVirtualPage(
      pages,
      replacement.pageIndex,
      replacement
    )
    set({
      pages: nextPages,
      isDirty: true,
      history: {
        past: [...history.past, pages],
        future: []
      }
    })
  },

  removeTextReplacement: (pageIndex, replacementId) => {
    const { pages, history, document } = get()
    if (!document) return

    const nextPages = removeTextReplacementOnVirtualPage(
      pages,
      pageIndex,
      replacementId
    )
    set({
      pages: nextPages,
      isDirty: true,
      history: {
        past: [...history.past, pages],
        future: []
      }
    })
  },

  insertPagesFromDocument: (sourceId, fileName, bytes, pageCount, insertIndex, dimensions) => {
    const { pages, sources, document, history, annotations } = get()
    if (!document) return

    const nextSources = new Map(sources)
    nextSources.set(sourceId, {
      id: sourceId,
      fileName,
      filePath: null,
      bytes,
      pageCount
    })

    const safeIndex = Math.max(0, Math.min(pages.length, insertIndex))
    const newPages = createInitialVirtualPages(sourceId, pageCount, undefined, dimensions)
    const nextPages = insertVirtualPagesAt(pages, newPages, safeIndex)

    // 새로 삽입된 페이지들을 선택 상태로 지정
    const insertedIndices = Array.from({ length: pageCount }, (_, i) => safeIndex + i)

    // 기존 주석의 pageIndex 재매핑 (삽입 위치 이상인 경우 pageCount 만큼 뒤로 시프트)
    const nextAnnotations = annotations.map((ann) =>
      ann.pageIndex >= safeIndex
        ? { ...ann, pageIndex: ann.pageIndex + pageCount }
        : ann
    )

    set({
      sources: nextSources,
      pages: nextPages,
      currentPage: safeIndex,
      selectedPageIndices: insertedIndices,
      selectionAnchorIndex: safeIndex,
      annotations: nextAnnotations,
      document: {
        ...document,
        pageCount: nextPages.length,
        rotations: nextPages.map((p) => p.rotation)
      },
      isDirty: true,
      history: {
        past: [...history.past, pages],
        future: []
      }
    })
  },

  insertPdfPagesAt: (sourceId, fileName, bytes, pageCount, insertIndex, dimensions) =>
    get().insertPagesFromDocument(sourceId, fileName, bytes, pageCount, insertIndex, dimensions),

  undo: () => {
    const { history, pages, document } = get()
    if (history.past.length === 0 || !document) return

    const previous = history.past[history.past.length - 1]
    const newPast = history.past.slice(0, -1)

    set({
      pages: previous,
      document: {
        ...document,
        pageCount: previous.length,
        rotations: previous.map((p) => p.rotation)
      },
      currentPage: Math.min(get().currentPage, previous.length - 1),
      history: {
        past: newPast,
        future: [pages, ...history.future]
      },
      isDirty: true
    })
  },

  redo: () => {
    const { history, pages, document } = get()
    if (history.future.length === 0 || !document) return

    const next = history.future[0]
    const newFuture = history.future.slice(1)

    set({
      pages: next,
      document: {
        ...document,
        pageCount: next.length,
        rotations: next.map((p) => p.rotation)
      },
      currentPage: Math.min(get().currentPage, next.length - 1),
      history: {
        past: [...history.past, pages],
        future: newFuture
      },
      isDirty: true
    })
  }
}))
