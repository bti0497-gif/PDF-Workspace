/**
 * 정보 모달 (자동 업데이트 확인 기능 포함)
 */
import { useState, useEffect, useRef } from 'react'
import { version as appVersion } from '../../../package.json'
import { useStore } from '../../lib/store'
import type { UpdateCheckResult } from '../../../electron/shared/types'
import { pdfjsVersion } from '../../lib/pdf-worker'
import splashLogo from '../../assets/splash-logo.png'

interface Props {
  onClose: () => void
}

export function AboutModal({ onClose }: Props) {
  const [updateState, setUpdateState] = useState<UpdateCheckResult>({ status: 'idle' })
  const [installing, setInstalling] = useState(false)
  const eventSequence = useRef(0)
  const checking = ['checking', 'available', 'downloading'].includes(updateState.status)
  const updateMsg = updateState.message || ({
    checking: '업데이트 서버를 확인하고 있습니다.',
    available: '새 버전을 찾았습니다. 자동 다운로드를 시작합니다.',
    'not-available': '현재 최신 버전을 사용 중입니다.',
    downloading: '새 버전 다운로드 중: ' + (updateState.percent ?? 0) + '%',
    downloaded: '새 버전 ' + (updateState.version ?? '') + ' 다운로드 완료. 문서를 저장한 뒤 재시작하여 적용하세요.',
    restarting: '업데이트를 적용하고 재시작합니다.',
    error: '업데이트 처리 중 오류가 발생했습니다.'
  } as Record<string, string>)[updateState.status]

  useEffect(() => {
    let active = true
    const initialSequence = eventSequence.current
    const unsubscribe = window.workspace.onUpdaterStatus?.((data) => {
      eventSequence.current++
      if (data.status === 'error') setInstalling(false)
      setUpdateState(data)
    })
    void window.workspace.getUpdateStatus?.().then((data) => {
      if (active && eventSequence.current === initialSequence) setUpdateState(data)
    }).catch(() => {})
    return () => { active = false; unsubscribe?.() }
  }, [])

  const handleCheckUpdate = async () => {
    const sequence = eventSequence.current
    setUpdateState((old) => ({ ...old, status: 'checking', message: undefined }))
    try {
      const result = await window.workspace.checkForUpdates()
      if (eventSequence.current === sequence) setUpdateState(result)
    } catch (error: any) {
      setUpdateState((old) => ({ ...old, status: 'error', message: error?.message || String(error) }))
    }
  }
  const handleInstallUpdate = async () => {
    const current = useStore.getState()
    if (current.isDirty || current.isLoading) {
      setUpdateState((old) => ({ ...old, message: '편집한 문서를 먼저 저장하고 진행 중인 작업이 끝난 뒤 적용해 주세요.' }))
      return
    }
    setInstalling(true)
    try {
      const result = await window.workspace.installUpdate()
      setUpdateState(result)
      if (result.status !== 'restarting') setInstalling(false)
    } catch (error: any) {
      setInstalling(false)
      setUpdateState((old) => ({ ...old, status: 'error', message: error?.message || String(error) }))
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
            버전 {appVersion}
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
              disabled={checking || installing}
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
            {updateState.readyToInstall && (
              <button className="btn btn--primary" onClick={handleInstallUpdate} disabled={installing}
                style={{ width: '100%', marginTop: 8 }}>
                {installing ? '재시작 준비 중...' : '재시작하여 업데이트 적용'}
              </button>
            )}
            <p style={{ marginTop: 8, fontSize: 11, color: 'var(--color-text-muted)' }}>
              새 버전은 자동 다운로드하며, 설치는 저장 후 재시작을 선택할 때만 진행됩니다.
            </p>
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
