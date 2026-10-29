/** Explicit opt-in only. Rasterize edited pages before copying into output so old text is not retained. */
import type { VirtualPage } from '../domain/page'
import { PDFDocument } from 'pdf-lib'
export async function rasterizedTextSource(bytes: Uint8Array, v: VirtualPage): Promise<PDFDocument> {
  if (v.normalizedConfig) throw new Error('본문 수정과 조판 규격 변경을 함께 저장할 수 없습니다. 먼저 수정 사본을 저장하세요.')
  if (!v.textReplacements?.every(t=>t.safeRasterize)) throw new Error('기존 본문 수정은 원문이 남을 수 있습니다. 수정 문구를 다시 열고 안전 교체 안내에 동의한 후 적용하세요.')
  const { getDocument } = await import('pdfjs-dist')
  const task = getDocument({data:bytes.slice(),cMapUrl:'./cmaps/',cMapPacked:true,standardFontDataUrl:'./standard_fonts/'})
  const pdf = await task.promise
  try {
    const p = await pdf.getPage(v.sourcePageIndex+1)
    if(p.view[0]!==0||p.view[1]!==0) throw new Error('원점이 이동된 PDF의 본문 교체는 지원하지 않습니다.')
    const unit=p.getViewport({scale:1,rotation:0})
    const scale=Math.min(3,5000/Math.max(unit.width,unit.height))
    const viewport=p.getViewport({scale,rotation:0})
    const canvas=document.createElement('canvas')
    canvas.width=Math.ceil(viewport.width);canvas.height=Math.ceil(viewport.height)
    const ctx=canvas.getContext('2d')!
    await p.render({canvasContext:ctx,viewport,background:'#ffffff'}).promise
    for(const t of v.textReplacements??[]) {
      ctx.fillStyle=t.backgroundColor??'#ffffff'
      ctx.fillRect(t.vx*scale,t.vy*scale,(t.origVw??t.vw)*scale,t.vh*scale)
    }
    const blob=await new Promise<Blob>((resolve,reject)=>canvas.toBlob(b=>b?resolve(b):reject(new Error('본문 교체 이미지 생성 실패')),'image/png'))
    const result=await PDFDocument.create()
    const image=await result.embedPng(await blob.arrayBuffer())
    const page=result.addPage([unit.width,unit.height])
    page.drawImage(image,{x:0,y:0,width:unit.width,height:unit.height})
    canvas.width=canvas.height=0
    return result
  } finally { await pdf.destroy() }
}
