/**
 * 단축키 안내 모달
 */
interface Props {
  onClose: () => void
}

const SHORTCUTS: { category: string; keys: string; action: string }[] = [
  { category: '파일', keys: 'Ctrl+O', action: '파일 열기' },
  { category: '파일', keys: 'Ctrl+W', action: '문서만 닫기 (수정 시 저장 확인)' },
  { category: '파일', keys: 'Ctrl+S', action: '문서 저장' },
  { category: '파일', keys: 'Ctrl+Shift+S', action: '다른 이름으로 저장' },
  { category: '파일', keys: 'Ctrl+P', action: '인쇄 및 인쇄 내보내기' },
  { category: '편집', keys: 'Ctrl+Z', action: '실행 취소 (Undo)' },
  { category: '편집', keys: 'Ctrl+Y / Ctrl+Shift+Z', action: '다시 실행 (Redo)' },
  { category: '편집', keys: 'Ctrl+F', action: '본문 실시간 검색' },
  { category: '페이지', keys: 'Ctrl+A', action: '페이지 전체 선택' },
  { category: '편집', keys: 'Delete', action: '선택한 페이지 또는 주석 삭제' },
  { category: '편집', keys: 'Esc', action: '모달 / 리본 닫기 & 선택 초기화' },
  { category: '탐색', keys: '← / →', action: '이전 / 다음 페이지' },
  { category: '탐색', keys: 'PageUp / PageDown', action: '이전 / 다음 페이지' },
  { category: '탐색', keys: 'Home / End', action: '첫 / 마지막 페이지' },
  { category: '보기', keys: 'Ctrl+= / Ctrl+-', action: '화면 확대 / 축소' },
  { category: '보기', keys: 'Ctrl+0', action: '100% 실제 크기' },
  { category: '보기', keys: 'Ctrl + 마우스 휠', action: '썸네일 크기 확대/축소' },
  { category: '보기', keys: 'F4', action: '사이드바 패널 토글' },
  { category: '보기', keys: 'Ctrl+Shift+D', action: '다크 / 라이트 모드 전환' },
  { category: '주석', keys: 'Ctrl+C / Ctrl+V', action: '선택한 주석 복사 / 붙여넣기' },
  { category: '주석', keys: '더블 클릭 (텍스트)', action: '텍스트 박스 내용 즉시 재편집' }
]

export function ShortcutsModal({ onClose }: Props) {
  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div className="modal" onMouseDown={(e) => e.stopPropagation()} style={{ minWidth: 460 }}>
        <div className="modal__header">
          <span>⌨️ 키보드 단축키 안내</span>
          <button className="modal__close" onClick={onClose} aria-label="닫기">
            ×
          </button>
        </div>
        <div className="modal__body" style={{ maxHeight: '65vh', overflowY: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <thead>
              <tr style={{ borderBottom: '2px solid var(--color-border)', textAlign: 'left', color: 'var(--color-text-muted)', fontSize: 11 }}>
                <th style={{ padding: '6px 0', width: '18%' }}>분류</th>
                <th style={{ padding: '6px 0', width: '42%' }}>단축키</th>
                <th style={{ padding: '6px 0' }}>동작</th>
              </tr>
            </thead>
            <tbody>
              {SHORTCUTS.map((s, i) => (
                <tr key={i} style={{ borderBottom: '1px solid var(--color-border)' }}>
                  <td style={{ padding: '7px 0', color: 'var(--color-text-muted)', fontSize: 12 }}>
                    {s.category}
                  </td>
                  <td style={{ padding: '7px 0' }}>
                    <kbd
                      style={{
                        background: 'var(--color-bg)',
                        border: '1px solid var(--color-border)',
                        borderRadius: 4,
                        padding: '2px 7px',
                        fontFamily: 'Consolas, monospace',
                        fontSize: 12
                      }}
                    >
                      {s.keys}
                    </kbd>
                  </td>
                  <td style={{ padding: '7px 0' }}>{s.action}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="modal__footer">
          <button className="btn btn--primary" onClick={onClose}>
            닫기
          </button>
        </div>
      </div>
    </div>
  )
}
