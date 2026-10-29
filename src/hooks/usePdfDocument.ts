/**
 * PDF 문서 로드 훅 - pdfjs-dist 로 문서를 열고 페이지 메타데이터 및 북마크 관리
 */
import { useCallback } from 'react'
import * as pdfjsLib from 'pdfjs-dist'
import { useStore } from '../lib/store'
import type { DocumentMeta } from '../lib/store'
import { ROOT_DOC_ID } from '../core/domain/document'
import { parsePdfBookmarks } from '../core/services/pdf-engine.service'
import { clearTextEditorCache } from '../core/services/text-editor.service'
import { clearRenderCache } from './usePdfRender'
import { addRecentFile } from '../lib/recent-files'
import '../lib/pdf-worker' // 워커 설정 부수효과

/** 로드된 pdfjs 문서 객체 캐시 (소스 문서별 관리) */
const pdfDocProxies = new Map<string, pdfjsLib.PDFDocumentProxy>()

/** pdfjs 문서 객체 반환 (렌더링/썸네일용) */
export function getPdfDoc(sourceDocId: string = ROOT_DOC_ID): pdfjsLib.PDFDocumentProxy | null {
  return pdfDocProxies.get(sourceDocId) ?? pdfDocProxies.get(ROOT_DOC_ID) ?? null
}

/** 소스 문서의 pdfjs 프록시 반환 (없으면 온디맨드 로드) */
export async function getOrLoadPdfDoc(
  sourceDocId: string,
  bytes: Uint8Array
): Promise<pdfjsLib.PDFDocumentProxy> {
  const existing = pdfDocProxies.get(sourceDocId)
  if (existing) return existing

  const task = pdfjsLib.getDocument({
    data: bytes,
    cMapUrl: './cmaps/',
    cMapPacked: true,
    standardFontDataUrl: './standard_fonts/'
  })
  const doc = await task.promise
  pdfDocProxies.set(sourceDocId, doc)
  return doc
}

export interface LoadedPdfResult {
  meta: DocumentMeta
  dimensions?: { width: number; height: number }
}

/** PDF 데이터로부터 루트 문서 열기 */
export async function loadPdfFromBytes(
  bytes: Uint8Array,
  fileName: string,
  filePath: string | null
): Promise<LoadedPdfResult> {
  console.log('[loadPdfFromBytes] Loading PDF:', fileName, 'Length:', bytes.length)
  // 기존 에디터 및 렌더 캐시 초기화
  clearTextEditorCache()
  clearRenderCache()

  // 기존 문서들 메모리 정리
  for (const doc of pdfDocProxies.values()) {
    try {
      await doc.destroy()
    } catch {
      /* noop */
    }
  }
  pdfDocProxies.clear()

  const task = pdfjsLib.getDocument({
    data: bytes,
    cMapUrl: './cmaps/',
    cMapPacked: true,
    standardFontDataUrl: './standard_fonts/'
  })
  const doc = await task.promise
  console.log('[loadPdfFromBytes] PDF loaded successfully! Pages:', doc.numPages)
  pdfDocProxies.set(ROOT_DOC_ID, doc)

  // 1페이지 치수 측정 (가상 스크롤 뷰어용)
  let dimensions: { width: number; height: number } | undefined
  try {
    const page1 = await doc.getPage(1)
    const v1 = page1.getViewport({ scale: 1 })
    dimensions = { width: v1.width, height: v1.height }
  } catch {
    /* fallback */
  }

  // 북마크 비동기 파싱
  parsePdfBookmarks(doc).then((bookmarks) => {
    useStore.getState().setBookmarks(bookmarks)
  })

  // 최근 파일 목록에 기록
  if (filePath) {
    addRecentFile({
      fileName,
      filePath,
      pageCount: doc.numPages,
      openedAt: Date.now()
    })
  }

  return {
    meta: {
      bytes,
      filePath,
      fileName,
      pageCount: doc.numPages,
      rotations: new Array(doc.numPages).fill(0)
    },
    dimensions
  }
}

/**
 * 파일 열기 IPC -> pdfjs 로드 -> store 반영
 */
export function usePdfDocument() {
  const setDocument = useStore((s) => s.setDocument)
  const setLoading = useStore((s) => s.setLoading)

  const openFile = useCallback(async () => {
    setLoading(true)
    const setLoadingProgress = useStore.getState().setLoadingProgress
    try {
      const result = await window.workspace.openFile()
      if (!result) return null

      setLoadingProgress({
        visible: true,
        percent: 25,
        title: 'PDF 문서 로드 중',
        detail: `파일을 읽고 있습니다: ${result.fileName}`
      })

      const bytes = new Uint8Array(result.data)

      setLoadingProgress({
        visible: true,
        percent: 65,
        title: 'PDF 문서 로드 중',
        detail: '페이지 구조 및 폰트 파싱 중...'
      })

      const { meta, dimensions } = await loadPdfFromBytes(
        bytes,
        result.fileName,
        result.filePath
      )

      setLoadingProgress({
        visible: true,
        percent: 95,
        title: 'PDF 문서 로드 중',
        detail: `${meta.pageCount}개 페이지 뷰어 준비 중...`
      })

      setDocument(meta, dimensions)

      setLoadingProgress({
        visible: true,
        percent: 100,
        title: '완료',
        detail: '문서가 성공적으로 로드되었습니다.'
      })
      setTimeout(() => setLoadingProgress(null), 300)
      return meta
    } catch (err) {
      console.error('PDF 열기 실패:', err)
      setLoadingProgress(null)
      alert(`PDF 파일을 열지 못했습니다: ${err instanceof Error ? err.message : String(err)}`)
      return null
    } finally {
      setLoading(false)
    }
  }, [setDocument, setLoading])

  /** 경로로부터 파일 열기 (최근 문서 클릭 등) */
  const openFileByPath = useCallback(
    async (filePath: string) => {
      setLoading(true)
      const setLoadingProgress = useStore.getState().setLoadingProgress
      try {
        setLoadingProgress({
          visible: true,
          percent: 20,
          title: 'PDF 문서 로드 중',
          detail: `파일을 읽고 있습니다...`
        })

        const data = await window.workspace.readFile(filePath)
        if (!data) throw new Error('파일 데이터를 읽을 수 없습니다.')

        const fileName = filePath.replace(/^.*[\\/]/, '') || 'document.pdf'
        const bytes = new Uint8Array(data)

        setLoadingProgress({
          visible: true,
          percent: 60,
          title: 'PDF 문서 로드 중',
          detail: '페이지 구조 및 폰트 파싱 중...'
        })

        const { meta, dimensions } = await loadPdfFromBytes(
          bytes,
          fileName,
          filePath
        )

        setLoadingProgress({
          visible: true,
          percent: 95,
          title: 'PDF 문서 로드 중',
          detail: `${meta.pageCount}개 페이지 뷰어 준비 중...`
        })

        setDocument(meta, dimensions)

        setLoadingProgress({
          visible: true,
          percent: 100,
          title: '완료',
          detail: '문서가 성공적으로 로드되었습니다.'
        })
        setTimeout(() => setLoadingProgress(null), 300)
        return meta
      } catch (err) {
        console.error('최근 파일 열기 실패:', err)
        setLoadingProgress(null)
        alert(`파일을 열지 못했습니다: ${err instanceof Error ? err.message : String(err)}`)
        return null
      } finally {
        setLoading(false)
      }
    },
    [setDocument, setLoading]
  )

  /** 바이트 배열로부터 직접 로드 (합치기/분할 결과) */
  const loadFromBytes = useCallback(
    async (bytes: Uint8Array, fileName: string, filePath: string | null) => {
      setLoading(true)
      try {
        const { meta, dimensions } = await loadPdfFromBytes(bytes, fileName, filePath)
        setDocument(meta, dimensions)
        return meta
      } finally {
        setLoading(false)
      }
    },
    [setDocument, setLoading]
  )

  /** 가상 회전 적용 (포토샵 스타일: 바이트를 직접 굽지 않고 가상 매니페스트만 0ms 변경) */
  const applyRotation = useCallback(
    (indices: number[], direction: 'left' | 'right') => {
      useStore.getState().rotatePages(indices, direction)
    },
    []
  )

  return { openFile, openFileByPath, loadFromBytes, applyRotation }
}
