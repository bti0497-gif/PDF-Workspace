/**
 * 페이지 여백 자르기 (Crop & Trim) 모달
 * - 스캔 테두리 및 불필요한 여백 상/하/좌/우 지정 크롭
 * - mm 또는 pt 단위 입력 지원
 */
import { useState } from 'react'
import { useStore } from '../../lib/store'

interface Props {
  onClose: () => void
}

export function CropModal({ onClose }: Props) {
  const pages = useStore((s) => s.pages)
  const selectedPageIndices = useStore((s) => s.selectedPageIndices)
  const cropPages = useStore((s) => s.cropPages)

  const [unit, setUnit] = useState<'mm' | 'pt'>('mm')
  const [top, setTop] = useState(5)
  const [bottom, setBottom] = useState(5)
  const [left, setLeft] = useState(5)
  const [right, setRight] = useState(5)
  const [targetScope, setTargetScope] = useState<'all' | 'selected'>(
    selectedPageIndices.length > 1 ? 'selected' : 'all'
  )

  const handleApply = () => {
    // 1 mm = 2.83465 pt
    const factor = unit === 'mm' ? 2.83465 : 1
    const margins = {
      top: Math.max(0, top * factor),
      bottom: Math.max(0, bottom * factor),
      left: Math.max(0, left * factor),
      right: Math.max(0, right * factor)
    }

    const targetIndices =
      targetScope === 'all'
        ? pages.map((_, i) => i)
        : selectedPageIndices

    cropPages(targetIndices, margins)
    onClose()
  }

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div className="modal" onMouseDown={(e) => e.stopPropagation()} style={{ minWidth: 380 }}>
        <div className="modal__header">
          <span>여백 자르기 (Crop & Trim)</span>
          <button className="modal__close" onClick={onClose} aria-label="닫기">
            ×
          </button>
        </div>

        <div className="modal__body" style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          {/* 단위 선택 */}
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span style={{ fontWeight: 500 }}>여백 단위</span>
            <div style={{ display: 'flex', gap: 12 }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: 4, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="unit"
                  value="mm"
                  checked={unit === 'mm'}
                  onChange={() => setUnit('mm')}
                />
                <span>밀리미터 (mm)</span>
              </label>
              <label style={{ display: 'flex', alignItems: 'center', gap: 4, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="unit"
                  value="pt"
                  checked={unit === 'pt'}
                  onChange={() => setUnit('pt')}
                />
                <span>포인트 (pt)</span>
              </label>
            </div>
          </div>

          {/* 상하좌우 여백 입력 박스 */}
          <div style={{ background: 'var(--color-bg)', padding: 14, borderRadius: 6 }}>
            <div style={{ textAlign: 'center', marginBottom: 8 }}>
              <label style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>위 (Top)</label>
              <br />
              <input
                className="form-input"
                type="number"
                min={0}
                value={top}
                onChange={(e) => setTop(parseFloat(e.target.value) || 0)}
                style={{ width: 70, textAlign: 'center', marginTop: 2 }}
              />
            </div>

            <div style={{ display: 'flex', justifyContent: 'space-around', alignItems: 'center' }}>
              <div>
                <label style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>왼쪽 (Left)</label>
                <br />
                <input
                  className="form-input"
                  type="number"
                  min={0}
                  value={left}
                  onChange={(e) => setLeft(parseFloat(e.target.value) || 0)}
                  style={{ width: 70, textAlign: 'center', marginTop: 2 }}
                />
              </div>

              <div style={{ color: 'var(--color-text-muted)', fontSize: 12 }}>
                ✂️ 자를 영역
              </div>

              <div>
                <label style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>오른쪽 (Right)</label>
                <br />
                <input
                  className="form-input"
                  type="number"
                  min={0}
                  value={right}
                  onChange={(e) => setRight(parseFloat(e.target.value) || 0)}
                  style={{ width: 70, textAlign: 'center', marginTop: 2 }}
                />
              </div>
            </div>

            <div style={{ textAlign: 'center', marginTop: 8 }}>
              <label style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>아래 (Bottom)</label>
              <br />
              <input
                className="form-input"
                type="number"
                min={0}
                value={bottom}
                onChange={(e) => setBottom(parseFloat(e.target.value) || 0)}
                style={{ width: 70, textAlign: 'center', marginTop: 2 }}
              />
            </div>
          </div>

          {/* 적용 대상 범위 */}
          <div>
            <label style={{ display: 'block', marginBottom: 6, fontWeight: 500 }}>
              적용 대상
            </label>
            <div style={{ display: 'flex', gap: 16 }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: 6, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="cropScope"
                  value="all"
                  checked={targetScope === 'all'}
                  onChange={() => setTargetScope('all')}
                />
                <span>전체 페이지 ({pages.length}장)</span>
              </label>

              <label style={{ display: 'flex', alignItems: 'center', gap: 6, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="cropScope"
                  value="selected"
                  checked={targetScope === 'selected'}
                  onChange={() => setTargetScope('selected')}
                  disabled={selectedPageIndices.length === 0}
                />
                <span>선택된 페이지만 ({selectedPageIndices.length}장)</span>
              </label>
            </div>
          </div>
        </div>

        <div className="modal__footer">
          <button className="btn btn--secondary" onClick={onClose}>
            취소
          </button>
          <button className="btn btn--primary" onClick={handleApply}>
            여백 자르기 적용
          </button>
        </div>
      </div>
    </div>
  )
}
