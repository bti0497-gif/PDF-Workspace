/**
 * 오른쪽 메인 뷰어 패널
 * - PDF 가 없으면 빈 상태(열기 버튼) 표시
 * - 있으면 PageCanvas + TextLayer + AnnotationLayer 중첩
 */
import { useRef, useEffect } from 'react'
import { useStore } from '../lib/store'
import { PageCanvas } from './PageCanvas'
import { AnnotationLayer } from './AnnotationLayer'
import { clearRenderCache } from '../hooks/usePdfRender'

interface Props {
  onOpenFile: () => Promise<unknown>
}

export function ViewerPanel({ onOpenFile }: Props) {
  const doc = useStore((s) => s.document)
  const isLoading = useStore((s) => s.isLoading)
  const currentPage = useStore((s) => s.currentPage)
  const zoom = useStore((s) => s.zoom)
  const containerRef = useRef<HTMLDivElement>(null)

  // 페이지나 줌 변경 시 상단으로 스크롤
  useEffect(() => {
    if (containerRef.current && doc) {
      containerRef.current.scrollTop = 0
    }
  }, [currentPage, zoom, doc])

  // 키보드 단축키 (페이지 이동)
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!doc) return
      const target = e.target as HTMLElement
      // 입력 필드에서는 무시
      if (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA') return
      const s = useStore.getState()
      if (e.key === 'Home') s.setCurrentPage(0)
      else if (e.key === 'End') s.setCurrentPage(doc.pageCount - 1)
      else if (e.key === 'PageUp') {
        if (e.shiftKey) s.setCurrentPage(Math.max(0, s.currentPage - 1))
      } else if (e.key === 'PageDown') {
        if (e.shiftKey) s.setCurrentPage(Math.min(doc.pageCount - 1, s.currentPage + 1))
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [doc])

  // 문서가 바뀌면 렌더 캐시 비움
  useEffect(() => {
    if (doc) clearRenderCache()
  }, [doc?.filePath, doc?.fileName])

  if (!doc) {
    return (
      <main className="viewer" ref={containerRef}>
        <div className="viewer__empty">
          <div className="viewer__empty-icon">📄</div>
          <div className="viewer__empty-title">PDF Workspace</div>
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

  const rotation = doc.rotations[currentPage] ?? 0

  return (
    <main className="viewer" ref={containerRef}>
      {isLoading && (
        <div className="loading-overlay">
          <div className="loading-overlay__spinner" />
          <div>로드 중...</div>
        </div>
      )}
      <div className="viewer__page-wrapper" data-page={currentPage}>
        <PageCanvas
          pageIndex={currentPage}
          zoom={zoom}
          rotation={rotation}
        />
        <AnnotationLayer
          pageIndex={currentPage}
          zoom={zoom}
          rotation={rotation}
        />
      </div>
    </main>
  )
}
