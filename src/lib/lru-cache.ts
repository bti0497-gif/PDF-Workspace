/**
 * 간단한 LRU(Least Recently Used) 캐시
 * - 렌더링된 페이지 비트맵 캐싱에 사용
 * - 캐시 용량 초과 시 가장 오래 전에 사용된 항목부터 제거
 */
export class LRU<K, V> {
  private map = new Map<K, V>()
  private readonly max: number

  constructor(max = 20) {
    this.max = max
  }

  /** 항목 조회 (사용 시각 갱신 - Map 은 삽입 순서 유지) */
  get(key: K): V | undefined {
    const value = this.map.get(key)
    if (value === undefined) return undefined
    // 삭제 후 재삽입하여 최근 사용으로 표시
    this.map.delete(key)
    this.map.set(key, value)
    return value
  }

  /** 항목 저장 */
  set(key: K, value: V): void {
    if (this.map.has(key)) this.map.delete(key)
    this.map.set(key, value)
    // 용량 초과 시 가장 오래된 항목(첫 항목) 제거
    while (this.map.size > this.max) {
      const firstKey = this.map.keys().next().value
      if (firstKey === undefined) break
      const stale = this.map.get(firstKey)
      this.map.delete(firstKey)
      // 캔버스/이미지Bitmap 인 경우 리소스 해제
      this.dispose(firstKey, stale)
    }
  }

  /** 특정 키 제거 */
  delete(key: K): void {
    const value = this.map.get(key)
    if (value !== undefined) {
      this.dispose(key, value)
      this.map.delete(key)
    }
  }

  /** 캐시 비움 */
  clear(): void {
    for (const [k, v] of this.map) this.dispose(k, v)
    this.map.clear()
  }

  /** 항목 수 */
  get size(): number {
    return this.map.size
  }

  /** 캐시된 키 목록 */
  keys(): K[] {
    return [...this.map.keys()]
  }

  /** 제거 시 리소스 해제 (ImageBitmap 등) */
  private dispose(_key: K, value: V): void {
    if (value && typeof value === 'object' && 'close' in value) {
      try {
        ;(value as { close: () => void }).close()
      } catch {
        /* noop */
      }
    }
  }
}
