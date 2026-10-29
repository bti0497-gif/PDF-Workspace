import { useEffect, useRef, useState } from 'react'
import { useStore } from '../../lib/store'
import './options-modal.css'

const COLORS = [
  ['#10b981', '초록'], ['#f59e0b', '금색'], ['#0e6fd6', '파랑'],
  ['#f43f5e', '빨강'], ['#8b5cf6', '보라']
] as const

export function OptionsModal({ onClose }: { onClose: () => void }) {
  const [draft, setDraft] = useState(() => {
    const s = useStore.getState()
    return { theme: s.theme, selectionColor: s.selectionColor, thumbnailSize: s.thumbnailSize,
      thumbnailColumns: s.thumbnailColumns, sidebarWidth: s.sidebarWidth }
  })
  const root = useRef<HTMLDivElement>(null)
  useEffect(() => { root.current?.querySelector<HTMLSelectElement>('select')?.focus() }, [])
  const apply = () => {
    const s = useStore.getState()
    s.setTheme(draft.theme)
    s.setSelectionColor(draft.selectionColor)
    s.setThumbnailSize(draft.thumbnailSize)
    s.setThumbnailColumns(draft.thumbnailColumns)
    s.setSidebarWidth(draft.sidebarWidth)
    onClose()
  }
  return <div className="modal-backdrop">
    <div ref={root} className="modal appearance-options" role="dialog" aria-modal="true" aria-labelledby="options-title"
      onKeyDown={e => {
        e.stopPropagation()
        if (e.key === 'Escape') { e.preventDefault(); onClose(); return }
        if ((e.ctrlKey || e.metaKey) && ['w', 's', 'o', 'p', 'f', 'z', 'y'].includes(e.key.toLowerCase())) { e.preventDefault(); return }
        if (e.key === 'Tab') {
          const controls = Array.from(root.current?.querySelectorAll<HTMLElement>('button:not(:disabled),select:not(:disabled),input:not(:disabled)') ?? [])
          const first = controls[0], last = controls[controls.length - 1]
          if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last?.focus() }
          else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first?.focus() }
        }
      }}>
      <div className="modal__header"><span id="options-title">옵션</span><button className="modal__close" aria-label="옵션 닫기" onClick={onClose}>×</button></div>
      <div className="modal__body appearance-options__body">
        <p className="appearance-options__hint">자주 바꾸지 않는 표시 설정을 한곳에서 관리합니다. PDF 내용은 변경하지 않습니다.</p>
        <fieldset><legend>화면</legend>
          <label className="appearance-options__row"><span>화면 테마</span><select aria-label="화면 테마" value={draft.theme} onChange={e => setDraft({ ...draft, theme: e.target.value as 'light' | 'dark' })}><option value="light">밝게</option><option value="dark">어둡게</option></select></label>
        </fieldset>
        <fieldset><legend>썸네일과 페이지 선택</legend>
          <label className="appearance-options__row"><span>썸네일 열 수</span><select aria-label="옵션 썸네일 열 수" value={draft.thumbnailColumns} onChange={e => setDraft({ ...draft, thumbnailColumns: Number(e.target.value) as 1 | 2 | 3 })}>{[1,2,3].map(n => <option key={n} value={n}>{n}열</option>)}</select></label>
          <label className="appearance-options__row"><span>썸네일 크기</span><span className="appearance-options__range"><input type="range" min={72} max={260} step={4} aria-label="옵션 썸네일 크기" value={draft.thumbnailSize} onChange={e => setDraft({ ...draft, thumbnailSize: Number(e.target.value) })}/><output>{draft.thumbnailSize}px</output></span></label>
          <label className="appearance-options__row"><span>왼쪽 패널 폭</span><span className="appearance-options__range"><input type="range" min={170} max={640} step={1} aria-label="왼쪽 패널 폭" value={draft.sidebarWidth} onChange={e => setDraft({ ...draft, sidebarWidth: Number(e.target.value) })}/><output>{draft.sidebarWidth}px</output></span></label>
          <div className="appearance-options__color-label">선택 강조 색상</div>
          <div className="appearance-options__colors">{COLORS.map(([color,label]) => <button type="button" className="appearance-options__color" key={color} aria-label={`${label} 선택 강조색`} aria-pressed={draft.selectionColor.toLowerCase() === color} onClick={() => setDraft({ ...draft, selectionColor: color })}><span style={{ background: color }}/>{label}</button>)}</div>
          <label className="appearance-options__row"><span>사용자 지정 색상</span><span className="appearance-options__custom"><input type="color" aria-label="사용자 지정 선택 강조색" value={draft.selectionColor} onChange={e => setDraft({ ...draft, selectionColor: e.target.value })}/><output>{draft.selectionColor.toUpperCase()}</output></span></label>
          <div className="appearance-options__sample" style={{ borderColor: draft.selectionColor }}><span style={{ background: draft.selectionColor }} aria-hidden="true"/>선택 표시 미리보기</div>
          <p className="appearance-options__hint">작업 중 크기·열 수는 썸네일 위에서 빠르게 바꿀 수 있습니다. 열이 많거나 패널이 좁으면 썸네일은 칸에 맞게 줄어듭니다.</p>
        </fieldset>
        <p className="appearance-options__hint">설정 저장 시 현재 화면에 적용되며, 다음 실행에도 유지됩니다.</p>
      </div>
      <div className="modal__footer"><button className="btn" style={{ marginRight: 'auto' }} onClick={() => setDraft({ theme: 'light', selectionColor: '#10b981', thumbnailSize: 96, thumbnailColumns: 2, sidebarWidth: 220 })}>기본값</button><button className="btn" onClick={onClose}>취소</button><button className="btn btn--primary" onClick={apply}>설정 저장</button></div>
    </div>
  </div>
}
