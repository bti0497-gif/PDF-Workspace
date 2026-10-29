/** PDF outline tree. Page IDs survive reorder, insertion and undo. */
export interface BookmarkNode {
  id: string
  title: string
  targetPageIndex: number | null
  targetPageId?: string
  dest?: unknown
  isExpanded?: boolean
  children: BookmarkNode[]
}
let _bmCounter = 0
export function generateBookmarkId(): string {
  return `bm_${Date.now().toString(36)}_${++_bmCounter}`
}
export function bindBookmarkTargets(nodes: BookmarkNode[], pages: ReadonlyArray<{ id: string }>): BookmarkNode[] {
  return nodes.map(node => ({ ...node,
    targetPageId: node.targetPageId ?? (node.targetPageIndex !== null ? pages[node.targetPageIndex]?.id : undefined),
    children: bindBookmarkTargets(node.children ?? [], pages)
  }))
}
/** Keep missing IDs so undoing a page deletion restores the destination. */
export function remapBookmarkTargets(nodes: BookmarkNode[], previousPages: ReadonlyArray<{ id: string }>, nextPages: ReadonlyArray<{ id: string }>): BookmarkNode[] {
  const indices = new Map(nextPages.map((page, index) => [page.id, index]))
  const visit = (items: BookmarkNode[]): BookmarkNode[] => items.map(node => {
    const id = node.targetPageId ?? (node.targetPageIndex !== null ? previousPages[node.targetPageIndex]?.id : undefined)
    return { ...node, targetPageId: id, targetPageIndex: id ? indices.get(id) ?? null : null, children: visit(node.children ?? []) }
  })
  return visit(nodes)
}
export function findBookmark(nodes: BookmarkNode[], id: string): BookmarkNode | undefined {
  for (const node of nodes) {
    if (node.id === id) return node
    const child = findBookmark(node.children ?? [], id)
    if (child) return child
  }
  return undefined
}
export function updateBookmark(nodes: BookmarkNode[], id: string, patch: Partial<Omit<BookmarkNode, 'id' | 'children'>>): BookmarkNode[] {
  return nodes.map(node => ({ ...node, ...(node.id === id ? patch : {}), children: updateBookmark(node.children ?? [], id, patch) }))
}
export function deleteBookmark(nodes: BookmarkNode[], id: string): BookmarkNode[] {
  return nodes.filter(node => node.id !== id).map(node => ({ ...node, children: deleteBookmark(node.children ?? [], id) }))
}
export type BookmarkMove = 'up' | 'down' | 'indent' | 'outdent'
/** Move an entire subtree, retaining all descendant destinations. */
export function moveBookmark(nodes: BookmarkNode[], id: string, direction: BookmarkMove): BookmarkNode[] {
  const clone = (items: BookmarkNode[]): BookmarkNode[] => items.map(node => ({ ...node, children: clone(node.children ?? []) }))
  const result = clone(nodes)
  const locate = (items: BookmarkNode[], parentList?: BookmarkNode[], parentIndex?: number): boolean => {
    const index = items.findIndex(node => node.id === id)
    if (index >= 0) {
      if (direction === 'up' && index > 0) [items[index - 1], items[index]] = [items[index], items[index - 1]]
      if (direction === 'down' && index < items.length - 1) [items[index + 1], items[index]] = [items[index], items[index + 1]]
      if (direction === 'indent' && index > 0) {
        const [node] = items.splice(index, 1)
        items[index - 1].children.push(node)
        items[index - 1].isExpanded = true
      }
      if (direction === 'outdent' && parentList && parentIndex !== undefined) {
        const [node] = items.splice(index, 1)
        parentList.splice(parentIndex + 1, 0, node)
      }
      return true
    }
    for (let i = 0; i < items.length; i++) if (locate(items[i].children, items, i)) return true
    return false
  }
  locate(result)
  return result
}
