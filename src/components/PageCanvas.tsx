/**
 * PDF 페이지 캔버스 렌더링 컴포넌트
 * - usePdfRender 훅으로 캔버스에 페이지 렌더링
 * - 텍스트 레이어를 별도 div 로 겹침 (선택 가능)
 * - 크롭 가이드라인, 워터마크, 페이지 번호 실시간 오버레이
 */
import { useRef, useEffect, useState } from 'react'
import { useStore } from '../lib/store'
import { usePdfRender, renderTextLayer } from '../hooks/usePdfRender'
import { TextSelectionPopup } from './TextSelectionPopup'

interface Props {
  pageIndex: number
  zoom: number
  rotation: number
}

export function PageCanvas({ pageIndex, zoom, rotation }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const textLayerRef = useRef<HTMLDivElement>(null)
  const [pageSize, setPageSize] = useState<{ width: number; height: number } | null>(null)

  const vPage = useStore((s) => s.pages[pageIndex])

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
    const controller = new AbortController()
    void renderTextLayer(container, pageIndex, zoom, rotation, controller.signal).catch((err) => {
      if (!controller.signal.aborted) console.error('텍스트 레이어 오류:', err)
    })
    return () => {
      controller.abort()
      // getPage/getTextContent 대기 중 취소된 이전 레이어도 즉시 분리한다.
      container.replaceChildren()
    }
  }, [pageIndex, zoom, rotation])

  const hasCrop =
    vPage?.cropMargins &&
    ((vPage.cropMargins.top ?? 0) > 0 ||
      (vPage.cropMargins.bottom ?? 0) > 0 ||
      (vPage.cropMargins.left ?? 0) > 0 ||
      (vPage.cropMargins.right ?? 0) > 0)

  return (
    <>
      <canvas ref={canvasRef} className="page-canvas" />
      <div
        ref={textLayerRef}
        className="text-layer"
        style={pageSize ? { width: pageSize.width, height: pageSize.height } : undefined}
      />
      <TextSelectionPopup
        pageIndex={pageIndex}
        zoom={zoom}
        containerRef={textLayerRef}
      />

      {/* 여백 자르기 (Crop) 가이드 오버레이 */}
      {hasCrop && pageSize && (
        <div
          className="crop-overlay"
          style={{
            position: 'absolute',
            top: 0,
            left: 0,
            width: pageSize.width,
            height: pageSize.height,
            overflow: 'hidden',
            pointerEvents: 'none',
            zIndex: 3
          }}
        >
          <div
            style={{
              position: 'absolute',
              top: (vPage.cropMargins?.top ?? 0) * zoom,
              left: (vPage.cropMargins?.left ?? 0) * zoom,
              right: (vPage.cropMargins?.right ?? 0) * zoom,
              bottom: (vPage.cropMargins?.bottom ?? 0) * zoom,
              border: '2px dashed #e5534b',
              boxShadow: '0 0 0 9999px rgba(0, 0, 0, 0.35)',
              pointerEvents: 'none'
            }}
          >
            <span
              style={{
                position: 'absolute',
                top: 2,
                left: 4,
                background: 'rgba(229, 83, 75, 0.9)',
                color: '#fff',
                fontSize: 10,
                padding: '1px 4px',
                borderRadius: 2
              }}
            >
              ✂ 자르기 영역
            </span>
          </div>
        </div>
      )}

      {/* 워터마크 (Watermark) 오버레이 */}
      {vPage?.watermarkConfig && pageSize && (
        <div
          className="watermark-overlay"
          style={{
            position: 'absolute',
            top: 0,
            left: 0,
            width: pageSize.width,
            height: pageSize.height,
            overflow: 'hidden',
            pointerEvents: 'none',
            zIndex: 4
          }}
        >
          {vPage.watermarkConfig.tiled ? (
            <div
              style={{
                width: '100%',
                height: '100%',
                display: 'grid',
                gridTemplateColumns: 'repeat(3, 1fr)',
                gridTemplateRows: 'repeat(3, 1fr)'
              }}
            >
              {Array.from({ length: 9 }).map((_, i) => (
                <div
                  key={i}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    overflow: 'hidden'
                  }}
                >
                  <span
                    style={{
                      transform: `rotate(${vPage.watermarkConfig!.rotation}deg)`,
                      fontSize: `${Math.max(12, vPage.watermarkConfig!.fontSize * zoom * 0.7)}px`,
                      color: vPage.watermarkConfig!.color,
                      opacity: vPage.watermarkConfig!.opacity,
                      fontWeight: 700,
                      userSelect: 'none',
                      whiteSpace: 'nowrap'
                    }}
                  >
                    {vPage.watermarkConfig!.text}
                  </span>
                </div>
              ))}
            </div>
          ) : (
            <div
              style={{
                width: '100%',
                height: '100%',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center'
              }}
            >
              <span
                style={{
                  transform: `rotate(${vPage.watermarkConfig.rotation}deg)`,
                  fontSize: `${vPage.watermarkConfig.fontSize * zoom}px`,
                  color: vPage.watermarkConfig.color,
                  opacity: vPage.watermarkConfig.opacity,
                  fontWeight: 700,
                  letterSpacing: '0.08em',
                  userSelect: 'none',
                  whiteSpace: 'nowrap'
                }}
              >
                {vPage.watermarkConfig.text}
              </span>
            </div>
          )}
        </div>
      )}

      {/* 페이지 번호 (Page Number) 오버레이 */}
      {vPage?.pageNumberConfig && pageSize && (() => {
        const pn = vPage.pageNumberConfig
        const style: React.CSSProperties = {
          position: 'absolute',
          fontSize: `${pn.fontSize * zoom}px`,
          color: pn.color,
          pointerEvents: 'none',
          userSelect: 'none',
          zIndex: 4,
          fontFamily: 'sans-serif',
          fontWeight: 500,
          whiteSpace: 'nowrap'
        }

        if (pn.position.startsWith('top')) {
          style.top = `${pn.margin * zoom}px`
        } else {
          style.bottom = `${pn.margin * zoom}px`
        }

        if (pn.position.endsWith('left')) {
          style.left = `${pn.margin * zoom}px`
        } else if (pn.position.endsWith('right')) {
          style.right = `${pn.margin * zoom}px`
        } else {
          style.left = '50%'
          style.transform = 'translateX(-50%)'
        }

        return (
          <div className="pagenumber-overlay" style={style}>
            {pn.text}
          </div>
        )
      })()}
    </>
  )
}
