import { PDFDocument, PDFDict, PDFHexString, PDFName, PDFNumber, PDFRef } from 'pdf-lib'
import type { BookmarkNode } from '../domain/bookmark'

/** Write local page destinations, with Unicode titles and the complete tree hierarchy.
 * Unknown/external actions are deliberately not executed or copied as stale source refs.
 * Page destinations use Fit, so source-specific zoom/XYZ offsets are not preserved.
 */
export function writePdfBookmarks(
  doc: PDFDocument, nodes: BookmarkNode[], pages: ReadonlyArray<{ id: string }>
): void {
  if (!nodes.length) {
    doc.catalog.delete(PDFName.of('Outlines'))
    return
  }
  const pageIndices = new Map(pages.map((page, index) => [page.id, index]))
  const pdfPages = doc.getPages()
  const root = doc.context.obj({ Type: PDFName.of('Outlines') })
  const rootRef = doc.context.register(root)
  const visibleCount = (items: BookmarkNode[]): number => items.reduce((sum, node) => sum + 1 + (node.isExpanded === false ? 0 : visibleCount(node.children ?? [])), 0)
  const writeLevel = (items: BookmarkNode[], parent: PDFDict, parentRef: PDFRef): void => {
    if (!items.length) return
    const entries = items.map(node => {
      const dict = doc.context.obj({ Title: PDFHexString.fromText(node.title || '제목 없음'), Parent: parentRef })
      return { node, dict, ref: doc.context.register(dict) }
    })
    parent.set(PDFName.of('First'), entries[0].ref)
    parent.set(PDFName.of('Last'), entries[entries.length - 1].ref)
    entries.forEach(({ node, dict, ref }, index) => {
      if (index > 0) dict.set(PDFName.of('Prev'), entries[index - 1].ref)
      if (index + 1 < entries.length) dict.set(PDFName.of('Next'), entries[index + 1].ref)
      const target = node.targetPageId ? pageIndices.get(node.targetPageId) : node.targetPageIndex
      if (target !== null && target !== undefined && pdfPages[target]) {
        dict.set(PDFName.of('Dest'), doc.context.obj([pdfPages[target].ref, PDFName.of('Fit')]))
      }
      const children = node.children ?? []
      if (children.length) {
        writeLevel(children, dict, ref)
        const count = visibleCount(children)
        dict.set(PDFName.of('Count'), PDFNumber.of(node.isExpanded === false ? -count : count))
      }
    })
  }
  writeLevel(nodes, root, rootRef)
  root.set(PDFName.of('Count'), PDFNumber.of(visibleCount(nodes)))
  doc.catalog.set(PDFName.of('Outlines'), rootRef)
}
