/**
 * 애플리케이션 최상위 컴포넌트
 * - 레이아웃 구성 (툴바 / 썸네일 / 뷰어 / 상태표시줄)
 * - 메뉴 이벤트 라우팅
 * - 테마 적용
 */
import { useCallback, useEffect, useState } from 'react'
import { MenuToolbar } from './components/MenuToolbar'
import { UpdateNotice } from './components/UpdateNotice'
import { ThumbnailPanel } from './components/ThumbnailPanel'
import { ViewerPanel } from './components/ViewerPanel'
import { PageOrganizer } from './components/PageOrganizer'
import { StatusBar } from './components/StatusBar'
import { MergeModal } from './components/modals/MergeModal'
import { SplitModal } from './components/modals/SplitModal'
import { AboutModal } from './components/modals/AboutModal'
import { ShortcutsModal } from './components/modals/ShortcutsModal'
import { NormalizeModal } from './components/modals/NormalizeModal'
import { CropModal } from './components/modals/CropModal'
import { PrintModal } from './components/modals/PrintModal'
import { WatermarkModal } from './components/modals/WatermarkModal'
import { PageNumberModal } from './components/modals/PageNumberModal'
import { StampModal } from './components/modals/StampModal'
import { ExportImageModal } from './components/modals/ExportImageModal'
import { OcrModal } from './components/modals/OcrModal'
import { MetadataModal } from './components/modals/MetadataModal'
import { ProgressBarModal } from './components/modals/ProgressBarModal'
import { SplashScreen } from './components/SplashScreen'
import { OptionsModal } from './components/modals/OptionsModal'
import { CloseDocumentModal } from './components/modals/CloseDocumentModal'
import { useStore } from './lib/store'
import { releasePdfDocuments, usePdfDocument } from './hooks/usePdfDocument'
import { saveCurrentDocument, useMenuActions } from './hooks/useMenuActions'
import type { MenuEvent } from '../electron/shared/types'

export default function App() {
  const theme = useStore((s) => s.theme)
  const document = useStore((s) => s.document)
  const isDirty = useStore((s) => s.isDirty)
  const isLoading = useStore((s) => s.isLoading)
  const sources = useStore((s) => s.sources)
  const { openFile, openFileByPath } = usePdfDocument()
  const { handleMenuEvent } = useMenuActions()

  const [modal, setModal] = useState<
    | null
    | 'merge'
    | 'split'
    | 'about'
    | 'shortcuts'
    | 'normalize'
    | 'crop'
    | 'print'
    | 'watermark'
    | 'pageNumber'
    | 'image'
    | 'stamp'
    | 'exportImage'
    | 'ocr'
    | 'metadata'
    | 'options'
  >(null)
  const [isInitialized, setIsInitialized] = useState(false)
  const [organize, setOrganize] = useState(false)
  const [closeTarget, setCloseTarget] = useState<typeof sources | null>(null)
  const closeDocument = useCallback(() => {
    useStore.getState().setDocument(null)
    releasePdfDocuments()
    setCloseTarget(null)
    setModal(null)
    setOrganize(false)
  }, [])
  const requestCloseDocument = useCallback(() => {
    const current = useStore.getState()
    if (!current.document || current.isLoading || window.document.querySelector('[role="dialog"]')) return
    if (current.isDirty) setCloseTarget(current.sources)
    else closeDocument()
  }, [closeDocument])
  useEffect(() => {
    window.addEventListener('workspace:close-document', requestCloseDocument)
    return () => window.removeEventListener('workspace:close-document', requestCloseDocument)
  }, [requestCloseDocument])
  useEffect(() => {
    if (closeTarget && sources !== closeTarget) setCloseTarget(null)
  }, [sources, closeTarget])
  useEffect(() => {
    const show = () => setOrganize(true)
    window.addEventListener('workspace:organize', show)
    return () => window.removeEventListener('workspace:organize', show)
  }, [])
  useEffect(() => { if (!document) setOrganize(false) }, [document])

  // 테마를 <html data-theme> 에 적용
  useEffect(() => {
    document_root_setTheme(theme)
  }, [theme])

  // Sync immediately and answer update safety checks from the current store, not a stale render.
  useEffect(() => {
    window.workspace.setDocumentDirty?.(useStore.getState().isDirty)
    const unsubscribe = useStore.subscribe((state, previous) => {
      if (state.isDirty !== previous.isDirty) window.workspace.setDocumentDirty?.(state.isDirty)
    })
    const unsubscribeSafety = window.workspace.onUpdateSafetyCheck?.(() => {
      const current = useStore.getState()
      return !current.isDirty && !current.isLoading
    })
    return () => { unsubscribe(); unsubscribeSafety?.() }
  }, [])

  // 시작 파일(.pdf 더블클릭) 및 앱 실행 중 외부 열기 이벤트 수신
  useEffect(() => {
    window.workspace.getStartupFile?.().then((startupPath) => {
      if (startupPath) {
        console.log('[App] Opening startup PDF file:', startupPath)
        void openFileByPath(startupPath)
      }
    })

    const unsubscribe = window.workspace.onOpenExternalPath?.((filePath) => {
      console.log('[App] Received external PDF path to open:', filePath)
      void openFileByPath(filePath)
    })
    return () => {
      unsubscribe?.()
    }
  }, [openFileByPath])

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
      const closeShortcut = (e.ctrlKey || e.metaKey) && !e.shiftKey && e.key.toLowerCase() === 'w'
      if (closeShortcut) {
        e.preventDefault()
        requestCloseDocument()
        return
      }
      if (window.document.querySelector('[role="dialog"]')) return
      const target = e.target as HTMLElement | null
      const isInput =
        target &&
        (target.tagName === 'INPUT' ||
          target.tagName === 'TEXTAREA' ||
          target.isContentEditable)
      if (isInput) return

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
  }, [handleMenuEvent, openFile, requestCloseDocument])

  // 문서가 없을 때 드래그&드롭으로 열기 (자식 컴포넌트에서 stopPropagation/defaultPrevented 시 무시)
  useEffect(() => {
    const onDrop = async (e: DragEvent) => {
      if (e.defaultPrevented) return
      e.preventDefault()
      if (window.document.querySelector('[role="dialog"]')) return
      const file = e.dataTransfer?.files[0]
      if (!file || file.type !== 'application/pdf') return
      if (useStore.getState().isDirty && !confirm('저장하지 않은 변경 사항을 버리고 드롭한 문서를 여시겠습니까?')) return
      const arrayBuffer = await file.arrayBuffer()
      const bytes = new Uint8Array(arrayBuffer)
      const { loadPdfFromBytes } = await import('./hooks/usePdfDocument')
      const { meta, dimensions } = await loadPdfFromBytes(bytes, file.name, null)
      useStore.getState().setDocument(meta, dimensions)
    }
    const onDragOver = (e: DragEvent) => {
      if (!e.defaultPrevented) e.preventDefault()
    }
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
      <UpdateNotice onDetails={() => setModal('about')} />
      {document && <div className="document-workspace-bar">
        <span className="document-workspace-bar__name">{document.fileName}{isDirty ? ' · 수정됨' : ''}</span>
        <div className="document-workspace-bar__modes">
          <button aria-pressed={!organize} onClick={() => setOrganize(false)}>문서 보기</button>
          <button aria-pressed={organize} onClick={() => setOrganize(true)}>페이지 정리</button>
          <button onClick={() => setModal('merge')}>PDF 병합</button>
          <button title="문서 닫기 (Ctrl+W)" onClick={requestCloseDocument} disabled={isLoading}>문서 닫기</button>
        </div>
      </div>}
      <div id="text-edit-notice" />
      <div className="app__body">
        {!organize && <ThumbnailPanel />}
        {organize && document
          ? <PageOrganizer onRead={() => setOrganize(false)} onCrop={() => setModal('crop')} onMerge={() => setModal('merge')} />
          : <ViewerPanel onOpenFile={openFile} />}
      </div>
      <StatusBar />
      {closeTarget && document && <CloseDocumentModal
        fileName={document.fileName}
        onCancel={() => setCloseTarget(null)}
        onDiscard={() => { if (useStore.getState().sources === closeTarget) closeDocument() }}
        onSave={async () => {
          const saved = await saveCurrentDocument(true)
          if (saved && useStore.getState().sources === closeTarget && !useStore.getState().isDirty) {
            closeDocument()
            return true
          }
          return false
        }}
      />}
      {document === null && modal === null && null}
      {modal === 'merge' && <MergeModal onClose={() => setModal(null)} />}
      {modal === 'split' && <SplitModal onClose={() => setModal(null)} />}
      {modal === 'options' && <OptionsModal onClose={() => setModal(null)} />}
      {modal === 'about' && <AboutModal onClose={() => setModal(null)} />}
      {modal === 'shortcuts' && <ShortcutsModal onClose={() => setModal(null)} />}
      {modal === 'normalize' && <NormalizeModal onClose={() => setModal(null)} />}
      {modal === 'crop' && <CropModal onClose={() => setModal(null)} />}
      {modal === 'print' && <PrintModal onClose={() => setModal(null)} />}
      {modal === 'watermark' && <WatermarkModal onClose={() => setModal(null)} />}
      {modal === 'pageNumber' && <PageNumberModal onClose={() => setModal(null)} />}
      {modal === 'image' && <StampModal initialTab="image" onClose={() => setModal(null)} />}
      {modal === 'stamp' && <StampModal onClose={() => setModal(null)} />}
      {modal === 'exportImage' && <ExportImageModal onClose={() => setModal(null)} />}
      {modal === 'ocr' && <OcrModal onClose={() => setModal(null)} />}
      {modal === 'metadata' && <MetadataModal onClose={() => setModal(null)} />}
      <ProgressBarModal />
    </div>
  )
}

/** 테마를 DOM 에 적용하는 헬퍼 (document 충돌 회피) */
function document_root_setTheme(theme: 'light' | 'dark') {
  document.documentElement.setAttribute('data-theme', theme)
}
