/**
 * 정보 모달
 */
import { pdfjsVersion } from '../../lib/pdf-worker'
import splashLogo from '../../assets/splash-logo.png'

interface Props {
  onClose: () => void
}

export function AboutModal({ onClose }: Props) {
  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div className="modal" onMouseDown={(e) => e.stopPropagation()} style={{ minWidth: 400 }}>
        <div className="modal__header">
          <span>더존 PDF 편집기 정보</span>
          <button className="modal__close" onClick={onClose} aria-label="닫기">
            ×
          </button>
        </div>
        <div className="modal__body" style={{ textAlign: 'center' }}>
          <div style={{ marginBottom: 14 }}>
            <img
              src={splashLogo}
              alt="더존 PDF 편집기"
              style={{ width: 220, borderRadius: 12, boxShadow: '0 4px 12px rgba(0,0,0,0.2)' }}
            />
          </div>
          <h2 style={{ marginBottom: 4 }}>더존 PDF 편집기</h2>
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
            <div>편집 엔진: pdf-lib (High Performance Vector Engine)</div>
            <div>플랫폼: Electron Desktop</div>
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
