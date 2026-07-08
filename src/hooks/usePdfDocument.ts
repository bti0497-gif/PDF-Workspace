/**
 * PDF 문서 로드 훅 - pdfjs-dist 로 문서를 열고 페이지 메타데이터를 관리
 */
import { useCallback } from 'react'
import * as pdfjsLib from 'pdfjs-dist'
import { useStore } from '../lib/store'
import type { DocumentMeta } from '../lib/store'
import { rotatePages as rotatePagesOp } from '../lib/pdf-operations'
import '../lib/pdf-worker' // 워커 설정 부수효과

/** 로드된 pdfjs 문서 객체 캐시 (렌더러 메모리) */
let pdfDocProxy: pdfjsLib.PDFDocumentProxy | null = null

export interface LoadedPageInfo {
  page: pdfjsLib.PDFPageProxy
  viewport: pdfjsLib.PageViewport
}

/** pdfjs 문서 객체 반환 (렌더링/썸네일용) */
export function getPdfDoc(): pdfjsLib.PDFDocumentProxy | null {
  return pdfDocProxy
}

/** PDF 데이터로부터 문서 열기 */
export async function loadPdfFromBytes(
  bytes: Uint8Array,
  fileName: string,
  filePath: string | null
): Promise<DocumentMeta> {
  // 기존 문서 정리
  if (pdfDocProxy) {
    try {
      await pdfDocProxy.destroy()
    } catch {
      /* noop */
    }
    pdfDocProxy = null
  }

  const task = pdfjsLib.getDocument({ data: bytes })
  const doc = await task.promise
  pdfDocProxy = doc

  return {
    bytes,
    filePath,
    fileName,
    pageCount: doc.numPages,
    rotations: new Array(doc.numPages).fill(0)
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
    try {
      const result = await window.workspace.openFile()
      if (!result) return null
      const bytes = new Uint8Array(result.data)
      const meta = await loadPdfFromBytes(bytes, result.fileName, result.filePath)
      setDocument(meta)
      return meta
    } catch (err) {
      console.error('PDF 열기 실패:', err)
      throw err
    } finally {
      setLoading(false)
    }
  }, [setDocument, setLoading])

  /** 바이트 배열로부터 직접 로드 (합치기/분할 결과) */
  const loadFromBytes = useCallback(
    async (bytes: Uint8Array, fileName: string, filePath: string | null) => {
      setLoading(true)
      try {
        const meta = await loadPdfFromBytes(bytes, fileName, filePath)
        setDocument(meta)
        return meta
      } finally {
        setLoading(false)
      }
    },
    [setDocument, setLoading]
  )

  /** 회전을 pdf-lib 로 실제 적용하여 바이트 갱신 */
  const applyRotation = useCallback(
    async (indices: number[], direction: 'left' | 'right') => {
      const doc = useStore.getState().document
      if (!doc) return
      const newBytes = await rotatePagesOp(doc.bytes, indices, direction)
      const updated: DocumentMeta = { ...doc, bytes: newBytes }
      useStore.getState().setDocument(updated)
    },
    []
  )

  return { openFile, loadFromBytes, applyRotation }
}
