/**
 * PDF 페이지를 고화질 이미지(PNG / JPEG)로 내보내기 모달
 * - 포맷: PNG (무손실 고화질) | JPEG (압축률 우수)
 * - 해상도/DPI: 1x (72 DPI 화면용) | 2x (150 DPI 문서/인쇄용 기본값) | 3x (216 DPI 초고해상도)
 * - 대상 페이지: 현재 페이지 | 선택된 페이지 | 전체 페이지
 * - 페이지별 진행 상태 및 일괄 다운로드 지원
 */
import { useState } from 'react'
import { useStore } from '../../lib/store'
import { getPdfDoc } from '../../hooks/usePdfDocument'
import { ROOT_DOC_ID } from '../../core/domain/document'
import type { VirtualPage } from '../../core/domain/page'
import type { Annotation } from '../../../electron/shared/types'

interface Props {
  onClose: () => void
}

type ImageFormat = 'png' | 'jpeg'
type DpiScale = 1 | 2 | 3
type PageScope = 'current' | 'selected' | 'all'

export function ExportImageModal({ onClose }: Props) {
  const doc = useStore((s) => s.document)
  const pages = useStore((s) => s.pages)
  const currentPage = useStore((s) => s.currentPage)
  const selectedPageIndices = useStore((s) => s.selectedPageIndices)
  const annotations = useStore((s) => s.annotations)

  const [format, setFormat] = useState<ImageFormat>('png')
  const [scale, setScale] = useState<DpiScale>(2)
  const [scope, setScope] = useState<PageScope>(
    selectedPageIndices.length > 1 ? 'selected' : 'current'
  )
  const [isExporting, setIsExporting] = useState(false)
  const [progress, setProgress] = useState<{ current: number; total: number } | null>(null)

  const getTargetIndices = (): number[] => {
    if (scope === 'current') return [currentPage]
    if (scope === 'selected') {
      return selectedPageIndices.length > 0 ? selectedPageIndices : [currentPage]
    }
    return pages.map((_, i) => i)
  }

  const targetIndices = getTargetIndices()
  const baseName = doc?.fileName.replace(/\.pdf$/i, '') || 'document'

  const handleExport = async () => {
    if (targetIndices.length === 0) {
      alert('내보낼 페이지가 없습니다.')
      return
    }

    setIsExporting(true)
    setProgress({ current: 0, total: targetIndices.length })

    try {
      for (let i = 0; i < targetIndices.length; i++) {
        const pageIdx = targetIndices[i]
        const vPage = pages[pageIdx]
        if (!vPage) continue

        setProgress({ current: i + 1, total: targetIndices.length })

        const dataUrl = await renderVirtualPageToImage(
          vPage,
          pageIdx,
          scale,
          format,
          annotations
        )

        const ext = format === 'png' ? 'png' : 'jpg'
        const fileName = `${baseName}_p${pageIdx + 1}.${ext}`
        downloadDataUrl(dataUrl, fileName)

        // 브라우저의 다운로드 다이얼로그 연속 차단 방지를 위한 약간의 지연
        if (targetIndices.length > 1) {
          await new Promise((r) => setTimeout(r, 200))
        }
      }

      onClose()
    } catch (err) {
      console.error('이미지 내보내기 실패:', err)
      alert(`이미지 내보내기 중 오류가 발생했습니다: ${err instanceof Error ? err.message : String(err)}`)
    } finally {
      setIsExporting(false)
      setProgress(null)
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={isExporting ? undefined : onClose}>
      <div
        className="modal"
        onMouseDown={(e) => e.stopPropagation()}
        style={{ minWidth: 460, maxWidth: 520 }}
      >
        <div className="modal__header">
          <span>🖼️ 페이지를 이미지로 내보내기</span>
          <button
            className="modal__close"
            onClick={onClose}
            title="닫기"
            disabled={isExporting}
          >
            ✕
          </button>
        </div>

        <div className="modal__body">
          {/* 포맷 선택 */}
          <div style={{ marginBottom: 16 }}>
            <label style={{ display: 'block', fontSize: 13, fontWeight: 600, marginBottom: 6 }}>
              이미지 파일 포맷:
            </label>
            <div style={{ display: 'flex', gap: 12 }}>
              <label
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 6,
                  cursor: 'pointer',
                  padding: '8px 12px',
                  borderRadius: 6,
                  border: `1px solid ${format === 'png' ? 'var(--color-accent)' : 'var(--color-border)'}`,
                  background: format === 'png' ? 'var(--color-bg-active)' : 'transparent',
                  flex: 1
                }}
              >
                <input
                  type="radio"
                  name="image-format"
                  value="png"
                  checked={format === 'png'}
                  onChange={() => setFormat('png')}
                  disabled={isExporting}
                />
                <div>
                  <div style={{ fontWeight: 600 }}>PNG</div>
                  <div style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>
                    고화질 무손실 압축 (선명한 텍스트)
                  </div>
                </div>
              </label>

              <label
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 6,
                  cursor: 'pointer',
                  padding: '8px 12px',
                  borderRadius: 6,
                  border: `1px solid ${format === 'jpeg' ? 'var(--color-accent)' : 'var(--color-border)'}`,
                  background: format === 'jpeg' ? 'var(--color-bg-active)' : 'transparent',
                  flex: 1
                }}
              >
                <input
                  type="radio"
                  name="image-format"
                  value="jpeg"
                  checked={format === 'jpeg'}
                  onChange={() => setFormat('jpeg')}
                  disabled={isExporting}
                />
                <div>
                  <div style={{ fontWeight: 600 }}>JPEG</div>
                  <div style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>
                    작은 파일 크기 (고압축)
                  </div>
                </div>
              </label>
            </div>
          </div>

          {/* 해상도 / DPI 선택 */}
          <div style={{ marginBottom: 16 }}>
            <label style={{ display: 'block', fontSize: 13, fontWeight: 600, marginBottom: 6 }}>
              해상도 (DPI):
            </label>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 8 }}>
              {[
                { scale: 1 as DpiScale, label: '1x (72 DPI)', desc: '화면용 일반 크기' },
                { scale: 2 as DpiScale, label: '2x (150 DPI)', desc: '문서/인쇄용 (권장)' },
                { scale: 3 as DpiScale, label: '3x (216 DPI)', desc: '고해상도 프레젠테이션' }
              ].map((item) => (
                <button
                  key={item.scale}
                  type="button"
                  className={`btn ${scale === item.scale ? 'btn--primary' : ''}`}
                  onClick={() => setScale(item.scale)}
                  disabled={isExporting}
                  style={{
                    display: 'flex',
                    flexDirection: 'column',
                    alignItems: 'center',
                    padding: '8px 4px',
                    textAlign: 'center'
                  }}
                >
                  <span style={{ fontWeight: 600 }}>{item.label}</span>
                  <span style={{ fontSize: 10, opacity: 0.8, marginTop: 2 }}>
                    {item.desc}
                  </span>
                </button>
              ))}
            </div>
          </div>

          {/* 대상 페이지 범위 */}
          <div style={{ marginBottom: 16 }}>
            <label style={{ display: 'block', fontSize: 13, fontWeight: 600, marginBottom: 6 }}>
              대상 페이지:
            </label>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="export-scope"
                  value="current"
                  checked={scope === 'current'}
                  onChange={() => setScope('current')}
                  disabled={isExporting}
                />
                <span>현재 페이지만 ({currentPage + 1}p)</span>
              </label>

              <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="export-scope"
                  value="selected"
                  checked={scope === 'selected'}
                  onChange={() => setScope('selected')}
                  disabled={isExporting}
                />
                <span>
                  선택된 페이지 ({selectedPageIndices.length > 0 ? selectedPageIndices.length : 1}장)
                </span>
              </label>

              <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="export-scope"
                  value="all"
                  checked={scope === 'all'}
                  onChange={() => setScope('all')}
                  disabled={isExporting}
                />
                <span>전체 페이지 ({pages.length}장)</span>
              </label>
            </div>
          </div>

          {/* 진행 상태 바 */}
          {isExporting && progress && (
            <div style={{ marginTop: 12, padding: '10px 14px', background: 'var(--color-bg)', borderRadius: 6 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, marginBottom: 6 }}>
                <span>이미지 변환 중...</span>
                <span>
                  {progress.current} / {progress.total}
                </span>
              </div>
              <div
                style={{
                  height: 6,
                  background: 'var(--color-border)',
                  borderRadius: 3,
                  overflow: 'hidden'
                }}
              >
                <div
                  style={{
                    height: '100%',
                    width: `${Math.round((progress.current / progress.total) * 100)}%`,
                    background: 'var(--color-accent)',
                    transition: 'width 0.2s ease'
                  }}
                />
              </div>
            </div>
          )}
        </div>

        <div className="modal__footer">
          <button className="btn" onClick={onClose} disabled={isExporting}>
            취소
          </button>
          <button
            className="btn btn--primary"
            onClick={handleExport}
            disabled={isExporting || targetIndices.length === 0}
          >
            {isExporting ? '내보내는 중...' : `${targetIndices.length}개 페이지 이미지 저장`}
          </button>
        </div>
      </div>
    </div>
  )
}

/** VirtualPage를 HTML5 Canvas에 렌더링하고 이미지 DataURL 추출 */
async function renderVirtualPageToImage(
  vPage: VirtualPage,
  pageIndex: number,
  scale: number,
  format: 'png' | 'jpeg',
  annotations: Annotation[]
): Promise<string> {
  const sourceDocId = vPage.sourceDocId ?? ROOT_DOC_ID
  const pdfDoc = getPdfDoc(sourceDocId)
  if (!pdfDoc) throw new Error('PDF 문서를 찾을 수 없습니다.')

  const page = await pdfDoc.getPage(vPage.sourcePageIndex + 1)
  const rotation = vPage.rotation ?? 0
  const viewport = page.getViewport({ scale, rotation })

  const canvas = document.createElement('canvas')
  canvas.width = Math.floor(viewport.width)
  canvas.height = Math.floor(viewport.height)
  const ctx = canvas.getContext('2d')
  if (!ctx) throw new Error('2D 컨텍스트를 생성할 수 없습니다.')

  // 흰색 배경 채우기 (JPEG 투명 배경 방지 및 깔끔한 출력)
  ctx.fillStyle = '#ffffff'
  ctx.fillRect(0, 0, canvas.width, canvas.height)

  // PDF.js 렌더링
  await page.render({ canvasContext: ctx, viewport }).promise

  // 워터마크 그리기 (있는 경우)
  if (vPage.watermarkConfig) {
    const wm = vPage.watermarkConfig
    ctx.save()
    ctx.globalAlpha = wm.opacity
    ctx.fillStyle = wm.color
    ctx.font = `bold ${wm.fontSize * scale}px "Malgun Gothic", "Segoe UI", sans-serif`
    ctx.textAlign = 'center'
    ctx.textBaseline = 'middle'

    const rad = (wm.rotation * Math.PI) / 180
    if (wm.tiled) {
      const cols = [canvas.width / 6, canvas.width / 2, (5 * canvas.width) / 6]
      const rows = [canvas.height / 6, canvas.height / 2, (5 * canvas.height) / 6]
      for (const cx of cols) {
        for (const cy of rows) {
          ctx.save()
          ctx.translate(cx, cy)
          ctx.rotate(rad)
          ctx.fillText(wm.text, 0, 0)
          ctx.restore()
        }
      }
    } else {
      ctx.translate(canvas.width / 2, canvas.height / 2)
      ctx.rotate(rad)
      ctx.fillText(wm.text, 0, 0)
    }
    ctx.restore()
  }

  // 페이지 번호 그리기 (있는 경우)
  if (vPage.pageNumberConfig) {
    const pn = vPage.pageNumberConfig
    ctx.save()
    ctx.fillStyle = pn.color
    ctx.font = `${pn.fontSize * scale}px "Malgun Gothic", "Segoe UI", sans-serif`
    ctx.textBaseline = pn.position.startsWith('top') ? 'top' : 'bottom'
    const margin = pn.margin * scale
    let x = margin
    let y = pn.position.startsWith('top') ? margin : canvas.height - margin

    if (pn.position.endsWith('left')) {
      ctx.textAlign = 'left'
      x = margin
    } else if (pn.position.endsWith('right')) {
      ctx.textAlign = 'right'
      x = canvas.width - margin
    } else {
      ctx.textAlign = 'center'
      x = canvas.width / 2
    }
    ctx.fillText(pn.text, x, y)
    ctx.restore()
  }

  // 주석 그리기 (스탬프 / 서명 / 텍스트 등)
  const pageAnns = annotations.filter((a) => a.pageIndex === pageIndex)
  for (const ann of pageAnns) {
    if (ann.imageDataUrl) {
      const img = new Image()
      img.src = ann.imageDataUrl
      await new Promise<void>((resolve) => {
        if (img.complete) resolve()
        else {
          img.onload = () => resolve()
          img.onerror = () => resolve()
        }
      })
      ctx.save()
      ctx.globalAlpha = ann.opacity ?? 1
      ctx.drawImage(img, ann.x * scale, ann.y * scale, ann.width * scale, ann.height * scale)
      ctx.restore()
    } else if (ann.type === 'highlight') {
      ctx.save()
      ctx.fillStyle = ann.color
      ctx.globalAlpha = (ann.opacity ?? 1) * 0.35
      ctx.fillRect(ann.x * scale, ann.y * scale, ann.width * scale, ann.height * scale)
      ctx.restore()
    } else if (ann.type === 'rectangle') {
      ctx.save()
      ctx.strokeStyle = ann.color
      ctx.lineWidth = (ann.strokeWidth ?? 2) * scale
      ctx.globalAlpha = ann.opacity ?? 1
      ctx.strokeRect(ann.x * scale, ann.y * scale, ann.width * scale, ann.height * scale)
      ctx.restore()
    } else if (ann.type === 'textbox' && ann.text) {
      ctx.save()
      ctx.fillStyle = ann.color
      ctx.globalAlpha = ann.opacity ?? 1
      ctx.font = `${(ann.fontSize ?? 14) * scale}px "Malgun Gothic", "Segoe UI", sans-serif`
      ctx.fillText(ann.text, (ann.x + 4) * scale, (ann.y + (ann.fontSize ?? 14)) * scale)
      ctx.restore()
    }
  }

  const mime = format === 'png' ? 'image/png' : 'image/jpeg'
  const quality = format === 'jpeg' ? 0.92 : undefined
  return canvas.toDataURL(mime, quality)
}

function downloadDataUrl(dataUrl: string, fileName: string) {
  const link = document.createElement('a')
  link.href = dataUrl
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
}
