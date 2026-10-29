/**
 * 하단 상태 표시줄 - 페이지 정보, 선택 개수, 줌, 파일명
 */
import { useStore } from '../lib/store'

export function StatusBar() {
  const doc = useStore((s) => s.document)
  const pages = useStore((s) => s.pages)
  const selectedPageIndices = useStore((s) => s.selectedPageIndices)
  const currentPage = useStore((s) => s.currentPage)
  const setCurrentPage = useStore((s) => s.setCurrentPage)
  const zoom = useStore((s) => s.zoom)
  const isDirty = useStore((s) => s.isDirty)
  const activeTool = useStore((s) => s.activeTool)

  const pageCount = pages.length > 0 ? pages.length : doc?.pageCount ?? 0

  const toolLabels: Record<string, string> = {
    select: '선택',
    'text-edit': '본문 편집',
    highlight: '형광펜',
    rectangle: '사각형',
    textbox: '텍스트',
    drawing: '그리기',
    stamp: '도장/서명'
  }

  return (
    <footer className="statusbar">
      <div className="statusbar__item">
        {doc ? (
          <>
            <input
              className="statusbar__page-input"
              type="number"
              min={1}
              max={pageCount}
              value={currentPage + 1}
              onChange={(e) => {
                const v = parseInt(e.target.value, 10)
                if (!isNaN(v)) setCurrentPage(Math.max(0, Math.min(pageCount - 1, v - 1)))
              }}
            />
            <span>/ {pageCount} 페이지</span>
            {selectedPageIndices.length > 1 && (
              <span style={{ marginLeft: 8, color: 'var(--color-accent)' }}>
                ({selectedPageIndices.length}개 선택됨)
              </span>
            )}
          </>
        ) : (
          <span>문서 없음</span>
        )}
      </div>

      <div className="statusbar__item">{Math.round(zoom * 100)}%</div>

      {doc && (
        <>
          <div className="statusbar__item">도구: {toolLabels[activeTool]}</div>
          <div className="statusbar__spacer" />
          <div className="statusbar__item" title={doc.filePath ?? '저장되지 않음'}>
            {doc.fileName}
          </div>
          {isDirty && <span className="statusbar__dirty">● 저장되지 않은 변경</span>}
        </>
      )}
      {!doc && <div className="statusbar__spacer" />}
    </footer>
  )
}
