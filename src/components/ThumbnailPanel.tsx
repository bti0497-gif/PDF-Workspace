import { readPageGeometries } from '../hooks/usePdfDocument'
import './thumbnail-grid.css'

type ThumbnailGridColumns = 1 | 2 | 3

/**
 * 왼쪽 사이드바 패널 (60FPS 무지연 썸네일 D&D & 북마크 목차)
 *
 * 60FPS 무지연 D&D 핵심 원칙:
 * 1. dragover 이벤트 중 React 상태(setState) 변경 0회
 * 2. requestAnimationFrame 기반 CSS transform 으로 삽입선(Drop Indicator) 추적
 * 3. 캔버스 불변 보존 (순서가 바뀌어도 Canvas를 다시 그리지 않고 DOM 노드만 이동)
 * 4. 외부 PDF 파일 드롭 시 해당 위치에 즉시 페이지 순차 삽입 병합
 * 5. 다중 선택 (Ctrl/Shift/체크박스/러버밴드 마키) 및 다중 페이지 일괄 드래그 이동
 */
import { useEffect, useRef, useState, memo, useCallback } from 'react'
import { useStore } from '../lib/store'
import { getPdfDoc, getOrLoadPdfDoc } from '../hooks/usePdfDocument'
import { extractSelectedPagesAction, insertBlankPageAt } from '../hooks/useMenuActions'
import { BookmarkPanel } from './BookmarkPanel'
import { AnnotationListPanel } from './AnnotationListPanel'
import type { VirtualPage } from '../core/domain/page'

export function ThumbnailPanel() {
  const visible = useStore((s) => s.thumbnailsVisible)
  const doc = useStore((s) => s.document)
  const pages = useStore((s) => s.pages)
  const selectedPageIndices = useStore((s) => s.selectedPageIndices)
  const currentPage = useStore((s) => s.currentPage)
  const sidebarTab = useStore((s) => s.sidebarTab)
  const bookmarks = useStore((s) => s.bookmarks)
  const annotations = useStore((s) => s.annotations)
  const thumbnailSize = useStore((s) => s.thumbnailSize)
  const sidebarWidth = useStore((s) => s.sidebarWidth)
  const selectionColor = useStore((s) => s.selectionColor)

  const setSidebarTab = useStore((s) => s.setSidebarTab)
  const setThumbnailSize = useStore((s) => s.setThumbnailSize)
  const setSidebarWidth = useStore((s) => s.setSidebarWidth)
  const selectPage = useStore((s) => s.selectPage)
  const selectAllPages = useStore((s) => s.selectAllPages)
  const clearPageSelection = useStore((s) => s.clearPageSelection)
  const selectPagesByPredicate = useStore((s) => s.selectPagesByPredicate)
  const reorderPages = useStore((s) => s.reorderPages)
  const reorderMultiplePages = useStore((s) => s.reorderMultiplePages)
  const rotatePages = useStore((s) => s.rotatePages)
  const deletePages = useStore((s) => s.deletePages)
  const duplicatePages = useStore((s) => s.duplicatePages)
  const insertPagesFromDocument = useStore((s) => s.insertPagesFromDocument)

  const containerRef = useRef<HTMLDivElement | null>(null)
  const [scrollRoot, setScrollRoot] = useState<HTMLDivElement | null>(null)
  const setScrollContainer = useCallback((node: HTMLDivElement | null) => {
    containerRef.current = node
    setScrollRoot(node)
  }, [])
  const listRef = useRef<HTMLDivElement>(null)
  const indicatorRef = useRef<HTMLDivElement>(null)
  const draggedIndexRef = useRef<number | null>(null)
  const targetDropIndexRef = useRef<number | null>(null)
  const rafIdRef = useRef<number | null>(null)

  const [draggingIndex, setDraggingIndex] = useState<number | null>(null)
  const gridColumns = useStore((s) => s.thumbnailColumns)
  const setGridColumns = useStore((s) => s.setThumbnailColumns)

  // 러버밴드 마키(Marquee) 영역 선택 상태
  const [marqueeBox, setMarqueeBox] = useState<{
    startX: number
    startY: number
    currentX: number
    currentY: number
  } | null>(null)
  const isMarqueeActiveRef = useRef(false)

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

  // Ctrl + Wheel 썸네일 크기 부드러운 조정 (passive: false 로 기본 브라우저 줌 방지)
  useEffect(() => {
    const container = containerRef.current
    if (!container) return

    const onWheel = (e: WheelEvent) => {
      if (e.ctrlKey || e.metaKey) {
        e.preventDefault()
        const delta = e.deltaY < 0 ? 16 : -16
        const cur = useStore.getState().thumbnailSize
        useStore.getState().setThumbnailSize(cur + delta)
      }
    }

    container.addEventListener('wheel', onWheel, { passive: false })
    return () => {
      container.removeEventListener('wheel', onWheel)
    }
  }, [])

  // 뷰어 스크롤 동기화: currentPage 변경 시 좌측 썸네일도 해당 위치로 자동 스크롤
  useEffect(() => {
    if (sidebarTab !== 'thumbnails') return
    const container = containerRef.current
    if (!container) return
    const activeThumb = container.querySelector<HTMLElement>(`.thumbnail[data-index="${currentPage}"]`)
    if (activeThumb) {
      // Keep the active thumbnail visible without scrolling ancestors/header.
      const pane = container.getBoundingClientRect()
      const thumb = activeThumb.getBoundingClientRect()
      const top = pane.top + container.clientTop
      const bottom = top + container.clientHeight
      const delta = thumb.top < top ? thumb.top - top : thumb.bottom > bottom ? thumb.bottom - bottom : 0
      if (delta) container.scrollTo({ top: container.scrollTop + delta, behavior: 'smooth' })
    }
  }, [currentPage, sidebarTab])

  // 사이드바 너비 드래그 조절기 (Resizer) 핸들러
  const handleResizerMouseDown = (e: React.MouseEvent) => {
    e.preventDefault()
    const startX = e.clientX
    const startWidth = sidebarWidth
    const onMouseMove = (ev: MouseEvent) => {
      setSidebarWidth(startWidth + (ev.clientX - startX))
    }
    const onMouseUp = () => {
      window.removeEventListener('mousemove', onMouseMove)
      window.removeEventListener('mouseup', onMouseUp)
    }
    window.addEventListener('mousemove', onMouseMove)
    window.addEventListener('mouseup', onMouseUp)
  }

  // 러버밴드 마키 박스 드래그 선택 시작
  const handleContainerMouseDown = (e: React.MouseEvent<HTMLDivElement>) => {
    // 좌클릭만 허용, 썸네일/체크박스/컨텍스트메뉴 클릭 시 마키 시작 방지
    if (e.button !== 0) return
    const target = e.target as HTMLElement
    if (
      target.closest('.thumbnail') ||
      target.closest('.thumbnail-controls') ||
      target.closest('.context-menu') ||
      target.closest('.thumbnail-panel__resizer')
    ) {
      return
    }

    const container = containerRef.current
    if (!container) return

    const rect = container.getBoundingClientRect()
    const startX = e.clientX - rect.left
    const startY = e.clientY - rect.top + container.scrollTop

    isMarqueeActiveRef.current = true
    setMarqueeBox({
      startX,
      startY,
      currentX: startX,
      currentY: startY
    })

    const onMouseMove = (ev: MouseEvent) => {
      if (!isMarqueeActiveRef.current || !containerRef.current) return
      const cRect = containerRef.current.getBoundingClientRect()
      const curX = ev.clientX - cRect.left
      const curY = ev.clientY - cRect.top + containerRef.current.scrollTop

      setMarqueeBox((prev) => (prev ? { ...prev, currentX: curX, currentY: curY } : null))

      // 마키 박스 좌표 계산 (컨테이너 내부 스크롤 기준)
      const boxLeft = Math.min(startX, curX)
      const boxRight = Math.max(startX, curX)
      const boxTop = Math.min(startY, curY)
      const boxBottom = Math.max(startY, curY)

      const thumbEls = Array.from(
        containerRef.current.querySelectorAll<HTMLElement>('.thumbnail[data-index]')
      )
      const intersecting: number[] = []

      for (const el of thumbEls) {
        const elTop = el.offsetTop
        const elBottom = elTop + el.offsetHeight
        const elLeft = el.offsetLeft
        const elRight = elLeft + el.offsetWidth

        // 겹침 검사
        if (boxLeft <= elRight && boxRight >= elLeft && boxTop <= elBottom && boxBottom >= elTop) {
          const idx = Number(el.getAttribute('data-index'))
          if (!isNaN(idx)) intersecting.push(idx)
        }
      }

      if (intersecting.length > 0) {
        selectPagesByPredicate(intersecting)
      }
    }

    const onMouseUp = () => {
      isMarqueeActiveRef.current = false
      setMarqueeBox(null)
      window.removeEventListener('mousemove', onMouseMove)
      window.removeEventListener('mouseup', onMouseUp)
    }

    window.addEventListener('mousemove', onMouseMove)
    window.addEventListener('mouseup', onMouseUp)
  }

  // 키보드 단축키 핸들러
  const handleKeyDown = (e: React.KeyboardEvent) => {
    // Modal dialogs own keyboard interaction; never alter thumbnail selection behind them.
    if (document.querySelector('[role="dialog"]')) return

    const isCmdOrCtrl = e.ctrlKey || e.metaKey

    if (isCmdOrCtrl && e.key.toLowerCase() === 'a') {
      e.preventDefault()
      selectAllPages()
      return
    }
    if (e.key === 'Escape') {
      e.preventDefault()
      clearPageSelection()
      return
    }
    if (e.key === 'Delete') {
      e.preventDefault()
      if (pages.length > selectedPageIndices.length && selectedPageIndices.length > 0) {
        deletePages(selectedPageIndices)
      }
      return
    }
    if (e.key === 'ArrowUp' || e.key === 'ArrowLeft') {
      e.preventDefault()
      const nextIdx = Math.max(0, currentPage - 1)
      if (e.shiftKey) {
        selectPage(nextIdx, 'range')
      } else {
        selectPage(nextIdx, 'single')
      }
      return
    }
    if (e.key === 'ArrowDown' || e.key === 'ArrowRight') {
      e.preventDefault()
      const nextIdx = Math.min(pages.length - 1, currentPage + 1)
      if (e.shiftKey) {
        selectPage(nextIdx, 'range')
      } else {
        selectPage(nextIdx, 'single')
      }
      return
    }
  }

  // 드래그 오버 핸들러. Drop slots use the actual grid card rectangles rather than
  // a single-column Y scan, so explicit 1/2/3-column layouts keep correct ordering.
  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault()
    e.stopPropagation()
    e.dataTransfer.dropEffect = 'move'

    const container = containerRef.current
    const indicator = indicatorRef.current
    if (!container || !indicator) return

    const items = Array.from(container.querySelectorAll<HTMLElement>('.thumbnail'))
    if (items.length === 0) return

    const containerRect = container.getBoundingClientRect()
    const rects = items.map((item) => item.getBoundingClientRect())
    const lastBottom = Math.max(...rects.map((rect) => rect.bottom))
    let targetItemIndex = 0
    let before = true

    if (e.clientY > lastBottom + 8) {
      targetItemIndex = items.length - 1
      before = false
    } else {
      let closestDistance = Number.POSITIVE_INFINITY
      rects.forEach((rect, index) => {
        const dx = e.clientX - (rect.left + rect.width / 2)
        const dy = e.clientY - (rect.top + rect.height / 2)
        const distance = dx * dx + dy * dy
        if (distance < closestDistance) {
          closestDistance = distance
          targetItemIndex = index
        }
      })
      before = e.clientY < rects[targetItemIndex].top + rects[targetItemIndex].height / 2
    }

    const targetRect = rects[targetItemIndex]
    const dropIndex = before ? targetItemIndex : targetItemIndex + 1
    const indicatorLeft = targetRect.left - containerRect.left + container.scrollLeft
    const indicatorTop =
      targetRect.top - containerRect.top + container.scrollTop + (before ? -4 : targetRect.height + 1)

    targetDropIndexRef.current = dropIndex

    if (rafIdRef.current) cancelAnimationFrame(rafIdRef.current)
    rafIdRef.current = requestAnimationFrame(() => {
      indicator.style.display = 'block'
      indicator.style.width = `${targetRect.width}px`
      indicator.style.transform = `translate(${indicatorLeft}px, ${indicatorTop}px)`
    })
  }, [])

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    if (e.currentTarget === e.target) {
      if (indicatorRef.current) indicatorRef.current.style.display = 'none'
    }
  }, [])

  const handleDrop = useCallback(
    async (e: React.DragEvent) => {
      e.preventDefault()
      e.stopPropagation() // 상위 윈도우 drop 리스너로 전파 차단
      if (indicatorRef.current) indicatorRef.current.style.display = 'none'

      const targetIndex = targetDropIndexRef.current ?? pages.length

      // 1. 외부 PDF 파일 드롭 처리 (원하는 위치에 끼워넣기 순차 병합)
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
          let runningInsertIdx = targetIndex
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

              let dimensions: { width: number; height: number; pageGeometries?: { width: number; height: number; rotation: number }[] } | undefined
              try {
                const page1 = await loadedDoc.getPage(1)
                const v1 = page1.getViewport({ scale: 1, rotation: 0 })
                dimensions = { width: v1.width, height: v1.height, pageGeometries: await readPageGeometries(loadedDoc) }
              } catch {
                /* fallback */
              }

              insertPagesFromDocument(
                sourceId,
                file.name,
                bytes,
                loadedDoc.numPages,
                runningInsertIdx,
                dimensions
              )
              runningInsertIdx += loadedDoc.numPages
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

      // 2. 내부 썸네일 재배열 처리 (다중 선택된 경우 블록 일괄 이동)
      const fromIndex = draggedIndexRef.current
      if (fromIndex !== null && fromIndex !== targetIndex) {
        const isMulti =
          selectedPageIndices.includes(fromIndex) && selectedPageIndices.length > 1
        if (isMulti) {
          reorderMultiplePages(selectedPageIndices, targetIndex)
        } else {
          const adjustedTo = fromIndex < targetIndex ? targetIndex - 1 : targetIndex
          reorderPages(fromIndex, adjustedTo)
        }
      }

      draggedIndexRef.current = null
      targetDropIndexRef.current = null
      setDraggingIndex(null)
    },
    [pages.length, selectedPageIndices, insertPagesFromDocument, reorderPages, reorderMultiplePages]
  )

  // Shared callbacks keep memoized items stable when only the active page changes.
  const handleThumbnailSelect = useCallback((index: number, e: React.MouseEvent) => {
    if (e.ctrlKey || e.metaKey) selectPage(index, 'toggle')
    else if (e.shiftKey) selectPage(index, 'range')
    else selectPage(index, 'single')
  }, [selectPage])

  const handleThumbnailToggleSelect = useCallback((index: number, e: React.MouseEvent) => {
    e.stopPropagation()
    selectPage(index, 'toggle')
  }, [selectPage])

  const handleThumbnailContextMenu = useCallback((index: number, e: React.MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()
    if (!useStore.getState().selectedPageIndices.includes(index)) useStore.getState().selectPage(index, 'single')
    setContextMenu({ x: e.clientX, y: e.clientY, targetIndex: index })
  }, [])

  const handleThumbnailDragStart = useCallback((index: number, e: React.DragEvent) => {
    draggedIndexRef.current = index
    setDraggingIndex(index)
    if (!useStore.getState().selectedPageIndices.includes(index)) useStore.getState().selectPage(index, 'single')
    e.dataTransfer.setData('text/plain', String(index))
    e.dataTransfer.effectAllowed = 'move'
  }, [])

  const handleThumbnailDragEnd = useCallback(() => {
    draggedIndexRef.current = null
    setDraggingIndex(null)
  }, [])

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
      let runningInsertIdx = insertIndex
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

        let dimensions: { width: number; height: number; pageGeometries?: { width: number; height: number; rotation: number }[] } | undefined
        try {
          const page1 = await loadedDoc.getPage(1)
          const v1 = page1.getViewport({ scale: 1, rotation: 0 })
          dimensions = { width: v1.width, height: v1.height, pageGeometries: await readPageGeometries(loadedDoc) }
        } catch {
          /* fallback */
        }

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
          runningInsertIdx,
          dimensions
        )
        runningInsertIdx += loadedDoc.numPages
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

  const dragCount =
    draggingIndex !== null && selectedPageIndices.includes(draggingIndex)
      ? selectedPageIndices.length
      : 1

  return (
    <aside
      className={`sidebar ${visible ? '' : 'sidebar--hidden'}`}
      style={
        {
          '--sidebar-width': `${sidebarWidth}px`,
          '--thumb-size': `${thumbnailSize}px`,
          '--thumb-selected-color': selectionColor
        } as React.CSSProperties
      }
    >
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
        <button
          className={`sidebar__tab ${sidebarTab === 'annotations' ? 'sidebar__tab--active' : ''}`}
          onClick={() => setSidebarTab('annotations')}
          title="문서 내 주석 및 마크업 목록"
        >
          💬 주석 ({annotations.length})
        </button>
      </div>

      {/* 썸네일 탭 컨트롤 헤더 바 */}
      {sidebarTab === 'thumbnails' && (
        <div className="thumbnail-controls">
          {/* 1행: 크기 프리셋 & 슬라이더 */}
          <div className="thumbnail-controls__row">
            <div className="thumbnail-controls__group">
              <span className="thumbnail-controls__badge">크기:</span>
              <button
                className={`thumbnail-controls__btn ${thumbnailSize <= 120 ? 'thumbnail-controls__btn--active' : ''}`}
                onClick={() => setThumbnailSize(72)}
                title="작은 썸네일 (72px)"
              >
                소
              </button>
              <button
                className={`thumbnail-controls__btn ${thumbnailSize > 120 && thumbnailSize <= 180 ? 'thumbnail-controls__btn--active' : ''}`}
                onClick={() => setThumbnailSize(156)}
                title="중간 썸네일 (156px)"
              >
                중
              </button>
              <button
                className={`thumbnail-controls__btn ${thumbnailSize > 180 ? 'thumbnail-controls__btn--active' : ''}`}
                onClick={() => setThumbnailSize(210)}
                title="큰 썸네일 (210px)"
              >
                대
              </button>
            </div>
            <div className="thumbnail-controls__group thumbnail-controls__layout" aria-label="썸네일 열 수">
              <span className="thumbnail-controls__layout-label">열:</span>
              {([1, 2, 3] as ThumbnailGridColumns[]).map((columns) => (
                <button
                  key={columns}
                  className={`thumbnail-controls__btn thumbnail-controls__layout-button ${gridColumns === columns ? 'thumbnail-controls__btn--active' : ''}`}
                  onClick={() => setGridColumns(columns)}
                  title={`${columns}열 레이아웃`}
                  aria-pressed={gridColumns === columns}
                >
                  {columns}
                </button>
              ))}
            </div>

          </div>

          {/* 페이지 선택은 작업 명령, 색상/정밀 크기는 옵션 화면에서 관리 */}
          <div className="thumbnail-controls__row">
            <select className="thumbnail-controls__selection-menu" aria-label="페이지 선택 작업" value=""
              onChange={e => { if (e.target.value) selectPagesByPredicate(e.target.value as 'all' | 'none' | 'even' | 'odd' | 'invert') }}>
              <option value="" disabled>페이지 선택</option>
              <option value="all">전체 선택</option>
              <option value="none">선택 해제</option>
              <option value="even">짝수 페이지</option>
              <option value="odd">홀수 페이지</option>
              <option value="invert">선택 반전</option>
            </select>
            <span className="thumbnail-controls__badge">선택 {selectedPageIndices.length} / {pages.length}</span>
          </div>
        </div>
      )}

      {/* 사이드바 컨텐츠 */}
      {sidebarTab === 'bookmarks' ? (
        <div className="sidebar__content">
          <BookmarkPanel />
        </div>
      ) : sidebarTab === 'annotations' ? (
        <div className="sidebar__content">
          <AnnotationListPanel />
        </div>
      ) : (
        <div
          ref={setScrollContainer}
          className="sidebar__content"
          tabIndex={0}
          onMouseDown={handleContainerMouseDown}
          onKeyDown={handleKeyDown}
          onDragOver={handleDragOver}
          onDragLeave={handleDragLeave}
          onDrop={handleDrop}
        >
          {/* RAF로 제어되는 초경량 드롭 인디케이터 (삽입선) */}
          <div ref={indicatorRef} className="thumbnail-drop-indicator thumbnail-grid-drop-indicator" />

          {/* 러버밴드 마키 박스 */}
          {marqueeBox && (
            <div
              className="thumbnail-marquee"
              style={{
                left: `${Math.min(marqueeBox.startX, marqueeBox.currentX)}px`,
                top: `${Math.min(marqueeBox.startY, marqueeBox.currentY)}px`,
                width: `${Math.abs(marqueeBox.currentX - marqueeBox.startX)}px`,
                height: `${Math.abs(marqueeBox.currentY - marqueeBox.startY)}px`
              }}
            />
          )}

          {/* 반응형 다열 CSS 그리드 썸네일 리스트 */}
          <div
            ref={listRef}
            className="thumbnail-panel__list thumbnail-panel__grid"
            style={{ '--thumbnail-columns': gridColumns } as React.CSSProperties}
          >
            {pages.map((vPage, index) => {
              const isSelected = selectedPageIndices.includes(index)
              const isActive = index === currentPage
              const isDragging = draggingIndex === index

              return (
                <ThumbnailItem
                  key={vPage.id}
                  virtualPage={vPage}
                  index={index}
                  isSelected={isSelected}
                  isActive={isActive}
                  isDragging={isDragging}
                  dragCount={dragCount}
                  thumbnailSize={thumbnailSize}
                  scrollRoot={scrollRoot}
                  onSelect={handleThumbnailSelect}
                  onToggleSelect={handleThumbnailToggleSelect}
                  onContextMenu={handleThumbnailContextMenu}
                  onDragStart={handleThumbnailDragStart}
                  onDragEnd={handleThumbnailDragEnd}
                />
              )
            })}
          </div>
        </div>
      )}

      {/* 우측 가장자리 너비 조절 리사이저 */}
      <div
        className="thumbnail-panel__resizer"
        onMouseDown={handleResizerMouseDown}
        title="사이드바 너비 조절"
      />

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
          <button
            className="context-menu__item"
            onClick={() => {
              rotatePages(selectedPageIndices, '180')
              setContextMenu(null)
            }}
          >
            <span>🔄</span> 180° 상하 반전
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
              void insertBlankPageAt(contextMenu.targetIndex + 1)
              setContextMenu(null)
            }}
          >
            <span>📄</span> 여기에 빈 페이지 삽입
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
// 개별 썸네일 아이템 (React.memo 로 불필요한 리렌더링 차단)
// ─────────────────────────────────────────────────────────────

interface ThumbItemProps {
  virtualPage: VirtualPage
  index: number
  isSelected: boolean
  isActive: boolean
  isDragging: boolean
  dragCount: number
  thumbnailSize: number
  scrollRoot: HTMLDivElement | null
  onSelect: (index: number, e: React.MouseEvent) => void
  onToggleSelect: (index: number, e: React.MouseEvent) => void
  onContextMenu: (index: number, e: React.MouseEvent) => void
  onDragStart: (index: number, e: React.DragEvent) => void
  onDragEnd: () => void
}

const ThumbnailItem = memo(function ThumbnailItem({
  virtualPage,
  index,
  isSelected,
  isActive,
  isDragging,
  dragCount,
  thumbnailSize,
  scrollRoot,
  onSelect,
  onToggleSelect,
  onContextMenu,
  onDragStart,
  onDragEnd
}: ThumbItemProps) {
  const containerRef = useRef<HTMLDivElement>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const [isVisible, setIsVisible] = useState(false)
  const renderedIdentity = useRef<string | null>(null)
  const renderTaskRef = useRef<{ cancel: () => void; promise: Promise<unknown> } | null>(null)
  const isMounted = useRef(true)

  useEffect(() => {
    isMounted.current = true
    return () => {
      isMounted.current = false
    }
  }, [])

  // Bound canvases to the sidebar scrollport; leaving this window unmounts the bitmap.
  useEffect(() => {
    const el = containerRef.current
    if (!el || !scrollRoot) return
    const observer = new IntersectionObserver(
      ([entry]) => setIsVisible(entry.isIntersecting),
      { root: scrollRoot, rootMargin: '600px 0px 600px 0px', threshold: 0 }
    )
    observer.observe(el)
    return () => observer.disconnect()
  }, [scrollRoot])

  useEffect(() => {
    if (!isVisible) renderedIdentity.current = null
  }, [isVisible])

  // A canvas has one active PDF.js render task at a time.
  useEffect(() => {
    if (!isVisible) return
    const identity = [virtualPage.sourceDocId, virtualPage.sourcePageIndex, virtualPage.rotation, thumbnailSize].join(':')
    if (renderedIdentity.current === identity) return

    let cancelled = false
    let task: { cancel: () => void; promise: Promise<unknown> } | null = null
    const render = async () => {
      const previous = renderTaskRef.current
      if (previous) {
        previous.cancel()
        try { await previous.promise } catch { /* expected cancellation */ }
      }
      if (cancelled || !isMounted.current) return

      const doc = getPdfDoc(virtualPage.sourceDocId)
      const canvas = canvasRef.current
      if (!doc || !canvas) return
      try {
        const page = await doc.getPage(virtualPage.sourcePageIndex + 1)
        if (cancelled || !isMounted.current || canvasRef.current !== canvas) return
        const baseViewport = page.getViewport({ scale: 1 })
        const scale = (thumbnailSize / baseViewport.width) * window.devicePixelRatio
        const viewport = page.getViewport({ scale, rotation: virtualPage.rotation })
        const ctx = canvas.getContext('2d', { alpha: false })
        if (!ctx) return
        canvas.width = Math.floor(viewport.width)
        canvas.height = Math.floor(viewport.height)
        canvas.style.width = `${thumbnailSize}px`
        canvas.style.height = `${(thumbnailSize / viewport.width) * viewport.height}px`
        ctx.fillStyle = '#ffffff'
        ctx.fillRect(0, 0, canvas.width, canvas.height)
        task = page.render({ canvasContext: ctx, viewport, canvas } as Parameters<typeof page.render>[0])
        renderTaskRef.current = task
        await task.promise
        if (!cancelled && isMounted.current && canvasRef.current === canvas) renderedIdentity.current = identity
      } catch (err) {
        if (!cancelled) console.error(`썸네일 ${index + 1} 렌더링 실패:`, err)
      } finally {
        if (renderTaskRef.current === task) renderTaskRef.current = null
      }
    }

    const win = window as unknown as { requestIdleCallback?: (cb: () => void) => number; cancelIdleCallback?: (id: number) => void }
    const timer = typeof win.requestIdleCallback === 'function' ? win.requestIdleCallback(() => void render()) : window.setTimeout(() => void render(), 30)
    return () => {
      cancelled = true
      if (typeof win.cancelIdleCallback === 'function' && typeof win.requestIdleCallback === 'function') win.cancelIdleCallback(timer)
      else clearTimeout(timer)
      // Keep the cancelled task registered until its promise settles so the next
      // effect waits before reusing this canvas.
      task?.cancel()
    }
  }, [isVisible, virtualPage.sourceDocId, virtualPage.sourcePageIndex, virtualPage.rotation, thumbnailSize, index])

  const displayWidth = virtualPage.rotation % 180 === 0
    ? virtualPage.width ?? 595.28
    : virtualPage.height ?? 841.89
  const displayHeight = virtualPage.rotation % 180 === 0
    ? virtualPage.height ?? 841.89
    : virtualPage.width ?? 595.28
  const displayAspectRatio = `${displayWidth} / ${displayHeight}`

  const classNames = [
    'thumbnail',
    isActive ? 'thumbnail--active' : '',
    isSelected ? 'thumbnail--selected' : '',
    isDragging ? 'thumbnail--dragging' : ''
  ]
    .filter(Boolean)
    .join(' ')

  return (
    <div
      ref={containerRef}
      className={classNames}
      data-index={index}
      draggable
      onClick={(e) => onSelect(index, e)}
      onContextMenu={(e) => onContextMenu(index, e)}
      onDragStart={(e) => onDragStart(index, e)}
      onDragEnd={onDragEnd}
      style={{ width: `${thumbnailSize}px` }}
    >
      {/* 썸네일 좌상단 선택 체크박스 */}
      <div
        className={`thumbnail__checkbox ${isSelected ? 'thumbnail__checkbox--checked' : ''}`}
        onClick={(e) => onToggleSelect(index, e)}
        title="선택 토글"
      >
        {isSelected ? '✓' : ''}
      </div>

      {isVisible ? (
        <canvas
          ref={canvasRef}
          className="thumbnail__canvas"
          style={{ aspectRatio: displayAspectRatio }}
        />
      ) : (
        <div
          className="thumbnail__placeholder"
          style={{ width: `${thumbnailSize}px`, height: 'auto', minHeight: 0, aspectRatio: displayAspectRatio }}
        >
          <span className="thumbnail__placeholder-icon">📄</span>
          <span className="thumbnail__placeholder-text">대기 중...</span>
        </div>
      )}
      {/* 상태 배지 목록 */}
      <div className="thumbnail__badges">
        {virtualPage.cropMargins && (
          <span className="thumbnail__badge" title="여백 자르기 적용됨">✂</span>
        )}
        {virtualPage.normalizedConfig && (
          <span className="thumbnail__badge" title="조판 규격화 적용됨">⛶</span>
        )}
        {virtualPage.watermarkConfig && (
          <span className="thumbnail__badge" title={`워터마크: ${virtualPage.watermarkConfig.text}`}>💧</span>
        )}
        {virtualPage.pageNumberConfig && (
          <span className="thumbnail__badge" title={`페이지 번호: ${virtualPage.pageNumberConfig.text}`}>🔢</span>
        )}
      </div>

      <span className="thumbnail__label">{index + 1}</span>
      <span
        className={`thumbnail__rotation ${virtualPage.rotation % 360 !== 0 ? 'thumbnail__rotation--visible' : ''}`}
      >
        {virtualPage.rotation}°
      </span>

      {isDragging && dragCount > 1 && (
        <div className="thumbnail__drag-badge">{dragCount}개 이동 중</div>
      )}
    </div>
  )
})
