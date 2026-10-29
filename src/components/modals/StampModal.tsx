/**
 * 도장 · 비즈니스 스탬프 · 전자서명 · 이미지 삽입 모달
 * - 탭 1: 결재 도장 / 비즈니스 스탬프 (프리셋, 사용자 입력, 날짜, 서명자, 사각/원형/타원)
 * - 탭 2: 자필 전자서명 그리기 (마우스 드로잉, 잉크 색상/굵기, 투명 배경)
 * - 탭 3: 이미지 파일 불러오기 (회사 직인, 로고, 사진)
 */
import { useState, useRef, useEffect, useCallback } from 'react'
import { useStore } from '../../lib/store'
import type { Annotation } from '../../../electron/shared/types'

interface Props {
  onClose: () => void
  initialTab?: TabType
}

type TabType = 'stamp' | 'signature' | 'image'
type StampShape = 'square' | 'circle' | 'oval'

const PRESET_STAMPS = [
  '승인 (APPROVED)',
  '결재완료',
  '검토필',
  '반려 (REJECTED)',
  '대외비 (CONFIDENTIAL)',
  '사본 (COPY)',
  '긴급 (URGENT)'
]

const STAMP_COLORS = [
  { label: '인주 빨강', color: '#d92d20' },
  { label: '포인트 파랑', color: '#0e6fd6' },
  { label: '확인 초록', color: '#10b981' },
  { label: '진한 검정', color: '#1e1e1e' }
]

const SIGNATURE_COLORS = [
  { label: '검정', color: '#111111' },
  { label: '청색', color: '#0e6fd6' },
  { label: '적색', color: '#d92d20' }
]

export function StampModal({ onClose, initialTab = 'stamp' }: Props) {
  const currentPage = useStore((s) => s.currentPage)
  const addAnnotation = useStore((s) => s.addAnnotation)
  const selectAnnotation = useStore((s) => s.selectAnnotation)
  const setActiveTool = useStore((s) => s.setActiveTool)

  const [activeTab, setActiveTab] = useState<TabType>(initialTab)

  // ── 탭 1: 스탬프 상태 ──
  const [stampText, setStampText] = useState('승인 (APPROVED)')
  const [signerName, setSignerName] = useState('더존비즈온')
  const [includeDate, setIncludeDate] = useState(true)
  const [stampShape, setStampShape] = useState<StampShape>('square')
  const [stampColor, setStampColor] = useState('#d92d20')
  const stampCanvasRef = useRef<HTMLCanvasElement>(null)

  // ── 탭 2: 자필 서명 상태 ──
  const signCanvasRef = useRef<HTMLCanvasElement>(null)
  const [signColor, setSignColor] = useState('#111111')
  const [signStrokeWidth, setSignStrokeWidth] = useState(3)
  const [isDrawing, setIsDrawing] = useState(false)
  const [hasSignature, setHasSignature] = useState(false)
  const lastPosRef = useRef<{ x: number; y: number } | null>(null)

  // ── 탭 3: 이미지 파일 상태 ──
  const [uploadedImageUrl, setUploadedImageUrl] = useState<string | null>(null)
  const [uploadedImageDim, setUploadedImageDim] = useState<{ width: number; height: number } | null>(null)
  const [uploadedFileName, setUploadedFileName] = useState<string>('')

  // ── 오늘 날짜 포맷 (YYYY.MM.DD) ──
  const todayStr = (() => {
    const d = new Date()
    const year = d.getFullYear()
    const month = String(d.getMonth() + 1).padStart(2, '0')
    const day = String(d.getDate()).padStart(2, '0')
    return `${year}.${month}.${day}`
  })()

  // ── 스탬프 캔버스 실시간 렌더링 ──
  const drawStamp = useCallback(() => {
    const canvas = stampCanvasRef.current
    if (!canvas) return
    const ctx = canvas.getContext('2d')
    if (!ctx) return

    // 고해상도 캔버스 설정 (280 x 140)
    const w = 320
    const h = stampShape === 'circle' ? 240 : 160
    canvas.width = w * 2
    canvas.height = h * 2
    canvas.style.width = `${w}px`
    canvas.style.height = `${h}px`

    ctx.save()
    ctx.scale(2, 2)
    ctx.clearRect(0, 0, w, h)

    const color = stampColor
    ctx.strokeStyle = color
    ctx.fillStyle = color

    const pad = 8

    if (stampShape === 'square') {
      // 외곽 사각형 (라운드 4px)
      ctx.lineWidth = 3.5
      drawRoundedRect(ctx, pad, pad, w - pad * 2, h - pad * 2, 6)
      ctx.stroke()

      // 내부 얇은 보더
      ctx.lineWidth = 1.2
      drawRoundedRect(ctx, pad + 4, pad + 4, w - pad * 2 - 8, h - pad * 2 - 8, 4)
      ctx.stroke()

      // 상단 구분선 (서명자/부서명이 있을 때)
      let textCenterY = h / 2
      if (signerName.trim() && includeDate) {
        textCenterY = h / 2 - 12
      } else if (signerName.trim() || includeDate) {
        textCenterY = h / 2 - 8
      }

      // 메인 텍스트
      ctx.font = 'bold 22px "Malgun Gothic", "Segoe UI", sans-serif'
      ctx.textAlign = 'center'
      ctx.textBaseline = 'middle'
      ctx.fillText(stampText || '승인', w / 2, textCenterY)

      // 서명자 및 날짜
      let subY = textCenterY + 26
      if (signerName.trim()) {
        ctx.font = '600 13px "Malgun Gothic", "Segoe UI", sans-serif'
        ctx.fillText(signerName.trim(), w / 2, subY)
        subY += 18
      }
      if (includeDate) {
        ctx.font = '12px "Malgun Gothic", "Segoe UI", sans-serif'
        ctx.fillText(todayStr, w / 2, subY)
      }
    } else if (stampShape === 'circle') {
      const cx = w / 2
      const cy = h / 2
      const r = Math.min(w, h) / 2 - pad

      // 외곽 원
      ctx.lineWidth = 3.5
      ctx.beginPath()
      ctx.arc(cx, cy, r, 0, Math.PI * 2)
      ctx.stroke()

      // 내부 얇은 원
      ctx.lineWidth = 1.2
      ctx.beginPath()
      ctx.arc(cx, cy, r - 5, 0, Math.PI * 2)
      ctx.stroke()

      // 메인 텍스트
      let textCenterY = cy
      if (signerName.trim() && includeDate) {
        textCenterY = cy - 10
      } else if (signerName.trim() || includeDate) {
        textCenterY = cy - 6
      }

      ctx.font = 'bold 20px "Malgun Gothic", "Segoe UI", sans-serif'
      ctx.textAlign = 'center'
      ctx.textBaseline = 'middle'
      ctx.fillText(stampText || '승인', cx, textCenterY)

      let subY = textCenterY + 24
      if (signerName.trim()) {
        ctx.font = '600 12px "Malgun Gothic", "Segoe UI", sans-serif'
        ctx.fillText(signerName.trim(), cx, subY)
        subY += 16
      }
      if (includeDate) {
        ctx.font = '11px "Malgun Gothic", "Segoe UI", sans-serif'
        ctx.fillText(todayStr, cx, subY)
      }
    } else if (stampShape === 'oval') {
      const cx = w / 2
      const cy = h / 2
      const rx = w / 2 - pad
      const ry = h / 2 - pad

      // 외곽 타원
      ctx.lineWidth = 3.5
      ctx.beginPath()
      ctx.ellipse(cx, cy, rx, ry, 0, 0, Math.PI * 2)
      ctx.stroke()

      // 내부 얇은 타원
      ctx.lineWidth = 1.2
      ctx.beginPath()
      ctx.ellipse(cx, cy, rx - 5, ry - 5, 0, 0, Math.PI * 2)
      ctx.stroke()

      let textCenterY = cy
      if (signerName.trim() && includeDate) {
        textCenterY = cy - 12
      } else if (signerName.trim() || includeDate) {
        textCenterY = cy - 8
      }

      ctx.font = 'bold 22px "Malgun Gothic", "Segoe UI", sans-serif'
      ctx.textAlign = 'center'
      ctx.textBaseline = 'middle'
      ctx.fillText(stampText || '승인', cx, textCenterY)

      let subY = textCenterY + 26
      if (signerName.trim()) {
        ctx.font = '600 13px "Malgun Gothic", "Segoe UI", sans-serif'
        ctx.fillText(signerName.trim(), cx, subY)
        subY += 18
      }
      if (includeDate) {
        ctx.font = '12px "Malgun Gothic", "Segoe UI", sans-serif'
        ctx.fillText(todayStr, cx, subY)
      }
    }

    ctx.restore()
  }, [stampText, signerName, includeDate, stampShape, stampColor, todayStr])

  useEffect(() => {
    if (activeTab === 'stamp') {
      drawStamp()
    }
  }, [activeTab, drawStamp])

  // ── 서명 캔버스 초기화 ──
  const clearSignature = () => {
    const canvas = signCanvasRef.current
    if (!canvas) return
    const ctx = canvas.getContext('2d')
    if (!ctx) return
    ctx.clearRect(0, 0, canvas.width, canvas.height)
    setHasSignature(false)
  }

  // ── 서명 마우스 이벤트 ──
  const getCanvasPos = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = signCanvasRef.current
    if (!canvas) return { x: 0, y: 0 }
    const rect = canvas.getBoundingClientRect()
    const scaleX = canvas.width / rect.width
    const scaleY = canvas.height / rect.height
    return {
      x: (e.clientX - rect.left) * scaleX,
      y: (e.clientY - rect.top) * scaleY
    }
  }

  const handleSignMouseDown = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const pos = getCanvasPos(e)
    setIsDrawing(true)
    lastPosRef.current = pos
    const ctx = signCanvasRef.current?.getContext('2d')
    if (ctx) {
      ctx.beginPath()
      ctx.arc(pos.x, pos.y, signStrokeWidth / 2, 0, Math.PI * 2)
      ctx.fillStyle = signColor
      ctx.fill()
    }
  }

  const handleSignMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    if (!isDrawing || !lastPosRef.current) return
    const pos = getCanvasPos(e)
    const ctx = signCanvasRef.current?.getContext('2d')
    if (ctx) {
      ctx.strokeStyle = signColor
      ctx.lineWidth = signStrokeWidth * 2 // 캔버스 레티나 스케일 대응
      ctx.lineCap = 'round'
      ctx.lineJoin = 'round'
      ctx.beginPath()
      ctx.moveTo(lastPosRef.current.x, lastPosRef.current.y)
      ctx.lineTo(pos.x, pos.y)
      ctx.stroke()
      setHasSignature(true)
    }
    lastPosRef.current = pos
  }

  const handleSignMouseUp = () => {
    setIsDrawing(false)
    lastPosRef.current = null
  }

  // ── 탭 3: 이미지 파일 업로드 ──
  const handleImageFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return

    const reader = new FileReader()
    reader.onload = (event) => {
      const dataUrl = event.target?.result as string
      if (dataUrl) {
        const img = new Image()
        img.onload = () => {
          setUploadedImageUrl(dataUrl)
          setUploadedImageDim({ width: img.naturalWidth, height: img.naturalHeight })
          setUploadedFileName(file.name)
        }
        img.src = dataUrl
      }
    }
    reader.readAsDataURL(file)
  }

  // ── 현재 페이지에 배치 ──
  const handlePlaceOnPage = () => {
    let dataUrl: string | null = null
    let aspect = 2 // 기본 너비 / 높이 비

    if (activeTab === 'stamp') {
      const canvas = stampCanvasRef.current
      if (canvas) {
        dataUrl = canvas.toDataURL('image/png')
        aspect = canvas.width / canvas.height
      }
    } else if (activeTab === 'signature') {
      const canvas = signCanvasRef.current
      if (canvas && hasSignature) {
        dataUrl = canvas.toDataURL('image/png')
        aspect = canvas.width / canvas.height
      } else {
        alert('서명을 먼저 마우스로 그려주세요.')
        return
      }
    } else if (activeTab === 'image') {
      if (uploadedImageUrl && uploadedImageDim) {
        dataUrl = uploadedImageUrl
        aspect = uploadedImageDim.width / uploadedImageDim.height
      } else {
        alert('배치할 이미지 파일을 먼저 선택해주세요.')
        return
      }
    }

    if (!dataUrl) return

    const baseWidth = 140
    const calculatedHeight = Math.round(baseWidth / aspect)
    const id = `stamp-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`

    const newAnnotation: Annotation = {
      id,
      type: 'stamp',
      pageIndex: Math.max(0, currentPage),
      x: 180,
      y: 200,
      width: baseWidth,
      height: Math.max(30, Math.min(200, calculatedHeight)),
      color: activeTab === 'stamp' ? stampColor : '#1e1e1e',
      opacity: 1,
      imageDataUrl: dataUrl
    }

    addAnnotation(newAnnotation)
    selectAnnotation(id)
    setActiveTool('select')
    onClose()
  }

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div
        className="modal"
        onMouseDown={(e) => e.stopPropagation()}
        style={{ minWidth: 540, maxWidth: 620 }}
      >
        <div className="modal__header">
          <span>💮 도장 · 전자서명 · 이미지 삽입</span>
          <button className="modal__close" onClick={onClose} title="닫기">
            ✕
          </button>
        </div>

        {/* 탭 헤더 */}
        <div
          style={{
            display: 'flex',
            borderBottom: '1px solid var(--color-border)',
            background: 'var(--color-bg-panel)',
            padding: '0 16px'
          }}
        >
          <button
            onClick={() => setActiveTab('stamp')}
            style={{
              padding: '10px 16px',
              border: 'none',
              borderBottom: activeTab === 'stamp' ? '2px solid var(--color-accent)' : '2px solid transparent',
              background: 'transparent',
              color: activeTab === 'stamp' ? 'var(--color-accent)' : 'var(--color-text)',
              fontWeight: activeTab === 'stamp' ? 600 : 400,
              cursor: 'pointer',
              fontSize: 13
            }}
          >
            결재 도장 / 스탬프
          </button>
          <button
            onClick={() => setActiveTab('signature')}
            style={{
              padding: '10px 16px',
              border: 'none',
              borderBottom: activeTab === 'signature' ? '2px solid var(--color-accent)' : '2px solid transparent',
              background: 'transparent',
              color: activeTab === 'signature' ? 'var(--color-accent)' : 'var(--color-text)',
              fontWeight: activeTab === 'signature' ? 600 : 400,
              cursor: 'pointer',
              fontSize: 13
            }}
          >
            자필 전자서명
          </button>
          <button
            onClick={() => setActiveTab('image')}
            style={{
              padding: '10px 16px',
              border: 'none',
              borderBottom: activeTab === 'image' ? '2px solid var(--color-accent)' : '2px solid transparent',
              background: 'transparent',
              color: activeTab === 'image' ? 'var(--color-accent)' : 'var(--color-text)',
              fontWeight: activeTab === 'image' ? 600 : 400,
              cursor: 'pointer',
              fontSize: 13
            }}
          >
            이미지 불러오기
          </button>
        </div>

        <div className="modal__body" style={{ maxHeight: '72vh', overflowY: 'auto' }}>
          {/* ────────────────────────────────────────────────────────── */}
          {/* 탭 1: 결재 도장 / 비즈니스 스탬프 */}
          {/* ────────────────────────────────────────────────────────── */}
          {activeTab === 'stamp' && (
            <div>
              {/* 프리셋 버튼 */}
              <div style={{ marginBottom: 12 }}>
                <div style={{ fontSize: 12, color: 'var(--color-text-muted)', marginBottom: 6 }}>
                  자주 쓰는 결재 스탬프 프리셋:
                </div>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                  {PRESETSTAMPS_map(PRESET_STAMPS, stampText, setStampText)}
                </div>
              </div>

              {/* 스탬프 속성 폼 */}
              <div
                style={{
                  display: 'grid',
                  gridTemplateColumns: '1fr 1fr',
                  gap: 12,
                  marginBottom: 14
                }}
              >
                <div>
                  <label style={{ display: 'block', fontSize: 12, marginBottom: 4 }}>
                    스탬프 문구:
                  </label>
                  <input
                    type="text"
                    value={stampText}
                    onChange={(e) => setStampText(e.target.value)}
                    placeholder="예: 승인, 결재완료"
                    style={{
                      width: '100%',
                      padding: '6px 8px',
                      background: 'var(--color-bg)',
                      border: '1px solid var(--color-border)',
                      borderRadius: 4,
                      color: 'inherit'
                    }}
                  />
                </div>

                <div>
                  <label style={{ display: 'block', fontSize: 12, marginBottom: 4 }}>
                    서명자 / 부서명 (선택):
                  </label>
                  <input
                    type="text"
                    value={signerName}
                    onChange={(e) => setSignerName(e.target.value)}
                    placeholder="예: 더존비즈온, 홍길동 인"
                    style={{
                      width: '100%',
                      padding: '6px 8px',
                      background: 'var(--color-bg)',
                      border: '1px solid var(--color-border)',
                      borderRadius: 4,
                      color: 'inherit'
                    }}
                  />
                </div>
              </div>

              {/* 모양 및 색상 & 옵션 */}
              <div
                style={{
                  display: 'grid',
                  gridTemplateColumns: '1fr 1fr',
                  gap: 12,
                  alignItems: 'center',
                  marginBottom: 14
                }}
              >
                <div>
                  <label style={{ display: 'block', fontSize: 12, marginBottom: 4 }}>
                    도장 형태:
                  </label>
                  <div style={{ display: 'flex', gap: 8 }}>
                    {(
                      [
                        { id: 'square', label: '사각 결재인' },
                        { id: 'circle', label: '원형 도장' },
                        { id: 'oval', label: '타원 스탬프' }
                      ] as const
                    ).map((shape) => (
                      <button
                        key={shape.id}
                        type="button"
                        onClick={() => setStampShape(shape.id)}
                        className={`btn ${stampShape === shape.id ? 'btn--primary' : ''}`}
                        style={{ padding: '4px 10px', fontSize: 12 }}
                      >
                        {shape.label}
                      </button>
                    ))}
                  </div>
                </div>

                <div>
                  <label style={{ display: 'block', fontSize: 12, marginBottom: 4 }}>
                    인주 색상:
                  </label>
                  <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                    {STAMP_COLORS.map((c) => (
                      <button
                        key={c.color}
                        type="button"
                        onClick={() => setStampColor(c.color)}
                        title={c.label}
                        style={{
                          width: 26,
                          height: 26,
                          borderRadius: '50%',
                          backgroundColor: c.color,
                          border: stampColor === c.color ? '2px solid #fff' : '1px solid transparent',
                          outline: stampColor === c.color ? '2px solid var(--color-accent)' : 'none',
                          cursor: 'pointer'
                        }}
                      />
                    ))}
                  </div>
                </div>
              </div>

              <div style={{ marginBottom: 14 }}>
                <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 12, cursor: 'pointer' }}>
                  <input
                    type="checkbox"
                    checked={includeDate}
                    onChange={(e) => setIncludeDate(e.target.checked)}
                  />
                  <span>오늘 날짜 포함 ({todayStr})</span>
                </label>
              </div>

              {/* 라이브 미리보기 */}
              <div
                style={{
                  background: 'repeating-conic-gradient(#80808020 0% 25%, transparent 0% 50%) 50% / 16px 16px',
                  border: '1px dashed var(--color-border)',
                  borderRadius: 6,
                  padding: 16,
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  justifyContent: 'center',
                  minHeight: 180
                }}
              >
                <canvas ref={stampCanvasRef} />
              </div>
            </div>
          )}

          {/* ────────────────────────────────────────────────────────── */}
          {/* 탭 2: 자필 전자서명 그리기 */}
          {/* ────────────────────────────────────────────────────────── */}
          {activeTab === 'signature' && (
            <div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
                <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>
                  마우스를 드래그하여 서명을 그리세요:
                </div>
                <div style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
                  <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                    <span style={{ fontSize: 12 }}>잉크:</span>
                    {SIGNATURE_COLORS.map((c) => (
                      <button
                        key={c.color}
                        type="button"
                        onClick={() => setSignColor(c.color)}
                        title={c.label}
                        style={{
                          width: 20,
                          height: 20,
                          borderRadius: '50%',
                          backgroundColor: c.color,
                          border: signColor === c.color ? '2px solid #fff' : '1px solid transparent',
                          outline: signColor === c.color ? '2px solid var(--color-accent)' : 'none',
                          cursor: 'pointer'
                        }}
                      />
                    ))}
                  </div>

                  <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                    <span style={{ fontSize: 12 }}>굵기:</span>
                    {[2, 3, 5].map((w) => (
                      <button
                        key={w}
                        type="button"
                        onClick={() => setSignStrokeWidth(w)}
                        className={`btn ${signStrokeWidth === w ? 'btn--primary' : ''}`}
                        style={{ padding: '2px 8px', fontSize: 11 }}
                      >
                        {w}px
                      </button>
                    ))}
                  </div>

                  <button
                    type="button"
                    onClick={clearSignature}
                    className="btn btn--danger"
                    style={{ padding: '4px 10px', fontSize: 12 }}
                  >
                    지우기
                  </button>
                </div>
              </div>

              {/* 서명 드로잉 캔버스 */}
              <div
                style={{
                  background: 'repeating-conic-gradient(#80808020 0% 25%, transparent 0% 50%) 50% / 16px 16px',
                  border: '1px solid var(--color-border)',
                  borderRadius: 6,
                  overflow: 'hidden',
                  display: 'flex',
                  justifyContent: 'center',
                  alignItems: 'center',
                  cursor: 'crosshair',
                  backgroundColor: 'rgba(255,255,255,0.02)'
                }}
              >
                <canvas
                  ref={signCanvasRef}
                  width={840}
                  height={360}
                  style={{ width: 420, height: 180, touchAction: 'none' }}
                  onMouseDown={handleSignMouseDown}
                  onMouseMove={handleSignMouseMove}
                  onMouseUp={handleSignMouseUp}
                  onMouseLeave={handleSignMouseUp}
                />
              </div>
              <div style={{ fontSize: 11, color: 'var(--color-text-muted)', marginTop: 6, textAlign: 'center' }}>
                배경이 투명한 PNG 서명으로 저장되어 본문 글자나 결재란 위에 자연스럽게 얹어집니다.
              </div>
            </div>
          )}

          {/* ────────────────────────────────────────────────────────── */}
          {/* 탭 3: 이미지 파일 불러오기 */}
          {/* ────────────────────────────────────────────────────────── */}
          {activeTab === 'image' && (
            <div>
              <div style={{ marginBottom: 14 }}>
                <label style={{ display: 'block', fontSize: 12, marginBottom: 6 }}>
                  회사 직인 / 도장 / 로고 이미지 파일 선택:
                </label>
                <input
                  type="file"
                  accept="image/png,image/jpeg,image/webp"
                  onChange={handleImageFileChange}
                  style={{
                    display: 'block',
                    width: '100%',
                    padding: '8px',
                    background: 'var(--color-bg)',
                    border: '1px dashed var(--color-border)',
                    borderRadius: 6,
                    color: 'inherit',
                    cursor: 'pointer'
                  }}
                />
                <div style={{ fontSize: 11, color: 'var(--color-text-muted)', marginTop: 4 }}>
                  지원 형식: PNG (투명 배경 권장), JPEG, WebP
                </div>
              </div>

              {/* 이미지 미리보기 */}
              {uploadedImageUrl ? (
                <div
                  style={{
                    background: 'repeating-conic-gradient(#80808020 0% 25%, transparent 0% 50%) 50% / 16px 16px',
                    border: '1px solid var(--color-border)',
                    borderRadius: 6,
                    padding: 16,
                    display: 'flex',
                    flexDirection: 'column',
                    alignItems: 'center',
                    justifyContent: 'center',
                    minHeight: 180
                  }}
                >
                  <img
                    src={uploadedImageUrl}
                    alt="업로드 미리보기"
                    style={{
                      maxWidth: '100%',
                      maxHeight: 180,
                      objectFit: 'contain'
                    }}
                  />
                  <div style={{ fontSize: 12, marginTop: 8, color: 'var(--color-text-muted)' }}>
                    {uploadedFileName} ({uploadedImageDim?.width} × {uploadedImageDim?.height}px)
                  </div>
                </div>
              ) : (
                <div
                  style={{
                    border: '1px dashed var(--color-border)',
                    borderRadius: 6,
                    padding: 32,
                    textAlign: 'center',
                    color: 'var(--color-text-muted)'
                  }}
                >
                  선택된 이미지가 없습니다. 위에서 이미지 파일을 선택해주세요.
                </div>
              )}
            </div>
          )}
        </div>

        <div className="modal__footer">
          <button className="btn" onClick={onClose}>
            취소
          </button>
          <button className="btn btn--primary" onClick={handlePlaceOnPage}>
            현재 페이지({currentPage + 1}p)에 배치
          </button>
        </div>
      </div>
    </div>
  )
}

function PRESETSTAMPS_map(
  presets: string[],
  current: string,
  onSelect: (t: string) => void
) {
  return presets.map((preset) => (
    <button
      key={preset}
      type="button"
      onClick={() => onSelect(preset)}
      className={`btn ${current === preset ? 'btn--primary' : ''}`}
      style={{ padding: '3px 8px', fontSize: 11 }}
    >
      {preset}
    </button>
  ))
}

function drawRoundedRect(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  w: number,
  h: number,
  r: number
) {
  ctx.beginPath()
  ctx.moveTo(x + r, y)
  ctx.lineTo(x + w - r, y)
  ctx.quadraticCurveTo(x + w, y, x + w, y + r)
  ctx.lineTo(x + w, y + h - r)
  ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h)
  ctx.lineTo(x + r, y + h)
  ctx.quadraticCurveTo(x, y + h, x, y + h - r)
  ctx.lineTo(x, y + r)
  ctx.quadraticCurveTo(x, y, x + r, y)
  ctx.closePath()
}
