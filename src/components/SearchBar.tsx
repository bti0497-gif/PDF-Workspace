/**
 * 문서 내 텍스트 검색 팝업 바 (Ctrl+F)
 */
import { useEffect, useRef, useState, useCallback } from 'react'
import { useStore } from '../lib/store'
import { searchInDocument } from '../core/services/search.service'

export function SearchBar() {
  const visible = useStore((s) => s.searchBarVisible)
  const searchQuery = useStore((s) => s.searchQuery)
  const searchMatches = useStore((s) => s.searchMatches)
  const activeMatchIndex = useStore((s) => s.activeMatchIndex)
  const pages = useStore((s) => s.pages)

  const openSearchBar = useStore((s) => s.openSearchBar)
  const closeSearchBar = useStore((s) => s.closeSearchBar)
  const setSearchQuery = useStore((s) => s.setSearchQuery)
  const setSearchMatches = useStore((s) => s.setSearchMatches)
  const nextSearchMatch = useStore((s) => s.nextSearchMatch)
  const prevSearchMatch = useStore((s) => s.prevSearchMatch)

  const inputRef = useRef<HTMLInputElement>(null)
  const [isSearching, setIsSearching] = useState(false)

  // 전역 Ctrl+F 단축키 바인딩
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'f') {
        e.preventDefault()
        openSearchBar()
        setTimeout(() => {
          inputRef.current?.focus()
          inputRef.current?.select()
        }, 50)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [openSearchBar])

  // 검색창 열릴 때 자동 포커스
  useEffect(() => {
    if (visible) {
      inputRef.current?.focus()
      inputRef.current?.select()
    }
  }, [visible])

  // 검색 실행 (디바운스 250ms)
  const doSearch = useCallback(
    async (q: string) => {
      if (!q.trim()) {
        setSearchMatches([])
        setIsSearching(false)
        return
      }
      setIsSearching(true)
      try {
        const matches = await searchInDocument(pages, q)
        setSearchMatches(matches)
      } finally {
        setIsSearching(false)
      }
    },
    [pages, setSearchMatches]
  )

  useEffect(() => {
    const timer = setTimeout(() => {
      if (visible && searchQuery) {
        void doSearch(searchQuery)
      }
    }, 250)
    return () => clearTimeout(timer)
  }, [searchQuery, visible, doSearch])

  if (!visible) return null

  const matchCount = searchMatches.length
  let statusText = ''
  if (isSearching) {
    statusText = '검색 중...'
  } else if (!searchQuery.trim()) {
    statusText = ''
  } else if (matchCount === 0) {
    statusText = '결과 없음'
  } else {
    statusText = `${activeMatchIndex + 1} / ${matchCount}`
  }

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape') {
      e.preventDefault()
      closeSearchBar()
    } else if (e.key === 'Enter') {
      e.preventDefault()
      if (e.shiftKey) {
        prevSearchMatch()
      } else {
        nextSearchMatch()
      }
    }
  }

  return (
    <div className="search-bar">
      <span>🔍</span>
      <input
        ref={inputRef}
        className="search-bar__input"
        type="text"
        placeholder="문서 내 검색..."
        value={searchQuery}
        onChange={(e) => setSearchQuery(e.target.value)}
        onKeyDown={handleKeyDown}
      />
      {statusText && <span className="search-bar__count">{statusText}</span>}
      <button
        className="search-bar__btn"
        title="이전 결과 (Shift+Enter)"
        onClick={prevSearchMatch}
        disabled={matchCount === 0}
      >
        ▲
      </button>
      <button
        className="search-bar__btn"
        title="다음 결과 (Enter)"
        onClick={nextSearchMatch}
        disabled={matchCount === 0}
      >
        ▼
      </button>
      <button
        className="search-bar__btn search-bar__btn--close"
        title="닫기 (Esc)"
        onClick={closeSearchBar}
      >
        ✕
      </button>
    </div>
  )
}
