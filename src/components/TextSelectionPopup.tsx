/**
 * 어도비 아크로뱃 스타일 텍스트 선택 플로팅 툴바 (Floating Text Selection Popup Toolbar)
 * - 사용자가 본문 텍스트를 드래그 선택하면 선택 영역 상단에 다크 필(Pill) 툴바 자동 노출
 * - [🖍️ 형광펜]: 텍스트 행 단위 정밀 스냅 하이라이트 주석 자동 생성
 * - [〰️ 밑줄]: 텍스트 하단 정밀 밑줄 주석 자동 생성
 * - [̶S̶ 취소선]: 텍스트 중앙 관통 취소선 주석 자동 생성
 * - [📋 복사]: 클립보드에 선택 텍스트 복사
 */
import { useEffect, useState, useRef } from 'react'
import { useStore } from '../lib/store'
import type { Annotation } from '../../electron/shared/types'

interface RectBox {
  left: number
  top: number
  width: number
  height: number
  right: number
  bottom: number
}

interface SelectionState {
  text: string
  lines: RectBox[]
  popupX: number
  popupY: number
}

interface Props {
  pageIndex: number
  zoom: number
  containerRef: React.RefObject<HTMLDivElement>
}

/** 같은 텍스트 라인에 있는 사각형들을 행(Line) 단위로 병합 */
function mergeLineRects(rects: DOMRect[]): RectBox[] {
  if (rects.length === 0) return []
  const sorted = [...rects].sort((a, b) => {
    const dy = a.top - b.top
    if (Math.abs(dy) > 4) return dy
    return a.left - b.left
  })

  const lines: RectBox[] = []
  let cur: RectBox | null = null

  for (const r of sorted) {
    if (r.width < 1 || r.height < 1) continue
    if (!cur) {
      cur = {
        left: r.left,
        top: r.top,
        width: r.width,
        height: r.height,
        right: r.right,
        bottom: r.bottom
      }
      lines.push(cur)
      continue
    }

    const yOverlap = Math.min(cur.bottom, r.bottom) - Math.max(cur.top, r.top)
    const isSameLine = yOverlap > 0 || Math.abs(cur.top - r.top) < 4

    if (isSameLine) {
      cur.left = Math.min(cur.left, r.left)
      cur.top = Math.min(cur.top, r.top)
      cur.right = Math.max(cur.right, r.right)
      cur.bottom = Math.max(cur.bottom, r.bottom)
      cur.width = cur.right - cur.left
      cur.height = cur.bottom - cur.top
    } else {
      cur = {
        left: r.left,
        top: r.top,
        width: r.width,
        height: r.height,
        right: r.right,
        bottom: r.bottom
      }
      lines.push(cur)
    }
  }

  return lines
}

export function TextSelectionPopup({ pageIndex, zoom, containerRef }: Props) {
  const [selection, setSelection] = useState<SelectionState | null>(null)
  const popupRef = useRef<HTMLDivElement>(null)

  const toolColor = useStore((s) => s.toolColor)
  const addAnnotation = useStore((s) => s.addAnnotation)

  useEffect(() => {
    const onSelectionChange = () => {
      const sel = window.getSelection()
      if (!sel || sel.isCollapsed || sel.rangeCount === 0) {
        setSelection(null)
        return
      }

      const text = sel.toString().trim()
      if (!text) {
        setSelection(null)
        return
      }

      const container = containerRef.current
      if (!container) return

      const range = sel.getRangeAt(0)
      // 선택 영역이 현재 페이지 컨테이너 내부인지 확인
      if (!container.contains(range.commonAncestorContainer)) {
        setSelection(null)
        return
      }

      const clientRects = Array.from(range.getClientRects())
      if (clientRects.length === 0) {
        setSelection(null)
        return
      }

      const lines = mergeLineRects(clientRects)
      if (lines.length === 0) {
        setSelection(null)
        return
      }

      const containerRect = container.getBoundingClientRect()
      // 전체 선택 영역의 첫 행 상단 중앙에 팝업 배치
      const firstLine = lines[0]
      const popupX = Math.max(
        10,
        firstLine.left + firstLine.width / 2 - containerRect.left
      )
      const popupY = Math.max(8, firstLine.top - containerRect.top - 46)

      setSelection({
        text,
        lines,
        popupX,
        popupY
      })
    }

    const onMouseUp = () => {
      // 텍스트 선택이 끝난 직후 측정
      setTimeout(onSelectionChange, 10)
    }

    document.addEventListener('mouseup', onMouseUp)
    return () => {
      document.removeEventListener('mouseup', onMouseUp)
    }
  }, [containerRef])

  if (!selection) return null

  const clearSelection = () => {
    window.getSelection()?.removeAllRanges()
    setSelection(null)
  }

  // 1. 🖍️ 형광펜 주석 생성
  const handleHighlight = () => {
    const container = containerRef.current
    if (!container) return
    const cRect = container.getBoundingClientRect()

    for (const line of selection.lines) {
      const ann: Annotation = {
        id: `ann-hl-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
        type: 'highlight',
        pageIndex,
        x: (line.left - cRect.left) / zoom,
        y: (line.top - cRect.top) / zoom,
        width: line.width / zoom,
        height: line.height / zoom,
        color: toolColor || '#ffeb3b',
        opacity: 0.35
      }
      addAnnotation(ann)
    }
    clearSelection()
  }

  // 2. 〰️ 밑줄 주석 생성
  const handleUnderline = () => {
    const container = containerRef.current
    if (!container) return
    const cRect = container.getBoundingClientRect()
    const lineThickness = 2

    for (const line of selection.lines) {
      const ann: Annotation = {
        id: `ann-ul-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
        type: 'rectangle',
        pageIndex,
        x: (line.left - cRect.left) / zoom,
        y: (line.bottom - cRect.top) / zoom - lineThickness,
        width: line.width / zoom,
        height: lineThickness,
        color: toolColor === '#ffeb3b' ? '#0e6fd6' : toolColor,
        strokeWidth: lineThickness,
        opacity: 1
      }
      addAnnotation(ann)
    }
    clearSelection()
  }

  // 3. ̶S̶ 취소선 주석 생성
  const handleStrikethrough = () => {
    const container = containerRef.current
    if (!container) return
    const cRect = container.getBoundingClientRect()
    const lineThickness = 2

    for (const line of selection.lines) {
      const ann: Annotation = {
        id: `ann-st-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
        type: 'rectangle',
        pageIndex,
        x: (line.left - cRect.left) / zoom,
        y: (line.top + line.height / 2 - cRect.top) / zoom - lineThickness / 2,
        width: line.width / zoom,
        height: lineThickness,
        color: '#e5534b',
        strokeWidth: lineThickness,
        opacity: 1
      }
      addAnnotation(ann)
    }
    clearSelection()
  }

  // 4. 📋 복사
  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(selection.text)
    } catch {
      /* noop */
    }
    clearSelection()
  }

  return (
    <div
      ref={popupRef}
      className="text-selection-popup"
      onMouseDown={(e) => e.stopPropagation()}
      style={{
        position: 'absolute',
        left: selection.popupX,
        top: selection.popupY,
        transform: 'translateX(-50%)',
        zIndex: 50,
        display: 'flex',
        alignItems: 'center',
        gap: 4,
        padding: '3px 6px',
        backgroundColor: '#1e1e1e',
        color: '#ffffff',
        borderRadius: 20,
        boxShadow: '0 6px 20px rgba(0, 0, 0, 0.4), 0 0 0 1px rgba(255, 255, 255, 0.15)',
        fontSize: 12,
        userSelect: 'none',
        animation: 'fadeInPopup 0.15s ease-out'
      }}
    >
      <button
        type="button"
        onClick={handleHighlight}
        style={btnStyle}
        title="선택한 텍스트에 형광펜 칠하기"
      >
        🖍️ 형광펜
      </button>

      <button
        type="button"
        onClick={handleUnderline}
        style={btnStyle}
        title="선택한 텍스트 하단에 밑줄 긋기"
      >
        〰️ 밑줄
      </button>

      <button
        type="button"
        onClick={handleStrikethrough}
        style={btnStyle}
        title="선택한 텍스트에 취소선 긋기"
      >
        ̶S̶ 취소선
      </button>

      <div style={{ width: 1, height: 16, background: 'rgba(255, 255, 255, 0.2)', margin: '0 2px' }} />

      <button
        type="button"
        onClick={handleCopy}
        style={btnStyle}
        title="선택 텍스트 복사 (Ctrl+C)"
      >
        📋 복사
      </button>
    </div>
  )
}

const btnStyle: React.CSSProperties = {
  background: 'transparent',
  border: 'none',
  color: '#ffffff',
  padding: '4px 8px',
  borderRadius: 14,
  cursor: 'pointer',
  fontSize: 12,
  fontWeight: 500,
  display: 'flex',
  alignItems: 'center',
  gap: 4,
  transition: 'background 0.12s ease',
  whiteSpace: 'nowrap'
}
