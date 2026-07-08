/**
 * 왼쪽 썸네일 패널 - 각 페이지의 미리보기를 표시
 * 썸네일은 idle 시간에 백그라운드에서 생성 (성능)
 */
import { useEffect, useRef } from 'react'
import { useStore } from '../lib/store'
import { getPdfDoc } from '../hooks/usePdfDocument'
import type { DocumentMeta } from '../lib/store'

const THUMB_WIDTH = 156 // CSS 픽셀 기준 썸네일 폭

export function ThumbnailPanel() {
  const visible = useStore((s) => s.thumbnailsVisible)
  const doc = useStore((s) => s.document)
  const currentPage = useStore((s) => s.currentPage)
  const setCurrentPage = useStore((s) => s.setCurrentPage)

  if (!doc) {
    return <aside className={`thumbnails ${visible ? '' : 'thumbnails--hidden'}`} />
  }

  return (
    <aside className={`thumbnails ${visible ? '' : 'thumbnails--hidden'}`}>
      {Array.from({ length: doc.pageCount }, (_, i) => (
        <ThumbnailItem
          key={i}
          index={i}
          rotation={doc.rotations[i] ?? 0}
          isActive={i === currentPage}
          pageCount={doc.pageCount}
          onClick={() => setCurrentPage(i)}
        />
      ))}
    </aside>
  )
}

interface ThumbProps {
  index: number
  rotation: number
  isActive: boolean
  pageCount: number
  onClick: () => void
}

function ThumbnailItem({ index, rotation, isActive, onClick }: ThumbProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const rendered = useRef(false)

  useEffect(() => {
    if (rendered.current) return
    let cancelled = false

    const render = async () => {
      const doc = getPdfDoc()
      const canvas = canvasRef.current
      if (!doc || !canvas) return

      try {
        const page = await doc.getPage(index + 1)
        if (cancelled) return
        // 화면 DPR 기준 스케일 계산
        const baseViewport = page.getViewport({ scale: 1 })
        const scale = (THUMB_WIDTH / baseViewport.width) * window.devicePixelRatio
        const viewport = page.getViewport({ scale, rotation })
        const ctx = canvas.getContext('2d', { alpha: false })
        if (!ctx) return
        canvas.width = Math.floor(viewport.width)
        canvas.height = Math.floor(viewport.height)
        canvas.style.width = `${THUMB_WIDTH}px`
        canvas.style.height = `${(THUMB_WIDTH / viewport.width) * viewport.height}px`
        ctx.fillStyle = '#fff'
        ctx.fillRect(0, 0, canvas.width, canvas.height)
        await page.render({
          canvasContext: ctx,
          viewport,
          canvas
        } as Parameters<typeof page.render>[0]).promise
        rendered.current = true
      } catch (err) {
        if (!cancelled) console.error(`썸네일 ${index + 1} 생성 실패:`, err)
      }
    }

    // requestIdleCallback 가 지원되면 유휴 시간에 렌더링, 아니면 지연
    const ric =
      'requestIdleCallback' in window
        ? (window as unknown as { requestIdleCallback: (cb: () => void) => number }).requestIdleCallback(() => void render())
        : window.setTimeout(() => void render(), 50)

    return () => {
      cancelled = true
      if (typeof ric === 'number') {
        clearTimeout(ric)
      }
    }
    // rotation 이 바뀌면 다시 렌더링
  }, [index, rotation])

  return (
    <div
      className={`thumbnail ${isActive ? 'thumbnail--active' : ''}`}
      onClick={onClick}
    >
      <canvas ref={canvasRef} className="thumbnail__canvas" />
      <span className="thumbnail__label">{index + 1}</span>
      <span
        className={`thumbnail__rotation ${rotation % 360 !== 0 ? 'thumbnail__rotation--visible' : ''}`}
      >
        {rotation}°
      </span>
    </div>
  )
}
