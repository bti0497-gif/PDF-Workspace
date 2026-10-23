/**
 * 주석 오버레이 레이어 (고도화: 선택, 8방향 리사이즈 핸들, 드래그 이동, 복사/붙여넣기)
 */
import { useRef, useState, useEffect, useCallback } from 'react'
import { useStore } from '../lib/store'
import type { Annotation } from '../../electron/shared/types'

interface Props {
  pageIndex: number
  zoom: number
  rotation: number
}

type ResizeHandleType = 'nw' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w'

interface DragState {
  mode: 'create' | 'move' | 'resize'
  handleType?: ResizeHandleType
  startX: number
  startY: number
  currentX: number
  currentY: number
  points?: { x: number; y: number }[]
  initialAnn?: Annotation
}

export function AnnotationLayer({ pageIndex, zoom }: Props) {
  const layerRef = useRef<HTMLDivElement>(null)
  const [drag, setDrag] = useState<DragState | null>(null)
  const [draftText, setDraftText] = useState<{
    x: number
    y: number
    w: number
    h: number
  } | null>(null)
  const [textInput, setTextInput] = useState('')

  const activeTool = useStore((s) => s.activeTool)
  const toolColor = useStore((s) => s.toolColor)
  const annotations = useStore((s) => s.annotations)
  const addAnnotation = useStore((s) => s.addAnnotation)
  const updateAnnotation = useStore((s) => s.updateAnnotation)
  const removeAnnotation = useStore((s) => s.removeAnnotation)
  const selectAnnotation = useStore((s) => s.selectAnnotation)
  const selectedAnnotationId = useStore((s) => s.selectedAnnotationId)
  const copySelectedAnnotation = useStore((s) => s.copySelectedAnnotation)
  const pasteAnnotation = useStore((s) => s.pasteAnnotation)

  const pageAnnotations = annotations.filter((a) => a.pageIndex === pageIndex)
  const selectedAnn = pageAnnotations.find((a) => a.id === selectedAnnotationId)

  const isDrawingTool = activeTool !== 'select'

  /** 마우스 위치를 레이어 내 로컬 좌표로 변환 */
  const getLocalPos = useCallback((e: React.MouseEvent | MouseEvent) => {
    const rect = layerRef.current?.getBoundingClientRect()
    if (!rect) return { x: 0, y: 0 }
    return {
      x: e.clientX - rect.left,
      y: e.clientY - rect.top
    }
  }, [])

  // ── 드래그 생성 / 이동 / 리사이즈 시작 ──
  const onMouseDown = (e: React.MouseEvent) => {
    const pos = getLocalPos(e)

    if (activeTool === 'textbox') {
      setDraftText({ x: pos.x, y: pos.y, w: 0, h: 0 })
      setTextInput('')
      return
    }

    if (activeTool !== 'select') {
      setDrag({
        mode: 'create',
        startX: pos.x,
        startY: pos.y,
        currentX: pos.x,
        currentY: pos.y,
        points: [{ x: pos.x, y: pos.y }]
      })
      return
    }

    // select 모드에서 빈 공간 클릭 시 선택 해제
    selectAnnotation(null)
  }

  // 주석 본체 드래그 시작 (이동)
  const handleStartMove = (e: React.MouseEvent, ann: Annotation) => {
    if (activeTool !== 'select') return
    e.stopPropagation()
    selectAnnotation(ann.id)
    const pos = getLocalPos(e)
    setDrag({
      mode: 'move',
      startX: pos.x,
      startY: pos.y,
      currentX: pos.x,
      currentY: pos.y,
      initialAnn: { ...ann }
    })
  }

  // 리사이즈 핸들 드래그 시작
  const handleStartResize = (
    e: React.MouseEvent,
    ann: Annotation,
    handle: ResizeHandleType
  ) => {
    if (activeTool !== 'select') return
    e.stopPropagation()
    const pos = getLocalPos(e)
    setDrag({
      mode: 'resize',
      handleType: handle,
      startX: pos.x,
      startY: pos.y,
      currentX: pos.x,
      currentY: pos.y,
      initialAnn: { ...ann }
    })
  }

  // ── 마우스 이동 ──
  const onMouseMove = (e: React.MouseEvent) => {
    if (!drag) return
    const pos = getLocalPos(e)

    if (drag.mode === 'create') {
      if (activeTool === 'drawing') {
        setDrag((d) =>
          d
            ? {
                ...d,
                currentX: pos.x,
                currentY: pos.y,
                points: [...(d.points || []), { x: pos.x, y: pos.y }]
              }
            : d
        )
      } else {
        setDrag((d) => (d ? { ...d, currentX: pos.x, currentY: pos.y } : d))
      }
      return
    }

    if (!drag.initialAnn) return
    const init = drag.initialAnn
    const dx = (pos.x - drag.startX) / zoom
    const dy = (pos.y - drag.startY) / zoom

    // 이동 처리
    if (drag.mode === 'move') {
      updateAnnotation(init.id, {
        x: Math.max(0, init.x + dx),
        y: Math.max(0, init.y + dy)
      })
      return
    }

    // 8방향 리사이징 처리
    if (drag.mode === 'resize' && drag.handleType) {
      let nextX = init.x
      let nextY = init.y
      let nextW = init.width
      let nextH = init.height

      const h = drag.handleType
      if (h.includes('w')) {
        nextX = init.x + dx
        nextW = init.width - dx
      }
      if (h.includes('e')) {
        nextW = init.width + dx
      }
      if (h.includes('n')) {
        nextY = init.y + dy
        nextH = init.height - dy
      }
      if (h.includes('s')) {
        nextH = init.height + dy
      }

      // 최소 크기 제한 (8px)
      if (nextW > 8 && nextH > 8) {
        updateAnnotation(init.id, {
          x: nextX,
          y: nextY,
          width: nextW,
          height: nextH
        })
      }
    }
  }

  // ── 마우스 놓기 ──
  const onMouseUp = () => {
    if (!drag) return

    if (drag.mode === 'create') {
      const { startX, startY, currentX, currentY, points } = drag
      const x = Math.min(startX, currentX)
      const y = Math.min(startY, currentY)
      const w = Math.abs(currentX - startX)
      const h = Math.abs(currentY - startY)

      if (w >= 4 || h >= 4 || activeTool === 'drawing') {
        const id = `ann-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`
        const ann: Annotation = {
          id,
          type: activeTool as Annotation['type'],
          pageIndex,
          x: x / zoom,
          y: y / zoom,
          width: w / zoom,
          height: h / zoom,
          color: toolColor,
          strokeWidth: 2,
          points:
            activeTool === 'drawing'
              ? points?.map((p) => ({ x: p.x / zoom, y: p.y / zoom }))
              : undefined
        }
        addAnnotation(ann)
      }
    }

    setDrag(null)
  }

  // 텍스트 박스 커밋
  const commitTextbox = useCallback(() => {
    if (!draftText || !textInput.trim()) {
      setDraftText(null)
      setTextInput('')
      return
    }
    const id = `ann-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`
    const w = Math.max(120, draftText.w)
    const h = Math.max(28, draftText.h)
    addAnnotation({
      id,
      type: 'textbox',
      pageIndex,
      x: draftText.x / zoom,
      y: draftText.y / zoom,
      width: w / zoom,
      height: h / zoom,
      color: toolColor,
      text: textInput
    })
    setDraftText(null)
    setTextInput('')
  }, [draftText, textInput, addAnnotation, pageIndex, zoom, toolColor])

  // 키보드 단축키 (Delete, Ctrl+C, Ctrl+V)
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement
      if (
        target.tagName === 'INPUT' ||
        target.tagName === 'TEXTAREA' ||
        target.isContentEditable
      ) {
        return
      }

      if ((e.key === 'Delete' || e.key === 'Backspace') && selectedAnnotationId) {
        removeAnnotation(selectedAnnotationId)
      } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'c') {
        if (selectedAnnotationId) copySelectedAnnotation()
      } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'v') {
        pasteAnnotation()
      }
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [selectedAnnotationId, removeAnnotation, copySelectedAnnotation, pasteAnnotation])

  return (
    <div
      ref={layerRef}
      className={`annotation-layer ${isDrawingTool ? '' : 'annotation-layer--select'}`}
      onMouseDown={onMouseDown}
      onMouseMove={onMouseMove}
      onMouseUp={onMouseUp}
      onMouseLeave={onMouseUp}
    >
      {/* SVG 주석 렌더링 */}
      <svg
        className="annotation-svg"
        style={{
          position: 'absolute',
          inset: 0,
          pointerEvents: 'none',
          width: '100%',
          height: '100%'
        }}
      >
        {pageAnnotations.map((ann) => {
          const isSelected = ann.id === selectedAnnotationId
          const ax = ann.x * zoom
          const ay = ann.y * zoom
          const aw = ann.width * zoom
          const ah = ann.height * zoom

          return (
            <g
              key={ann.id}
              style={{
                pointerEvents: activeTool === 'select' ? 'auto' : 'none',
                cursor: activeTool === 'select' ? 'move' : 'crosshair'
              }}
              onMouseDown={(e) => handleStartMove(e, ann)}
            >
              {ann.type === 'highlight' && (
                <rect
                  x={ax}
                  y={ay}
                  width={aw}
                  height={ah}
                  fill={ann.color}
                  fillOpacity={0.35}
                />
              )}
              {ann.type === 'rectangle' && (
                <rect
                  x={ax}
                  y={ay}
                  width={aw}
                  height={ah}
                  fill="none"
                  stroke={ann.color}
                  strokeWidth={ann.strokeWidth ?? 2}
                />
              )}
              {ann.type === 'drawing' && ann.points && (
                <polyline
                  points={ann.points.map((p) => `${p.x * zoom},${p.y * zoom}`).join(' ')}
                  fill="none"
                  stroke={ann.color}
                  strokeWidth={ann.strokeWidth ?? 2}
                  strokeLinejoin="round"
                  strokeLinecap="round"
                />
              )}
              {isSelected && (
                <rect
                  x={ax - 3}
                  y={ay - 3}
                  width={aw + 6}
                  height={ah + 6}
                  fill="none"
                  stroke="var(--color-accent)"
                  strokeWidth={1.5}
                  strokeDasharray="4 3"
                />
              )}
            </g>
          )
        })}

        {/* 생성 중인 드래그 미리보기 */}
        {drag && drag.mode === 'create' && activeTool === 'highlight' && (
          <rect
            x={Math.min(drag.startX, drag.currentX)}
            y={Math.min(drag.startY, drag.currentY)}
            width={Math.abs(drag.currentX - drag.startX)}
            height={Math.abs(drag.currentY - drag.startY)}
            fill={toolColor}
            fillOpacity={0.35}
          />
        )}
        {drag && drag.mode === 'create' && activeTool === 'rectangle' && (
          <rect
            x={Math.min(drag.startX, drag.currentX)}
            y={Math.min(drag.startY, drag.currentY)}
            width={Math.abs(drag.currentX - drag.startX)}
            height={Math.abs(drag.currentY - drag.startY)}
            fill="none"
            stroke={toolColor}
            strokeWidth={2}
          />
        )}
        {drag && drag.mode === 'create' && activeTool === 'drawing' && drag.points && (
          <polyline
            points={drag.points.map((p) => `${p.x},${p.y}`).join(' ')}
            fill="none"
            stroke={toolColor}
            strokeWidth={2}
            strokeLinejoin="round"
            strokeLinecap="round"
          />
        )}
      </svg>

      {/* 텍스트 주석 DOM 렌더링 */}
      {pageAnnotations
        .filter((a) => a.type === 'textbox')
        .map((ann) => {
          const isSelected = ann.id === selectedAnnotationId
          return (
            <div
              key={ann.id}
              onMouseDown={(e) => handleStartMove(e, ann)}
              style={{
                position: 'absolute',
                left: ann.x * zoom,
                top: ann.y * zoom,
                width: ann.width * zoom,
                minHeight: ann.height * zoom,
                color: ann.color,
                fontSize: 14 * zoom,
                padding: 4 * zoom,
                border: `1px solid ${ann.color}`,
                pointerEvents: activeTool === 'select' ? 'auto' : 'none',
                cursor: activeTool === 'select' ? 'move' : 'crosshair',
                outline: isSelected ? '1.5px dashed var(--color-accent)' : 'none',
                background: 'rgba(255,255,255,0.9)',
                wordBreak: 'break-word',
                whiteSpace: 'pre-wrap',
                userSelect: 'none'
              }}
            >
              {ann.text}
            </div>
          )
        })}

      {/* 8방향 리사이즈 핸들 (선택 모드 & 주석 선택 시) */}
      {activeTool === 'select' && selectedAnn && selectedAnn.type !== 'drawing' && (
        <ResizeHandles
          ann={selectedAnn}
          zoom={zoom}
          onResizeStart={handleStartResize}
        />
      )}

      {/* 텍스트 박스 신규 입력창 */}
      {draftText && (
        <div
          style={{
            position: 'absolute',
            left: draftText.x,
            top: draftText.y,
            minWidth: 120
          }}
        >
          <textarea
            autoFocus
            value={textInput}
            onChange={(e) => setTextInput(e.target.value)}
            onBlur={commitTextbox}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault()
                commitTextbox()
              }
            }}
            placeholder="텍스트 입력 후 Enter..."
            style={{
              width: 180,
              minHeight: 28,
              fontSize: 14,
              padding: 4,
              color: toolColor,
              border: `1px solid ${toolColor}`,
              background: 'rgba(255,255,255,0.95)',
              resize: 'both',
              outline: 'none',
              fontFamily: 'inherit'
            }}
          />
        </div>
      )}
    </div>
  )
}

// ─────────────────────────────────────────────────────────────
// 8방향 리사이즈 핸들 컴포넌트
// ─────────────────────────────────────────────────────────────

interface ResizeHandlesProps {
  ann: Annotation
  zoom: number
  onResizeStart: (
    e: React.MouseEvent,
    ann: Annotation,
    handle: ResizeHandleType
  ) => void
}

function ResizeHandles({ ann, zoom, onResizeStart }: ResizeHandlesProps) {
  const ax = ann.x * zoom
  const ay = ann.y * zoom
  const aw = ann.width * zoom
  const ah = ann.height * zoom

  const handles: ResizeHandleType[] = [
    'nw',
    'n',
    'ne',
    'e',
    'se',
    's',
    'sw',
    'w'
  ]

  return (
    <div
      style={{
        position: 'absolute',
        left: ax,
        top: ay,
        width: aw,
        height: ah,
        pointerEvents: 'none'
      }}
    >
      {handles.map((h) => (
        <div
          key={h}
          className={`annotation-handle annotation-handle--${h}`}
          style={{ pointerEvents: 'auto' }}
          onMouseDown={(e) => onResizeStart(e, ann, h)}
        />
      ))}
    </div>
  )
}
