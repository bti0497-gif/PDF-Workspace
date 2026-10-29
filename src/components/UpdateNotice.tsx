import { useEffect, useState } from 'react'
import type { UpdateCheckResult } from '../../electron/shared/types'

export function UpdateNotice({ onDetails }: { onDetails: () => void }) {
  const [state, setState] = useState<UpdateCheckResult>({ status: 'idle' })
  useEffect(() => {
    let active = true
    let receivedEvent = false
    const unsubscribe = window.workspace.onUpdaterStatus?.((next) => { receivedEvent = true; setState(next) })
    void window.workspace.getUpdateStatus?.().then(next => { if (active && !receivedEvent) setState(next) }).catch(() => {})
    return () => { active = false; unsubscribe?.() }
  }, [])
  if (!state.readyToInstall && !['available', 'downloading', 'restarting'].includes(state.status)) return null
  const text = state.status === 'restarting' ? '업데이트를 적용하고 재시작합니다.'
    : state.readyToInstall ? `새 버전 ${state.version ?? ''} 설치 준비가 됐습니다. 문서를 저장한 뒤 적용하세요.`
    : `새 버전 ${state.version ?? ''} 자동 다운로드 중${state.percent == null ? '' : ` (${state.percent}%)`}`
  return <div role="status" style={{ flexShrink: 0, display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8, padding: '5px 12px', background: 'var(--color-bg-active)', color: 'var(--color-text)', fontSize: 12 }}>
    <span style={{ flex: '1 1 240px' }}>{text}</span>
    <button className="btn btn--secondary" onClick={onDetails} disabled={state.status === 'restarting'}>업데이트 상세</button>
  </div>
}
