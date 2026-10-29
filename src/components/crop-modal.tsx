/** Direct-manipulation crop: absolute margins relative to the original CropBox. */
import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import { PDFDocument } from 'pdf-lib'
import * as pdfjs from 'pdfjs-dist'
import { useStore } from '../lib/store'
import { bakeManifestToPdf } from '../core/services/pdf-engine.service'
import type { CropMargins } from '../core/domain/page'
import { displayMargins, pdfMargins, validCrop, ZERO_CROP, MIN_CROP_SIZE } from './crop-geometry'
import './crop-workspace.css'

interface Props { onClose: () => void }
type Edge = keyof CropMargins
type Corner = 'nw' | 'ne' | 'sw' | 'se'
type Geometry = { width: number; height: number; rotation: number }
const LABELS: Record<Edge, string> = { top: '위', bottom: '아래', left: '왼쪽', right: '오른쪽' }

export function CropModal({ onClose }: Props) {
  const pages = useStore(s => s.pages)
  const sources = useStore(s => s.sources)
  const annotations = useStore(s => s.annotations)
  const currentPage = useStore(s => s.currentPage)
  const selected = useStore(s => s.selectedPageIndices)
  const [previewIndex, setPreviewIndex] = useState(currentPage)
  const [scope, setScope] = useState<'current' | 'selected' | 'all'>(selected.length > 1 ? 'selected' : 'current')
  const [unit, setUnit] = useState<'mm' | 'pt'>('mm')
  const [margins, setMargins] = useState<CropMargins>(() => displayMargins(pages[currentPage]?.cropMargins ?? ZERO_CROP, pages[currentPage]?.rotation ?? 0))
  const [geometry, setGeometry] = useState<Geometry[]>([])
  const [preview, setPreview] = useState<{ url: string; width: number; height: number } | null>(null)
  const [busy, setBusy] = useState(true)
  const [error, setError] = useState('')
  const sheetRef = useRef<HTMLDivElement>(null)
  const dragRef = useRef<{ startX: number; startY: number; corner?: Corner; original: CropMargins } | null>(null)
  const factor = unit === 'mm' ? 72 / 25.4 : 1
  const targets = scope === 'all' ? pages.map((_, i) => i) : scope === 'selected' ? selected : [previewIndex]
  const g = geometry[previewIndex]
  const rotated = g && (g.rotation % 180 !== 0)
  const visualWidth = g ? (rotated ? g.height : g.width) : 1
  const visualHeight = g ? (rotated ? g.width : g.height) : 1
  const invalid = targets.find(i => {
    const meta = geometry[i]
    return !meta || !validCrop(pdfMargins(margins, meta.rotation), meta.width, meta.height)
  })
  const canApply = !busy && !!preview && targets.length > 0 && invalid === undefined && !error

  useEffect(() => {
    let disposed = false
    void (async () => {
      try {
        const docs = new Map<string, PDFDocument>()
        for (const [id, source] of sources) docs.set(id, await PDFDocument.load(source.bytes))
        const result = pages.map(p => {
          const source = docs.get(p.sourceDocId)?.getPage(p.sourcePageIndex)
          if (!source) throw new Error('원본 페이지 정보를 읽을 수 없습니다.')
          let { width, height } = source.getCropBox()
          if (p.normalizedConfig) {
            width = p.normalizedConfig.targetWidth
            height = p.normalizedConfig.targetHeight
            const size = source.getSize()
            if (p.normalizedConfig.autoOrientation && (size.width > size.height) !== (width > height)) [width, height] = [height, width]
          }
          return { width, height, rotation: ((p.rotation % 360) + 360) % 360 }
        })
        if (!disposed) setGeometry(result)
      } catch (e) { if (!disposed) { setError(e instanceof Error ? e.message : '페이지 크기 확인 실패'); setBusy(false) } }
    })()
    return () => { disposed = true }
  }, [pages, sources])

  useEffect(() => {
    let disposed = false
    let doc: pdfjs.PDFDocumentProxy | undefined
    let task: pdfjs.RenderTask | undefined
    setBusy(true)
    setPreview(null)
    setError('')
    void (async () => {
      try {
        const p = pages[previewIndex]
        if (!p) throw new Error('미리 볼 페이지가 없습니다.')
        const pageAnnotations = annotations.filter(a => a.pageIndex === previewIndex).map(a => ({ ...a, pageIndex: 0 }))
        const needsFont = !!(p.textReplacements?.length || p.watermarkConfig || p.pageNumberConfig || pageAnnotations.length)
        const font = needsFont ? await window.workspace.getKoreanFont() : null
        const bytes = await bakeManifestToPdf({ pages: [{ ...p, cropMargins: undefined }], sources, annotations: pageAnnotations,
          customFont: font ? new Uint8Array(font) : undefined })
        if (disposed) return
        doc = await pdfjs.getDocument({ data: bytes, cMapUrl: './cmaps/', cMapPacked: true, standardFontDataUrl: './standard_fonts/' }).promise
        if (disposed) return
        const pdfPage = await doc.getPage(1)
        const size = pdfPage.getViewport({ scale: 1 })
        const scale = Math.min(540 / size.width, 490 / size.height, 1.5)
        const viewport = pdfPage.getViewport({ scale: scale * Math.min(window.devicePixelRatio || 1, 2) })
        const canvas = document.createElement('canvas')
        canvas.width = Math.ceil(viewport.width)
        canvas.height = Math.ceil(viewport.height)
        const ctx = canvas.getContext('2d')
        if (!ctx) throw new Error('미리보기 캔버스를 만들 수 없습니다.')
        task = pdfPage.render({ canvasContext: ctx, viewport })
        await task.promise
        if (!disposed) setPreview({ url: canvas.toDataURL('image/png'), width: size.width * scale, height: size.height * scale })
      } catch (e) { if (!disposed) setError(e instanceof Error ? e.message : '미리보기 생성 실패') }
      finally { if (!disposed) setBusy(false); if (doc) void doc.destroy() }
    })()
    return () => { disposed = true; task?.cancel(); if (doc) void doc.destroy() }
  }, [pages, sources, previewIndex, annotations])

  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); onClose() }
      else if ((e.ctrlKey || e.metaKey) && ['s', 'z', 'y', 'o', 'p'].includes(e.key.toLowerCase())) { e.preventDefault(); e.stopPropagation() }
    }
    window.addEventListener('keydown', handleKey, true)
    return () => window.removeEventListener('keydown', handleKey, true)
  }, [onClose])

  const point = (e: ReactPointerEvent) => {
    const rect = sheetRef.current!.getBoundingClientRect()
    return { x: Math.max(0, Math.min(visualWidth, (e.clientX - rect.left) / rect.width * visualWidth)),
      y: Math.max(0, Math.min(visualHeight, (e.clientY - rect.top) / rect.height * visualHeight)) }
  }
  const start = (e: ReactPointerEvent, corner?: Corner) => {
    if (busy || !g || e.button !== 0) return
    e.preventDefault(); e.stopPropagation()
    const p = point(e)
    dragRef.current = { startX: p.x, startY: p.y, corner, original: { ...margins } }
    sheetRef.current!.setPointerCapture(e.pointerId)
  }
  const move = (e: ReactPointerEvent) => {
    const d = dragRef.current
    if (!d || !g) return
    const p = point(e)
    let x1 = d.original.left, x2 = visualWidth - d.original.right
    let y1 = d.original.top, y2 = visualHeight - d.original.bottom
    if (d.corner) {
      if (d.corner.includes('w')) x1 = Math.min(p.x, x2 - MIN_CROP_SIZE)
      else x2 = Math.max(p.x, x1 + MIN_CROP_SIZE)
      if (d.corner.includes('n')) y1 = Math.min(p.y, y2 - MIN_CROP_SIZE)
      else y2 = Math.max(p.y, y1 + MIN_CROP_SIZE)
    } else {
      x1 = Math.min(d.startX, p.x); x2 = Math.max(d.startX, p.x)
      y1 = Math.min(d.startY, p.y); y2 = Math.max(d.startY, p.y)
    }
    setMargins({ left: Math.max(0, x1), top: Math.max(0, y1), right: Math.max(0, visualWidth - x2), bottom: Math.max(0, visualHeight - y2) })
  }
  const finish = () => {
    const d = dragRef.current
    if (!d) return
    if (!validCrop(margins, visualWidth, visualHeight)) setMargins(d.original)
    dragRef.current = null
  }
  const apply = () => {
    if (!canApply) return
    const state = useStore.getState()
    const indices = new Set(targets)
    const next = state.pages.map((p, i) => {
      if (!indices.has(i)) return p
      const meta = geometry[i]
      const raw = pdfMargins(margins, meta.rotation)
      return { ...p, cropMargins: raw, width: meta.width - raw.left - raw.right, height: meta.height - raw.top - raw.bottom }
    })
    // Rotation-dependent margins still form one atomic Undo action.
    useStore.setState({ pages: next, isDirty: true, history: { past: [...state.history.past, state.pages], future: [] } })
    onClose()
  }
  const selection = { left: `${margins.left / visualWidth * 100}%`, top: `${margins.top / visualHeight * 100}%`,
    width: `${Math.max(0, visualWidth - margins.left - margins.right) / visualWidth * 100}%`,
    height: `${Math.max(0, visualHeight - margins.top - margins.bottom) / visualHeight * 100}%` }

  return <div className="modal-backdrop">
    <div className="modal crop-workspace" role="dialog" aria-modal="true" aria-labelledby="crop-title">
      <div className="modal__header"><span id="crop-title">페이지 자르기</span><button className="modal__close" onClick={onClose} aria-label="자르기 취소">×</button></div>
      <div className="modal__body crop-workspace__body">
        <div className="crop-workspace__preview">
          <div style={{ color: '#fff', fontSize: 12 }}>남길 영역을 드래그하세요. 모서리 손잡이로 크기를 조절할 수 있습니다.</div>
          {busy && <p role="status" style={{ color: '#fff' }}>페이지 미리보기 준비 중...</p>}
          {preview && <div ref={sheetRef} className="crop-workspace__sheet" aria-label="페이지 자르기 미리보기" style={{ width: preview.width, aspectRatio: `${preview.width} / ${preview.height}`, maxWidth: '100%' }}
            onPointerDown={e => start(e)} onPointerMove={move} onPointerUp={finish} onLostPointerCapture={finish}
            onPointerCancel={() => { if (dragRef.current) setMargins(dragRef.current.original); dragRef.current = null }}>
            <img src={preview.url} alt={`${previewIndex + 1}쪽 원본 용지 미리보기`} draggable={false} style={{ width: '100%', height: '100%', display: 'block', pointerEvents: 'none' }} />
            <div className="crop-workspace__selection" style={selection} />
            {(['nw', 'ne', 'sw', 'se'] as Corner[]).map(c => <button key={c} type="button" className="crop-workspace__handle" tabIndex={-1} aria-label={`${c} 자르기 크기 조절`}
              onPointerDown={e => start(e, c)} style={{ left: `${(c.includes('w') ? margins.left : visualWidth - margins.right) / visualWidth * 100}%`,
                top: `${(c.includes('n') ? margins.top : visualHeight - margins.bottom) / visualHeight * 100}%`, cursor: c === 'nw' || c === 'se' ? 'nwse-resize' : 'nesw-resize' }} />)}
          </div>}
        </div>
        <div className="crop-workspace__settings">
          <label>미리보기 페이지 <select className="form-input" value={previewIndex} onChange={e => setPreviewIndex(Number(e.target.value))}>
            {pages.map((_, i) => <option key={i} value={i}>{i + 1} / {pages.length}</option>)}
          </select></label>
          <label>여백 단위 <select className="form-input" value={unit} onChange={e => setUnit(e.target.value as 'mm' | 'pt')}><option value="mm">밀리미터 (mm)</option><option value="pt">포인트 (pt)</option></select></label>
          <div className="crop-workspace__fields">{(['top', 'bottom', 'left', 'right'] as Edge[]).map(edge => <label key={edge}>{LABELS[edge]} ({unit})
            <input className="form-input" aria-label={`${LABELS[edge]} 여백`} type="number" min="0" step="0.1" value={Number((margins[edge] / factor).toFixed(2))}
              onChange={e => { const n = Number(e.target.value); if (Number.isFinite(n)) setMargins(m => ({ ...m, [edge]: Math.max(0, n * factor) })) }} />
          </label>)}</div>
          <button className="btn" onClick={() => setMargins({ ...ZERO_CROP })}>여백 초기화 (원본 영역)</button>
          <button className="btn" onClick={() => setMargins(displayMargins(pages[previewIndex]?.cropMargins ?? ZERO_CROP, pages[previewIndex]?.rotation ?? 0))}>기존 자르기 설정으로 되돌리기</button>
          <fieldset className="crop-workspace__scope"><legend>적용 대상</legend>
            <label><input type="radio" name="crop-scope" checked={scope === 'current'} onChange={() => setScope('current')} />현재 미리보기 페이지 ({previewIndex + 1}쪽)</label>
            <label><input type="radio" name="crop-scope" checked={scope === 'selected'} disabled={!selected.length} onChange={() => setScope('selected')} />선택 페이지 ({selected.length}장)</label>
            <label><input type="radio" name="crop-scope" checked={scope === 'all'} onChange={() => setScope('all')} />전체 페이지 ({pages.length}장)</label>
          </fieldset>
          {g && <p className="crop-workspace__hint">남는 크기: {(Math.max(0, visualWidth - margins.left - margins.right) / factor).toFixed(1)} × {(Math.max(0, visualHeight - margins.top - margins.bottom) / factor).toFixed(1)} {unit}</p>}
          {invalid !== undefined && geometry.length > 0 && <p className="crop-workspace__error" role="alert">{invalid + 1}쪽의 남는 영역이 너무 작습니다. 가로·세로 최소 10pt가 필요합니다. 여백 또는 적용 범위를 줄이세요.</p>}
          {error && <p className="crop-workspace__error" role="alert">{error}</p>}
          <p className="crop-workspace__hint">여백은 화면 방향 기준이며, 여러 페이지에 같은 길이로 적용됩니다. 자르기는 내용을 삭제하지 않고 표시 영역만 변경합니다. 보안용 정보 삭제가 아닙니다.</p>
        </div>
      </div>
      <div className="modal__footer"><span style={{ marginRight: 'auto', fontSize: 12 }}>적용 대상 {targets.length}장</span><button className="btn" onClick={onClose}>취소</button><button className="btn btn--primary" disabled={!canApply} onClick={apply}>자르기 적용</button></div>
    </div>
  </div>
}
