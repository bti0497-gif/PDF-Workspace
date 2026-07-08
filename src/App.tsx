/**
 * 애플리케이션 최상위 컴포넌트
 * - 레이아웃 구성 (툴바 / 썸네일 / 뷰어 / 상태표시줄)
 * - 메뉴 이벤트 라우팅
 * - 테마 적용
 */
import { useEffect, useState } from 'react'
import { MenuToolbar } from './components/MenuToolbar'
import { ThumbnailPanel } from './components/ThumbnailPanel'
import { ViewerPanel } from './components/ViewerPanel'
import { StatusBar } from './components/StatusBar'
import { MergeModal } from './components/modals/MergeModal'
import { SplitModal } from './components/modals/SplitModal'
import { AboutModal } from './components/modals/AboutModal'
import { ShortcutsModal } from './components/modals/ShortcutsModal'
import { useStore } from './lib/store'
import { usePdfDocument } from './hooks/usePdfDocument'
import { useMenuActions } from './hooks/useMenuActions'
import type { MenuEvent } from '../electron/shared/types'

export default function App() {
  const theme = useStore((s) => s.theme)
  const document = useStore((s) => s.document)
  const { openFile } = usePdfDocument()
  const { handleMenuEvent } = useMenuActions()

  const [modal, setModal] = useState<null | 'merge' | 'split' | 'about' | 'shortcuts'>(null)

  // 테마를 <html data-theme> 에 적용
  useEffect(() => {
    document_root_setTheme(theme)
  }, [theme])

  // 네이티브 메뉴 이벤트 구독
  useEffect(() => {
    const unsubscribe = window.workspace.onMenuEvent((event: MenuEvent) => {
      // 모달형 이벤트는 여기서 가로채기
      if (event === 'tool:merge') {
        setModal('merge')
        return
      }
      if (event === 'tool:split') {
        setModal('split')
        return
      }
      if (event === 'help:about') {
        setModal('about')
        return
      }
      if (event === 'help:shortcuts') {
        setModal('shortcuts')
        return
      }
      handleMenuEvent(event)
    })
    return unsubscribe
  }, [handleMenuEvent])

  // 문서가 없을 때 드래그&드롭으로 열기
  useEffect(() => {
    const onDrop = async (e: DragEvent) => {
      e.preventDefault()
      const file = e.dataTransfer?.files[0]
      if (!file || file.type !== 'application/pdf') return
      const arrayBuffer = await file.arrayBuffer()
      const bytes = new Uint8Array(arrayBuffer)
      const { loadPdfFromBytes } = await import('./hooks/usePdfDocument')
      const meta = await loadPdfFromBytes(bytes, file.name, null)
      useStore.getState().setDocument(meta)
    }
    const onDragOver = (e: DragEvent) => e.preventDefault()
    window.addEventListener('drop', onDrop)
    window.addEventListener('dragover', onDragOver)
    return () => {
      window.removeEventListener('drop', onDrop)
      window.removeEventListener('dragover', onDragOver)
    }
  }, [])

  return (
    <div className="app">
      <MenuToolbar onOpenModal={setModal} />
      <div className="app__body">
        <ThumbnailPanel />
        <ViewerPanel onOpenFile={openFile} />
      </div>
      <StatusBar />
      {document === null && modal === null && null}
      {modal === 'merge' && <MergeModal onClose={() => setModal(null)} />}
      {modal === 'split' && <SplitModal onClose={() => setModal(null)} />}
      {modal === 'about' && <AboutModal onClose={() => setModal(null)} />}
      {modal === 'shortcuts' && <ShortcutsModal onClose={() => setModal(null)} />}
    </div>
  )
}

/** 테마를 DOM 에 적용하는 헬퍼 (document 충돌 회피) */
function document_root_setTheme(theme: 'light' | 'dark') {
  document.documentElement.setAttribute('data-theme', theme)
}
