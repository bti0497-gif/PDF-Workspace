/**
 * 본문 텍스트 인라인 직접 편집 레이어 (Direct In-place Text Editing Layer)
 * - PDF.js TextContent 기반 텍스트 블록 실시간 감지 및 인라인 에디터 제공
 * - 텍스트 수정 시 실시간 화이트아웃(Whiteout) 및 나눔고딕 오버레이
 * - 수정 내역 원클릭 복원(Revert) 및 재수정 지원
 */
import { useState, useEffect, useRef } from 'react'
import { useStore } from '../lib/store'
import { getEditableTextItems, type EditableTextItem } from '../core/services/text-editor.service'
import type { TextReplacement } from '../core/domain/page'

interface Props {
  pageIndex: number
  zoom: number
  rotation: number
}

export function TextEditLayer({ pageIndex, zoom }: Props) {
  const pages = useStore((s) => s.pages)
  const activeTool = useStore((s) => s.activeTool)
  const toolColor = useStore((s) => s.toolColor)
  const addTextReplacement = useStore((s) => s.addTextReplacement)
  const removeTextReplacement = useStore((s) => s.removeTextReplacement)

  const virtualPage = pages[pageIndex]
  const textReplacements = virtualPage?.textReplacements ?? []

  const [textItems, setTextItems] = useState<EditableTextItem[]>([])
  const [hoveredId, setHoveredId] = useState<string | null>(null)
  const [editingItem, setEditingItem] = useState<EditableTextItem | null>(null)
  const [editText, setEditText] = useState('')
  const [editColor, setEditColor] = useState('#000000')

  const inputRef = useRef<HTMLInputElement>(null)

  // 텍스트 블록 데이터 로드
  useEffect(() => {
    if (!virtualPage) return
    let active = true
    void getEditableTextItems(virtualPage).then((items) => {
      if (active) setTextItems(items)
    })
    return () => {
      active = false
    }
  }, [virtualPage])

  // 인라인 편집기 열릴 때 자동 포커스
  useEffect(() => {
    if (editingItem && inputRef.current) {
      inputRef.current.focus()
      inputRef.current.select()
    }
  }, [editingItem])

  const isEditMode = activeTool === 'text-edit'

  // 편집 시작
  const startEditing = (item: EditableTextItem) => {
    setEditingItem(item)
    setEditText(item.str)
    setEditColor(toolColor && toolColor !== '#ffeb3b' ? toolColor : '#1e1e1e')
  }

  // 편집 커밋
  const commitEdit = () => {
    if (!editingItem) return
    const trimmed = editText.trim()
    if (trimmed && trimmed !== editingItem.str) {
      const estimatedWidth = Math.max(
        editingItem.pdfWidth,
        editText.length * (editingItem.fontSize * 0.6)
      )
      const estimatedVWidth = Math.max(
        editingItem.vw,
        editText.length * (editingItem.fontSize * 0.6)
      )

      const replacement: TextReplacement = {
        id: `tr_${Date.now()}_${Math.random().toString(36).slice(2, 6)}`,
        pageIndex,
        pdfX: editingItem.pdfX,
        pdfY: editingItem.pdfY,
        pdfWidth: estimatedWidth,
        pdfHeight: editingItem.pdfHeight,
        vx: editingItem.vx,
        vy: editingItem.vy,
        vw: estimatedVWidth,
        vh: editingItem.vh,
        originalText: editingItem.str,
        newText: editText,
        fontSize: editingItem.fontSize,
        color: editColor,
        backgroundColor: '#ffffff'
      }
      addTextReplacement(replacement)
    }
    setEditingItem(null)
  }

  // 편집 취소
  const cancelEdit = () => {
    setEditingItem(null)
  }

  return (
    <div
      className={`text-edit-layer ${isEditMode ? 'text-edit-layer--active' : ''}`}
      style={{
        position: 'absolute',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        pointerEvents: isEditMode || textReplacements.length > 0 ? 'auto' : 'none',
        zIndex: 15
      }}
    >
      {/* ── 1. 기존 수정 내역 (화이트아웃 소거 + 새 텍스트 렌더링) ── */}
      {textReplacements.map((tr) => (
        <div key={tr.id}>
          {/* 원본 텍스트 소거 배경 (Whiteout) */}
          <div
            className="text-replacement__whiteout"
            style={{
              position: 'absolute',
              left: `${tr.vx * zoom - 1}px`,
              top: `${tr.vy * zoom - 1}px`,
              width: `${tr.vw * zoom + 4}px`,
              height: `${tr.vh * zoom + 2}px`,
              backgroundColor: tr.backgroundColor || '#ffffff',
              zIndex: 12
            }}
          />

          {/* 대체된 새 텍스트 */}
          <div
            className="text-replacement__badge"
            style={{
              position: 'absolute',
              left: `${tr.vx * zoom}px`,
              top: `${tr.vy * zoom}px`,
              minWidth: `${tr.vw * zoom}px`,
              height: `${tr.vh * zoom}px`,
              zIndex: 13,
              fontSize: `${Math.max(9, tr.fontSize * zoom)}px`,
              color: tr.color,
              lineHeight: 1,
              fontFamily: 'NanumGothic, "Malgun Gothic", sans-serif'
            }}
          >
            <span className="text-replacement__content">{tr.newText}</span>
            <button
              className="text-replacement__revert-btn"
              title={`원본으로 복원: "${tr.originalText}"`}
              onClick={(e) => {
                e.stopPropagation()
                removeTextReplacement(pageIndex, tr.id)
              }}
            >
              ✕
            </button>
          </div>
        </div>
      ))}

      {/* ── 2. 본문 편집 모드일 때 감지된 텍스트 블록 하이라이트 ── */}
      {isEditMode &&
        textItems.map((item) => {
          const isHovered = hoveredId === item.id
          const isEditingThis = editingItem?.id === item.id
          if (isEditingThis) return null

          return (
            <div
              key={item.id}
              className={`text-detect-box ${isHovered ? 'text-detect-box--hovered' : ''}`}
              style={{
                position: 'absolute',
                left: `${item.vx * zoom - 1}px`,
                top: `${item.vy * zoom - 1}px`,
                width: `${item.vw * zoom + 2}px`,
                height: `${item.vh * zoom + 2}px`,
                cursor: 'text',
                zIndex: 14
              }}
              onMouseEnter={() => setHoveredId(item.id)}
              onMouseLeave={() => setHoveredId(null)}
              onClick={() => startEditing(item)}
              title="클릭하여 이 본문 텍스트 수정"
            >
              {isHovered && (
                <span className="text-detect-badge">✍️ 수정</span>
              )}
            </div>
          )
        })}

      {/* ── 3. 활성화된 인라인 텍스트 에디터 오버레이 ── */}
      {editingItem && (
        <div
          className="inline-text-editor"
          style={{
            position: 'absolute',
            left: `${editingItem.vx * zoom - 2}px`,
            top: `${editingItem.vy * zoom - 4}px`,
            zIndex: 30
          }}
          onClick={(e) => e.stopPropagation()}
        >
          <div className="inline-text-editor__input-wrapper">
            <input
              ref={inputRef}
              type="text"
              className="inline-text-editor__input"
              value={editText}
              onChange={(e) => setEditText(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault()
                  commitEdit()
                } else if (e.key === 'Escape') {
                  e.preventDefault()
                  cancelEdit()
                }
              }}
              style={{
                fontSize: `${Math.max(11, editingItem.fontSize * zoom)}px`,
                color: editColor,
                minWidth: `${Math.max(120, editingItem.vw * zoom + 20)}px`
              }}
            />
            <div className="inline-text-editor__toolbar">
              <input
                type="color"
                className="inline-text-editor__color"
                value={editColor}
                onChange={(e) => setEditColor(e.target.value)}
                title="글자 색상"
              />
              <button
                className="inline-text-editor__btn inline-text-editor__btn--commit"
                title="적용 (Enter)"
                onClick={commitEdit}
              >
                ✓ 적용
              </button>
              <button
                className="inline-text-editor__btn inline-text-editor__btn--cancel"
                title="취소 (Esc)"
                onClick={cancelEdit}
              >
                ✕
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
