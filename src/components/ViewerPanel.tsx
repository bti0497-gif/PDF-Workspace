/**
 * 오른쪽 메인 뷰어 패널
 * - 단일 페이지 뷰(Single) ↔ 연속 세로 스크롤 뷰(Continuous) 완벽 지원
 * - 가상화(IntersectionObserver)로 대용량 문서에서도 메모리 누수 없이 화면 내 페이지만 렌더링
 * - 스크롤 위치와 currentPage 실시간 양방향 동기화
 */
import { useRef, useEffect, useState, memo, useCallback } from 'react'
import { useStore } from '../lib/store'
import { ProcessedPageView } from './ProcessedPageView'
import { PageCanvas } from './PageCanvas'
import { AnnotationLayer } from './AnnotationLayer'
import { SearchBar } from './SearchBar'
import { SearchHighlightLayer } from './SearchHighlightLayer'
import { TextEditLayer } from './TextEditLayer'
import { clearRenderCache } from '../hooks/usePdfRender'
import { usePdfDocument } from '../hooks/usePdfDocument'
import { getRecentFiles, removeRecentFile, clearRecentFiles, RecentFileItem } from '../lib/recent-files'
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

  const { openFileByPath } = usePdfDocument()
  const [recentFiles, setRecentFiles] = useState<RecentFileItem[]>(() => getRecentFiles())

  // 문서가 닫히거나 변경될 때 최근 파일 목록 갱신
  useEffect(() => {
    if (!doc) {
      setRecentFiles(getRecentFiles())
    }
  }, [doc])

  const containerRef = useRef<HTMLDivElement>(null)
  const isScrollingBySyncRef = useRef(false)
  const lastScrolledPageRef = useRef<number>(currentPage)

  const pageCount = pages.length > 0 ? pages.length : doc?.pageCount ?? 0

  // ── 스페이스바 손바닥 끌기(Hold-to-Pan) 상태 ──
  const [isSpacePressed, setIsSpacePressed] = useState(false)
  const [isSpaceDragging, setIsSpaceDragging] = useState(false)
  const panStartRef = useRef<{ x: number; y: number; scrollLeft: number; scrollTop: number } | null>(null)

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null
      const isInput =
        target &&
        (target.tagName === 'INPUT' ||
          target.tagName === 'TEXTAREA' ||
          target.isContentEditable)
      if (isInput) return

      if (e.code === 'Space' && !e.repeat && !isSpacePressed) {
        e.preventDefault()
        setIsSpacePressed(true)
      }
    }

    const onKeyUp = (e: KeyboardEvent) => {
      if (e.code === 'Space') {
        setIsSpacePressed(false)
        setIsSpaceDragging(false)
        panStartRef.current = null
      }
    }

    window.addEventListener('keydown', onKeyDown)
    window.addEventListener('keyup', onKeyUp)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('keyup', onKeyUp)
    }
  }, [isSpacePressed])

  const handlePanMouseDown = (e: React.MouseEvent) => {
    if (!isSpacePressed || !containerRef.current) return
    e.preventDefault()
    setIsSpaceDragging(true)
    panStartRef.current = {
      x: e.clientX,
      y: e.clientY,
      scrollLeft: containerRef.current.scrollLeft,
      scrollTop: containerRef.current.scrollTop
    }

    const onMouseMove = (moveEvent: MouseEvent) => {
      if (!panStartRef.current || !containerRef.current) return
      const dx = moveEvent.clientX - panStartRef.current.x
      const dy = moveEvent.clientY - panStartRef.current.y
      containerRef.current.scrollLeft = panStartRef.current.scrollLeft - dx
      containerRef.current.scrollTop = panStartRef.current.scrollTop - dy
    }

    const onMouseUp = () => {
      setIsSpaceDragging(false)
      panStartRef.current = null
      window.removeEventListener('mousemove', onMouseMove)
      window.removeEventListener('mouseup', onMouseUp)
    }

    window.addEventListener('mousemove', onMouseMove)
    window.addEventListener('mouseup', onMouseUp)
  }

  // 단일 페이지 모드: 페이지나 줌 변경 시 상단으로 스크롤
  useEffect(() => {
    if (viewMode === 'single' && containerRef.current && doc) {
      containerRef.current.scrollTop = 0
    }
  }, [currentPage, zoom, doc, viewMode])

  // 연속/양면 스크롤 모드: currentPage 가 썸네일/목차 클릭으로 변경되었을 때 해당 위치로 부드럽게 점프
  useEffect(() => {
    if (viewMode === 'single' || !containerRef.current) return
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

  // 연속/양면 스크롤 모드: 스크롤 시 화면 중심에 있는 페이지 번호 실시간 감지 (썸네일/상태표시줄과 동기화)
  useEffect(() => {
    if (viewMode === 'single') return
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

          {/* 최근 열어본 문서 목록 */}
          {recentFiles.length > 0 && (
            <div
              className="viewer__recent-section"
              style={{
                marginTop: 28,
                width: '100%',
                maxWidth: 520,
                textAlign: 'left'
              }}
            >
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  marginBottom: 10,
                  padding: '0 4px'
                }}
              >
                <span
                  style={{
                    fontSize: 13,
                    fontWeight: 600,
                    color: 'var(--color-text)'
                  }}
                >
                  🕒 최근 열어본 문서 ({recentFiles.length})
                </span>
                <button
                  type="button"
                  onClick={() => {
                    clearRecentFiles()
                    setRecentFiles([])
                  }}
                  style={{
                    background: 'transparent',
                    border: 'none',
                    color: 'var(--color-text-muted)',
                    fontSize: 11,
                    cursor: 'pointer',
                    padding: '2px 6px',
                    borderRadius: 3
                  }}
                  title="최근 문서 목록 전체 지우기"
                >
                  목록 비우기
                </button>
              </div>

              <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                {recentFiles.map((file) => (
                  <div
                    key={file.filePath}
                    onClick={() => void openFileByPath(file.filePath)}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      padding: '10px 14px',
                      borderRadius: 6,
                      background: 'var(--color-bg-elevated)',
                      border: '1px solid var(--color-border)',
                      cursor: 'pointer',
                      transition: 'background 0.15s ease'
                    }}
                    title={`${file.filePath}\n클릭하여 바로 열기`}
                  >
                    <div
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 12,
                        minWidth: 0,
                        flex: 1
                      }}
                    >
                      <span style={{ fontSize: 20 }}>📄</span>
                      <div style={{ minWidth: 0, flex: 1 }}>
                        <div
                          style={{
                            fontSize: 13,
                            fontWeight: 600,
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                            whiteSpace: 'nowrap',
                            color: 'var(--color-text)'
                          }}
                        >
                          {file.fileName}
                        </div>
                        <div
                          style={{
                            fontSize: 11,
                            color: 'var(--color-text-muted)',
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                            whiteSpace: 'nowrap',
                            marginTop: 2
                          }}
                        >
                          {file.pageCount} 페이지 • {file.filePath}
                        </div>
                      </div>
                    </div>

                    <button
                      type="button"
                      onClick={(e) => {
                        e.stopPropagation()
                        removeRecentFile(file.filePath)
                        setRecentFiles(getRecentFiles())
                      }}
                      style={{
                        background: 'transparent',
                        border: 'none',
                        color: 'var(--color-text-muted)',
                        fontSize: 16,
                        cursor: 'pointer',
                        padding: '0 6px',
                        marginLeft: 8,
                        borderRadius: 4
                      }}
                      title="최근 목록에서 제거"
                    >
                      ✕
                    </button>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      </main>
    )
  }

  // ── 연속 스크롤 / 양면 보기(Two-Page) 모드 ──
  if (viewMode === 'continuous' || viewMode === 'two-page') {
    const isTwoPage = viewMode === 'two-page'
    return (
      <main
        className={`viewer ${isTwoPage ? 'viewer--two-page' : 'viewer--continuous'}`}
        ref={containerRef}
        style={isSpacePressed ? { cursor: isSpaceDragging ? 'grabbing' : 'grab' } : undefined}
        onMouseDown={handlePanMouseDown}
      >
        <SearchBar />
        {isLoading && (
          <div className="loading-overlay">
            <div className="loading-overlay__spinner" />
            <div>로드 중...</div>
          </div>
        )}
        <div
          className="viewer__continuous-list"
          style={
            isTwoPage
              ? {
                  display: 'grid',
                  gridTemplateColumns: 'repeat(2, max-content)',
                  justifyContent: 'center',
                  gap: '24px',
                  alignItems: 'start',
                  padding: '24px 16px'
                }
              : undefined
          }
        >
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
    <main
      className="viewer"
      ref={containerRef}
      style={isSpacePressed ? { cursor: isSpaceDragging ? 'grabbing' : 'grab' } : undefined}
      onMouseDown={handlePanMouseDown}
    >
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
        <PageSurface index={currentPage} zoom={zoom} rotation={rotation} />
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
        <PageSurface index={index} zoom={zoom} rotation={virtualPage.rotation} />
      ) : (
        <div className="viewer__page-placeholder">
          <span>{index + 1} 페이지</span>
        </div>
      )}
    </div>
  )
})

function PageSurface({ index, zoom, rotation }: { index: number; zoom: number; rotation: number }) {
  const page = useStore(s => s.pages[index])
  if (page?.cropMargins || page?.normalizedConfig) return <ProcessedPageView pageIndex={index} zoom={zoom} />
  return <><PageCanvas pageIndex={index} zoom={zoom} rotation={rotation} /><SearchHighlightLayer pageIndex={index} zoom={zoom} /><TextEditLayer pageIndex={index} zoom={zoom} rotation={rotation} /><AnnotationLayer pageIndex={index} zoom={zoom} rotation={rotation} /></>
}
