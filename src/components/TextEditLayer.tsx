/**
 * 본문 텍스트 인라인 직접 편집 레이어 (Direct In-place Text Editing Layer)
 * - PDF.js TextContent 기반 텍스트 블록 실시간 감지 및 인라인 에디터 제공
 * - 텍스트 수정 시 실시간 화이트아웃(Whiteout) 및 나눔고딕 오버레이
 * - 수정 내역 원클릭 복원(Revert) 및 기존 수정 항목 재편집(Re-edit) 완벽 지원
 * - 좌/중/우 정렬 및 폰트 크기 조절 미니 툴바 제공
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
  const [editingItem, setEditingItem] = useState<{
    id: string
    originalItemId: string
    str: string
    pdfX: number
    pdfY: number
    origPdfWidth: number
    pdfHeight: number
    fontSize: number
    vx: number
    vy: number
    origVw: number
    vh: number
    existingReplacementId?: string
  } | null>(null)

  const [editText, setEditText] = useState('')
  const [editColor, setEditColor] = useState('#1e1e1e')
  const [editFontSize, setEditFontSize] = useState<number>(12)
  const [editAlign, setEditAlign] = useState<'left' | 'center' | 'right'>('left')

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

  // 이미 대체된 원본 아이템 ID 집합 (중복 덮어쓰기 방지)
  const replacedOriginalIds = new Set(
    textReplacements
      .map((tr) => tr.originalItemId)
      .filter((id): id is string => Boolean(id))
  )

  // 1. 신규 편집 시작
  const startEditingNew = (item: EditableTextItem) => {
    setEditingItem({
      id: item.id,
      originalItemId: item.id,
      str: item.str,
      pdfX: item.pdfX,
      pdfY: item.pdfY,
      origPdfWidth: item.pdfWidth,
      pdfHeight: item.pdfHeight,
      fontSize: item.fontSize,
      vx: item.vx,
      vy: item.vy,
      origVw: item.vw,
      vh: item.vh
    })
    setEditText(item.str)
    setEditFontSize(item.fontSize)
    setEditAlign('left')
    setEditColor(toolColor && toolColor !== '#ffeb3b' ? toolColor : '#1e1e1e')
  }

  // 2. 기존 수정된 항목 재편집 (Re-edit)
  const startReEditing = (tr: TextReplacement) => {
    setEditingItem({
      id: tr.id,
      originalItemId: tr.originalItemId ?? tr.id,
      str: tr.originalText,
      pdfX: tr.pdfX,
      pdfY: tr.pdfY,
      origPdfWidth: tr.origPdfWidth ?? tr.pdfWidth,
      pdfHeight: tr.pdfHeight,
      fontSize: tr.fontSize,
      vx: tr.vx,
      vy: tr.vy,
      origVw: tr.origVw ?? tr.vw,
      vh: tr.vh,
      existingReplacementId: tr.id
    })
    setEditText(tr.newText)
    setEditFontSize(tr.fontSize)
    setEditAlign(tr.align ?? 'left')
    setEditColor(tr.color)
  }

  // 편집 커밋
  const commitEdit = () => {
    if (!editingItem) return
    const trimmed = editText.trim()

    // 기존 수정본이 있는 경우 먼저 제거하고 갱신
    if (editingItem.existingReplacementId) {
      removeTextReplacement(pageIndex, editingItem.existingReplacementId)
    }

    if (trimmed && trimmed !== editingItem.str) {
      const estimatedVWidth = Math.max(
        editingItem.origVw,
        editText.length * (editFontSize * 0.6)
      )
      const estimatedPdfWidth = Math.max(
        editingItem.origPdfWidth,
        editText.length * (editFontSize * 0.6)
      )

      const replacement: TextReplacement = {
        id: `tr_${Date.now()}_${Math.random().toString(36).slice(2, 6)}`,
        pageIndex,
        originalItemId: editingItem.originalItemId,
        pdfX: editingItem.pdfX,
        pdfY: editingItem.pdfY,
        pdfWidth: estimatedPdfWidth,
        pdfHeight: editingItem.pdfHeight,
        origPdfWidth: editingItem.origPdfWidth,
        vx: editingItem.vx,
        vy: editingItem.vy,
        vw: estimatedVWidth,
        vh: editingItem.vh,
        origVw: editingItem.origVw,
        originalText: editingItem.str,
        newText: editText,
        fontSize: editFontSize,
        align: editAlign,
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
      {/* ── 1. 기존 수정 내역 (Whiteout 소거 + 새 텍스트 렌더링) ── */}
      {textReplacements.map((tr) => {
        const isEditingThis = editingItem?.existingReplacementId === tr.id
        if (isEditingThis) return null

        const whiteoutW = (tr.origVw ?? tr.vw) * zoom
        const whiteoutH = tr.vh * zoom

        return (
          <div key={tr.id}>
            {/* 원본 텍스트 소거 배경 (Whiteout): 항상 원본 텍스트 영역만큼만 정확히 은폐 */}
            <div
              className="text-replacement__whiteout"
              style={{
                position: 'absolute',
                left: `${tr.vx * zoom - 1}px`,
                top: `${tr.vy * zoom - 1}px`,
                width: `${whiteoutW + 2}px`,
                height: `${whiteoutH + 2}px`,
                backgroundColor: tr.backgroundColor || '#ffffff',
                zIndex: 12
              }}
            />

            {/* 대체된 새 텍스트 (클릭 시 재편집 모드로 전환) */}
            <div
              className="text-replacement__badge"
              onClick={() => isEditMode && startReEditing(tr)}
              style={{
                position: 'absolute',
                left: `${tr.vx * zoom}px`,
                top: `${tr.vy * zoom}px`,
                width: `${whiteoutW}px`,
                height: `${whiteoutH}px`,
                display: 'flex',
                alignItems: 'center',
                justifyContent:
                  tr.align === 'center'
                    ? 'center'
                    : tr.align === 'right'
                    ? 'flex-end'
                    : 'flex-start',
                zIndex: 13,
                fontSize: `${Math.max(9, tr.fontSize * zoom)}px`,
                color: tr.color,
                lineHeight: 1,
                fontFamily: 'NanumGothic, "Malgun Gothic", sans-serif',
                cursor: isEditMode ? 'pointer' : 'default',
                outline: isEditMode ? '1px dashed var(--color-accent)' : 'none'
              }}
              title={isEditMode ? '클릭하여 다시 편집' : undefined}
            >
              <span className="text-replacement__content" style={{ whiteSpace: 'nowrap' }}>
                {tr.newText}
              </span>
              {isEditMode && (
                <button
                  type="button"
                  className="text-replacement__revert-btn"
                  title={`원본으로 복원: "${tr.originalText}"`}
                  onClick={(e) => {
                    e.stopPropagation()
                    removeTextReplacement(pageIndex, tr.id)
                  }}
                  style={{
                    background: 'rgba(229, 83, 75, 0.9)',
                    color: '#fff',
                    border: 'none',
                    borderRadius: '50%',
                    width: 14,
                    height: 14,
                    fontSize: 9,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    cursor: 'pointer',
                    marginLeft: 4
                  }}
                >
                  ✕
                </button>
              )}
            </div>
          </div>
        )
      })}

      {/* ── 2. 본문 편집 모드일 때 감지된 텍스트 블록 하이라이트 (이미 대체된 항목은 제외) ── */}
      {isEditMode &&
        textItems
          .filter((item) => !replacedOriginalIds.has(item.id))
          .map((item) => {
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
                  zIndex: 14,
                  border: isHovered ? '1px solid var(--color-accent)' : '1px dashed rgba(16, 185, 129, 0.4)',
                  background: isHovered ? 'rgba(16, 185, 129, 0.12)' : 'transparent',
                  borderRadius: 2
                }}
                onMouseEnter={() => setHoveredId(item.id)}
                onMouseLeave={() => setHoveredId(null)}
                onClick={() => startEditingNew(item)}
                title={`클릭하여 수정: "${item.str}"`}
              >
                {isHovered && (
                  <span
                    style={{
                      position: 'absolute',
                      top: -18,
                      left: 0,
                      background: 'var(--color-accent)',
                      color: '#ffffff',
                      fontSize: 10,
                      fontWeight: 600,
                      padding: '1px 5px',
                      borderRadius: 3,
                      whiteSpace: 'nowrap',
                      pointerEvents: 'none',
                      zIndex: 20
                    }}
                  >
                    ✍️ 수정
                  </span>
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
            zIndex: 35
          }}
          onClick={(e) => e.stopPropagation()}
        >
          <div
            className="inline-text-editor__input-wrapper"
            style={{
              display: 'flex',
              flexDirection: 'column',
              background: 'var(--color-bg-elevated)',
              padding: 4,
              borderRadius: 6,
              boxShadow: '0 6px 20px rgba(0, 0, 0, 0.35), 0 0 0 1px var(--color-accent)'
            }}
          >
            {/* 상단 미니 툴바 (폰트 크기, 정렬, 색상) */}
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 4,
                marginBottom: 4,
                fontSize: 11,
                borderBottom: '1px solid var(--color-border)',
                paddingBottom: 4
              }}
            >
              {/* 글자 크기 조절 */}
              <span style={{ color: 'var(--color-text-muted)' }}>크기:</span>
              <button
                type="button"
                onClick={() => setEditFontSize((s) => Math.max(8, Number((s - 0.5).toFixed(1))))}
                style={toolBtnStyle}
                title="글자 크기 축소 (-0.5pt)"
              >
                -
              </button>
              <span style={{ minWidth: 28, textAlign: 'center', fontWeight: 600 }}>
                {editFontSize}pt
              </span>
              <button
                type="button"
                onClick={() => setEditFontSize((s) => Math.min(72, Number((s + 0.5).toFixed(1))))}
                style={toolBtnStyle}
                title="글자 크기 확대 (+0.5pt)"
              >
                +
              </button>

              <div style={{ width: 1, height: 12, background: 'var(--color-border)', margin: '0 2px' }} />

              {/* 정렬 선택 */}
              <button
                type="button"
                onClick={() => setEditAlign('left')}
                style={{
                  ...toolBtnStyle,
                  background: editAlign === 'left' ? 'var(--color-accent)' : 'transparent',
                  color: editAlign === 'left' ? '#fff' : 'inherit'
                }}
                title="왼쪽 정렬"
              >
                좌
              </button>
              <button
                type="button"
                onClick={() => setEditAlign('center')}
                style={{
                  ...toolBtnStyle,
                  background: editAlign === 'center' ? 'var(--color-accent)' : 'transparent',
                  color: editAlign === 'center' ? '#fff' : 'inherit'
                }}
                title="가운데 정렬"
              >
                중
              </button>
              <button
                type="button"
                onClick={() => setEditAlign('right')}
                style={{
                  ...toolBtnStyle,
                  background: editAlign === 'right' ? 'var(--color-accent)' : 'transparent',
                  color: editAlign === 'right' ? '#fff' : 'inherit'
                }}
                title="오른쪽 정렬"
              >
                우
              </button>

              <div style={{ width: 1, height: 12, background: 'var(--color-border)', margin: '0 2px' }} />

              {/* 글자 색상 */}
              <input
                type="color"
                value={editColor}
                onChange={(e) => setEditColor(e.target.value)}
                style={{
                  width: 18,
                  height: 18,
                  border: 'none',
                  padding: 0,
                  background: 'transparent',
                  cursor: 'pointer'
                }}
                title="글자 색상"
              />
            </div>

            {/* 입력 필드 */}
            <div style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
              <input
                ref={inputRef}
                type="text"
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
                  fontSize: `${Math.max(11, editFontSize * zoom)}px`,
                  color: editColor,
                  textAlign: editAlign,
                  minWidth: `${Math.max(140, editingItem.origVw * zoom + 16)}px`,
                  padding: '4px 6px',
                  borderRadius: 4,
                  border: '1px solid var(--color-border)',
                  background: '#ffffff',
                  outline: 'none',
                  fontFamily: 'NanumGothic, "Malgun Gothic", sans-serif'
                }}
              />
              <button
                type="button"
                onClick={commitEdit}
                style={{
                  padding: '4px 8px',
                  borderRadius: 4,
                  background: 'var(--color-accent)',
                  color: '#ffffff',
                  border: 'none',
                  fontSize: 11,
                  fontWeight: 600,
                  cursor: 'pointer',
                  whiteSpace: 'nowrap'
                }}
                title="적용 (Enter)"
              >
                ✓ 적용
              </button>
              <button
                type="button"
                onClick={cancelEdit}
                style={{
                  padding: '4px 6px',
                  borderRadius: 4,
                  background: 'transparent',
                  color: 'var(--color-text-muted)',
                  border: 'none',
                  fontSize: 12,
                  cursor: 'pointer'
                }}
                title="취소 (Esc)"
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

const toolBtnStyle: React.CSSProperties = {
  background: 'transparent',
  border: '1px solid var(--color-border)',
  borderRadius: 3,
  padding: '1px 5px',
  fontSize: 10,
  cursor: 'pointer',
  color: 'inherit'
}
