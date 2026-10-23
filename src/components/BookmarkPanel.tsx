/**
 * 북마크(목차 / Outline) 계층 트리 패널
 */
import { useState } from 'react'
import { useStore } from '../lib/store'
import type { BookmarkNode } from '../core/domain/bookmark'

export function BookmarkPanel() {
  const bookmarks = useStore((s) => s.bookmarks)
  const currentPage = useStore((s) => s.currentPage)
  const setCurrentPage = useStore((s) => s.setCurrentPage)

  if (bookmarks.length === 0) {
    return (
      <div className="bookmark-tree__empty">
        <div>📑</div>
        <div style={{ marginTop: 8 }}>문서에 포함된 목차가 없습니다.</div>
      </div>
    )
  }

  return (
    <div className="bookmark-tree">
      {bookmarks.map((bm) => (
        <BookmarkItemView
          key={bm.id}
          node={bm}
          currentPage={currentPage}
          onSelect={(pageIndex) => setCurrentPage(pageIndex)}
          depth={0}
        />
      ))}
    </div>
  )
}

interface ItemProps {
  node: BookmarkNode
  currentPage: number
  onSelect: (pageIndex: number) => void
  depth: number
}

function BookmarkItemView({ node, currentPage, onSelect, depth }: ItemProps) {
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
      </div>

      {hasChildren && expanded && (
        <div>
          {node.children.map((child) => (
            <BookmarkItemView
              key={child.id}
              node={child}
              currentPage={currentPage}
              onSelect={onSelect}
              depth={depth + 1}
            />
          ))}
        </div>
      )}
    </div>
  )
}
