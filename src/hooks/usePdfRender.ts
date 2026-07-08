/**
 * PDF 페이지 렌더링 훅
 * - OffscreenCanvas + Worker 활용 (가능한 경우)
 * - 렌더링 결과를 ImageBitmap 으로 캐싱 (LRU)
 * - 줌 변경 시 점진적 업데이트
 */
import { useEffect, useRef, useCallback } from 'react'
import * as pdfjsLib from 'pdfjs-dist'
import { LRU } from '../lib/lru-cache'
import { getPdfDoc } from './usePdfDocument'
import { useStore } from '../lib/store'

/** 렌더링 결과 캐시 키: "페이지인덱스:줌:회전" */
const renderCache = new LRU<string, ImageBitmap>(24)

export function renderCacheKey(pageIndex: number, zoom: number, rotation: number): string {
  return `${pageIndex}:${zoom.toFixed(2)}:${rotation}`
}

/** 캐시에서 비트맵 조회 */
export function getCachedBitmap(pageIndex: number, zoom: number, rotation: number) {
  return renderCache.get(renderCacheKey(pageIndex, zoom, rotation))
}

/** 특정 페이지의 캐시 무효화 (회전/편집 후) */
export function invalidatePage(pageIndex: number): void {
  const keys = renderCache.keys()
  for (const key of keys) {
    if (key.startsWith(`${pageIndex}:`)) {
      const [idx] = key.split(':')
      renderCache.delete(key)
      void idx
    }
  }
}

/** 전체 캐시 비움 (문서 변경 시) */
export function clearRenderCache(): void {
  renderCache.clear()
}

interface RenderParams {
  canvas: HTMLCanvasElement
  pageIndex: number
  zoom: number
  rotation: number
  signal?: AbortSignal
}

/**
 * 단일 페이지를 캔버스에 렌더링
 * 캐시 히트 시 즉시 drawImage, 미스 시 pdfjs 렌더링 후 캐싱
 */
export async function renderPageToCanvas({
  canvas,
  pageIndex,
  zoom,
  rotation,
  signal
}: RenderParams): Promise<{ width: number; height: number }> {
  const doc = getPdfDoc()
  if (!doc) throw new Error('PDF 문서가 로드되지 않았습니다.')

  const ctx = canvas.getContext('2d', { alpha: false })
  if (!ctx) throw new Error('2D 컨텍스트를 생성할 수 없습니다.')

  const page = await doc.getPage(pageIndex + 1) // pdfjs 는 1-base
  const viewport = page.getViewport({ scale: zoom * window.devicePixelRatio, rotation })

  // 캐시 히트 확인
  const key = renderCacheKey(pageIndex, zoom, rotation)
  const cached = renderCache.get(key)

  canvas.width = Math.floor(viewport.width)
  canvas.height = Math.floor(viewport.height)
  canvas.style.width = `${Math.floor(viewport.width / window.devicePixelRatio)}px`
  canvas.style.height = `${Math.floor(viewport.height / window.devicePixelRatio)}px`

  if (cached) {
    // 캐시된 비트맵 즉시 그리기
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, canvas.width, canvas.height)
    ctx.drawImage(cached, 0, 0, canvas.width, canvas.height)
    return { width: viewport.width, height: viewport.height }
  }

  // pdfjs 렌더링
  ctx.fillStyle = '#ffffff'
  ctx.fillRect(0, 0, canvas.width, canvas.height)

  const renderTask = page.render({
    canvasContext: ctx,
    viewport,
    canvas
  } as Parameters<typeof page.render>[0])

  // 취소 신호 연결
  if (signal) {
    signal.addEventListener('abort', () => {
      try {
        renderTask.cancel()
      } catch {
        /* noop */
      }
    })
  }

  try {
    await renderTask.promise

    // 렌더링 결과를 ImageBitmap 으로 캐싱 (createImageBitmap 지원 시)
    if ('createImageBitmap' in window && !signal?.aborted) {
      try {
        const bitmap = await createImageBitmap(canvas)
        renderCache.set(key, bitmap)
      } catch {
        /* 캐싱 실패는 무시 */
      }
    }
  } catch (err) {
    // 취소로 인한 오류는 무시
    if ((err as Error)?.name === 'RenderingCancelledException') return { width: 0, height: 0 }
    throw err
  }

  return { width: viewport.width, height: viewport.height }
}

/**
 * 텍스트 레이어 렌더링 (선택/검색 가능하게)
 * pdfjs 4.x 의 renderTextLayer 사용
 */
export async function renderTextLayer(
  container: HTMLElement,
  pageIndex: number,
  zoom: number,
  rotation: number
): Promise<void> {
  const doc = getPdfDoc()
  if (!doc) return
  const page = await doc.getPage(pageIndex + 1)
  const viewport = page.getViewport({ scale: zoom, rotation })

  const textContent = await page.getTextContent()
  container.innerHTML = ''
  container.style.setProperty('--scale-factor', String(zoom))

  // pdfjs 4.x: renderTextLayer 는 별도 모듈
  const { renderTextLayer } = await import('pdfjs-dist/build/pdf.mjs')
  await renderTextLayer({
    textContentSource: textContent,
    container,
    viewport
  } as Parameters<typeof renderTextLayer>[0]).promise
}

/**
 * React 컴포넌트에서 사용하는 렌더링 훅
 * canvas ref 와 페이지 정보를 받아 자동 렌더링
 */
export function usePdfRender(
  canvasRef: React.RefObject<HTMLCanvasElement>,
  pageIndex: number,
  zoom: number,
  rotation: number
) {
  const abortRef = useRef<AbortController | null>(null)
  const isLoading = useStore((s) => s.isLoading)

  const render = useCallback(async () => {
    const canvas = canvasRef.current
    if (!canvas) return
    // 이전 렌더링 취소
    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller

    try {
      await renderPageToCanvas({
        canvas,
        pageIndex,
        zoom,
        rotation,
        signal: controller.signal
      })
    } catch (err) {
      if (!controller.signal.aborted) console.error('렌더링 오류:', err)
    }
  }, [canvasRef, pageIndex, zoom, rotation])

  useEffect(() => {
    void render()
  }, [render])

  return { render, isLoading }
}
