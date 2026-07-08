/**
 * 주석 오버레이 레이어
 * - 활성 도구에 따라 마우스 드래그로 주석 생성
 * - select 모드에서는 기존 주석 클릭/삭제
 * - 좌표는 화면 픽셀 -> PDF 포인트 변환하여 저장
 */
import { useRef, useState, useEffect, useCallback } from 'react'
import { useStore } from '../lib/store'
import type { Annotation } from '../../electron/shared/types'

interface Props {
  pageIndex: number
  zoom: number
  rotation: number
}

interface DragState {
  startX: number
  startY: number
  currentX: number
  currentY: number
  points: { x: number; y: number }[]
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

  const pageAnnotations = annotations.filter((a) => a.pageIndex === pageIndex)

  // 그리기 도구가 아니면 커서 기본
  const isDrawingTool = activeTool !== 'select'

  /** 마우스 위치를 레이어 내 로컬 좌표로 변환 */
  const getLocalPos = useCallback((e: React.MouseEvent | MouseEvent) => {
    const rect = layerRef.current?.getBoundingClientRect()
    if (!rect) return { x: 0, y: 0 }
    return {
      x: e.clientX - rect.left,
      y: e.clientY - rect.top,
      width: rect.width,
      height: rect.height
    }
  }, [])

  const onMouseDown = (e: React.MouseEvent) => {
    if (activeTool === 'select') return
    const pos = getLocalPos(e)
    if (activeTool === 'textbox') {
      setDraftText({ x: pos.x, y: pos.y, w: 0, h: 0 })
      setTextInput('')
      return
    }
    setDrag({
      startX: pos.x,
      startY: pos.y,
      currentX: pos.x,
      currentY: pos.y,
      points: [{ x: pos.x, y: pos.y }]
    })
  }

  const onMouseMove = (e: React.MouseEvent) => {
    if (!drag) return
    const pos = getLocalPos(e)
    if (activeTool === 'drawing') {
      setDrag((d) =>
        d ? { ...d, points: [...d.points, { x: pos.x, y: pos.y }] } : d
      )
    } else {
      setDrag((d) => (d ? { ...d, currentX: pos.x, currentY: pos.y } : d))
    }
  }

  const onMouseUp = () => {
    if (!drag) return
    const { startX, startY, currentX, currentY, points } = drag
    const x = Math.min(startX, currentX)
    const y = Math.min(startY, currentY)
    const w = Math.abs(currentX - startX)
    const h = Math.abs(currentY - startY)

    // 너무 작은 드래그는 무시
    if (w < 4 && h < 4 && activeTool !== 'drawing') {
      setDrag(null)
      return
    }

    const id = `ann-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`
    const ann: Annotation = {
      id,
      type: activeTool as Annotation['type'],
      pageIndex,
      // 화면 좌표 -> PDF 좌표 (1/zoom 스케일)
      x: x / zoom,
      y: y / zoom,
      width: w / zoom,
      height: h / zoom,
      color: toolColor,
      strokeWidth: activeTool === 'drawing' ? 2 : 2,
      points: activeTool === 'drawing' ? points.map((p) => ({ x: p.x / zoom, y: p.y / zoom })) : undefined
    }
    addAnnotation(ann)
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

  // Enter/Escape 처리
  useEffect(() => {
    if (!draftText) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setDraftText(null)
        setTextInput('')
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [draftText])

  // 선택 도구에서 Delete 키로 주석 삭제
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.key === 'Delete' || e.key === 'Backspace') && selectedAnnotationId) {
        const target = e.target as HTMLElement
        if (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA') return
        removeAnnotation(selectedAnnotationId)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [selectedAnnotationId, removeAnnotation])

  return (
    <div
      ref={layerRef}
      className={`annotation-layer ${isDrawingTool ? '' : 'annotation-layer--select'}`}
      onMouseDown={onMouseDown}
      onMouseMove={onMouseMove}
      onMouseUp={onMouseUp}
      onMouseLeave={onMouseUp}
    >
      {/* 기존 주석 렌더링 */}
      <svg
        className="annotation-svg"
        style={{ position: 'absolute', inset: 0, pointerEvents: 'none', width: '100%', height: '100%' }}
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
              style={{ pointerEvents: 'auto', cursor: activeTool === 'select' ? 'pointer' : 'crosshair' }}
              onMouseDown={(e) => {
                if (activeTool === 'select') {
                  e.stopPropagation()
                  selectAnnotation(ann.id)
                }
              }}
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
                  x={ax - 4}
                  y={ay - 4}
                  width={aw + 8}
                  height={ah + 8}
                  fill="none"
                  stroke="#0e6fd6"
                  strokeWidth={1}
                  strokeDasharray="4 3"
                />
              )}
            </g>
          )
        })}

        {/* 진행 중인 드래그 미리보기 */}
        {drag && activeTool === 'highlight' && (
          <rect
            x={Math.min(drag.startX, drag.currentX)}
            y={Math.min(drag.startY, drag.currentY)}
            width={Math.abs(drag.currentX - drag.startX)}
            height={Math.abs(drag.currentY - drag.startY)}
            fill={toolColor}
            fillOpacity={0.35}
          />
        )}
        {drag && activeTool === 'rectangle' && (
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
        {drag && activeTool === 'drawing' && (
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

      {/* 텍스트 주석 표시 */}
      {pageAnnotations
        .filter((a) => a.type === 'textbox')
        .map((ann) => (
          <div
            key={ann.id}
            onMouseDown={(e) => {
              if (activeTool === 'select') {
                e.stopPropagation()
                selectAnnotation(ann.id)
              }
            }}
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
              cursor: activeTool === 'select' ? 'pointer' : 'crosshair',
              outline:
                ann.id === selectedAnnotationId ? '1px dashed #0e6fd6' : 'none',
              background: 'rgba(255,255,255,0.85)',
              wordBreak: 'break-word',
              whiteSpace: 'pre-wrap'
            }}
          >
            {ann.text}
          </div>
        ))}

      {/* 텍스트 박스 초안 입력 */}
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
