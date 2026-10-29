/**
 * 페이지 번호 매기기 (Page Numbering / Header & Footer) 모달
 * - 6개 위치 그리드 (상단 좌/중/우, 하단 좌/중/우)
 * - 번호 서식 (1, 2, 3... / - 1 - / 1 / N / Page 1 of N)
 * - 시작 번호, 폰트 크기, 여백, 색상 설정
 * - 대상 페이지 범위 지정 및 페이지 번호 즉시 제거 지원
 */
import { useState } from 'react'
import { useStore } from '../../lib/store'
import type { PageNumberConfig } from '../../core/domain/page'

interface Props {
  onClose: () => void
}

type Position = PageNumberConfig['position']
type FormatType = 'number' | 'dash' | 'slash' | 'pageOf'

const POSITIONS: { id: Position; label: string }[] = [
  { id: 'top-left', label: '상단 좌측' },
  { id: 'top-center', label: '상단 중앙' },
  { id: 'top-right', label: '상단 우측' },
  { id: 'bottom-left', label: '하단 좌측' },
  { id: 'bottom-center', label: '하단 중앙' },
  { id: 'bottom-right', label: '하단 우측' }
]

const FORMATS: { id: FormatType; label: string; preview: string }[] = [
  { id: 'number', label: '단순 번호', preview: '1, 2, 3...' },
  { id: 'dash', label: '대시 강조', preview: '- 1 -, - 2 -...' },
  { id: 'slash', label: '전체 포함 (/)', preview: '1 / N, 2 / N...' },
  { id: 'pageOf', label: 'Page of 영문', preview: 'Page 1 of N...' }
]

export function PageNumberModal({ onClose }: Props) {
  const pages = useStore((s) => s.pages)
  const selectedPageIndices = useStore((s) => s.selectedPageIndices)
  const currentPage = useStore((s) => s.currentPage)
  const applyPageNumbering = useStore((s) => s.applyPageNumbering)

  const activePage = pages[currentPage]
  const currentPn = activePage?.pageNumberConfig

  const [position, setPosition] = useState<Position>(currentPn?.position ?? 'bottom-center')
  const [format, setFormat] = useState<FormatType>('dash')
  const [startNumber, setStartNumber] = useState(1)
  const [fontSize, setFontSize] = useState(currentPn?.fontSize ?? 11)
  const [margin, setMargin] = useState(currentPn?.margin ?? 24)
  const [color, setColor] = useState(currentPn?.color ?? '#333333')
  const [targetScope, setTargetScope] = useState<'all' | 'selected'>(
    selectedPageIndices.length > 1 ? 'selected' : 'all'
  )

  const getTargetIndices = () => {
    if (targetScope === 'all') {
      return pages.map((_, i) => i)
    }
    return selectedPageIndices.length > 0 ? selectedPageIndices : [currentPage]
  }

  const handleApply = () => {
    applyPageNumbering(getTargetIndices(), {
      format,
      startNumber,
      position,
      fontSize,
      color,
      margin
    })
    onClose()
  }

  const handleRemove = () => {
    applyPageNumbering(getTargetIndices(), null)
    onClose()
  }

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div
        className="modal"
        onMouseDown={(e) => e.stopPropagation()}
        style={{ minWidth: 440, maxWidth: 500 }}
      >
        <div className="modal__header">
          <span>🔢 페이지 번호 매기기 (머리글/바닥글)</span>
          <button className="modal__close" onClick={onClose} aria-label="닫기">
            ×
          </button>
        </div>

        <div className="modal__body" style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          {/* 위치 선택 (6-분할 그리드) */}
          <div>
            <label style={{ fontSize: 13, fontWeight: 600, display: 'block', marginBottom: 8 }}>
              표시 위치 선택
            </label>
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(3, 1fr)',
                gap: 8,
                background: 'var(--color-bg)',
                padding: 10,
                borderRadius: 6,
                border: '1px solid var(--color-border)'
              }}
            >
              {POSITIONS.map((p) => (
                <button
                  key={p.id}
                  type="button"
                  className={`btn btn--small ${position === p.id ? 'btn--primary' : 'btn--secondary'}`}
                  onClick={() => setPosition(p.id)}
                  style={{
                    padding: '8px 4px',
                    fontSize: 12,
                    fontWeight: position === p.id ? 600 : 400
                  }}
                >
                  {p.label}
                </button>
              ))}
            </div>
          </div>

          {/* 서식 선택 */}
          <div>
            <label style={{ fontSize: 13, fontWeight: 600, display: 'block', marginBottom: 6 }}>
              번호 형식
            </label>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
              {FORMATS.map((f) => (
                <label
                  key={f.id}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 6,
                    padding: '6px 8px',
                    borderRadius: 4,
                    border: `1px solid ${format === f.id ? 'var(--color-accent)' : 'var(--color-border)'}`,
                    cursor: 'pointer',
                    background: format === f.id ? 'rgba(14, 111, 214, 0.08)' : 'transparent'
                  }}
                >
                  <input
                    type="radio"
                    name="pn-format"
                    checked={format === f.id}
                    onChange={() => setFormat(f.id)}
                  />
                  <div style={{ fontSize: 12 }}>
                    <div style={{ fontWeight: 500 }}>{f.label}</div>
                    <div style={{ color: 'var(--color-text-muted)', fontSize: 11 }}>{f.preview}</div>
                  </div>
                </label>
              ))}
            </div>
          </div>

          {/* 세부 수치 설정 (시작 번호, 폰트 크기, 여백) */}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 10 }}>
            <div>
              <label style={{ fontSize: 12, color: 'var(--color-text-muted)', display: 'block', marginBottom: 4 }}>
                시작 번호
              </label>
              <input
                type="number"
                className="form-input"
                min={1}
                value={startNumber}
                onChange={(e) => setStartNumber(Math.max(1, parseInt(e.target.value, 10) || 1))}
                style={{ width: '100%', textAlign: 'center' }}
              />
            </div>
            <div>
              <label style={{ fontSize: 12, color: 'var(--color-text-muted)', display: 'block', marginBottom: 4 }}>
                크기: {fontSize}pt
              </label>
              <input
                type="range"
                min={8}
                max={24}
                step={1}
                value={fontSize}
                onChange={(e) => setFontSize(Number(e.target.value))}
                style={{ width: '100%', marginTop: 6 }}
              />
            </div>
            <div>
              <label style={{ fontSize: 12, color: 'var(--color-text-muted)', display: 'block', marginBottom: 4 }}>
                가장자리 여백: {margin}pt
              </label>
              <input
                type="range"
                min={12}
                max={72}
                step={2}
                value={margin}
                onChange={(e) => setMargin(Number(e.target.value))}
                style={{ width: '100%', marginTop: 6 }}
              />
            </div>
          </div>

          {/* 색상 및 대상 범위 */}
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <span style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>번호 색상:</span>
              <input
                type="color"
                value={color}
                onChange={(e) => setColor(e.target.value)}
                style={{ width: 30, height: 26, padding: 0, border: 'none', cursor: 'pointer' }}
              />
              <span style={{ fontSize: 12, fontFamily: 'monospace' }}>{color}</span>
            </div>

            <div style={{ display: 'flex', gap: 12, fontSize: 12 }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: 4, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="pn-scope"
                  checked={targetScope === 'all'}
                  onChange={() => setTargetScope('all')}
                />
                <span>전체 ({pages.length})</span>
              </label>
              <label style={{ display: 'flex', alignItems: 'center', gap: 4, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="pn-scope"
                  checked={targetScope === 'selected'}
                  onChange={() => setTargetScope('selected')}
                />
                <span>선택 ({selectedPageIndices.length})</span>
              </label>
            </div>
          </div>
        </div>

        <div className="modal__footer" style={{ display: 'flex', justifyContent: 'space-between' }}>
          <button type="button" className="btn btn--danger" onClick={handleRemove}>
            페이지 번호 제거
          </button>
          <div style={{ display: 'flex', gap: 8 }}>
            <button type="button" className="btn btn--secondary" onClick={onClose}>
              취소
            </button>
            <button type="button" className="btn btn--primary" onClick={handleApply}>
              적용
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
