import { useEffect, useRef, useState } from 'react'
import * as pdfjs from 'pdfjs-dist'
import { useStore } from '../lib/store'
import { bakeManifestToPdf } from '../core/services/pdf-engine.service'

/** A transformed page displays the actual export, not an unmodified source canvas. */
export function ProcessedPageView({ pageIndex, zoom }: { pageIndex: number; zoom: number }) {
  const page = useStore(s => s.pages[pageIndex])
  const sources = useStore(s => s.sources)
  const annotations = useStore(s => s.annotations)
  const activeTool = useStore(s => s.activeTool)
  const canvas = useRef<HTMLCanvasElement>(null)
  const text = useRef<HTMLDivElement>(null)
  const [error, setError] = useState('')
  useEffect(() => {
    let disposed = false
    let doc: pdfjs.PDFDocumentProxy | undefined
    let render: pdfjs.RenderTask | undefined
    let textLayer: pdfjs.TextLayer | undefined
    void (async () => {
      try {
        const rawFont = await window.workspace.getKoreanFont()
        const bytes = await bakeManifestToPdf({ pages: [page], sources,
          annotations: annotations.filter(a => a.pageIndex === pageIndex).map(a => ({ ...a, pageIndex: 0 })),
          customFont: rawFont ? new Uint8Array(rawFont) : undefined })
        if (disposed) return
        doc = await pdfjs.getDocument({ data: bytes, cMapUrl: './cmaps/', cMapPacked: true, standardFontDataUrl: './standard_fonts/' }).promise
        const pdfPage = await doc.getPage(1)
        if (disposed || !canvas.current || !text.current) return
        const dpr = window.devicePixelRatio || 1
        const vp = pdfPage.getViewport({ scale: zoom })
        canvas.current.width = Math.ceil(vp.width * dpr); canvas.current.height = Math.ceil(vp.height * dpr)
        canvas.current.style.width = `${vp.width}px`; canvas.current.style.height = `${vp.height}px`
        text.current.style.width = `${vp.width}px`; text.current.style.height = `${vp.height}px`
        render = pdfPage.render({ canvasContext: canvas.current.getContext('2d')!, viewport: pdfPage.getViewport({ scale: zoom * dpr }) })
        await render.promise
        if (disposed) return
        text.current.replaceChildren(); text.current.style.setProperty('--scale-factor', String(zoom))
        textLayer = new pdfjs.TextLayer({ textContentSource: await pdfPage.getTextContent(), container: text.current, viewport: vp })
        await textLayer.render()
      } catch (e) { if (!disposed) setError(e instanceof Error ? e.message : String(e)) }
    })()
    return () => { disposed = true; render?.cancel(); textLayer?.cancel(); if (doc) void doc.destroy() }
  }, [page, pageIndex, sources, annotations, zoom])
  return <><canvas ref={canvas} className="page-canvas" /><div ref={text} className="text-layer" />
    {activeTool !== 'select' && <div role="status" style={{ position:'absolute',left:8,top:8,right:8,padding:10,background:'var(--color-bg-elevated)',border:'1px solid var(--color-border)',zIndex:25,fontSize:12 }}>용지 크기·자르기 결과 미리보기입니다. 본문·주석을 더 편집하려면 저장 후 다시 열어 주세요.</div>}
    {error && <div role="alert">미리보기 실패: {error}</div>}</>
}
