/**
 * 메뉴/단축키 이벤트를 스토어 액션으로 라우팅
 * - 가상 매니페스트 기반 Undo/Redo 완벽 연동
 * - 지연 베이킹(Deferred Baking) 저장 연동
 * - 다중 선택 페이지 일괄 회전/삭제/추출 연동
 */
import { useCallback } from 'react'
import { useStore } from '../lib/store'
import { usePdfDocument } from './usePdfDocument'
import { bakeManifestToPdf, extractVirtualPagesToPdf } from '../core/services/pdf-engine.service'
import type { MenuEvent } from '../../electron/shared/types'

export function useMenuActions() {
  const store = useStore
  const { openFile } = usePdfDocument()

  const handleMenuEvent = useCallback(
    async (event: MenuEvent) => {
      const s = store.getState()
      const doc = s.document
      const page = s.currentPage
      const selectedIndices =
        s.selectedPageIndices.length > 0 ? s.selectedPageIndices : [page]

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
          s.undo()
          break
        case 'edit:redo':
          s.redo()
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
          if (doc) s.rotatePages(selectedIndices, 'left')
          break
        case 'page:rotateRight':
          if (doc) s.rotatePages(selectedIndices, 'right')
          break
        case 'page:delete':
          if (doc && s.pages.length > selectedIndices.length) {
            s.deletePages(selectedIndices)
          }
          break
        case 'page:extract':
          if (doc) {
            await extractSelectedPagesAction(selectedIndices)
          }
          break
        case 'page:previous':
          if (page > 0) s.setCurrentPage(page - 1)
          break
        case 'page:next':
          if (doc && page < s.pages.length - 1) s.setCurrentPage(page + 1)
          break

        default:
          console.log('처리되지 않은 메뉴 이벤트:', event)
      }
    },
    [openFile, store]
  )

  return { handleMenuEvent }
}

/** 현재 가상 매니페스트를 실제 PDF로 지연 일괄 합성하여 저장 */
async function saveCurrentDocument(saveInPlace: boolean): Promise<void> {
  const s = useStore.getState()
  const doc = s.document
  if (!doc) return

  // 한글 폰트 로드
  let fontBytes: Uint8Array | undefined
  try {
    const fontData = await window.workspace.getKoreanFont()
    if (fontData) fontBytes = new Uint8Array(fontData)
  } catch {
    /* 폰트 없으면 기본 폰트 사용 */
  }

  // 지연 베이킹 (가상 매니페스트 + 소스 바이트 + 주석 일괄 합성)
  const finalBytes = await bakeManifestToPdf({
    pages: s.pages,
    sources: s.sources,
    annotations: s.annotations,
    customFont: fontBytes
  })

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
          ? {
              ...prev.document,
              bytes: finalBytes,
              filePath: savedPath,
              fileName: savedPath.split(/[\\/]/).pop()!
            }
          : null,
        isDirty: false
      }))
    }
  }
}

/** 선택된 페이지들을 새 PDF로 내보내기 */
export async function extractSelectedPagesAction(indices: number[]): Promise<void> {
  const s = useStore.getState()
  const doc = s.document
  if (!doc || indices.length === 0) return

  const extracted = await extractVirtualPagesToPdf(s.pages, s.sources, indices)
  const baseName = doc.fileName.replace(/\.pdf$/i, '')
  const suffix =
    indices.length === 1 ? `_p${indices[0] + 1}` : `_선택${indices.length}페이지`

  await window.workspace.savePdfBytes(Array.from(extracted), `${baseName}${suffix}.pdf`)
}
