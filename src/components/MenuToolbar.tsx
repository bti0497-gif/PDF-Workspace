/**
 * 상단 툴바 - 자주 쓰는 동작을 버튼으로 노출
 */
import { useStore } from '../lib/store'
import { usePdfDocument } from '../hooks/usePdfDocument'
import type { ToolType } from '../lib/store'

interface Props {
  onOpenModal: (modal: 'merge' | 'split' | 'about' | 'shortcuts') => void
}

const TOOLS: { id: ToolType; label: string; icon: string }[] = [
  { id: 'select', label: '선택', icon: '▦' },
  { id: 'highlight', label: '형광펜', icon: '█' },
  { id: 'rectangle', label: '사각형', icon: '▭' },
  { id: 'textbox', label: '텍스트', icon: 'T' },
  { id: 'drawing', label: '그리기', icon: '✎' }
]

export function MenuToolbar({ onOpenModal }: Props) {
  const doc = useStore((s) => s.document)
  const activeTool = useStore((s) => s.activeTool)
  const toolColor = useStore((s) => s.toolColor)
  const zoom = useStore((s) => s.zoom)
  const thumbnailsVisible = useStore((s) => s.thumbnailsVisible)
  const isDirty = useStore((s) => s.isDirty)

  const setActiveTool = useStore((s) => s.setActiveTool)
  const setToolColor = useStore((s) => s.setToolColor)
  const zoomIn = useStore((s) => s.zoomIn)
  const zoomOut = useStore((s) => s.zoomOut)
  const resetZoom = useStore((s) => s.resetZoom)
  const toggleThumbnails = useStore((s) => s.toggleThumbnails)
  const toggleTheme = useStore((s) => s.toggleTheme)
  const { openFile } = usePdfDocument()

  const handleSave = async () => {
    // 저장은 메뉴 이벤트 경유
    window.dispatchEvent(new CustomEvent('trigger-save'))
  }

  return (
    <div className="toolbar">
      {/* 열기 / 저장 */}
      <div className="toolbar__group">
        <button
          className="toolbar__btn"
          title="열기 (Ctrl+O)"
          onClick={() => void openFile()}
        >
          📂 열기
        </button>
        <button
          className="toolbar__btn"
          title="저장 (Ctrl+S)"
          onClick={() => void handleSave()}
          disabled={!doc}
        >
          💾 저장
        </button>
      </div>

      <div className="toolbar__divider" />

      {/* 페이지 이동 */}
      <div className="toolbar__group">
        <NavButton dir="prev" />
        <NavButton dir="next" />
      </div>

      <div className="toolbar__divider" />

      {/* 줌 */}
      <div className="toolbar__group toolbar__zoom">
        <button
          className="toolbar__btn toolbar__btn--icon"
          title="축소 (Ctrl+-)"
          onClick={zoomOut}
          disabled={!doc}
        >
          －
        </button>
        <button
          className="toolbar__btn toolbar__zoom-value"
          title="실제 크기 (Ctrl+0)"
          onClick={resetZoom}
          disabled={!doc}
        >
          {Math.round(zoom * 100)}%
        </button>
        <button
          className="toolbar__btn toolbar__btn--icon"
          title="확대 (Ctrl+=)"
          onClick={zoomIn}
          disabled={!doc}
        >
          ＋
        </button>
      </div>

      <div className="toolbar__divider" />

      {/* 주석 도구 */}
      <div className="toolbar__group">
        {TOOLS.map((t) => (
          <button
            key={t.id}
            className={`toolbar__btn ${activeTool === t.id ? 'toolbar__btn--active' : ''}`}
            title={t.label}
            onClick={() => setActiveTool(t.id)}
            disabled={!doc}
          >
            <span aria-hidden>{t.icon}</span> {t.label}
          </button>
        ))}
        <input
          className="toolbar__color-input"
          type="color"
          value={toolColor}
          onChange={(e) => setToolColor(e.target.value)}
          title="주석 색상"
        />
      </div>

      <div className="toolbar__spacer" />

      {/* 파일명 */}
      {doc && (
        <span className="toolbar__title" title={doc.filePath ?? '저장되지 않음'}>
          {isDirty ? '● ' : ''}
          {doc.fileName}
        </span>
      )}

      <div className="toolbar__divider" />

      {/* 보기 토글 */}
      <div className="toolbar__group">
        <button
          className={`toolbar__btn ${thumbnailsVisible ? 'toolbar__btn--active' : ''}`}
          title="썸네일 패널 (F4)"
          onClick={toggleThumbnails}
        >
          ▥ 패널
        </button>
        <button
          className="toolbar__btn"
          title="다크 모드 전환 (Ctrl+Shift+D)"
          onClick={toggleTheme}
        >
          🌓 테마
        </button>
        <button
          className="toolbar__btn"
          title="PDF 합치기"
          onClick={() => onOpenModal('merge')}
        >
          ⊕ 합치기
        </button>
        <button
          className="toolbar__btn"
          title="PDF 나누기"
          onClick={() => onOpenModal('split')}
          disabled={!doc}
        >
          ⊘ 나누기
        </button>
      </div>
    </div>
  )
}

/** 페이지 이동 버튼 */
function NavButton({ dir }: { dir: 'prev' | 'next' }) {
  const doc = useStore((s) => s.document)
  const currentPage = useStore((s) => s.currentPage)
  const setCurrentPage = useStore((s) => s.setCurrentPage)
  const disabled =
    !doc || (dir === 'prev' ? currentPage === 0 : currentPage >= doc.pageCount - 1)
  return (
    <button
      className="toolbar__btn toolbar__btn--icon"
      title={dir === 'prev' ? '이전 페이지 (←)' : '다음 페이지 (→)'}
      disabled={disabled}
      onClick={() =>
        setCurrentPage(dir === 'prev' ? currentPage - 1 : currentPage + 1)
      }
    >
      {dir === 'prev' ? '◀' : '▶'}
    </button>
  )
}
