/** Conservative PDF text selection: preserve original run/font boundaries, never merge cells by proximity. */
import { getPdfDoc } from '../../hooks/usePdfDocument'
import type { VirtualPage } from '../domain/page'
export interface EditableTextItem {
  id: string; str: string; pdfX: number; pdfY: number; pdfWidth: number; pdfHeight: number
  fontSize: number; vx: number; vy: number; vw: number; vh: number
}
const cache = new Map<string, EditableTextItem[]>()
export function clearTextEditorCache(): void { cache.clear() }
export async function getEditableTextItems(v: VirtualPage): Promise<EditableTextItem[]> {
  if (v.normalizedConfig || v.rotation % 360 !== 0) return []
  const key = `${v.sourceDocId}:${v.sourcePageIndex}:${JSON.stringify(v.cropMargins)}`
  if (cache.has(key)) return cache.get(key)!
  const doc = getPdfDoc(v.sourceDocId)
  if (!doc) return []
  const page = await doc.getPage(v.sourcePageIndex + 1)
  const viewport = page.getViewport({ scale: 1, rotation: 0 })
  // Non-zero origins/cropped originals need a separate coordinate transform before safe editing.
  if (page.view[0] !== 0 || page.view[1] !== 0) return []
  const content = await page.getTextContent()
  const items: EditableTextItem[] = []
  content.items.forEach((item, i) => {
    if (!('str' in item) || !item.str.trim() || /\s{2,}/.test(item.str)) return
    const [a,b,c,d,x,y] = item.transform
    if (Math.abs(b) > 0.01 || Math.abs(c) > 0.01 || a <= 0 || d <= 0) return
    const size = Math.hypot(a,b)
    const style = content.styles[item.fontName]
    const ascent = Math.min(1.2, Math.max(0.5, style?.ascent ?? 0.88))
    const descent = Math.max(-0.4, Math.min(0, style?.descent ?? -0.18))
    const [x1,y1,x2,y2] = viewport.convertToViewportRectangle([x,y + size*descent,x+item.width,y+size*ascent])
    items.push({ id:`txt_${v.sourcePageIndex}_${i}`,str:item.str,pdfX:x,pdfY:y,pdfWidth:item.width,pdfHeight:size*(ascent-descent),fontSize:size,vx:Math.min(x1,x2),vy:Math.min(y1,y2),vw:Math.abs(x2-x1),vh:Math.abs(y2-y1) })
  })
  // Overlapping runs cannot safely be erased independently (e.g. faux bold, overprint).
  const safe = items.filter((a,i) => !items.some((b,j) => i!==j && Math.min(a.vx+a.vw,b.vx+b.vw)-Math.max(a.vx,b.vx)>0.5 && Math.min(a.vy+a.vh,b.vy+b.vh)-Math.max(a.vy,b.vy)>Math.min(a.vh,b.vh)*0.3))
  cache.set(key,safe)
  return safe
}
