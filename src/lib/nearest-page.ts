/** Find the closest page centre without measuring every page on every scroll frame.
 * Page rows are ordered vertically. A two-page row may contain unequal page heights.
 * readMiddle is called at most columns * (ceil(log2(rows)) + 2) times.
 */
export function nearestPageByRow(
  count: number,
  columns: 1 | 2,
  centerY: number,
  readMiddle: (index: number) => number
): number {
  if (count === 0) return 0
  const rows = Math.ceil(count / columns)
  const cache = new Map<number, number>()
  const middle = (index: number): number => {
    if (!cache.has(index)) cache.set(index, readMiddle(index))
    return cache.get(index)!
  }
  const rowLastMiddle = (row: number): number => {
    const first = row * columns
    let last = middle(first)
    if (columns === 2 && first + 1 < count) last = Math.max(last, middle(first + 1))
    return last
  }
  let low = 0
  let high = rows
  while (low < high) {
    const mid = (low + high) >>> 1
    if (rowLastMiddle(mid) < centerY) low = mid + 1
    else high = mid
  }
  let closest = Math.max(0, low - 1) * columns
  let distance = Infinity
  for (let row = Math.max(0, low - 1); row <= Math.min(rows - 1, low); row++) {
    for (let index = row * columns; index < Math.min(count, (row + 1) * columns); index++) {
      const delta = Math.abs(middle(index) - centerY)
      if (delta < distance) { distance = delta; closest = index }
    }
  }
  return closest
}
