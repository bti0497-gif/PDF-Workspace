/**
 * 조판 크기 일치화 (표준 판형 정규화) 모달
 * - A4, Letter, A3 등 목표 판형 일괄 변환
 * - 비율 유지 여백 맞춤(Letterbox) / 용지 채우기(Fill)
 * - 가로/세로 방향 지능적 자동 감지
 */
import { useState } from 'react'
import { useStore } from '../../lib/store'
import { STANDARD_PAGE_FORMATS } from '../../core/domain/page'
import type { PageFitMode } from '../../core/domain/page'

interface Props {
  onClose: () => void
}

export function NormalizeModal({ onClose }: Props) {
  const pages = useStore((s) => s.pages)
  const selectedPageIndices = useStore((s) => s.selectedPageIndices)
  const normalizePages = useStore((s) => s.normalizePages)

  const [formatId, setFormatId] = useState('a4')
  const [fitMode, setFitMode] = useState<PageFitMode>('letterbox')
  const [autoOrientation, setAutoOrientation] = useState(true)
  const [targetScope, setTargetScope] = useState<'all' | 'selected'>(
    selectedPageIndices.length > 1 ? 'selected' : 'all'
  )

  const handleApply = () => {
    const format =
      STANDARD_PAGE_FORMATS.find((f) => f.id === formatId) ??
      STANDARD_PAGE_FORMATS[0]

    const targetIndices =
      targetScope === 'all'
        ? pages.map((_, i) => i)
        : selectedPageIndices

    normalizePages(targetIndices, {
      targetWidth: format.width,
      targetHeight: format.height,
      fitMode,
      autoOrientation
    })

    onClose()
  }

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div className="modal" onMouseDown={(e) => e.stopPropagation()} style={{ minWidth: 420 }}>
        <div className="modal__header">
          <span>조판 규격 일치화 (표준 판형 통일)</span>
          <button className="modal__close" onClick={onClose} aria-label="닫기">
            ×
          </button>
        </div>

        <div className="modal__body" style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          {/* 목표 규격 선택 */}
          <div>
            <label style={{ display: 'block', marginBottom: 6, fontWeight: 500 }}>
              목표 용지 규격
            </label>
            <select
              className="form-select"
              value={formatId}
              onChange={(e) => setFormatId(e.target.value)}
              style={{ width: '100%', padding: '6px 8px' }}
            >
              {STANDARD_PAGE_FORMATS.map((f) => (
                <option key={f.id} value={f.id}>
                  {f.name}
                </option>
              ))}
            </select>
          </div>

          {/* 배치 맞춤 방식 */}
          <div>
            <label style={{ display: 'block', marginBottom: 6, fontWeight: 500 }}>
              배치 맞춤 방식
            </label>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="fitMode"
                  value="letterbox"
                  checked={fitMode === 'letterbox'}
                  onChange={() => setFitMode('letterbox')}
                />
                <div>
                  <strong>비율 유지 여백 맞춤 (Letterbox)</strong>
                  <div style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>
                    원본 왜곡 없이 용지 안에 꽉 차도록 배치하고 남는 공간은 흰색 여백 추가 (권장)
                  </div>
                </div>
              </label>

              <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="fitMode"
                  value="fill"
                  checked={fitMode === 'fill'}
                  onChange={() => setFitMode('fill')}
                />
                <div>
                  <strong>용지 꽉 채우기 (Fill / Crop)</strong>
                  <div style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>
                    여백 없이 용지에 꽉 차게 확대하고 넘치는 영역은 크롭
                  </div>
                </div>
              </label>

              <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="fitMode"
                  value="stretch"
                  checked={fitMode === 'stretch'}
                  onChange={() => setFitMode('stretch')}
                />
                <div>
                  <strong>늘이기 (Stretch)</strong>
                  <div style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>
                    비율 상관없이 용지 가로/세로 크기에 딱 맞춤
                  </div>
                </div>
              </label>
            </div>
          </div>

          {/* 지능적 방향 감지 */}
          <div style={{ background: 'var(--color-bg)', padding: 10, borderRadius: 4 }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
              <input
                type="checkbox"
                checked={autoOrientation}
                onChange={(e) => setAutoOrientation(e.target.checked)}
              />
              <span style={{ fontSize: 12 }}>
                가로/세로 방향 자동 감지 (가로 문서는 가로 판형으로 최적 정렬)
              </span>
            </label>
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
                  name="targetScope"
                  value="all"
                  checked={targetScope === 'all'}
                  onChange={() => setTargetScope('all')}
                />
                <span>전체 페이지 ({pages.length}장)</span>
              </label>

              <label style={{ display: 'flex', alignItems: 'center', gap: 6, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="targetScope"
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
            조판 크기 일치시키기
          </button>
        </div>
      </div>
    </div>
  )
}
