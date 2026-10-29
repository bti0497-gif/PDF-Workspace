/** Page-stable, editable outline tree. */
import { useEffect, useState } from 'react'
import { useStore } from '../lib/store'
import {
  bindBookmarkTargets, deleteBookmark, findBookmark, generateBookmarkId,
  moveBookmark, updateBookmark, type BookmarkMove, type BookmarkNode
} from '../core/domain/bookmark'

export function BookmarkPanel() {
  const bookmarks = useStore(s => s.bookmarks)
  const pages = useStore(s => s.pages)
  const currentPage = useStore(s => s.currentPage)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [title, setTitle] = useState('')
  const [newTitle, setNewTitle] = useState('')
  const [deletePending, setDeletePending] = useState(false)
  const selected = selectedId ? findBookmark(bookmarks, selectedId) : undefined
  useEffect(() => { setTitle(selected?.title ?? ''); setDeletePending(false) }, [selected?.id, selected?.title])

  const commit = (change: (tree: BookmarkNode[]) => BookmarkNode[]) => {
    const state = useStore.getState()
    if (!state.document) return
    useStore.setState({ bookmarks: change(bindBookmarkTargets(state.bookmarks, state.pages)), isDirty: true })
  }
  const add = () => {
    const s = useStore.getState()
    const page = s.pages[s.currentPage]
    if (!page) return
    const id = generateBookmarkId()
    const node: BookmarkNode = { id, title: newTitle.trim() || `${s.currentPage + 1} 페이지`, targetPageIndex: s.currentPage, targetPageId: page.id, children: [], isExpanded: true }
    commit(tree => [...tree, node])
    setSelectedId(id)
    setNewTitle('')
  }
  const rename = () => {
    if (selectedId && title.trim()) commit(tree => updateBookmark(tree, selectedId, { title: title.trim() }))
  }
  const move = (direction: BookmarkMove) => selectedId && commit(tree => moveBookmark(tree, selectedId, direction))
  const retarget = () => {
    const s = useStore.getState()
    if (selectedId && s.pages[s.currentPage]) commit(tree => updateBookmark(tree, selectedId, { targetPageIndex: s.currentPage, targetPageId: s.pages[s.currentPage].id, dest: undefined }))
  }
  const pageIndex = (node: BookmarkNode) => node.targetPageId ? pages.findIndex(p => p.id === node.targetPageId) : node.targetPageIndex ?? -1
  const select = (node: BookmarkNode) => {
    setSelectedId(node.id)
    setDeletePending(false)
    const index = pageIndex(node)
    if (index >= 0 && index < pages.length) useStore.getState().setCurrentPage(index)
  }
  const buttonStyle = { padding: '5px 7px', fontSize: 12, whiteSpace: 'nowrap' as const }
  const inputStyle = { minWidth: 0, width: '100%', padding: '6px 7px', background: 'var(--color-bg)', border: '1px solid var(--color-border)', color: 'var(--color-text)', borderRadius: 4, fontSize: 12 }

  const renderNodes = (nodes: BookmarkNode[], depth: number): React.ReactNode => nodes.map(node => {
    const index = pageIndex(node)
    const expanded = node.isExpanded !== false
    const isSelected = node.id === selectedId
    return <div key={node.id} role="treeitem" aria-expanded={node.children.length ? expanded : undefined} aria-selected={isSelected}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 3, minHeight: 32, paddingLeft: 5 + depth * 14, borderRadius: 4, background: isSelected ? 'var(--color-accent, #1769aa)' : undefined, color: isSelected ? '#fff' : undefined }}
        onContextMenu={e => { e.preventDefault(); setSelectedId(node.id) }}>
        <button type="button" aria-label={`${node.title} ${expanded ? '접기' : '펼치기'}`} disabled={!node.children.length}
          onClick={() => commit(tree => updateBookmark(tree, node.id, { isExpanded: !expanded }))}
          style={{ width: 22, padding: 2, color: 'inherit', border: 0, background: 'transparent', opacity: node.children.length ? 1 : 0.2 }}>{node.children.length ? expanded ? '▾' : '▸' : '·'}</button>
        <button type="button" title={index >= 0 ? `${node.title} · ${index + 1}페이지` : `${node.title} · 이동 대상 없음`}
          onClick={() => select(node)} onDoubleClick={() => document.getElementById('bookmark-title-editor')?.focus()}
          style={{ flex: 1, minWidth: 0, display: 'flex', alignItems: 'center', gap: 6, textAlign: 'left', padding: '5px 3px', border: 0, background: 'transparent', color: 'inherit', cursor: 'pointer' }}>
          <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', fontSize: 12 }}>{node.title}</span>
          <span style={{ fontSize: 10, opacity: 0.8, whiteSpace: 'nowrap' }}>{index >= 0 ? `p.${index + 1}` : '대상 없음'}</span>
        </button>
      </div>
      {expanded && node.children.length > 0 && <div role="group">{renderNodes(node.children, depth + 1)}</div>}
    </div>
  })

  return <div style={{ display: 'flex', flexDirection: 'column', height: '100%', overflow: 'hidden' }}>
    <div style={{ padding: 10, borderBottom: '1px solid var(--color-border)', background: 'var(--color-bg-elevated)' }}>
      <div style={{ display: 'flex', gap: 5 }}>
        <input aria-label="새 책갈피 제목" value={newTitle} onChange={e => setNewTitle(e.target.value)} placeholder="새 책갈피 제목"
          onKeyDown={e => { if (e.key === 'Enter' && !e.nativeEvent.isComposing) add() }} style={inputStyle} />
        <button className="btn btn--primary" style={buttonStyle} onClick={add} disabled={!pages.length}>추가</button>
      </div>
      <div style={{ fontSize: 11, color: 'var(--color-text-muted)', marginTop: 7 }}>현재 {currentPage + 1}페이지에 책갈피 추가</div>
    </div>
    <div role="tree" aria-label="문서 책갈피" style={{ flex: 1, overflow: 'auto', padding: 6 }}>
      {bookmarks.length ? renderNodes(bookmarks, 0) : <div style={{ padding: '24px 10px', textAlign: 'center', color: 'var(--color-text-muted)', fontSize: 12 }}>등록된 책갈피가 없습니다.<br /><br />문서의 제목과 이동 위치를 등록하세요.</div>}
    </div>
    <div style={{ borderTop: '1px solid var(--color-border)', padding: 10, background: 'var(--color-bg-elevated)', display: 'flex', flexDirection: 'column', gap: 7 }}>
      <strong style={{ fontSize: 12 }}>책갈피 편집</strong>
      <div style={{ display: 'flex', gap: 5 }}>
        <input id="bookmark-title-editor" aria-label="선택 책갈피 제목" value={title} disabled={!selected} onChange={e => setTitle(e.target.value)}
          onKeyDown={e => { if (e.key === 'Enter' && !e.nativeEvent.isComposing) rename() }} style={inputStyle} placeholder="책갈피를 선택하세요" />
        <button className="btn" style={buttonStyle} disabled={!selected || !title.trim() || title.trim() === selected.title} onClick={rename}>변경</button>
      </div>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
        <button className="btn" style={buttonStyle} disabled={!selected} title="같은 계층에서 위로 이동" onClick={() => move('up')}>위로</button>
        <button className="btn" style={buttonStyle} disabled={!selected} title="같은 계층에서 아래로 이동" onClick={() => move('down')}>아래로</button>
        <button className="btn" style={buttonStyle} disabled={!selected} title="바로 앞 책갈피의 하위로 이동" onClick={() => move('indent')}>들여쓰기</button>
        <button className="btn" style={buttonStyle} disabled={!selected} title="부모 책갈피와 같은 계층으로 이동" onClick={() => move('outdent')}>내어쓰기</button>
      </div>
      <button className="btn" style={buttonStyle} disabled={!selected || !pages.length} onClick={retarget}>이동 대상을 현재 {currentPage + 1}페이지로 변경</button>
      {deletePending && selected ? <div style={{ border: '1px solid var(--color-danger, #b42318)', borderRadius: 4, padding: 7, fontSize: 11 }}>
        이 책갈피와 하위 책갈피를 삭제할까요? PDF 페이지는 유지됩니다.
        <div style={{ display: 'flex', gap: 5, marginTop: 6 }}>
          <button className="btn" style={buttonStyle} onClick={() => { commit(tree => deleteBookmark(tree, selected.id)); setSelectedId(null); setDeletePending(false) }}>삭제 확인</button>
          <button className="btn" style={buttonStyle} onClick={() => setDeletePending(false)}>취소</button>
        </div>
      </div> : <button className="btn" style={{ ...buttonStyle, color: 'var(--color-danger, #b42318)' }} disabled={!selected} onClick={() => setDeletePending(true)}>선택 책갈피 삭제</button>}
      <div style={{ fontSize: 10, color: 'var(--color-text-muted)', lineHeight: 1.5 }}>페이지를 옮겨도 대상이 따라갑니다. 대상 페이지를 삭제하면 ‘대상 없음’으로 표시됩니다.</div>
    </div>
  </div>
}
