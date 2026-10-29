/**
 * 정보 모달 (자동 업데이트 확인 기능 포함)
 */
import { useState, useEffect } from 'react'
import { pdfjsVersion } from '../../lib/pdf-worker'
import splashLogo from '../../assets/splash-logo.png'

interface Props {
  onClose: () => void
}

export function AboutModal({ onClose }: Props) {
  const [checking, setChecking] = useState(false)
  const [updateMsg, setUpdateMsg] = useState<string | null>(null)

  useEffect(() => {
    // 실시간 업데이트 진행 상태 수신
    const unsubscribe = window.workspace.onUpdaterStatus?.((data) => {
      if (data.status === 'available') {
        setUpdateMsg(`새 버전(v${data.version})이 발견되어 다운로드를 시작합니다.`)
      } else if (data.status === 'not-available') {
        setUpdateMsg('현재 최신 버전을 사용 중입니다.')
        setChecking(false)
      } else if (data.status === 'downloading') {
        setUpdateMsg(`새 버전 다운로드 중... (${data.percent ?? 0}%)`)
      } else if (data.status === 'downloaded') {
        setUpdateMsg(`새 버전(v${data.version}) 다운로드가 완료되었습니다. 앱 종료 시 자동 적용됩니다.`)
        setChecking(false)
      } else if (data.status === 'error') {
        setUpdateMsg(data.message || '업데이트 확인 중 오류가 발생했습니다.')
        setChecking(false)
      }
    })
    return () => {
      unsubscribe?.()
    }
  }, [])

  const handleCheckUpdate = async () => {
    setChecking(true)
    setUpdateMsg('업데이트 서버 확인 중...')
    try {
      const res = await window.workspace.checkForUpdates?.()
      if (res?.message) {
        setUpdateMsg(res.message)
      }
      if (res?.status === 'dev' || res?.status === 'web') {
        setChecking(false)
      }
    } catch (err: any) {
      setUpdateMsg(`확인 오류: ${err?.message || String(err)}`)
      setChecking(false)
    }
  }

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

          <div style={{ marginTop: 16 }}>
            <button
              className="btn btn--secondary"
              onClick={handleCheckUpdate}
              disabled={checking}
              style={{
                width: '100%',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                gap: 6
              }}
            >
              <span>{checking ? '⏳' : '🔄'}</span>
              <span>{checking ? '업데이트 확인 중...' : '최신 업데이트 확인'}</span>
            </button>
            {updateMsg && (
              <div
                style={{
                  marginTop: 10,
                  fontSize: 12,
                  color: 'var(--color-primary, #0e6fd6)',
                  wordBreak: 'break-word',
                  padding: '6px 10px',
                  background: 'rgba(14, 111, 214, 0.08)',
                  borderRadius: 4
                }}
              >
                {updateMsg}
              </div>
            )}
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
