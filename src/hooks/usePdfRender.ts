/**
 * PDF 페이지 렌더링 훅 (포토샵 스타일 프록시 점진적 렌더링 & LRU 캐시)
 * - 1단계: 캐시된 저해상도 프록시(이전 배율 비트맵)가 있으면 즉시 확대 렌더링 (흰 화면/지연 0%)
 * - 2단계: 백그라운드에서 선명한 고해상도 렌더링 완료 시 부드럽게 교체
 * - 가상 페이지 매니페스트(VirtualPage) 기반 소스 매핑
 */
import { useEffect, useRef, useCallback } from 'react'
import * as pdfjsLib from 'pdfjs-dist'
import { LRU } from '../lib/lru-cache'
import { getPdfDoc } from './usePdfDocument'
import { useStore } from '../lib/store'
import { ROOT_DOC_ID } from '../core/domain/document'

/** 렌더링 결과 캐시 키: "소스ID:소스페이지:줌:회전" */
const renderCache = new LRU<string, ImageBitmap>(32)
const canvasJobs = new WeakMap<HTMLCanvasElement, { task?: pdfjsLib.RenderTask }>()

export function renderCacheKey(
  sourceDocId: string,
  sourcePageIndex: number,
  zoom: number,
  rotation: number
): string {
  return `${sourceDocId}:${sourcePageIndex}:${zoom.toFixed(2)}:${rotation}`
}

/** 캐시에서 동일 페이지의 임의의 이전 비트맵(저해상도 프록시) 조회 */
function findProxyBitmap(
  sourceDocId: string,
  sourcePageIndex: number,
  rotation: number
): ImageBitmap | undefined {
  const prefix = `${sourceDocId}:${sourcePageIndex}:`
  const suffix = `:${rotation}`
  for (const key of renderCache.keys()) {
    if (key.startsWith(prefix) && key.endsWith(suffix)) {
      return renderCache.get(key)
    }
  }
  return undefined
}

/** 특정 페이지의 캐시 무효화 */
export function invalidatePage(sourceDocId: string, sourcePageIndex: number): void {
  const prefix = `${sourceDocId}:${sourcePageIndex}:`
  for (const key of renderCache.keys()) {
    if (key.startsWith(prefix)) {
      renderCache.delete(key)
    }
  }
}

/** 전체 캐시 비움 */
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
 * 단일 페이지를 캔버스에 점진적 렌더링
 */
export async function renderPageToCanvas({
  canvas,
  pageIndex,
  zoom,
  rotation,
  signal
}: RenderParams): Promise<{ width: number; height: number }> {
  const prior = canvasJobs.get(canvas)
  const job: { task?: pdfjsLib.RenderTask } = {}
  canvasJobs.set(canvas, job)
  if (prior?.task) { prior.task.cancel(); await prior.task.promise.catch(() => {}) }
  if (signal?.aborted || canvasJobs.get(canvas) !== job) return { width: 0, height: 0 }
  const state = useStore.getState()
  const vPage = state.pages[pageIndex]

  const sourceDocId = vPage?.sourceDocId ?? ROOT_DOC_ID
  const sourcePageIndex = vPage?.sourcePageIndex ?? pageIndex
  const pageRotation = vPage?.rotation ?? rotation

  const doc = getPdfDoc(sourceDocId)
  if (!doc) throw new Error('PDF 문서가 로드되지 않았습니다.')

  const ctx = canvas.getContext('2d', { alpha: false })
  if (!ctx) throw new Error('2D 컨텍스트를 생성할 수 없습니다.')

  const page = await doc.getPage(sourcePageIndex + 1) // pdfjs는 1-based
  if (signal?.aborted || canvasJobs.get(canvas) !== job) return { width: 0, height: 0 }
  const viewport = page.getViewport({ scale: zoom * window.devicePixelRatio, rotation: pageRotation })

  const exactKey = renderCacheKey(sourceDocId, sourcePageIndex, zoom, pageRotation)
  const cachedExact = renderCache.get(exactKey)

  canvas.width = Math.floor(viewport.width)
  canvas.height = Math.floor(viewport.height)
  canvas.style.width = `${Math.floor(viewport.width / window.devicePixelRatio)}px`
  canvas.style.height = `${Math.floor(viewport.height / window.devicePixelRatio)}px`

  // [1단계: 정확한 캐시 히트]
  if (cachedExact) {
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, canvas.width, canvas.height)
    ctx.drawImage(cachedExact, 0, 0, canvas.width, canvas.height)
    return { width: viewport.width, height: viewport.height }
  }

  // [2단계: 저해상도 프록시 프리뷰 즉각 렌더링 (흰 화면 방지)]
  const proxy = findProxyBitmap(sourceDocId, sourcePageIndex, pageRotation)
  if (proxy) {
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, canvas.width, canvas.height)
    ctx.drawImage(proxy, 0, 0, canvas.width, canvas.height)
  } else {
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, canvas.width, canvas.height)
  }

  // [3단계: 선명한 고해상도 백그라운드 렌더링]
  const renderTask = page.render({
    canvasContext: ctx,
    viewport,
    canvas
  } as Parameters<typeof page.render>[0])
  job.task = renderTask

  if (signal) {
    signal.addEventListener('abort', () => {
      try {
        renderTask.cancel()
      } catch {
        /* noop */
      }
    }, { once: true })
  }

  try {
    await renderTask.promise

    // 렌더링 결과를 ImageBitmap 으로 캐싱
    if ('createImageBitmap' in window && !signal?.aborted) {
      try {
        const bitmap = await createImageBitmap(canvas)
        renderCache.set(exactKey, bitmap)
      } catch {
        /* noop */
      }
    }
  } catch (err) {
    if ((err as Error)?.name === 'RenderingCancelledException') {
      return { width: 0, height: 0 }
    }
    throw err
  }

  return { width: viewport.width, height: viewport.height }
}

/**
 * 텍스트 레이어 렌더링 (선택/검색 가능)
 */
export async function renderTextLayer(
  container: HTMLElement,
  pageIndex: number,
  zoom: number,
  rotation: number
): Promise<void> {
  const state = useStore.getState()
  const vPage = state.pages[pageIndex]

  const sourceDocId = vPage?.sourceDocId ?? ROOT_DOC_ID
  const sourcePageIndex = vPage?.sourcePageIndex ?? pageIndex
  const pageRotation = vPage?.rotation ?? rotation

  const doc = getPdfDoc(sourceDocId)
  if (!doc) return

  const page = await doc.getPage(sourcePageIndex + 1)
  const viewport = page.getViewport({ scale: zoom, rotation: pageRotation })

  const textContent = await page.getTextContent()
  container.innerHTML = ''
  container.style.setProperty('--scale-factor', String(zoom))

  const textLayer = new pdfjsLib.TextLayer({
    textContentSource: textContent,
    container,
    viewport
  })
  await textLayer.render()
}

/**
 * React 컴포넌트 렌더링 훅
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

  // PageCanvas owns the render call and size update. Do not render twice.
  useEffect(() => () => { abortRef.current?.abort() }, [])

  return { render, isLoading }
}
