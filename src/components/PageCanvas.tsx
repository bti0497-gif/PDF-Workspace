/**
 * PDF 페이지 캔버스 렌더링 컴포넌트
 * - usePdfRender 훅으로 캔버스에 페이지 렌더링
 * - 텍스트 레이어를 별도 div 로 겹침 (선택 가능)
 */
import { useRef, useEffect, useState } from 'react'
import { usePdfRender, renderTextLayer } from '../hooks/usePdfRender'

interface Props {
  pageIndex: number
  zoom: number
  rotation: number
}

export function PageCanvas({ pageIndex, zoom, rotation }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const textLayerRef = useRef<HTMLDivElement>(null)
  const [pageSize, setPageSize] = useState<{ width: number; height: number } | null>(null)

  // 캔버스 렌더링
  const { render } = usePdfRender(canvasRef, pageIndex, zoom, rotation)

  // 렌더링 후 캔버스 크기 수집 -> 래퍼/텍스트레이어 동기화
  useEffect(() => {
    let active = true
    const updateSize = async () => {
      await render()
      if (!active) return
      const canvas = canvasRef.current
      if (canvas) {
        setPageSize({
          width: canvas.clientWidth,
          height: canvas.clientHeight
        })
      }
    }
    void updateSize()
    return () => {
      active = false
    }
  }, [render])

  // 텍스트 레이어 렌더링 (캔버스 위 투명)
  useEffect(() => {
    const container = textLayerRef.current
    if (!container) return
    let cancelled = false
    ;(async () => {
      try {
        await renderTextLayer(container, pageIndex, zoom, rotation)
      } catch (err) {
        if (!cancelled) console.error('텍스트 레이어 오류:', err)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [pageIndex, zoom, rotation])

  return (
    <>
      <canvas ref={canvasRef} className="page-canvas" />
      <div
        ref={textLayerRef}
        className="text-layer"
        style={pageSize ? { width: pageSize.width, height: pageSize.height } : undefined}
      />
    </>
  )
}
