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
import type { VirtualPage } from '../core/domain/page'
import {
  createInitialVirtualPages,
  reorderVirtualPages,
  rotateVirtualPages,
  deleteVirtualPages,
  duplicateVirtualPages,
  insertVirtualPagesAt,
  normalizeVirtualPages,
  cropVirtualPages,
  replaceTextOnVirtualPage,
  removeTextReplacementOnVirtualPage
} from '../core/domain/page'
import type {
  NormalizedPageConfig,
  CropMargins,
  TextReplacement
} from '../core/domain/page'
import type { SourceDocument } from '../core/domain/document'
import { ROOT_DOC_ID } from '../core/domain/document'
import type { BookmarkNode } from '../core/domain/bookmark'

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
export type ViewMode = 'single' | 'continuous'
export type SidebarTab = 'thumbnails' | 'bookmarks'

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

interface AppState {
  // ── 문서 & 가상 매니페스트 ──
  document: DocumentMeta | null
  pages: VirtualPage[]
  sources: Map<string, SourceDocument>
  isDirty: boolean
  isLoading: boolean
  loadingProgress: LoadingProgress | null

  // ── 사이드바 & 목차 ──
  sidebarTab: SidebarTab
  bookmarks: BookmarkNode[]

  // ── 보기 & 선택 ──
  currentPage: number
  selectedPageIndices: number[]
  viewMode: ViewMode
  zoom: number
  thumbnailsVisible: boolean
  theme: ThemeMode

  // ── 도구 ──
  activeTool: ToolType
  toolColor: string

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

  // ── 액션 ──
  setDocument: (doc: DocumentMeta | null, dimensions?: { width: number; height: number }) => void
  setDirty: (dirty: boolean) => void
  setLoading: (loading: boolean) => void
  setLoadingProgress: (progress: LoadingProgress | null) => void
  setSidebarTab: (tab: SidebarTab) => void
  setBookmarks: (bookmarks: BookmarkNode[]) => void

  setCurrentPage: (page: number) => void
  setViewMode: (mode: ViewMode) => void
  selectPage: (index: number, mode?: 'single' | 'toggle' | 'range') => void
  selectAllPages: () => void
  clearPageSelection: () => void

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
  rotatePages: (indices: number[], direction: 'left' | 'right') => void
  deletePages: (indices: number[]) => void
  duplicatePages: (indices: number[]) => void
  normalizePages: (indices: number[], config: NormalizedPageConfig) => void
  cropPages: (indices: number[], margins: CropMargins) => void
  insertPagesFromDocument: (
    sourceId: string,
    fileName: string,
    bytes: Uint8Array,
    pageCount: number,
    insertIndex: number
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

  currentPage: 0,
  selectedPageIndices: [0],
  viewMode: 'single',
  zoom: 1,
  thumbnailsVisible: true,
  theme: 'dark',

  activeTool: 'select',
  toolColor: '#ffeb3b',

  annotations: [],
  selectedAnnotationId: null,
  copiedAnnotation: null,

  searchQuery: '',
  searchMatches: [],
  activeMatchIndex: -1,
  searchBarVisible: false,

  history: { past: [], future: [] },

  setDocument: (doc, dimensions) => {
    if (!doc) {
      set({
        document: null,
        pages: [],
        sources: new Map(),
        bookmarks: [],
        currentPage: 0,
        selectedPageIndices: [],
        annotations: [],
        selectedAnnotationId: null,
        isDirty: false,
        history: { past: [], future: [] }
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
      zoom: 1,
      annotations: [],
      selectedAnnotationId: null,
      isDirty: false,
      history: { past: [], future: [] }
    })
  },

  setDirty: (dirty) => set({ isDirty: dirty }),
  setLoading: (loading) => set({ isLoading: loading }),
  setLoadingProgress: (progress) => set({ loadingProgress: progress }),
  setSidebarTab: (tab) => set({ sidebarTab: tab }),
  setBookmarks: (bookmarks) => set({ bookmarks }),

  setCurrentPage: (page) => {
    const { pages } = get()
    const safePage = Math.max(0, Math.min(pages.length - 1, page))
    set({ currentPage: safePage })
  },

  setViewMode: (mode) => set({ viewMode: mode }),

  selectPage: (index, mode = 'single') => {
    const { selectedPageIndices, pages } = get()
    if (index < 0 || index >= pages.length) return

    let nextSelected: number[]
    if (mode === 'single') {
      nextSelected = [index]
    } else if (mode === 'toggle') {
      if (selectedPageIndices.includes(index)) {
        nextSelected = selectedPageIndices.filter((i) => i !== index)
        if (nextSelected.length === 0) nextSelected = [index] // 최소 1개 유지
      } else {
        nextSelected = [...selectedPageIndices, index]
      }
    } else {
      // range (Shift+클릭)
      const lastIndex = selectedPageIndices[selectedPageIndices.length - 1] ?? 0
      const start = Math.min(lastIndex, index)
      const end = Math.max(lastIndex, index)
      const range: number[] = []
      for (let i = start; i <= end; i++) range.push(i)
      nextSelected = Array.from(new Set([...selectedPageIndices, ...range]))
    }

    set({
      selectedPageIndices: nextSelected,
      currentPage: index
    })
  },

  selectAllPages: () => {
    const { pages } = get()
    set({ selectedPageIndices: pages.map((_, i) => i) })
  },

  clearPageSelection: () => {
    const { currentPage } = get()
    set({ selectedPageIndices: [currentPage] })
  },

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
    const { pages, document, history, currentPage } = get()
    if (fromIndex === toIndex || !document) return

    const nextPages = reorderVirtualPages(pages, fromIndex, toIndex)
    let nextCurrent = currentPage
    if (currentPage === fromIndex) {
      nextCurrent = toIndex
    } else if (fromIndex < currentPage && toIndex >= currentPage) {
      nextCurrent = currentPage - 1
    } else if (fromIndex > currentPage && toIndex <= currentPage) {
      nextCurrent = currentPage + 1
    }

    set({
      pages: nextPages,
      currentPage: nextCurrent,
      selectedPageIndices: [nextCurrent],
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
    const { pages, document, history, currentPage } = get()
    if (!document || pages.length <= indices.length) return // 전체 삭제 방지

    const nextPages = deleteVirtualPages(pages, indices)
    const nextCurrent = Math.min(currentPage, nextPages.length - 1)

    set({
      pages: nextPages,
      currentPage: nextCurrent,
      selectedPageIndices: [nextCurrent],
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

  insertPagesFromDocument: (sourceId, fileName, bytes, pageCount, insertIndex) => {
    const { pages, sources, document, history } = get()
    if (!document) return

    const nextSources = new Map(sources)
    nextSources.set(sourceId, {
      id: sourceId,
      fileName,
      filePath: null,
      bytes,
      pageCount
    })

    const newPages = createInitialVirtualPages(sourceId, pageCount)
    const nextPages = insertVirtualPagesAt(pages, newPages, insertIndex)

    set({
      sources: nextSources,
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
