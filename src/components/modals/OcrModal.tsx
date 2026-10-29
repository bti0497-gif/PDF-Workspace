/**
 * AI/광학 문자 인식(OCR) 고도화 모달
 * - Tesseract.js WebAssembly 기반 한/영 고정밀 OCR 엔진
 * - 스캔 화질 자동 보정(노이즈 제거/대비 강화) 및 2x DPI 고해상도 처리
 * - 인식 신뢰도(Confidence) 표시 및 페이지별 텍스트 인라인 편집
 * - 클립보드 복사, .txt 파일 다운로드, 페이지 텍스트 주석 삽입
 */
import { useState } from 'react'
import { useStore } from '../../lib/store'
import { runOcrOnPages, type OcrPageResult } from '../../core/services/ocr.service'
import type { Annotation } from '../../../electron/shared/types'

interface Props {
  onClose: () => void
}

type OcrLang = 'kor+eng' | 'kor' | 'eng'
type PageScope = 'current' | 'selected' | 'all'

export function OcrModal({ onClose }: Props) {
  const doc = useStore((s) => s.document)
  const pages = useStore((s) => s.pages)
  const currentPage = useStore((s) => s.currentPage)
  const selectedPageIndices = useStore((s) => s.selectedPageIndices)
  const addAnnotation = useStore((s) => s.addAnnotation)
  const setActiveTool = useStore((s) => s.setActiveTool)
  const selectAnnotation = useStore((s) => s.selectAnnotation)

  // 옵션 설정
  const [lang, setLang] = useState<OcrLang>('kor+eng')
  const [scope, setScope] = useState<PageScope>(
    selectedPageIndices.length > 1 ? 'selected' : 'current'
  )
  const [autoEnhance, setAutoEnhance] = useState(true)
  const [highDpi, setHighDpi] = useState(true)
  const [hybrid, setHybrid] = useState(true)

  // 실행 및 진행 상태
  const [isRunning, setIsRunning] = useState(false)
  const [progressPct, setProgressPct] = useState(0)
  const [statusMsg, setStatusMsg] = useState('')
  const [results, setResults] = useState<OcrPageResult[] | null>(null)
  const [activeResultTab, setActiveResultTab] = useState<number>(0)
  const [copySuccess, setCopySuccess] = useState(false)

  const getTargetIndices = (): number[] => {
    if (scope === 'current') return [currentPage]
    if (scope === 'selected') {
      return selectedPageIndices.length > 0 ? selectedPageIndices : [currentPage]
    }
    return pages.map((_, i) => i)
  }

  const targetIndices = getTargetIndices()

  // OCR 실행
  const handleStartOcr = async () => {
    if (targetIndices.length === 0) {
      alert('인식할 페이지를 선택해주세요.')
      return
    }

    setIsRunning(true)
    setProgressPct(0)
    setStatusMsg('OCR 엔진 준비 중...')

    try {
      const res = await runOcrOnPages(pages, targetIndices, {
        lang,
        autoEnhance,
        highDpi,
        hybrid,
        onProgress: (percent, status) => {
          setProgressPct(percent)
          setStatusMsg(status)
        }
      })
      setResults(res)
      setActiveResultTab(0)
    } catch (err) {
      console.error('OCR 처리 실패:', err)
      alert(`문자 인식 중 오류가 발생했습니다: ${err instanceof Error ? err.message : String(err)}`)
    } finally {
      setIsRunning(false)
    }
  }

  // 텍스트 편집 핸들러
  const handleTextChange = (pageIdx: number, newText: string) => {
    if (!results) return
    setResults(
      results.map((r, i) => (i === pageIdx ? { ...r, text: newText } : r))
    )
  }

  // 전체 텍스트 합치기
  const getAllText = (): string => {
    if (!results) return ''
    return results
      .map(
        (r) =>
          `[=== ${r.pageNumber} 페이지 (신뢰도: ${r.confidence}%) ===]\n${r.text}\n`
      )
      .join('\n')
  }

  // 1. 클립보드 복사
  const handleCopyClipboard = async () => {
    const text = getAllText()
    if (!text) return
    try {
      await navigator.clipboard.writeText(text)
      setCopySuccess(true)
      setTimeout(() => setCopySuccess(false), 2000)
    } catch (err) {
      alert('클립보드 복사에 실패했습니다.')
    }
  }

  // 2. 텍스트 파일(.txt) 저장
  const handleSaveTxt = () => {
    const text = getAllText()
    if (!text) return
    const blob = new Blob([text], { type: 'text/plain;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    const baseName = doc?.fileName.replace(/\.pdf$/i, '') || 'document'
    a.href = url
    a.download = `${baseName}_ocr_결과.txt`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
  }

  // 3. 현재 페이지에 텍스트 주석으로 삽입
  const handleInsertAsAnnotation = () => {
    if (!results || results.length === 0) return
    const currentRes = results[activeResultTab] || results[0]
    const text = (currentRes.text || '').trim()
    if (!text) {
      alert('삽입할 텍스트가 비어있습니다.')
      return
    }

    const id = `ann-ocr-${Date.now()}`
    const ann: Annotation = {
      id,
      type: 'textbox',
      pageIndex: currentPage,
      x: 36,
      y: 36,
      width: 320,
      height: Math.min(240, Math.max(80, text.split('\n').length * 20 + 40)),
      color: '#1e1e1e',
      strokeWidth: 1,
      opacity: 1,
      fontSize: 12,
      text
    }

    addAnnotation(ann)
    setActiveTool('select')
    selectAnnotation(id)
    alert(`P.${currentPage + 1} 페이지에 텍스트 상자 주석이 삽입되었습니다. 위치와 크기를 자유롭게 조절하세요.`)
    onClose()
  }

  return (
    <div className="modal-backdrop" onMouseDown={isRunning ? undefined : onClose}>
      <div
        className="modal"
        onMouseDown={(e) => e.stopPropagation()}
        style={{ width: results ? 720 : 540, maxWidth: '92vw', maxHeight: '88vh', display: 'flex', flexDirection: 'column' }}
      >
        {/* 모달 헤더 */}
        <div className="modal__header" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', paddingBottom: 12, borderBottom: '1px solid var(--color-border)' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span style={{ fontSize: 20 }}>🔍</span>
            <h2 className="modal__title" style={{ margin: 0, fontSize: 16 }}>
              AI / 광학 문자 인식 (OCR)
            </h2>
          </div>
          {!isRunning && (
            <button
              onClick={onClose}
              style={{
                background: 'transparent',
                border: 'none',
                color: 'var(--color-text-muted)',
                fontSize: 18,
                cursor: 'pointer',
                padding: '2px 8px',
                borderRadius: 4
              }}
            >
              ✕
            </button>
          )}
        </div>

        {/* 모달 바디 */}
        <div style={{ flex: 1, overflowY: 'auto', padding: '16px 0', display: 'flex', flexDirection: 'column', gap: 16 }}>
          {!results ? (
            // 옵션 설정 뷰
            <>
              {/* 언어 선택 */}
              <div>
                <label style={{ fontSize: 13, fontWeight: 600, display: 'block', marginBottom: 6 }}>
                  인식 대상 언어
                </label>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 8 }}>
                  {[
                    { id: 'kor+eng', label: '한국어 + 영어', sub: '권장' },
                    { id: 'kor', label: '한국어 전용', sub: 'kor' },
                    { id: 'eng', label: '영어 전용', sub: 'eng' }
                  ].map((item) => (
                    <button
                      key={item.id}
                      type="button"
                      disabled={isRunning}
                      onClick={() => setLang(item.id as OcrLang)}
                      style={{
                        padding: '10px 8px',
                        borderRadius: 6,
                        border: lang === item.id ? '2px solid var(--color-accent)' : '1px solid var(--color-border)',
                        background: lang === item.id ? 'var(--color-bg-elevated)' : 'transparent',
                        color: 'var(--color-text)',
                        cursor: 'pointer',
                        textAlign: 'center',
                        fontSize: 12,
                        fontWeight: lang === item.id ? 600 : 400
                      }}
                    >
                      <div>{item.label}</div>
                      <div style={{ fontSize: 10, color: 'var(--color-text-muted)', marginTop: 2 }}>{item.sub}</div>
                    </button>
                  ))}
                </div>
              </div>

              {/* 대상 페이지 범위 */}
              <div>
                <label style={{ fontSize: 13, fontWeight: 600, display: 'block', marginBottom: 6 }}>
                  인식 범위
                </label>
                <div style={{ display: 'flex', gap: 16 }}>
                  <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, cursor: 'pointer' }}>
                    <input
                      type="radio"
                      name="ocrScope"
                      checked={scope === 'current'}
                      onChange={() => setScope('current')}
                      disabled={isRunning}
                    />
                    현재 페이지 ({currentPage + 1}p)
                  </label>
                  <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, cursor: 'pointer' }}>
                    <input
                      type="radio"
                      name="ocrScope"
                      checked={scope === 'selected'}
                      onChange={() => setScope('selected')}
                      disabled={isRunning || selectedPageIndices.length <= 1}
                    />
                    선택된 페이지 ({selectedPageIndices.length}장)
                  </label>
                  <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, cursor: 'pointer' }}>
                    <input
                      type="radio"
                      name="ocrScope"
                      checked={scope === 'all'}
                      onChange={() => setScope('all')}
                      disabled={isRunning}
                    />
                    전체 페이지 ({pages.length}장)
                  </label>
                </div>
              </div>

              {/* 화질 보정 & 전처리 옵션 */}
              <div style={{ background: 'var(--color-bg-elevated)', padding: 12, borderRadius: 8, border: '1px solid var(--color-border)' }}>
                <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 8, color: 'var(--color-accent)' }}>
                  ⚙️ 전처리 및 인식 고도화 옵션
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                  <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12, cursor: 'pointer' }}>
                    <input
                      type="checkbox"
                      checked={autoEnhance}
                      onChange={(e) => setAutoEnhance(e.target.checked)}
                      disabled={isRunning}
                    />
                    <span>
                      <strong>스캔 화질 자동 보정</strong> (회색 노이즈 제거 · 한글 획 선명화 대비 보정)
                    </span>
                  </label>
                  <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12, cursor: 'pointer' }}>
                    <input
                      type="checkbox"
                      checked={highDpi}
                      onChange={(e) => setHighDpi(e.target.checked)}
                      disabled={isRunning}
                    />
                    <span>
                      <strong>고해상도 정밀 스캔</strong> (2x DPI 렌더링으로 작은 글자 인식률 향상)
                    </span>
                  </label>
                  <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12, cursor: 'pointer' }}>
                    <input
                      type="checkbox"
                      checked={hybrid}
                      onChange={(e) => setHybrid(e.target.checked)}
                      disabled={isRunning}
                    />
                    <span>
                      <strong>하이브리드 모드</strong> (PDF 내장 벡터 텍스트 우선 추출 후 스캔 보완)
                    </span>
                  </label>
                </div>
              </div>

              {/* 진행률 바 (실행 중일 때) */}
              {isRunning && (
                <div style={{ background: 'var(--color-bg-elevated)', padding: 14, borderRadius: 8, border: '1px solid var(--color-accent)' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, marginBottom: 6 }}>
                    <span style={{ fontWeight: 600, color: 'var(--color-accent)' }}>{statusMsg}</span>
                    <span>{progressPct}%</span>
                  </div>
                  <div style={{ width: '100%', height: 8, background: 'rgba(0,0,0,0.2)', borderRadius: 4, overflow: 'hidden' }}>
                    <div
                      style={{
                        width: `${progressPct}%`,
                        height: '100%',
                        background: 'var(--color-accent)',
                        transition: 'width 0.2s ease'
                      }}
                    />
                  </div>
                </div>
              )}
            </>
          ) : (
            // 결과 편집기 뷰
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              {/* 페이지 선택 탭 */}
              {results.length > 1 && (
                <div style={{ display: 'flex', gap: 6, overflowX: 'auto', paddingBottom: 4 }}>
                  {results.map((r, idx) => (
                    <button
                      key={idx}
                      type="button"
                      onClick={() => setActiveResultTab(idx)}
                      style={{
                        padding: '6px 12px',
                        fontSize: 12,
                        borderRadius: 4,
                        border: activeResultTab === idx ? '1px solid var(--color-accent)' : '1px solid var(--color-border)',
                        background: activeResultTab === idx ? 'var(--color-accent)' : 'var(--color-bg-elevated)',
                        color: activeResultTab === idx ? '#fff' : 'var(--color-text)',
                        cursor: 'pointer',
                        whiteSpace: 'nowrap'
                      }}
                    >
                      P.{r.pageNumber} ({r.confidence}%)
                    </button>
                  ))}
                </div>
              )}

              {/* 활성 페이지 신뢰도 및 상태 배지 */}
              {results[activeResultTab] && (
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12 }}>
                    <span style={{ fontWeight: 600 }}>P.{results[activeResultTab].pageNumber} 인식 결과:</span>
                    <span
                      style={{
                        padding: '2px 8px',
                        borderRadius: 10,
                        fontSize: 11,
                        fontWeight: 600,
                        background:
                          results[activeResultTab].confidence >= 80
                            ? 'rgba(16, 185, 129, 0.2)'
                            : results[activeResultTab].confidence >= 50
                            ? 'rgba(245, 158, 11, 0.2)'
                            : 'rgba(229, 83, 75, 0.2)',
                        color:
                          results[activeResultTab].confidence >= 80
                            ? '#10b981'
                            : results[activeResultTab].confidence >= 50
                            ? '#f59e0b'
                            : '#e5534b'
                      }}
                    >
                      {results[activeResultTab].isEmbedded
                        ? '내장 텍스트 (100% 정밀)'
                        : `신뢰도 ${results[activeResultTab].confidence}%`}
                    </span>
                    <span style={{ color: 'var(--color-text-muted)', fontSize: 11 }}>
                      ({results[activeResultTab].text.length} 글자)
                    </span>
                  </div>
                  <span style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>
                    💡 오탈자를 아래 에디터에서 바로 수정할 수 있습니다.
                  </span>
                </div>
              )}

              {/* 텍스트 편집 영역 */}
              {results[activeResultTab] && (
                <textarea
                  value={results[activeResultTab].text}
                  onChange={(e) => handleTextChange(activeResultTab, e.target.value)}
                  style={{
                    width: '100%',
                    height: 240,
                    padding: 10,
                    fontSize: 13,
                    fontFamily: 'inherit',
                    lineHeight: 1.6,
                    borderRadius: 6,
                    border: '1px solid var(--color-border)',
                    background: 'var(--color-bg-elevated)',
                    color: 'var(--color-text)',
                    resize: 'vertical',
                    outline: 'none'
                  }}
                  placeholder="추출된 텍스트가 없습니다."
                />
              )}
            </div>
          )}
        </div>

        {/* 모달 하단 액션 버튼 */}
        <div
          className="modal__actions"
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            paddingTop: 12,
            borderTop: '1px solid var(--color-border)'
          }}
        >
          {!results ? (
            <>
              <button
                type="button"
                className="btn btn--secondary"
                onClick={onClose}
                disabled={isRunning}
              >
                취소
              </button>
              <button
                type="button"
                className="btn btn--primary"
                onClick={handleStartOcr}
                disabled={isRunning}
                style={{ minWidth: 120 }}
              >
                {isRunning ? '문자 인식 중...' : `OCR 실행 (${targetIndices.length}장)`}
              </button>
            </>
          ) : (
            <>
              <button
                type="button"
                className="btn btn--secondary"
                onClick={() => setResults(null)}
              >
                ← 다시 설정
              </button>

              <div style={{ display: 'flex', gap: 8 }}>
                <button
                  type="button"
                  className="btn btn--secondary"
                  onClick={handleCopyClipboard}
                  title="전체 추출 텍스트를 클립보드에 복사"
                >
                  {copySuccess ? '✓ 복사됨!' : '📋 전체 텍스트 복사'}
                </button>
                <button
                  type="button"
                  className="btn btn--secondary"
                  onClick={handleSaveTxt}
                  title="텍스트(.txt) 파일로 다운로드"
                >
                  💾 .txt 저장
                </button>
                <button
                  type="button"
                  className="btn btn--primary"
                  onClick={handleInsertAsAnnotation}
                  title="현재 페이지 위에 텍스트 박스 주석으로 삽입"
                >
                  📌 텍스트 주석으로 삽입
                </button>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
