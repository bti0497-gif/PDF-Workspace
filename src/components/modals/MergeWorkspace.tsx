import { useEffect, useRef, useState } from 'react'
import { PDFDocument } from 'pdf-lib'
import { getDocument, type PDFDocumentProxy } from 'pdfjs-dist'
import { useStore } from '../../lib/store'
import { clearRenderCache } from '../../hooks/usePdfRender'
import '../../lib/pdf-worker'
import './merge-workspace.css'
import { buildMergePlanPdf } from './merge-plan'

interface Props { onClose: () => void }
interface SourceFile { id: string; name: string; bytes: Uint8Array; doc: PDFDocumentProxy; count: number }
interface MergePage { id: string; sourceId: string; index: number; included: boolean }
interface IncomingFile { name: string; read: () => Promise<Uint8Array> }

function PagePreview({ source, index, large = false }: { source: SourceFile; index: number; large?: boolean }) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const hostRef = useRef<HTMLDivElement>(null)
  const [visible, setVisible] = useState(false)
  const [failed, setFailed] = useState(false)
  useEffect(() => {
    const host = hostRef.current
    if (!host) return
    const observer = new IntersectionObserver(([entry]) => {
      if (entry.isIntersecting) { setVisible(true); observer.disconnect() }
    }, { rootMargin: '180px' })
    observer.observe(host)
    return () => observer.disconnect()
  }, [])
  useEffect(() => {
    if (!visible) return
    let disposed = false
    let task: ReturnType<Awaited<ReturnType<PDFDocumentProxy['getPage']>>['render']> | undefined
    void (async () => {
      try {
        const pdfPage = await source.doc.getPage(index + 1)
        if (disposed || !canvasRef.current) return
        const base = pdfPage.getViewport({ scale: 1 })
        const viewport = pdfPage.getViewport({ scale: (large ? 150 : 58) / Math.max(base.width, base.height) })
        const canvas = canvasRef.current
        const ratio = Math.min(window.devicePixelRatio || 1, 2)
        canvas.width = Math.ceil(viewport.width * ratio)
        canvas.height = Math.ceil(viewport.height * ratio)
        canvas.style.width = `${viewport.width}px`
        canvas.style.height = `${viewport.height}px`
        task = pdfPage.render({ canvasContext: canvas.getContext('2d')!, viewport, transform: [ratio, 0, 0, ratio, 0, 0] })
        await task.promise
      } catch (error) {
        if (!disposed && !(error instanceof Error && error.name === 'RenderingCancelledException')) setFailed(true)
      }
    })()
    return () => { disposed = true; task?.cancel() }
  }, [visible, source, index, large])
  return <div ref={hostRef} className={`merge-preview ${large ? 'merge-preview--large' : ''}`}>
    {failed ? <span>미리보기 없음</span> : <canvas ref={canvasRef} aria-label={`${source.name} 원본 ${index + 1}쪽 미리보기`} />}
  </div>
}

export function MergeWorkspace({ onClose }: Props) {
  const [sources, setSources] = useState<SourceFile[]>([])
  const [pages, setPages] = useState<MergePage[]>([])
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [view, setView] = useState<'files' | 'result'>('files')
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [openAfterSave, setOpenAfterSave] = useState(false)
  const [outputName, setOutputName] = useState('병합된문서.pdf')
  const [dragOver, setDragOver] = useState<string | null>(null)
  const dragRef = useRef<{ kind: 'file' | 'page'; id: string } | null>(null)
  const docsRef = useRef<Set<PDFDocumentProxy>>(new Set())
  const busyRef = useRef(false)
  const alive = useRef(true)
  const fileInput = useRef<HTMLInputElement>(null)
  const selected = pages.filter((p) => p.included)
  const sourceMap = new Map(sources.map((s) => [s.id, s]))
  const setWorking = (value: boolean) => { busyRef.current = value; setBusy(value) }
  useEffect(() => {
    alive.current = true
    return () => { alive.current = false; for (const doc of docsRef.current) void doc.destroy(); docsRef.current.clear() }
  }, [])
  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { event.stopPropagation(); if (!busyRef.current) onClose() }
    }
    window.addEventListener('keydown', handler, true)
    return () => window.removeEventListener('keydown', handler, true)
  }, [onClose])

  const addFiles = async (incoming: IncomingFile[]) => {
    if (busyRef.current || incoming.length === 0) return
    setWorking(true); setError(null); setView('files')
    const added: SourceFile[] = []
    const failures: string[] = []
    for (let i = 0; i < incoming.length; i++) {
      const file = incoming[i]
      setStatus(`파일 읽는 중 ${i + 1}/${incoming.length}: ${file.name}`)
      try {
        const bytes = await file.read()
        const parsed = await PDFDocument.load(bytes)
        if (parsed.isEncrypted) throw new Error('암호화 PDF는 잠금을 해제한 뒤 추가하세요.')
        const doc = await getDocument({ data: bytes.slice(), cMapUrl: './cmaps/', cMapPacked: true, standardFontDataUrl: './standard_fonts/' }).promise
        if (!alive.current) { await doc.destroy(); break }
        docsRef.current.add(doc)
        added.push({ id: `merge_${Date.now()}_${i}_${Math.random().toString(36).slice(2)}`, name: file.name, bytes, doc, count: doc.numPages })
      } catch (err) { failures.push(`${file.name}: ${err instanceof Error ? err.message : '읽기 실패'}`) }
    }
    if (alive.current) {
      setSources((prev) => [...prev, ...added])
      setPages((prev) => [...prev, ...added.flatMap((file) => Array.from({ length: file.count }, (_, index) => ({ id: `${file.id}_${index}`, sourceId: file.id, index, included: true })))])
      setStatus(`${added.length}개 파일 추가됨`)
      if (failures.length) setError(failures.join('\n'))
      setWorking(false)
    }
  }
  const addBrowserFiles = (list: FileList | File[]) => addFiles(Array.from(list)
    .filter((file) => /\.pdf$/i.test(file.name) || file.type === 'application/pdf')
    .map((file) => ({ name: file.name, read: async () => new Uint8Array(await file.arrayBuffer()) })))
  const pickFiles = async () => {
    if (busyRef.current) return
    if (!navigator.userAgent.includes('Electron')) { fileInput.current?.click(); return }
    try {
      const paths = await window.workspace.openMultiple()
      await addFiles(paths.map((path) => ({ name: path.split(/[\\/]/).pop() || path, read: async () => new Uint8Array(await window.workspace.readFile(path)) })))
    } catch (err) { setError(err instanceof Error ? err.message : '파일 선택 실패') }
  }
  const removeFile = (id: string) => {
    setSources((prev) => prev.filter((s) => s.id !== id))
    setPages((prev) => prev.filter((p) => p.sourceId !== id))
  }
  const moveFile = (id: string, target: string) => {
    if (id === target) return
    setSources((prev) => {
      const file = prev.find((s) => s.id === id)!
      const rest = prev.filter((s) => s.id !== id)
      rest.splice(rest.findIndex((s) => s.id === target), 0, file)
      return rest
    })
    setPages((prev) => {
      const group = prev.filter((p) => p.sourceId === id)
      const rest = prev.filter((p) => p.sourceId !== id)
      const targetIndex = rest.findIndex((p) => p.sourceId === target)
      rest.splice(targetIndex < 0 ? rest.length : targetIndex, 0, ...group)
      return rest
    })
  }
  const movePage = (id: string, target: string) => {
    if (id === target) return
    setPages((prev) => {
      const moving = prev.find((p) => p.id === id)
      if (!moving) return prev
      const rest = prev.filter((p) => p.id !== id)
      rest.splice(rest.findIndex((p) => p.id === target), 0, moving)
      return rest
    })
  }
  const stepPage = (id: string, dir: number) => {
    const list = view === 'result' ? selected : pages.filter((p) => p.sourceId === pages.find((p) => p.id === id)?.sourceId)
    const index = list.findIndex((p) => p.id === id)
    const target = list[index + dir]
    if (!target) return
    setPages((prev) => { const next = [...prev]; const a = next.findIndex((p) => p.id === id); const b = next.findIndex((p) => p.id === target.id); [next[a], next[b]] = [next[b], next[a]]; return next })
  }
  const dropInternal = (kind: 'file' | 'page', target: string) => {
    if (busyRef.current) return
    const drag = dragRef.current
    if (drag?.kind === kind) {
      if (kind === 'file') moveFile(drag.id, target)
      else if (view === 'result' || pages.find((p) => p.id === drag.id)?.sourceId === pages.find((p) => p.id === target)?.sourceId) movePage(drag.id, target)
    }
    dragRef.current = null; setDragOver(null)
  }
  const save = async () => {
    if (busyRef.current || selected.length === 0) return
    if (openAfterSave && useStore.getState().isDirty && !window.confirm('현재 문서에 저장하지 않은 변경 사항이 있습니다. 병합 결과를 열면 현재 변경 사항을 잃습니다. 계속하시겠습니까?\n\n현재 문서를 유지하려면 취소 후 “저장 후 결과 열기”를 해제하세요.')) return
    setWorking(true); setError(null)
    try {
      setStatus('선택한 페이지를 최종 순서로 병합하는 중...')
      const bytes = await buildMergePlanPdf(new Map(sources.map((source) => [source.id, source.bytes])), pages)
      const name = outputName.trim() || '병합된문서.pdf'
      setStatus('새 PDF 저장 위치를 선택하세요.')
      const savedPath = await window.workspace.savePdfBytes(bytes, /\.pdf$/i.test(name) ? name : `${name}.pdf`)
      if (!savedPath) { setStatus('저장을 취소했습니다. 편집한 순서는 유지됩니다.'); return }
      if (openAfterSave) {
        const { loadPdfFromBytes } = await import('../../hooks/usePdfDocument')
        const { meta, dimensions } = await loadPdfFromBytes(bytes, savedPath.split(/[\\/]/).pop()!, savedPath)
        clearRenderCache(); useStore.getState().setDocument(meta, dimensions)
        onClose()
      } else setStatus(`${selected.length}쪽을 새 PDF로 저장했습니다. 현재 문서는 그대로 유지됩니다.`)
    } catch (err) { setError(`병합 실패: ${err instanceof Error ? err.message : String(err)}`) }
    finally { if (alive.current) setWorking(false) }
  }

  const pageCard = (entry: MergePage, resultIndex?: number) => {
    const source = sourceMap.get(entry.sourceId)!
    return <article key={entry.id} className={`merge-page-card ${!entry.included ? 'is-excluded' : ''} ${dragOver === entry.id ? 'is-drop-target' : ''}`}
      draggable={!busy} onDragStart={(event) => { event.stopPropagation(); dragRef.current = { kind: 'page', id: entry.id }; event.dataTransfer.effectAllowed = 'move'; event.dataTransfer.setData('text/plain', entry.id) }}
      onDragEnd={() => { dragRef.current = null; setDragOver(null) }}
      onDragOver={(event) => { if (dragRef.current?.kind === 'page') { event.preventDefault(); event.stopPropagation(); setDragOver(entry.id) } }}
      onDrop={(event) => { if (dragRef.current?.kind === 'page') { event.preventDefault(); event.stopPropagation(); dropInternal('page', entry.id) } }}>
      <PagePreview source={source} index={entry.index} large />
      <strong>{resultIndex === undefined ? `원본 ${entry.index + 1}쪽` : `결과 ${resultIndex + 1}쪽`}</strong>
      <small title={source.name}>{source.name}</small>
      {resultIndex !== undefined && <small>원본 {entry.index + 1}쪽</small>}
      <div className="merge-page-actions">
        <button type="button" disabled={busy} title="앞으로 이동" aria-label={`${source.name} ${entry.index + 1}쪽 앞으로 이동`} onClick={() => stepPage(entry.id, -1)}>←</button>
        <label><input type="checkbox" disabled={busy} checked={entry.included} onChange={() => setPages((prev) => prev.map((p) => p.id === entry.id ? { ...p, included: !p.included } : p))} />포함</label>
        <button type="button" disabled={busy} title="뒤로 이동" aria-label={`${source.name} ${entry.index + 1}쪽 뒤로 이동`} onClick={() => stepPage(entry.id, 1)}>→</button>
      </div>
    </article>
  }

  return <div className="modal-backdrop" onDragOver={(event) => { event.preventDefault(); event.stopPropagation() }} onDrop={(event) => {
    event.preventDefault(); event.stopPropagation(); if (!busyRef.current && event.dataTransfer.files.length) void addBrowserFiles(event.dataTransfer.files)
  }}>
    <section className="modal merge-workspace" role="dialog" aria-modal="true" aria-labelledby="merge-title" aria-busy={busy} onKeyDown={(event) => {
      event.stopPropagation()
      if ((event.ctrlKey || event.metaKey) && ['s', 'o', 'p'].includes(event.key.toLowerCase())) event.preventDefault()
      if (event.key === 'Tab') {
        const controls = Array.from(event.currentTarget.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled):not([hidden]), [tabindex="0"]'))
        const first = controls[0]; const last = controls[controls.length - 1]
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
      }
    }}>
      <header className="modal__header"><div><strong id="merge-title">PDF 병합 · 페이지 구성</strong><p className="merge-subtitle">원본 파일은 변경하지 않습니다. 파일과 페이지를 배치한 뒤 새 PDF로 저장하세요.</p></div><button className="modal__close" disabled={busy} onClick={onClose} aria-label="병합 창 닫기">×</button></header>
      <div className="merge-toolbar">
        <button className="btn btn--primary" autoFocus disabled={busy} onClick={() => void pickFiles()}>파일 추가</button>
        <input ref={fileInput} type="file" accept="application/pdf,.pdf" multiple hidden onChange={(event) => { if (event.target.files) void addBrowserFiles(event.target.files); event.target.value = '' }} />
        <div className="merge-view-switch" role="group" aria-label="병합 작업 보기"><button disabled={busy} aria-pressed={view === 'files'} onClick={() => setView('files')}>1. 원본 파일 · 페이지 선택</button><button disabled={busy || selected.length === 0} aria-pressed={view === 'result'} onClick={() => setView('result')}>2. 결과 순서 미리보기</button></div>
        <span className="merge-summary">{sources.length}개 파일 · {selected.length} / {pages.length}쪽 포함</span>
      </div>
      <div className="merge-content">
        {sources.length === 0 ? <div className="merge-empty"><strong>PDF 파일을 이곳으로 끌어 놓으세요</strong><p>여러 파일을 한 번에 추가하고 페이지별로 확인할 수 있습니다.</p><button className="btn" disabled={busy} onClick={() => void pickFiles()}>PDF 선택</button></div> : view === 'result' ? <><p className="merge-hint">왼쪽부터 최종 순서입니다. 페이지를 원하는 페이지 앞으로 끌어 놓거나 화살표로 이동하세요. 원본 파일이 달라도 이동할 수 있습니다.</p><div className="merge-page-grid">{selected.map((entry, index) => pageCard(entry, index))}</div></> : <>
          <p className="merge-hint">파일 제목 줄을 끌어 파일 묶음을 이동하세요. 페이지를 펼쳐 필요한 쪽만 포함하고, 파일 사이의 페이지 이동은 결과 순서 미리보기에서 하세요.</p>
          {sources.map((source, index) => { const localPages = pages.filter((p) => p.sourceId === source.id); const included = localPages.filter((p) => p.included).length; return <section key={source.id} className={`merge-source ${dragOver === source.id ? 'is-drop-target' : ''}`} onDragOver={(event) => { if (dragRef.current?.kind === 'file') { event.preventDefault(); setDragOver(source.id) } }} onDrop={(event) => { if (dragRef.current?.kind === 'file') { event.preventDefault(); event.stopPropagation(); dropInternal('file', source.id) } }}>
            <div className="merge-source-header" draggable={!busy} onDragStart={(event) => { dragRef.current = { kind: 'file', id: source.id }; event.dataTransfer.effectAllowed = 'move'; event.dataTransfer.setData('text/plain', source.id) }} onDragEnd={() => { dragRef.current = null; setDragOver(null) }}>
              <span className="merge-file-number">{index + 1}</span><PagePreview source={source} index={0} />
              <div className="merge-source-name"><strong title={source.name}>{source.name}</strong><small>원본 {source.count}쪽 · {included}쪽 포함</small></div>
              <button className="btn" disabled={busy} aria-expanded={expanded.has(source.id)} onClick={() => setExpanded((prev) => { const next = new Set(prev); next.has(source.id) ? next.delete(source.id) : next.add(source.id); return next })}>{expanded.has(source.id) ? '페이지 접기' : '페이지 펼치기'}</button>
              <button className="btn" disabled={busy || index === 0} aria-label={`${source.name} 파일 위로 이동`} onClick={() => moveFile(source.id, sources[index - 1].id)}>↑</button>
              <button className="btn" disabled={busy || index === sources.length - 1} aria-label={`${source.name} 파일 아래로 이동`} onClick={() => moveFile(sources[index + 1].id, source.id)}>↓</button>
              <button className="btn" disabled={busy} aria-label={`${source.name} 파일 제거`} onClick={() => removeFile(source.id)}>제거</button>
            </div>
            {expanded.has(source.id) && <><div className="merge-selection-tools"><button className="btn" disabled={busy} onClick={() => setPages((prev) => prev.map((p) => p.sourceId === source.id ? { ...p, included: true } : p))}>전체 포함</button><button className="btn" disabled={busy} onClick={() => setPages((prev) => prev.map((p) => p.sourceId === source.id ? { ...p, included: false } : p))}>전체 제외</button></div><div className="merge-page-grid">{localPages.map((entry) => pageCard(entry))}</div></>}
          </section> })}
        </>}
      </div>
      <div className="merge-feedback" role="status">{status || '페이지 순서를 확인한 후 새 PDF로 저장하세요.'}</div>
      {error && <div className="merge-error" role="alert">{error}</div>}
      <footer className="modal__footer merge-footer"><div className="merge-save-options"><label>파일 이름 <input className="form-input" value={outputName} disabled={busy} onChange={(event) => setOutputName(event.target.value)} /></label><label><input type="checkbox" checked={openAfterSave} disabled={busy} onChange={(event) => setOpenAfterSave(event.target.checked)} /> 저장 후 결과 열기 <span>(현재 문서 교체)</span></label><small>페이지 내용 기준 병합입니다. 원본 책갈피·문서 속성·전자서명은 유지되지 않을 수 있습니다.</small></div><button className="btn" disabled={busy} onClick={onClose}>닫기</button>{view === 'files' ? <button className="btn btn--primary" disabled={busy || selected.length === 0} onClick={() => setView('result')}>결과 확인</button> : <button className="btn btn--primary" disabled={busy || selected.length === 0} onClick={() => void save()}>{busy ? '처리 중...' : '새 PDF로 저장'}</button>}</footer>
    </section>
  </div>
}
