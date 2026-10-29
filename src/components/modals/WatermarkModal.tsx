/**
 * 워터마크 (Watermark) 설정 모달
 * - 프리셋 ("대외비", "사본 (COPY)", "검토용 (DRAFT)", "더존 (DOUZONE)")
 * - 폰트 크기, 색상, 투명도, 회전 각도, 타일 반복 여부 설정
 * - 대상 페이지 범위 지정 및 워터마크 즉시 제거 지원
 */
import { useState } from 'react'
import { useStore } from '../../lib/store'
import type { WatermarkConfig } from '../../core/domain/page'

interface Props {
  onClose: () => void
}

const PRESET_TEXTS = ['대외비', '사본 (COPY)', '검토용 (DRAFT)', '더존 (DOUZONE)']

const COLOR_PRESETS = [
  { color: '#e5534b', label: '빨강' },
  { color: '#0e6fd6', label: '파랑' },
  { color: '#333333', label: '차콜' },
  { color: '#f59e0b', label: '오렌지' },
  { color: '#10b981', label: '초록' }
]

export function WatermarkModal({ onClose }: Props) {
  const pages = useStore((s) => s.pages)
  const selectedPageIndices = useStore((s) => s.selectedPageIndices)
  const currentPage = useStore((s) => s.currentPage)
  const applyWatermark = useStore((s) => s.applyWatermark)

  const activePage = pages[currentPage]
  const currentWm = activePage?.watermarkConfig

  const [text, setText] = useState(currentWm?.text ?? '대외비')
  const [fontSize, setFontSize] = useState(currentWm?.fontSize ?? 48)
  const [color, setColor] = useState(currentWm?.color ?? '#e5534b')
  const [opacity, setOpacity] = useState(Math.round((currentWm?.opacity ?? 0.25) * 100))
  const [rotation, setRotation] = useState<number>(currentWm?.rotation ?? -45)
  const [tiled, setTiled] = useState(currentWm?.tiled ?? false)
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
    if (!text.trim()) {
      handleRemove()
      return
    }

    const config: WatermarkConfig = {
      text: text.trim(),
      fontSize,
      color,
      opacity: Math.max(0.05, Math.min(0.9, opacity / 100)),
      rotation,
      tiled
    }

    applyWatermark(getTargetIndices(), config)
    onClose()
  }

  const handleRemove = () => {
    applyWatermark(getTargetIndices(), undefined)
    onClose()
  }

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div
        className="modal"
        onMouseDown={(e) => e.stopPropagation()}
        style={{ minWidth: 460, maxWidth: 520 }}
      >
        <div className="modal__header">
          <span>💧 워터마크 설정</span>
          <button className="modal__close" onClick={onClose} aria-label="닫기">
            ×
          </button>
        </div>

        <div className="modal__body" style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          {/* 워터마크 문구 입력 및 프리셋 */}
          <div>
            <label style={{ fontSize: 13, fontWeight: 600, display: 'block', marginBottom: 6 }}>
              워터마크 문구
            </label>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginBottom: 8 }}>
              {PRESET_TEXTS.map((p) => (
                <button
                  key={p}
                  type="button"
                  className={`btn btn--small ${text === p ? 'btn--primary' : 'btn--secondary'}`}
                  onClick={() => setText(p)}
                >
                  {p}
                </button>
              ))}
            </div>
            <input
              type="text"
              className="form-input"
              value={text}
              onChange={(e) => setText(e.target.value)}
              placeholder="표시할 워터마크 텍스트를 입력하세요"
              style={{ width: '100%', fontSize: 14 }}
            />
          </div>

          {/* 서식 및 색상 */}
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            <div>
              <label style={{ fontSize: 12, color: 'var(--color-text-muted)', display: 'block', marginBottom: 4 }}>
                글자 크기: {fontSize}pt
              </label>
              <input
                type="range"
                min={24}
                max={120}
                step={4}
                value={fontSize}
                onChange={(e) => setFontSize(Number(e.target.value))}
                style={{ width: '100%' }}
              />
            </div>
            <div>
              <label style={{ fontSize: 12, color: 'var(--color-text-muted)', display: 'block', marginBottom: 4 }}>
                투명도: {opacity}%
              </label>
              <input
                type="range"
                min={10}
                max={80}
                step={5}
                value={opacity}
                onChange={(e) => setOpacity(Number(e.target.value))}
                style={{ width: '100%' }}
              />
            </div>
          </div>

          {/* 색상 및 프리셋 */}
          <div>
            <label style={{ fontSize: 12, color: 'var(--color-text-muted)', display: 'block', marginBottom: 4 }}>
              글자 색상
            </label>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              {COLOR_PRESETS.map((p) => (
                <button
                  key={p.color}
                  type="button"
                  style={{
                    width: 24,
                    height: 24,
                    borderRadius: 4,
                    backgroundColor: p.color,
                    border: color === p.color ? '2px solid var(--color-accent)' : '1px solid rgba(0,0,0,0.2)',
                    cursor: 'pointer'
                  }}
                  title={p.label}
                  onClick={() => setColor(p.color)}
                />
              ))}
              <input
                type="color"
                value={color}
                onChange={(e) => setColor(e.target.value)}
                style={{ width: 32, height: 26, padding: 0, border: 'none', cursor: 'pointer' }}
              />
              <span style={{ fontSize: 12, fontFamily: 'monospace' }}>{color}</span>
            </div>
          </div>

          {/* 회전 각도 및 배치 레이아웃 */}
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            <div>
              <label style={{ fontSize: 12, color: 'var(--color-text-muted)', display: 'block', marginBottom: 4 }}>
                회전 각도
              </label>
              <div style={{ display: 'flex', gap: 6 }}>
                <button
                  type="button"
                  className={`btn btn--small ${rotation === -45 ? 'btn--primary' : 'btn--secondary'}`}
                  onClick={() => setRotation(-45)}
                >
                  대각선 (-45°)
                </button>
                <button
                  type="button"
                  className={`btn btn--small ${rotation === 0 ? 'btn--primary' : 'btn--secondary'}`}
                  onClick={() => setRotation(0)}
                >
                  수평 (0°)
                </button>
                <button
                  type="button"
                  className={`btn btn--small ${rotation === 45 ? 'btn--primary' : 'btn--secondary'}`}
                  onClick={() => setRotation(45)}
                >
                  역대각선 (45°)
                </button>
              </div>
            </div>

            <div>
              <label style={{ fontSize: 12, color: 'var(--color-text-muted)', display: 'block', marginBottom: 4 }}>
                배치 방식
              </label>
              <div style={{ display: 'flex', gap: 6 }}>
                <button
                  type="button"
                  className={`btn btn--small ${!tiled ? 'btn--primary' : 'btn--secondary'}`}
                  onClick={() => setTiled(false)}
                >
                  중앙 단독
                </button>
                <button
                  type="button"
                  className={`btn btn--small ${tiled ? 'btn--primary' : 'btn--secondary'}`}
                  onClick={() => setTiled(true)}
                >
                  바둑판 반복 (타일)
                </button>
              </div>
            </div>
          </div>

          {/* 대상 범위 */}
          <div>
            <label style={{ fontSize: 12, color: 'var(--color-text-muted)', display: 'block', marginBottom: 4 }}>
              적용 대상 페이지
            </label>
            <div style={{ display: 'flex', gap: 16 }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: 6, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="wm-scope"
                  checked={targetScope === 'all'}
                  onChange={() => setTargetScope('all')}
                />
                <span>전체 페이지 ({pages.length}장)</span>
              </label>
              <label style={{ display: 'flex', alignItems: 'center', gap: 6, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="wm-scope"
                  checked={targetScope === 'selected'}
                  onChange={() => setTargetScope('selected')}
                />
                <span>선택된 페이지 ({selectedPageIndices.length}장)</span>
              </label>
            </div>
          </div>
        </div>

        <div className="modal__footer" style={{ display: 'flex', justifyContent: 'space-between' }}>
          <button type="button" className="btn btn--danger" onClick={handleRemove}>
            워터마크 제거
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
