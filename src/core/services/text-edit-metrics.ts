import { PDFDocument, type PDFFont } from 'pdf-lib'
import fontkit from '@pdf-lib/fontkit'

let fontPromise: Promise<PDFFont> | null = null
/** Same font bytes and metrics as the exported PDF; never guess Korean glyph widths. */
export function loadEditFont(): Promise<PDFFont> {
  if (!fontPromise) fontPromise = (async () => {
    const data = await window.workspace.getKoreanFont()
    if (!data || data.length === 0) throw new Error('편집용 한글 폰트를 불러오지 못했습니다.')
    const bytes = new Uint8Array(data)
    const doc = await PDFDocument.create()
    doc.registerFontkit(fontkit)
    const font = await doc.embedFont(bytes, { subset: true })
    const face = new FontFace('WorkspaceEditFont', bytes.buffer.slice(0))
    await face.load()
    document.fonts.add(face)
    return font
  })().catch(e => { fontPromise = null; throw e })
  return fontPromise
}
export function measureEditText(font: PDFFont, text: string, size: number): number {
  const supported = new Set(font.getCharacterSet())
  if ([...text].some(c => !supported.has(c.codePointAt(0)!))) throw new Error('대체 폰트에 없는 문자가 포함되어 있습니다.')
  return text ? font.widthOfTextAtSize(text, size) : 0
}
export function fitsText(width: number, available: number): boolean {
  return Number.isFinite(width) && width <= available + 0.1
}
