import { useEffect, useRef, useState } from 'react'
import { useStore } from '../lib/store'
import { renderPageToCanvas } from '../hooks/usePdfRender'
import { getOrLoadPdfDoc, readPageGeometries } from '../hooks/usePdfDocument'
import { extractSelectedPagesAction, insertBlankPageAt } from '../hooks/useMenuActions'
import type { VirtualPage } from '../core/domain/page'
import './page-organizer.css'

function PagePreview({ page, index, size }: { page: VirtualPage; index: number; size: number }) {
  const ref = useRef<HTMLCanvasElement>(null)
  const holder = useRef<HTMLDivElement>(null)
  const [visible, setVisible] = useState(false)
  const [error, setError] = useState('')
  useEffect(() => {
    const observer = new IntersectionObserver(entries => { if (entries[0].isIntersecting) setVisible(true) }, { rootMargin: '250px' })
    if (holder.current) observer.observe(holder.current)
    return () => observer.disconnect()
  }, [])
  useEffect(() => {
    if (!visible || !ref.current) return
    const controller = new AbortController()
    const w = page.width ?? 595.28, h = page.height ?? 841.89
    const zoom = size / Math.max(w, h)
    void renderPageToCanvas({ canvas: ref.current, pageIndex: index, zoom, rotation: page.rotation, signal: controller.signal })
      .catch(e => { if (!controller.signal.aborted) setError(String(e.message)) })
    return () => controller.abort()
  }, [visible, page, index, size])
  return <div ref={holder} className="organizer-preview" style={{ height: size + 16 }}>
    <canvas ref={ref} aria-label={`${index + 1}페이지 미리보기`} />
    {!visible && <span>미리보기 대기</span>}
    {error && <small>{error}</small>}
    {page.cropMargins && <span className="organizer-badge">자르기 설정</span>}
    {!!page.textReplacements?.length && <span className="organizer-badge">텍스트 수정</span>}
  </div>
}

export function PageOrganizer({ onRead, onCrop, onMerge }: { onRead: () => void; onCrop: () => void; onMerge: () => void }) {
  const state = useStore()
  const [size, setSize] = useState(164)
  const [dragged, setDragged] = useState<number[]>([])
  const [dropAt, setDropAt] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const fileInput = useRef<HTMLInputElement>(null)
  const selected = state.selectedPageIndices
  const target = selected.length ? selected : [state.currentPage]
  const run = async (action: () => unknown) => { try { setError(''); await action() } catch (e) { setError(e instanceof Error ? e.message : String(e)) } }
  async function insertFiles(files: File[], index: number) {
    setBusy(true)
    try {
      let at = index
      for (const file of files) {
        if (!/\.pdf$/i.test(file.name)) continue
        const bytes = new Uint8Array(await file.arrayBuffer())
        const source = `organizer_${crypto.randomUUID()}`
        const pdf = await getOrLoadPdfDoc(source, bytes)
        const pageGeometries = await readPageGeometries(pdf)
        useStore.getState().insertPagesFromDocument(source, file.name, bytes, pdf.numPages, at, { ...pageGeometries[0], pageGeometries })
        at += pdf.numPages
      }
    } finally { setBusy(false) }
  }
  function reorder(to: number) {
    if (dragged.length) state.reorderMultiplePages(dragged, to)
    setDragged([]); setDropAt(null)
  }
  return <main className="page-organizer" aria-label="페이지 정리 작업공간" onDragOver={e => { e.preventDefault(); e.stopPropagation() }} onDrop={e => {
    e.preventDefault(); e.stopPropagation()
    if (busy) return
    if (e.dataTransfer.files.length) void run(() => insertFiles(Array.from(e.dataTransfer.files), dropAt ?? state.pages.length))
    else reorder(dropAt ?? state.pages.length)
    setDropAt(null)
  }}>
    <div className="organizer-heading"><div><h2>페이지 정리</h2><p>페이지를 선택해 끌어 옮기거나, PDF 파일을 원하는 위치에 놓으세요.</p></div><button className="btn" onClick={onRead}>문서 보기</button></div>
    <div className="organizer-toolbar" role="toolbar" aria-label="페이지 정리 도구">
      <button className="btn" disabled={busy} onClick={() => fileInput.current?.click()}>PDF 삽입</button>
      <input ref={fileInput} type="file" accept=".pdf" multiple hidden onChange={e => { const files = Array.from(e.target.files ?? []); e.target.value = ''; void run(() => insertFiles(files, state.currentPage + 1)) }} />
      <button className="btn" disabled={busy} onClick={onMerge}>문서 병합</button>
      <button className="btn" disabled={busy} onClick={() => void run(() => insertBlankPageAt(state.currentPage + 1))}>빈 페이지</button>
      <span className="organizer-separator" />
      <button className="btn" disabled={busy || !selected.length} onClick={() => state.rotatePages(target, 'left')}>왼쪽 회전</button>
      <button className="btn" disabled={busy || !selected.length} onClick={() => state.rotatePages(target, 'right')}>오른쪽 회전</button>
      <button className="btn" disabled={busy || !selected.length} onClick={onCrop}>자르기</button>
      <button className="btn" disabled={busy || !selected.length} onClick={() => state.duplicatePages(target)}>복제</button>
      <button className="btn" disabled={busy || !selected.length} onClick={() => void run(() => extractSelectedPagesAction(target))}>추출</button>
      <button className="btn" disabled={busy || !selected.length || selected.length === state.pages.length} onClick={() => state.deletePages(target)}>삭제</button>
    </div>
    <div className="organizer-selection"><span><strong>{selected.length}</strong>장 선택 / 전체 {state.pages.length}장</span><button onClick={state.selectAllPages}>전체 선택</button><button onClick={() => state.selectPagesByPredicate('none')}>선택 해제</button><span className="organizer-hint">Ctrl: 개별 선택 · Shift: 범위 선택 · 두 번 클릭: 문서 보기</span><label>썸네일 <input aria-label="페이지 정리 썸네일 크기" type="range" min={110} max={240} value={size} onChange={e => setSize(+e.target.value)} /></label></div>
    {error && <p role="alert" className="organizer-error">{error}</p>}
    {busy && <p role="status">페이지를 읽고 있습니다...</p>}
    <div className="organizer-grid" style={{ gridTemplateColumns: `repeat(auto-fill, minmax(${size + 42}px, 1fr))` }}>
      {state.pages.map((p, i) => <div key={p.id} role="button" tabIndex={0} aria-label={`${i + 1}페이지 선택`} aria-pressed={selected.includes(i)}
        className={`organizer-card ${selected.includes(i) ? 'is-selected' : ''} ${dropAt === i ? 'drop-before' : ''}`}
        draggable={!busy} onDragStart={e => { const indices = selected.includes(i) ? selected : [i]; if (!selected.includes(i)) state.selectPage(i, 'single'); setDragged(indices); e.dataTransfer.setData('application/x-workspace-pages', JSON.stringify(indices)); e.dataTransfer.effectAllowed = 'move' }}
        onDragOver={e => { e.preventDefault(); e.stopPropagation(); const r = e.currentTarget.getBoundingClientRect(); setDropAt(e.clientX > r.left + r.width / 2 ? i + 1 : i) }}
        onDragEnd={() => { setDropAt(null); setDragged([]) }}
        onClick={e => state.selectPage(i, e.shiftKey ? 'range' : e.ctrlKey || e.metaKey ? 'toggle' : 'single')}
        onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); state.selectPage(i, e.shiftKey ? 'range' : e.ctrlKey ? 'toggle' : 'single') } }}
        onDoubleClick={() => { state.selectPage(i, 'single'); onRead() }}>
        <PagePreview page={p} index={i} size={size} />
        <div className="organizer-page-number">{i + 1}<span>{selected.includes(i) ? '선택됨' : ''}</span></div>
        <div className="organizer-source" title={state.sources.get(p.sourceDocId)?.fileName}>{state.sources.get(p.sourceDocId)?.fileName}</div>
        <small>원본 {p.sourcePageIndex + 1}쪽</small>
      </div>)}
      <button className={`organizer-add ${dropAt === state.pages.length ? 'drop-before' : ''}`} onDragOver={e => { e.preventDefault(); setDropAt(state.pages.length) }} onClick={() => fileInput.current?.click()}>+ PDF 추가</button>
    </div>
  </main>
}
