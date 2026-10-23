/**
 * 오른쪽 메인 뷰어 패널
 * - 단일 페이지 뷰(Single) ↔ 연속 세로 스크롤 뷰(Continuous) 완벽 지원
 * - 가상화(IntersectionObserver)로 대용량 문서에서도 메모리 누수 없이 화면 내 페이지만 렌더링
 * - 스크롤 위치와 currentPage 실시간 양방향 동기화
 */
import { useRef, useEffect, useState, memo, useCallback } from 'react'
import { useStore } from '../lib/store'
import { PageCanvas } from './PageCanvas'
import { AnnotationLayer } from './AnnotationLayer'
import { SearchBar } from './SearchBar'
import { SearchHighlightLayer } from './SearchHighlightLayer'
import { TextEditLayer } from './TextEditLayer'
import { clearRenderCache } from '../hooks/usePdfRender'
import type { VirtualPage } from '../core/domain/page'

interface Props {
  onOpenFile: () => Promise<unknown>
}

export function ViewerPanel({ onOpenFile }: Props) {
  const doc = useStore((s) => s.document)
  const pages = useStore((s) => s.pages)
  const isLoading = useStore((s) => s.isLoading)
  const currentPage = useStore((s) => s.currentPage)
  const viewMode = useStore((s) => s.viewMode)
  const zoom = useStore((s) => s.zoom)
  const setCurrentPage = useStore((s) => s.setCurrentPage)

  const containerRef = useRef<HTMLDivElement>(null)
  const isScrollingBySyncRef = useRef(false)
  const lastScrolledPageRef = useRef<number>(currentPage)

  const pageCount = pages.length > 0 ? pages.length : doc?.pageCount ?? 0

  // 단일 페이지 모드: 페이지나 줌 변경 시 상단으로 스크롤
  useEffect(() => {
    if (viewMode === 'single' && containerRef.current && doc) {
      containerRef.current.scrollTop = 0
    }
  }, [currentPage, zoom, doc, viewMode])

  // 연속 스크롤 모드: currentPage 가 썸네일/목차 클릭으로 변경되었을 때 해당 위치로 부드럽게 점프
  useEffect(() => {
    if (viewMode !== 'continuous' || !containerRef.current) return
    if (lastScrolledPageRef.current === currentPage) return

    lastScrolledPageRef.current = currentPage
    isScrollingBySyncRef.current = true

    const targetEl = containerRef.current.querySelector<HTMLElement>(
      `[data-page="${currentPage}"]`
    )
    if (targetEl) {
      targetEl.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }

    const timer = setTimeout(() => {
      isScrollingBySyncRef.current = false
    }, 600)
    return () => clearTimeout(timer)
  }, [currentPage, viewMode])

  // 연속 스크롤 모드: 스크롤 시 화면 중심에 있는 페이지 번호 실시간 감지 (썸네일/상태표시줄과 동기화)
  useEffect(() => {
    if (viewMode !== 'continuous') return
    const container = containerRef.current
    if (!container) return

    let rafId: number | null = null

    const onScroll = () => {
      if (isScrollingBySyncRef.current) return
      if (rafId) cancelAnimationFrame(rafId)

      rafId = requestAnimationFrame(() => {
        const pageElements = Array.from(
          container.querySelectorAll<HTMLElement>('.viewer__virtual-page')
        )
        if (pageElements.length === 0) return

        const containerRect = container.getBoundingClientRect()
        const centerY = containerRect.top + containerRect.height / 2

        let closestPage = 0
        let minDistance = Infinity

        for (const el of pageElements) {
          const rect = el.getBoundingClientRect()
          const elMidY = rect.top + rect.height / 2
          const dist = Math.abs(elMidY - centerY)

          if (dist < minDistance) {
            minDistance = dist
            closestPage = parseInt(el.getAttribute('data-page') ?? '0', 10)
          }
        }

        if (closestPage !== lastScrolledPageRef.current) {
          lastScrolledPageRef.current = closestPage
          setCurrentPage(closestPage)
        }
      })
    }

    container.addEventListener('scroll', onScroll, { passive: true })
    return () => {
      container.removeEventListener('scroll', onScroll)
      if (rafId) cancelAnimationFrame(rafId)
    }
  }, [viewMode, setCurrentPage])

  // 키보드 단축키 (페이지 이동)
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!doc || pageCount === 0) return
      const target = e.target as HTMLElement
      if (
        target.tagName === 'INPUT' ||
        target.tagName === 'TEXTAREA' ||
        target.isContentEditable
      ) {
        return
      }

      const s = useStore.getState()
      if (e.key === 'Home') {
        s.setCurrentPage(0)
      } else if (e.key === 'End') {
        s.setCurrentPage(pageCount - 1)
      } else if (e.key === 'PageUp' || (e.shiftKey && e.key === 'ArrowUp')) {
        s.setCurrentPage(Math.max(0, s.currentPage - 1))
      } else if (e.key === 'PageDown' || (e.shiftKey && e.key === 'ArrowDown')) {
        s.setCurrentPage(Math.min(pageCount - 1, s.currentPage + 1))
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [doc, pageCount])

  // 문서가 바뀌면 렌더 캐시 비움
  useEffect(() => {
    if (doc) clearRenderCache()
  }, [doc?.filePath, doc?.fileName])

  if (!doc || pageCount === 0) {
    return (
      <main className="viewer" ref={containerRef}>
        <div className="viewer__empty">
          <div className="viewer__empty-icon">📄</div>
          <div className="viewer__empty-title">더존 PDF 편집기</div>
          <div className="viewer__empty-hint">
            파일 &gt; 열기 로 PDF를 선택하거나, 여기로 파일을 끌어다 놓으세요
          </div>
          <button className="viewer__empty-btn" onClick={() => void onOpenFile()}>
            PDF 열기
          </button>
        </div>
      </main>
    )
  }

  // ── 연속 스크롤 모드 ──
  if (viewMode === 'continuous') {
    return (
      <main className="viewer viewer--continuous" ref={containerRef}>
        <SearchBar />
        {isLoading && (
          <div className="loading-overlay">
            <div className="loading-overlay__spinner" />
            <div>로드 중...</div>
          </div>
        )}
        <div className="viewer__continuous-list">
          {pages.map((vPage, index) => (
            <ContinuousPageItem
              key={vPage.id}
              virtualPage={vPage}
              index={index}
              zoom={zoom}
              containerRef={containerRef}
              isActive={index === currentPage}
            />
          ))}
        </div>
      </main>
    )
  }

  // ── 단일 페이지 모드 ──
  const activePage = pages[currentPage]
  const rotation = activePage?.rotation ?? doc.rotations[currentPage] ?? 0

  return (
    <main className="viewer" ref={containerRef}>
      <SearchBar />
      {isLoading && (
        <div className="loading-overlay">
          <div className="loading-overlay__spinner" />
          <div>로드 중...</div>
        </div>
      )}
      <div
        className="viewer__page-wrapper"
        data-page={currentPage}
        key={activePage?.id ?? currentPage}
      >
        <PageCanvas pageIndex={currentPage} zoom={zoom} rotation={rotation} />
        <SearchHighlightLayer pageIndex={currentPage} zoom={zoom} />
        <TextEditLayer pageIndex={currentPage} zoom={zoom} rotation={rotation} />
        <AnnotationLayer pageIndex={currentPage} zoom={zoom} rotation={rotation} />
      </div>
    </main>
  )
}

// ─────────────────────────────────────────────────────────────
// 연속 스크롤 개별 페이지 (IntersectionObserver 가상화)
// ─────────────────────────────────────────────────────────────

interface ContinuousItemProps {
  virtualPage: VirtualPage
  index: number
  zoom: number
  containerRef: React.RefObject<HTMLDivElement>
  isActive: boolean
}

const ContinuousPageItem = memo(function ContinuousPageItem({
  virtualPage,
  index,
  zoom,
  containerRef,
  isActive
}: ContinuousItemProps) {
  const itemRef = useRef<HTMLDivElement>(null)
  const [isVisible, setIsVisible] = useState(false)

  // 뷰포트 교차 관찰 (상하 350px 여유 공간)
  useEffect(() => {
    const el = itemRef.current
    const container = containerRef.current
    if (!el || !container) return

    const observer = new IntersectionObserver(
      (entries) => {
        const entry = entries[0]
        setIsVisible(entry.isIntersecting)
      },
      {
        root: container,
        rootMargin: '350px 0px 350px 0px',
        threshold: 0
      }
    )

    observer.observe(el)
    return () => observer.disconnect()
  }, [containerRef])

  // 회전 상태에 따른 종횡비 크기 계산
  const isRotated = virtualPage.rotation === 90 || virtualPage.rotation === 270
  const baseW = isRotated ? virtualPage.height ?? 841.89 : virtualPage.width ?? 595.28
  const baseH = isRotated ? virtualPage.width ?? 595.28 : virtualPage.height ?? 841.89

  const widthPx = Math.round(baseW * zoom)
  const heightPx = Math.round(baseH * zoom)

  return (
    <div
      ref={itemRef}
      className={`viewer__virtual-page ${isActive ? 'viewer__virtual-page--active' : ''}`}
      data-page={index}
      style={{
        width: `${widthPx}px`,
        height: `${heightPx}px`
      }}
    >
      {isVisible ? (
        <>
          <PageCanvas
            pageIndex={index}
            zoom={zoom}
            rotation={virtualPage.rotation}
          />
          <SearchHighlightLayer pageIndex={index} zoom={zoom} />
          <TextEditLayer
            pageIndex={index}
            zoom={zoom}
            rotation={virtualPage.rotation}
          />
          <AnnotationLayer
            pageIndex={index}
            zoom={zoom}
            rotation={virtualPage.rotation}
          />
        </>
      ) : (
        <div className="viewer__page-placeholder">
          <span>{index + 1} 페이지</span>
        </div>
      )}
    </div>
  )
})
