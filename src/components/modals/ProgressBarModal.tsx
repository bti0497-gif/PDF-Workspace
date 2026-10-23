/**
 * 대용량 PDF 로드 / 페이지 추가 / 병합 작업 시 실시간 진행률 프로그레스 바 모달
 */
import { useStore } from '../../lib/store'

export function ProgressBarModal() {
  const loadingProgress = useStore((s) => s.loadingProgress)
  if (!loadingProgress || !loadingProgress.visible) return null

  const pct = Math.min(100, Math.max(0, Math.round(loadingProgress.percent)))

  return (
    <div className="progress-modal-backdrop">
      <div className="progress-modal-card">
        <div className="progress-modal-header">
          <div className="progress-modal-spinner" />
          <div className="progress-modal-title">{loadingProgress.title}</div>
        </div>
        {loadingProgress.detail && (
          <div className="progress-modal-detail">{loadingProgress.detail}</div>
        )}
        <div className="progress-modal-track">
          <div className="progress-modal-fill" style={{ width: `${pct}%` }} />
        </div>
        <div className="progress-modal-footer">
          <span className="progress-modal-status">잠시만 기다려주세요...</span>
          <span className="progress-modal-percent">{pct}%</span>
        </div>
      </div>
    </div>
  )
}
