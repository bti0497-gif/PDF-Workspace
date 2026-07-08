/**
 * 정보 모달
 */
import { pdfjsVersion } from '../../lib/pdf-worker'

interface Props {
  onClose: () => void
}

export function AboutModal({ onClose }: Props) {
  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div className="modal" onMouseDown={(e) => e.stopPropagation()} style={{ minWidth: 380 }}>
        <div className="modal__header">
          <span>PDF Workspace 정보</span>
          <button className="modal__close" onClick={onClose} aria-label="닫기">
            ×
          </button>
        </div>
        <div className="modal__body" style={{ textAlign: 'center' }}>
          <div style={{ fontSize: 48, marginBottom: 12 }}>📄</div>
          <h2 style={{ marginBottom: 4 }}>PDF Workspace</h2>
          <p style={{ color: 'var(--color-text-muted)', marginBottom: 18 }}>
            버전 1.0.0
          </p>
          <div
            style={{
              textAlign: 'left',
              background: 'var(--color-bg)',
              padding: 14,
              borderRadius: 6,
              fontSize: 12,
              lineHeight: 1.9
            }}
          >
            <div>렌더링 엔진: PDF.js v{pdfjsVersion} (Mozilla)</div>
            <div>편집 라이브러리: pdf-lib</div>
            <div>프레임워크: Electron + React</div>
            <div style={{ marginTop: 8, color: 'var(--color-text-muted)' }}>
              오픈소스 소프트웨어로 제작되었습니다.
            </div>
          </div>
        </div>
        <div className="modal__footer">
          <button className="btn btn--primary" onClick={onClose}>
            확인
          </button>
        </div>
      </div>
    </div>
  )
}
