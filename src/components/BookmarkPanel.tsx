/**
 * 북마크(목차 / Outline) 계층 트리 패널 (인터랙티브 추가/삭제 완벽 지원)
 */
import { useState } from 'react'
import { useStore } from '../lib/store'
import type { BookmarkNode } from '../core/domain/bookmark'

export function BookmarkPanel() {
  const bookmarks = useStore((s) => s.bookmarks)
  const currentPage = useStore((s) => s.currentPage)
  const setCurrentPage = useStore((s) => s.setCurrentPage)
  const addBookmark = useStore((s) => s.addBookmark)
  const removeBookmark = useStore((s) => s.removeBookmark)

  const [newTitle, setNewTitle] = useState('')

  const handleAdd = () => {
    const title = newTitle.trim() || `${currentPage + 1} 페이지`
    addBookmark(title, currentPage)
    setNewTitle('')
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%', overflow: 'hidden' }}>
      {/* ── 상단 헤더: 현재 페이지 책갈피 추가 ── */}
      <div
        className="bookmark-panel__header"
        style={{
          padding: '8px 10px',
          borderBottom: '1px solid var(--color-border)',
          background: 'var(--color-bg-elevated)',
          display: 'flex',
          flexDirection: 'column',
          gap: 6
        }}
      >
        <div style={{ display: 'flex', gap: 4 }}>
          <input
            type="text"
            value={newTitle}
            onChange={(e) => setNewTitle(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') handleAdd()
            }}
            placeholder={`${currentPage + 1}p 책갈피 제목...`}
            style={{
              flex: 1,
              padding: '4px 6px',
              fontSize: 11,
              background: 'var(--color-bg)',
              border: '1px solid var(--color-border)',
              borderRadius: 3,
              color: 'inherit',
              outline: 'none'
            }}
          />
          <button
            type="button"
            className="btn btn--primary"
            onClick={handleAdd}
            style={{ padding: '3px 8px', fontSize: 11, whiteSpace: 'nowrap' }}
            title={`현재 ${currentPage + 1}페이지 책갈피 추가`}
          >
            + 추가
          </button>
        </div>
        <div style={{ fontSize: 10, color: 'var(--color-text-muted)' }}>
          현재 위치: <strong>{currentPage + 1} 페이지</strong>
        </div>
      </div>

      {/* ── 북마크 트리 본문 ── */}
      <div style={{ flex: 1, overflowY: 'auto', padding: '6px' }}>
        {bookmarks.length === 0 ? (
          <div className="bookmark-tree__empty">
            <div>📑</div>
            <div style={{ marginTop: 8 }}>등록된 책갈피(목차)가 없습니다.</div>
            <div style={{ fontSize: 11, marginTop: 4, opacity: 0.7 }}>
              상단 입력창을 통해 원하는 페이지에 책갈피를 추가해보세요.
            </div>
          </div>
        ) : (
          <div className="bookmark-tree">
            {bookmarks.map((bm) => (
              <BookmarkItemView
                key={bm.id}
                node={bm}
                currentPage={currentPage}
                onSelect={(pageIndex) => setCurrentPage(pageIndex)}
                onRemove={(id) => removeBookmark(id)}
                depth={0}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

interface ItemProps {
  node: BookmarkNode
  currentPage: number
  onSelect: (pageIndex: number) => void
  onRemove: (id: string) => void
  depth: number
}

function BookmarkItemView({
  node,
  currentPage,
  onSelect,
  onRemove,
  depth
}: ItemProps) {
  const [expanded, setExpanded] = useState(node.isExpanded ?? true)
  const hasChildren = node.children && node.children.length > 0
  const isActive = node.targetPageIndex !== null && node.targetPageIndex === currentPage

  const handleClick = (e: React.MouseEvent) => {
    e.stopPropagation()
    if (node.targetPageIndex !== null) {
      onSelect(node.targetPageIndex)
    } else if (hasChildren) {
      setExpanded((v) => !v)
    }
  }

  const handleToggle = (e: React.MouseEvent) => {
    e.stopPropagation()
    setExpanded((v) => !v)
  }

  return (
    <div>
      <div
        className={`bookmark-item ${isActive ? 'bookmark-item--active' : ''}`}
        style={{ paddingLeft: `${8 + depth * 14}px` }}
        onClick={handleClick}
        title={node.title}
      >
        {hasChildren ? (
          <span className="bookmark-item__toggle" onClick={handleToggle}>
            {expanded ? '▼' : '▶'}
          </span>
        ) : (
          <span className="bookmark-item__toggle" style={{ opacity: 0.2 }}>
            •
          </span>
        )}
        <span className="bookmark-item__title">{node.title}</span>
        {node.targetPageIndex !== null && (
          <span className="bookmark-item__page">p.{node.targetPageIndex + 1}</span>
        )}
        <button
          type="button"
          className="bookmark-item__delete"
          onClick={(e) => {
            e.stopPropagation()
            onRemove(node.id)
          }}
          title="책갈피 삭제"
          style={{
            background: 'none',
            border: 'none',
            color: 'inherit',
            opacity: 0.4,
            cursor: 'pointer',
            padding: '0 4px',
            marginLeft: 4,
            fontSize: 13,
            lineHeight: 1
          }}
          onMouseEnter={(e) => (e.currentTarget.style.opacity = '1')}
          onMouseLeave={(e) => (e.currentTarget.style.opacity = '0.4')}
        >
          ×
        </button>
      </div>

      {hasChildren && expanded && (
        <div>
          {node.children.map((child) => (
            <BookmarkItemView
              key={child.id}
              node={child}
              currentPage={currentPage}
              onSelect={onSelect}
              onRemove={onRemove}
              depth={depth + 1}
            />
          ))}
        </div>
      )}
    </div>
  )
}
