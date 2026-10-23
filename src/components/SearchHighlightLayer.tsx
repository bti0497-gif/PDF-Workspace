/**
 * 검색어 하이라이트 오버레이 레이어
 */
import { memo } from 'react'
import { useStore } from '../lib/store'

interface Props {
  pageIndex: number
  zoom: number
}

export const SearchHighlightLayer = memo(function SearchHighlightLayer({
  pageIndex,
  zoom
}: Props) {
  const searchMatches = useStore((s) => s.searchMatches)
  const activeMatchIndex = useStore((s) => s.activeMatchIndex)

  if (searchMatches.length === 0) return null

  const pageMatches = searchMatches
    .map((m, originalIndex) => ({ ...m, originalIndex }))
    .filter((m) => m.pageIndex === pageIndex)

  if (pageMatches.length === 0) return null

  return (
    <div className="search-highlight-layer">
      {pageMatches.map((m) => {
        const isActive = m.originalIndex === activeMatchIndex
        return (
          <div
            key={m.originalIndex}
            className={`search-highlight ${isActive ? 'search-highlight--active' : ''}`}
            style={{
              left: `${Math.round(m.x * zoom)}px`,
              top: `${Math.round(m.y * zoom)}px`,
              width: `${Math.round(m.width * zoom)}px`,
              height: `${Math.round(m.height * zoom)}px`
            }}
          />
        )
      })}
    </div>
  )
})
