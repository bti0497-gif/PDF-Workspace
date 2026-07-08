/**
 * 단축키 안내 모달
 */
interface Props {
  onClose: () => void
}

const SHORTCUTS: { keys: string; action: string }[] = [
  { keys: 'Ctrl+O', action: '파일 열기' },
  { keys: 'Ctrl+S', action: '저장' },
  { keys: 'Ctrl+Shift+S', action: '다른 이름으로 저장' },
  { keys: 'Ctrl+W', action: '문서 닫기' },
  { keys: '← / →', action: '이전 / 다음 페이지' },
  { keys: 'Home / End', action: '첫 / 마지막 페이지' },
  { keys: 'Ctrl+= / Ctrl+-', action: '확대 / 축소' },
  { keys: 'Ctrl+0', action: '실제 크기' },
  { keys: 'Ctrl+1~5', action: '도구 선택 (선택/형광펜/사각형/텍스트/그리기)' },
  { keys: 'Delete', action: '선택한 주석 삭제' },
  { keys: 'Ctrl+Shift+← / →', action: '페이지 회전' },
  { keys: 'F4', action: '썸네일 패널 토글' },
  { keys: 'Ctrl+Shift+D', action: '다크 모드 전환' }
]

export function ShortcutsModal({ onClose }: Props) {
  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div className="modal" onMouseDown={(e) => e.stopPropagation()}>
        <div className="modal__header">
          <span>단축키</span>
          <button className="modal__close" onClick={onClose} aria-label="닫기">
            ×
          </button>
        </div>
        <div className="modal__body">
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <tbody>
              {SHORTCUTS.map((s, i) => (
                <tr key={i} style={{ borderBottom: '1px solid var(--color-border)' }}>
                  <td style={{ padding: '8px 0', width: '45%' }}>
                    <kbd
                      style={{
                        background: 'var(--color-bg)',
                        border: '1px solid var(--color-border)',
                        borderRadius: 4,
                        padding: '2px 8px',
                        fontFamily: 'Consolas, monospace',
                        fontSize: 12
                      }}
                    >
                      {s.keys}
                    </kbd>
                  </td>
                  <td style={{ padding: '8px 0' }}>{s.action}</td>
                </tr>
              ))}
            </tbody>
          </table>
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
