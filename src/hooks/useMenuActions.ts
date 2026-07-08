/**
 * 메뉴/단축키 이벤트를 스토어 액션으로 라우팅
 */
import { useCallback } from 'react'
import { useStore } from '../lib/store'
import { usePdfDocument } from './usePdfDocument'
import {
  deletePages,
  extractPages
} from '../lib/pdf-operations'
import type { MenuEvent } from '../../electron/shared/types'

export function useMenuActions() {
  const store = useStore
  const { openFile, applyRotation } = usePdfDocument()

  const handleMenuEvent = useCallback(
    async (event: MenuEvent) => {
      const s = store.getState()
      const doc = s.document
      const page = s.currentPage

      switch (event) {
        // ── 파일 ──
        case 'file:open':
          await openFile()
          break
        case 'file:save':
        case 'file:saveAs':
          await saveCurrentDocument(event === 'file:save')
          break
        case 'file:close':
          store.getState().setDocument(null)
          break

        // ── 편집 ──
        case 'edit:undo':
          // 주석 삭제를 실행 취소하는 단순 구현 (히스토리는 추후 확장)
          break
        case 'edit:redo':
          break
        case 'edit:deleteAnnotation':
          if (s.selectedAnnotationId) {
            s.removeAnnotation(s.selectedAnnotationId)
          }
          break

        // ── 도구 ──
        case 'tool:select':
          s.setActiveTool('select')
          break
        case 'tool:highlight':
          s.setActiveTool('highlight')
          s.setToolColor('#ffeb3b')
          break
        case 'tool:rectangle':
          s.setActiveTool('rectangle')
          s.setToolColor('#e5534b')
          break
        case 'tool:textbox':
          s.setActiveTool('textbox')
          s.setToolColor('#1e1e1e')
          break
        case 'tool:drawing':
          s.setActiveTool('drawing')
          s.setToolColor('#0e6fd6')
          break

        // ── 보기 ──
        case 'view:zoomIn':
          s.zoomIn()
          break
        case 'view:zoomOut':
          s.zoomOut()
          break
        case 'view:zoomReset':
          s.resetZoom()
          break
        case 'view:fitWidth':
          s.setZoom(1.2)
          break
        case 'view:fitPage':
          s.setZoom(0.9)
          break
        case 'view:toggleThumbnails':
          s.toggleThumbnails()
          break
        case 'view:toggleTheme':
          s.toggleTheme()
          break

        // ── 페이지 ──
        case 'page:rotateLeft':
          if (doc) {
            await applyRotation([page], 'left')
            store.getState().rotatePages([page], 'left')
          }
          break
        case 'page:rotateRight':
          if (doc) {
            await applyRotation([page], 'right')
            store.getState().rotatePages([page], 'right')
          }
          break
        case 'page:delete':
          if (doc && doc.pageCount > 1) {
            await deletePageAction(page)
          }
          break
        case 'page:extract':
          if (doc) {
            await extractPageAction(page)
          }
          break
        case 'page:previous':
          if (page > 0) s.setCurrentPage(page - 1)
          break
        case 'page:next':
          if (doc && page < doc.pageCount - 1) s.setCurrentPage(page + 1)
          break

        default:
          console.log('처리되지 않은 메뉴 이벤트:', event)
      }
    },
    [openFile, applyRotation, store]
  )

  return { handleMenuEvent }
}

/** 현재 문서를 PDF로 저장 (주석 합성 포함) */
async function saveCurrentDocument(saveInPlace: boolean): Promise<void> {
  const s = useStore.getState()
  const doc = s.document
  if (!doc) return

  const { applyAnnotations } = await import('../lib/pdf-operations')
  const finalBytes = await applyAnnotations(doc.bytes, s.annotations)

  if (saveInPlace && doc.filePath) {
    await window.workspace.writeFile(doc.filePath, Array.from(finalBytes))
    useStore.setState((prev) => ({
      document: prev.document ? { ...prev.document, bytes: finalBytes } : null,
      isDirty: false
    }))
  } else {
    const savedPath = await window.workspace.savePdfBytes(
      Array.from(finalBytes),
      doc.fileName
    )
    if (savedPath) {
      useStore.setState((prev) => ({
        document: prev.document
          ? { ...prev.document, bytes: finalBytes, filePath: savedPath, fileName: savedPath.split(/[\\/]/).pop()! }
          : null,
        isDirty: false
      }))
    }
  }
}

/** 페이지 삭제 액션 */
async function deletePageAction(pageIndex: number): Promise<void> {
  const s = useStore.getState()
  const doc = s.document
  if (!doc) return
  const newBytes = await deletePages(doc.bytes, [pageIndex])
  const { clearRenderCache } = await import('./usePdfRender')
  clearRenderCache()
  useStore.setState({
    document: {
      ...doc,
      bytes: newBytes,
      pageCount: doc.pageCount - 1,
      rotations: doc.rotations.filter((_, i) => i !== pageIndex)
    },
    annotations: s.annotations.filter((a) => a.pageIndex !== pageIndex).map((a) => (a.pageIndex > pageIndex ? { ...a, pageIndex: a.pageIndex - 1 } : a)),
    currentPage: Math.min(s.currentPage, doc.pageCount - 2),
    isDirty: true
  })
}

/** 페이지 추출 액션 */
async function extractPageAction(pageIndex: number): Promise<void> {
  const s = useStore.getState()
  const doc = s.document
  if (!doc) return
  const extracted = await extractPages(doc.bytes, [pageIndex])
  const name = doc.fileName.replace(/\.pdf$/i, '')
  await window.workspace.savePdfBytes(Array.from(extracted), `${name}_p${pageIndex + 1}.pdf`)
}
