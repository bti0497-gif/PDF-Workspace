import { PDFDocument } from 'pdf-lib'

export interface MergePlanEntry { sourceId: string; index: number; included: boolean }

/** Copies only included pages, in the exact preview sequence. Source bytes are never mutated. */
export async function buildMergePlanPdf(
  sources: ReadonlyMap<string, Uint8Array>,
  plan: readonly MergePlanEntry[]
): Promise<Uint8Array> {
  const selected = plan.filter((page) => page.included)
  if (!selected.length) throw new Error('포함할 페이지를 한 쪽 이상 선택하세요.')
  const output = await PDFDocument.create()
  const parsed = new Map<string, PDFDocument>()
  for (const entry of selected) {
    let doc = parsed.get(entry.sourceId)
    if (!doc) {
      const bytes = sources.get(entry.sourceId)
      if (!bytes) throw new Error('병합할 원본 파일을 찾을 수 없습니다.')
      doc = await PDFDocument.load(bytes)
      parsed.set(entry.sourceId, doc)
    }
    if (!Number.isInteger(entry.index) || entry.index < 0 || entry.index >= doc.getPageCount()) {
      throw new Error('원본 페이지 범위를 벗어났습니다.')
    }
    const [copied] = await output.copyPages(doc, [entry.index])
    output.addPage(copied)
  }
  return output.save()
}
