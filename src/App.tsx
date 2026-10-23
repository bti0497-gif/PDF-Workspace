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
import { NormalizeModal } from './components/modals/NormalizeModal'
import { CropModal } from './components/modals/CropModal'
import { PrintModal } from './components/modals/PrintModal'
import { ProgressBarModal } from './components/modals/ProgressBarModal'
import { SplashScreen } from './components/SplashScreen'
import { useStore } from './lib/store'
import { usePdfDocument } from './hooks/usePdfDocument'
import { useMenuActions } from './hooks/useMenuActions'
import type { MenuEvent } from '../electron/shared/types'

export default function App() {
  const theme = useStore((s) => s.theme)
  const document = useStore((s) => s.document)
  const { openFile } = usePdfDocument()
  const { handleMenuEvent } = useMenuActions()

  const [modal, setModal] = useState<
    null | 'merge' | 'split' | 'about' | 'shortcuts' | 'normalize' | 'crop' | 'print'
  >(null)
  const [isInitialized, setIsInitialized] = useState(false)

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
      if (event === 'tool:normalize') {
        setModal('normalize')
        return
      }
      if (event === 'tool:crop') {
        setModal('crop')
        return
      }
      if (event === 'file:print') {
        setModal('print')
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

  // 툴바 저장/인쇄 이벤트 및 통합 글로벌 단축키 (네이티브 메뉴바 제거 대응)
  useEffect(() => {
    const onSave = () => {
      handleMenuEvent('file:save')
    }
    const onPrint = () => {
      setModal('print')
    }
    const onKeyDown = (e: KeyboardEvent) => {
      const isCmdOrCtrl = e.ctrlKey || e.metaKey

      // Ctrl + O: 파일 열기
      if (isCmdOrCtrl && e.key.toLowerCase() === 'o') {
        e.preventDefault()
        openFile()
        return
      }
      // Ctrl + S: 저장 (Shift 추가 시 다른 이름으로 저장)
      if (isCmdOrCtrl && e.key.toLowerCase() === 's') {
        e.preventDefault()
        if (e.shiftKey) {
          handleMenuEvent('file:saveAs')
        } else {
          handleMenuEvent('file:save')
        }
        return
      }
      // Ctrl + P: 인쇄
      if (isCmdOrCtrl && e.key.toLowerCase() === 'p') {
        e.preventDefault()
        setModal('print')
        return
      }
      // Ctrl + Z: 실행 취소 (Shift 추가 시 다시 실행)
      if (isCmdOrCtrl && e.key.toLowerCase() === 'z') {
        e.preventDefault()
        if (e.shiftKey) {
          useStore.getState().redo()
        } else {
          useStore.getState().undo()
        }
        return
      }
      // Ctrl + Y: 다시 실행
      if (isCmdOrCtrl && e.key.toLowerCase() === 'y') {
        e.preventDefault()
        useStore.getState().redo()
        return
      }
      // Ctrl + F: 본문 검색
      if (isCmdOrCtrl && e.key.toLowerCase() === 'f') {
        e.preventDefault()
        useStore.getState().openSearchBar()
        return
      }
    }
    window.addEventListener('trigger-save', onSave)
    window.addEventListener('trigger-print', onPrint)
    window.addEventListener('keydown', onKeyDown)
    return () => {
      window.removeEventListener('trigger-save', onSave)
      window.removeEventListener('trigger-print', onPrint)
      window.removeEventListener('keydown', onKeyDown)
    }
  }, [handleMenuEvent, openFile])

  // 문서가 없을 때 드래그&드롭으로 열기
  useEffect(() => {
    const onDrop = async (e: DragEvent) => {
      e.preventDefault()
      const file = e.dataTransfer?.files[0]
      if (!file || file.type !== 'application/pdf') return
      const arrayBuffer = await file.arrayBuffer()
      const bytes = new Uint8Array(arrayBuffer)
      const { loadPdfFromBytes } = await import('./hooks/usePdfDocument')
      const { meta, dimensions } = await loadPdfFromBytes(bytes, file.name, null)
      useStore.getState().setDocument(meta, dimensions)
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
      {!isInitialized && (
        <SplashScreen onComplete={() => setIsInitialized(true)} />
      )}
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
      {modal === 'normalize' && <NormalizeModal onClose={() => setModal(null)} />}
      {modal === 'crop' && <CropModal onClose={() => setModal(null)} />}
      {modal === 'print' && <PrintModal onClose={() => setModal(null)} />}
      <ProgressBarModal />
    </div>
  )
}

/** 테마를 DOM 에 적용하는 헬퍼 (document 충돌 회피) */
function document_root_setTheme(theme: 'light' | 'dark') {
  document.documentElement.setAttribute('data-theme', theme)
}
