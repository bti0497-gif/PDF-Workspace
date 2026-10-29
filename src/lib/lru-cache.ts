/**
 * 간단한 LRU(Least Recently Used) 캐시
 * - 항목 수와 선택적 weight(예: 비트맵 바이트) 상한을 함께 적용한다.
 * - 제거/교체된 ImageBitmap 등 close() 가능한 값은 즉시 해제한다.
 */
export class LRU<K, V> {
  private map = new Map<K, { value: V; weight: number }>()
  private total = 0
  private readonly max: number
  private readonly maxWeight: number
  private readonly weigh: (value: V, key: K) => number

  constructor(max = 20, maxWeight = Number.POSITIVE_INFINITY, weigh: (value: V, key: K) => number = () => 1) {
    this.max = Math.max(0, max)
    this.maxWeight = Math.max(0, maxWeight)
    this.weigh = weigh
  }

  get(key: K): V | undefined {
    const entry = this.map.get(key)
    if (!entry) return undefined
    this.map.delete(key)
    this.map.set(key, entry)
    return entry.value
  }

  set(key: K, value: V): void {
    const previous = this.map.get(key)
    if (previous) {
      this.map.delete(key)
      this.total -= previous.weight
      if (previous.value !== value) this.dispose(key, previous.value)
    }
    const weight = this.weightOf(value, key)
    // An oversized value must not flush all otherwise useful cached pages.
    if (this.max === 0 || weight > this.maxWeight) {
      this.dispose(key, value)
      return
    }
    this.map.set(key, { value, weight })
    this.total += weight
    this.evict()
  }

  delete(key: K): void {
    const entry = this.map.get(key)
    if (!entry) return
    this.map.delete(key)
    this.total -= entry.weight
    this.dispose(key, entry.value)
  }

  clear(): void {
    for (const [key, entry] of this.map) this.dispose(key, entry.value)
    this.map.clear()
    this.total = 0
  }

  get size(): number { return this.map.size }
  get weight(): number { return this.total }
  keys(): K[] { return [...this.map.keys()] }

  private weightOf(value: V, key: K): number {
    const weight = this.weigh(value, key)
    return Number.isFinite(weight) && weight > 0 ? weight : 0
  }

  private evict(): void {
    while (this.map.size > this.max || this.total > this.maxWeight) {
      const first = this.map.entries().next().value as [K, { value: V; weight: number }] | undefined
      if (!first) break
      const [key, entry] = first
      this.map.delete(key)
      this.total -= entry.weight
      this.dispose(key, entry.value)
    }
  }

  private dispose(_key: K, value: V): void {
    if (value && typeof value === 'object' && 'close' in value) {
      try { ;(value as { close: () => void }).close() } catch { /* noop */ }
    }
  }
}
