/**
 * 왼쪽 사이드바 패널 (60FPS 무지연 썸네일 D&D & 북마크 목차)
 *
 * 60FPS 무지연 D&D 핵심 원칙:
 * 1. dragover 이벤트 중 React 상태(setState) 변경 0회
 * 2. requestAnimationFrame 기반 CSS transform 으로 삽입선(Drop Indicator) 추적
 * 3. 캔버스 불변 보존 (순서가 바뀌어도 Canvas를 다시 그리지 않고 DOM 노드만 이동)
 * 4. 외부 PDF 파일 드롭 시 해당 위치에 즉시 페이지 삽입 병합
 * 5. 다중 선택 (Ctrl/Shift 클릭) 및 우클릭 컨텍스트 메뉴
 */
import { useEffect, useRef, useState, memo, useCallback } from 'react'
import { useStore } from '../lib/store'
import { getPdfDoc, getOrLoadPdfDoc } from '../hooks/usePdfDocument'
import { extractSelectedPagesAction } from '../hooks/useMenuActions'
import { BookmarkPanel } from './BookmarkPanel'
import type { VirtualPage } from '../core/domain/page'

const THUMB_WIDTH = 156 // 썸네일 기준 폭 (px)

export function ThumbnailPanel() {
  const visible = useStore((s) => s.thumbnailsVisible)
  const doc = useStore((s) => s.document)
  const pages = useStore((s) => s.pages)
  const selectedPageIndices = useStore((s) => s.selectedPageIndices)
  const currentPage = useStore((s) => s.currentPage)
  const sidebarTab = useStore((s) => s.sidebarTab)
  const bookmarks = useStore((s) => s.bookmarks)

  const setSidebarTab = useStore((s) => s.setSidebarTab)
  const selectPage = useStore((s) => s.selectPage)
  const reorderPages = useStore((s) => s.reorderPages)
  const rotatePages = useStore((s) => s.rotatePages)
  const deletePages = useStore((s) => s.deletePages)
  const duplicatePages = useStore((s) => s.duplicatePages)
  const insertPagesFromDocument = useStore((s) => s.insertPagesFromDocument)

  const containerRef = useRef<HTMLDivElement>(null)
  const indicatorRef = useRef<HTMLDivElement>(null)
  const draggedIndexRef = useRef<number | null>(null)
  const targetDropIndexRef = useRef<number | null>(null)
  const rafIdRef = useRef<number | null>(null)

  // 우클릭 컨텍스트 메뉴 상태
  const [contextMenu, setContextMenu] = useState<{
    x: number
    y: number
    targetIndex: number
  } | null>(null)

  // 컨텍스트 메뉴 외부 클릭 시 닫기
  useEffect(() => {
    const closeMenu = () => setContextMenu(null)
    window.addEventListener('click', closeMenu)
    window.addEventListener('contextmenu', closeMenu)
    return () => {
      window.removeEventListener('click', closeMenu)
      window.removeEventListener('contextmenu', closeMenu)
    }
  }, [])

  // 드래그 오버 핸들러 (React 상태 변경 없이 RAF로 60fps 인디케이터 추적)
  const handleDragOver = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault()
      e.dataTransfer.dropEffect = 'move'

      const container = containerRef.current
      const indicator = indicatorRef.current
      if (!container || !indicator) return

      const clientY = e.clientY
      const containerRect = container.getBoundingClientRect()
      const scrollY = container.scrollTop

      // 자식 thumbnail 요소들 위치 스캔
      const items = Array.from(
        container.querySelectorAll<HTMLElement>('.thumbnail')
      )
      if (items.length === 0) return

      let dropIndex = items.length
      let indicatorTop = items[items.length - 1].offsetTop + items[items.length - 1].offsetHeight + 4

      for (let i = 0; i < items.length; i++) {
        const item = items[i]
        const rect = item.getBoundingClientRect()
        const midY = rect.top + rect.height / 2

        if (clientY < midY) {
          dropIndex = i
          indicatorTop = item.offsetTop - 5
          break
        }
      }

      targetDropIndexRef.current = dropIndex

      // RAF로 인디케이터 위치 즉시 갱신 (리렌더링 없음)
      if (rafIdRef.current) cancelAnimationFrame(rafIdRef.current)
      rafIdRef.current = requestAnimationFrame(() => {
        indicator.style.display = 'block'
        indicator.style.transform = `translateY(${indicatorTop}px)`
      })
    },
    []
  )

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    // 컨테이너 영역 밖으로 나갈 때 인디케이터 숨김
    if (e.currentTarget === e.target) {
      if (indicatorRef.current) indicatorRef.current.style.display = 'none'
    }
  }, [])

  const handleDrop = useCallback(
    async (e: React.DragEvent) => {
      e.preventDefault()
      if (indicatorRef.current) indicatorRef.current.style.display = 'none'

      const targetIndex = targetDropIndexRef.current ?? pages.length

      // 1. 외부 PDF 파일 드롭 처리 (원하는 위치에 끼워넣기 병합)
      if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
        const setLoadingProgress = useStore.getState().setLoadingProgress
        const totalFiles = e.dataTransfer.files.length
        setLoadingProgress({
          visible: true,
          percent: 5,
          title: '외부 PDF 페이지 추가 중',
          detail: `${totalFiles}개의 PDF 파일을 분석하고 있습니다...`
        })

        try {
          for (let i = 0; i < totalFiles; i++) {
            const file = e.dataTransfer.files[i]
            if (file.type === 'application/pdf' || file.name.endsWith('.pdf')) {
              const currentPct = Math.round(10 + (i / totalFiles) * 70)
              setLoadingProgress({
                visible: true,
                percent: currentPct,
                title: 'PDF 페이지 추가 중',
                detail: `파일 로드 중 (${i + 1}/${totalFiles}): ${file.name}`
              })

              const buffer = await file.arrayBuffer()
              const bytes = new Uint8Array(buffer)
              const sourceId = `src_${Date.now()}_${i}`

              setLoadingProgress({
                visible: true,
                percent: currentPct + 15,
                title: 'PDF 페이지 추가 중',
                detail: `페이지 구조 분석 중 (${file.name})...`
              })

              const loadedDoc = await getOrLoadPdfDoc(sourceId, bytes)
              insertPagesFromDocument(
                sourceId,
                file.name,
                bytes,
                loadedDoc.numPages,
                targetIndex + i
              )
            }
          }
          setLoadingProgress({
            visible: true,
            percent: 100,
            title: '페이지 추가 완료',
            detail: '모든 페이지가 매니페스트에 등록되었습니다.'
          })
          setTimeout(() => setLoadingProgress(null), 350)
        } catch (err) {
          console.error('드롭 파일 추가 실패:', err)
          setLoadingProgress(null)
          alert(`페이지 추가 실패: ${err instanceof Error ? err.message : String(err)}`)
        }
        return
      }

      // 2. 내부 썸네일 재배열 처리
      const fromIndex = draggedIndexRef.current
      if (fromIndex !== null && fromIndex !== targetIndex) {
        // targetIndex 보정 (뒤로 이동할 때 인덱스 처리)
        const adjustedTo = fromIndex < targetIndex ? targetIndex - 1 : targetIndex
        reorderPages(fromIndex, adjustedTo)
      }

      draggedIndexRef.current = null
      targetDropIndexRef.current = null
    },
    [pages.length, insertPagesFromDocument, reorderPages]
  )

  const handleContextMenu = (e: React.MouseEvent, index: number) => {
    e.preventDefault()
    e.stopPropagation()
    if (!selectedPageIndices.includes(index)) {
      selectPage(index, 'single')
    }
    setContextMenu({
      x: e.clientX,
      y: e.clientY,
      targetIndex: index
    })
  }

  // 외부 PDF 삽입 다이얼로그 호출
  const handleInsertPdfDialog = async (insertIndex: number) => {
    const paths = await window.workspace.openMultiple()
    if (!paths || paths.length === 0) return

    const setLoadingProgress = useStore.getState().setLoadingProgress
    const total = paths.length
    setLoadingProgress({
      visible: true,
      percent: 5,
      title: 'PDF 페이지 삽입 중',
      detail: `${total}개의 파일을 선택했습니다...`
    })

    try {
      for (let i = 0; i < total; i++) {
        const p = paths[i]
        const currentPct = Math.round(10 + (i / total) * 75)
        const fileName = p.split(/[\\/]/).pop() ?? '문서.pdf'

        setLoadingProgress({
          visible: true,
          percent: currentPct,
          title: 'PDF 페이지 삽입 중',
          detail: `파일 읽는 중 (${i + 1}/${total}): ${fileName}`
        })

        const data = await window.workspace.readFile(p)
        const bytes = new Uint8Array(data)
        const sourceId = `src_ins_${Date.now()}_${i}`

        setLoadingProgress({
          visible: true,
          percent: currentPct + 10,
          title: 'PDF 페이지 삽입 중',
          detail: `페이지 파싱 중: ${fileName}`
        })

        const loadedDoc = await getOrLoadPdfDoc(sourceId, bytes)

        setLoadingProgress({
          visible: true,
          percent: currentPct + 20,
          title: 'PDF 페이지 삽입 중',
          detail: `${loadedDoc.numPages}개 페이지 조판 매니페스트에 추가 중...`
        })

        insertPagesFromDocument(
          sourceId,
          fileName,
          bytes,
          loadedDoc.numPages,
          insertIndex + i
        )
      }

      setLoadingProgress({
        visible: true,
        percent: 100,
        title: '완료',
        detail: '모든 페이지가 성공적으로 추가되었습니다.'
      })
      setTimeout(() => setLoadingProgress(null), 350)
    } catch (err) {
      console.error('PDF 삽입 실패:', err)
      setLoadingProgress(null)
      alert(`PDF 삽입 실패: ${err instanceof Error ? err.message : String(err)}`)
    }
  }

  if (!doc) {
    return <aside className={`sidebar ${visible ? '' : 'sidebar--hidden'}`} />
  }

  return (
    <aside className={`sidebar ${visible ? '' : 'sidebar--hidden'}`}>
      {/* 탭 헤더: 썸네일 vs 북마크(목차) */}
      <div className="sidebar__tabs">
        <button
          className={`sidebar__tab ${sidebarTab === 'thumbnails' ? 'sidebar__tab--active' : ''}`}
          onClick={() => setSidebarTab('thumbnails')}
          title="페이지 썸네일 목록"
        >
          ▥ 썸네일 ({pages.length})
        </button>
        <button
          className={`sidebar__tab ${sidebarTab === 'bookmarks' ? 'sidebar__tab--active' : ''}`}
          onClick={() => setSidebarTab('bookmarks')}
          title="문서 목차 (북마크)"
        >
          📑 목차 {bookmarks.length > 0 ? `(${bookmarks.length})` : ''}
        </button>
      </div>

      {/* 사이드바 컨텐츠 */}
      {sidebarTab === 'bookmarks' ? (
        <div className="sidebar__content">
          <BookmarkPanel />
        </div>
      ) : (
        <div
          ref={containerRef}
          className="sidebar__content"
          onDragOver={handleDragOver}
          onDragLeave={handleDragLeave}
          onDrop={handleDrop}
        >
          {/* RAF로 제어되는 초경량 드롭 인디케이터 (삽입선) */}
          <div ref={indicatorRef} className="thumbnail-drop-indicator" />

          {pages.map((vPage, index) => {
            const isSelected = selectedPageIndices.includes(index)
            const isActive = index === currentPage

            return (
              <ThumbnailItem
                key={vPage.id}
                virtualPage={vPage}
                index={index}
                isSelected={isSelected}
                isActive={isActive}
                onSelect={(e) => {
                  if (e.ctrlKey || e.metaKey) selectPage(index, 'toggle')
                  else if (e.shiftKey) selectPage(index, 'range')
                  else selectPage(index, 'single')
                }}
                onContextMenu={(e) => handleContextMenu(e, index)}
                onDragStart={(e) => {
                  draggedIndexRef.current = index
                  e.dataTransfer.setData('text/plain', String(index))
                  e.dataTransfer.effectAllowed = 'move'
                }}
              />
            )
          })}
        </div>
      )}

      {/* 우클릭 컨텍스트 메뉴 */}
      {contextMenu && (
        <div
          className="context-menu"
          style={{ top: contextMenu.y, left: contextMenu.x }}
          onClick={(e) => e.stopPropagation()}
        >
          <button
            className="context-menu__item"
            onClick={() => {
              rotatePages(selectedPageIndices, 'right')
              setContextMenu(null)
            }}
          >
            <span>↷</span> 시계 방향 90° 회전
          </button>
          <button
            className="context-menu__item"
            onClick={() => {
              rotatePages(selectedPageIndices, 'left')
              setContextMenu(null)
            }}
          >
            <span>↶</span> 반시계 방향 90° 회전
          </button>
          <div className="context-menu__divider" />
          <button
            className="context-menu__item"
            onClick={() => {
              duplicatePages(selectedPageIndices)
              setContextMenu(null)
            }}
          >
            <span>📋</span> 페이지 복제
          </button>
          <button
            className="context-menu__item"
            onClick={() => {
              extractSelectedPagesAction(selectedPageIndices)
              setContextMenu(null)
            }}
          >
            <span>📤</span> 선택한 페이지 내보내기...
          </button>
          <button
            className="context-menu__item"
            onClick={() => {
              handleInsertPdfDialog(contextMenu.targetIndex + 1)
              setContextMenu(null)
            }}
          >
            <span>📥</span> 여기에 다른 PDF 삽입...
          </button>
          <div className="context-menu__divider" />
          <button
            className="context-menu__item context-menu__item--danger"
            disabled={pages.length <= selectedPageIndices.length}
            onClick={() => {
              deletePages(selectedPageIndices)
              setContextMenu(null)
            }}
          >
            <span>🗑️</span> 선택한 페이지 삭제
          </button>
        </div>
      )}
    </aside>
  )
}

// ─────────────────────────────────────────────────────────────
// 개별 썸네일 아이템 (React.memo 로 리렌더링 차단)
// ─────────────────────────────────────────────────────────────

interface ThumbItemProps {
  virtualPage: VirtualPage
  index: number
  isSelected: boolean
  isActive: boolean
  onSelect: (e: React.MouseEvent) => void
  onContextMenu: (e: React.MouseEvent) => void
  onDragStart: (e: React.DragEvent) => void
}

const ThumbnailItem = memo(function ThumbnailItem({
  virtualPage,
  index,
  isSelected,
  isActive,
  onSelect,
  onContextMenu,
  onDragStart
}: ThumbItemProps) {
  const containerRef = useRef<HTMLDivElement>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const [isVisible, setIsVisible] = useState(false)
  const renderedRotation = useRef<number | null>(null)
  const isMounted = useRef(true)

  useEffect(() => {
    isMounted.current = true
    return () => {
      isMounted.current = false
    }
  }, [])

  // 썸네일 가상화: 뷰포트 인근(상하 350px)에 진입했을 때만 렌더링 활성화
  useEffect(() => {
    const el = containerRef.current
    if (!el) return

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0].isIntersecting) {
          setIsVisible(true)
        }
      },
      {
        rootMargin: '350px 0px 350px 0px',
        threshold: 0
      }
    )

    observer.observe(el)
    return () => observer.disconnect()
  }, [])

  // 썸네일 렌더링 (화면에 보이고, 회전 변경 시에만 다시 그리기)
  useEffect(() => {
    if (!isVisible) return
    if (renderedRotation.current === virtualPage.rotation) return
    let cancelled = false

    const render = async () => {
      const doc = getPdfDoc(virtualPage.sourceDocId)
      const canvas = canvasRef.current
      if (!doc || !canvas) return

      try {
        const page = await doc.getPage(virtualPage.sourcePageIndex + 1)
        if (cancelled || !isMounted.current) return

        const baseViewport = page.getViewport({ scale: 1 })
        const scale = (THUMB_WIDTH / baseViewport.width) * window.devicePixelRatio
        const viewport = page.getViewport({
          scale,
          rotation: virtualPage.rotation
        })

        const ctx = canvas.getContext('2d', { alpha: false })
        if (!ctx) return

        canvas.width = Math.floor(viewport.width)
        canvas.height = Math.floor(viewport.height)
        canvas.style.width = `${THUMB_WIDTH}px`
        canvas.style.height = `${(THUMB_WIDTH / viewport.width) * viewport.height}px`

        ctx.fillStyle = '#ffffff'
        ctx.fillRect(0, 0, canvas.width, canvas.height)

        await page.render({
          canvasContext: ctx,
          viewport,
          canvas
        } as Parameters<typeof page.render>[0]).promise

        if (!cancelled && isMounted.current) {
          renderedRotation.current = virtualPage.rotation
        }
      } catch (err) {
        if (!cancelled) console.error(`썸네일 ${index + 1} 렌더링 실패:`, err)
      }
    }

    const win = window as unknown as {
      requestIdleCallback?: (cb: () => void) => number
      cancelIdleCallback?: (id: number) => void
    }
    const timer =
      typeof win.requestIdleCallback === 'function'
        ? win.requestIdleCallback(() => void render())
        : window.setTimeout(() => void render(), 30)

    return () => {
      cancelled = true
      if (typeof win.cancelIdleCallback === 'function' && typeof win.requestIdleCallback === 'function') {
        win.cancelIdleCallback(timer)
      } else {
        clearTimeout(timer)
      }
    }
  }, [isVisible, virtualPage.sourceDocId, virtualPage.sourcePageIndex, virtualPage.rotation, index])

  const classNames = [
    'thumbnail',
    isActive ? 'thumbnail--active' : '',
    isSelected ? 'thumbnail--selected' : ''
  ]
    .filter(Boolean)
    .join(' ')

  return (
    <div
      ref={containerRef}
      className={classNames}
      draggable
      onClick={onSelect}
      onContextMenu={onContextMenu}
      onDragStart={onDragStart}
    >
      {isVisible ? (
        <canvas ref={canvasRef} className="thumbnail__canvas" />
      ) : (
        <div className="thumbnail__placeholder">
          <span className="thumbnail__placeholder-icon">📄</span>
          <span className="thumbnail__placeholder-text">대기 중...</span>
        </div>
      )}
      <span className="thumbnail__label">{index + 1}</span>
      <span
        className={`thumbnail__rotation ${virtualPage.rotation % 360 !== 0 ? 'thumbnail__rotation--visible' : ''}`}
      >
        {virtualPage.rotation}°
      </span>
    </div>
  )
})
