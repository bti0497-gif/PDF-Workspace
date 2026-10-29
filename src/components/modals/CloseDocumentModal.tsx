import { useEffect, useRef, useState } from 'react'

export function CloseDocumentModal({ fileName, onSave, onDiscard, onCancel }: {
  fileName: string
  onSave: () => Promise<boolean>
  onDiscard: () => void
  onCancel: () => void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const dialog = useRef<HTMLDivElement>(null)
  const cancel = useRef<HTMLButtonElement>(null)
  useEffect(() => { cancel.current?.focus() }, [])
  const save = async () => {
    if (busy) return
    setBusy(true); setError('')
    try {
      if (!await onSave()) setError('저장이 취소되었거나 새 변경 사항이 남아 있어 문서를 닫지 않았습니다.')
    } catch (err) {
      setError(`저장하지 못해 문서를 그대로 열어두었습니다. ${err instanceof Error ? err.message : String(err)}`)
    } finally { setBusy(false) }
  }
  return <div className="modal-backdrop">
    <div className="modal" role="dialog" aria-modal="true" aria-labelledby="close-document-title" ref={dialog}
      style={{ width: 460, minWidth: 0, maxWidth: 'calc(100vw - 32px)' }}
      onKeyDown={e => {
        e.stopPropagation()
        if ((e.ctrlKey || e.metaKey) && ['w', 's', 'o', 'p', 'f', 'z', 'y'].includes(e.key.toLowerCase())) { e.preventDefault(); return }
        if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); if (!busy) onCancel() }
        if (e.key === 'Tab') {
          const buttons = Array.from(dialog.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? [])
          if (!buttons.length) { e.preventDefault(); return }
          const first = buttons[0], last = buttons[buttons.length - 1]
          if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus() }
          else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus() }
        }
      }}>
      <div className="modal__header"><span id="close-document-title">문서를 닫기 전에 저장할까요?</span></div>
      <div className="modal__body">
        <p style={{ margin: '0 0 12px', fontWeight: 600, overflowWrap: 'anywhere' }}>{fileName}</p>
        <p style={{ margin: 0, lineHeight: 1.6 }}>수정한 내용이 있습니다. 저장하지 않고 닫으면 마지막 저장 이후의 변경 사항은 사라집니다.</p>
        <p style={{ margin: '10px 0 0', fontSize: 12, color: 'var(--color-text-muted)' }}>앱은 종료되지 않으며 문서 열기 화면으로 돌아갑니다.</p>
        {error && <p role="alert" style={{ color: 'var(--color-danger, #c22)', lineHeight: 1.5 }}>{error}</p>}
      </div>
      <div className="modal__footer" style={{ flexWrap: 'wrap' }}>
        <button className="btn" onClick={onDiscard} disabled={busy}>저장 안 함</button>
        <button className="btn" ref={cancel} onClick={onCancel} disabled={busy}>취소</button>
        <button className="btn btn--primary" onClick={() => void save()} disabled={busy}>{busy ? '저장 중...' : '저장 후 닫기'}</button>
      </div>
    </div>
  </div>
}
