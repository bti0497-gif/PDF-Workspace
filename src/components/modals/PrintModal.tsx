/**
 * 인쇄 및 인쇄용 고품질 PDF 내보내기 모달 (Print & Export to Print Modal)
 * - 인쇄 범위 선택: 전체 / 현재 페이지 / 선택된 페이지 / 사용자 지정 범위 (예: 1-5, 8)
 * - 비파괴 가상 매니페스트(수정된 텍스트, 주석, 회전, 크롭) 지연 일괄 베이킹
 * - [프린터로 인쇄] 시스템 인쇄 대화상자 호출
 * - [인쇄용 PDF 내보내기] 고품질 인쇄 규격 PDF 파일 저장
 */
import { useState } from 'react'
import { useStore } from '../../lib/store'
import { bakeManifestToPdf, extractVirtualPagesToPdf } from '../../core/services/pdf-engine.service'

interface Props {
  onClose: () => void
}

type PrintScope = 'all' | 'current' | 'selected' | 'range'

export function PrintModal({ onClose }: Props) {
  const doc = useStore((s) => s.document)
  const pages = useStore((s) => s.pages)
  const sources = useStore((s) => s.sources)
  const annotations = useStore((s) => s.annotations)
  const currentPage = useStore((s) => s.currentPage)
  const selectedPageIndices = useStore((s) => s.selectedPageIndices)

  const [scope, setScope] = useState<PrintScope>(
    selectedPageIndices.length > 1 ? 'selected' : 'all'
  )
  const [rangeText, setRangeText] = useState('')
  const [includeAnnotations, setIncludeAnnotations] = useState(true)
  const [isProcessing, setIsProcessing] = useState(false)

  const totalPages = pages.length > 0 ? pages.length : doc?.pageCount ?? 0

  /** 인쇄 대상 인덱스 배열 계산 */
  const getTargetIndices = (): number[] => {
    if (scope === 'current') {
      return [currentPage]
    }
    if (scope === 'selected') {
      return selectedPageIndices.length > 0 ? selectedPageIndices : [currentPage]
    }
    if (scope === 'range') {
      return parsePageRanges(rangeText, totalPages)
    }
    // 'all'
    return pages.map((_, i) => i)
  }

  const targetIndices = getTargetIndices()

  /** 인쇄 대상 페이지 바이트 생성 (지연 베이킹) */
  const generatePrintBytes = async (): Promise<Uint8Array | null> => {
    if (!doc || targetIndices.length === 0) return null

    // 폰트 로드
    let fontBytes: Uint8Array | undefined
    try {
      const fontData = await window.workspace.getKoreanFont()
      if (fontData) fontBytes = new Uint8Array(fontData)
    } catch {
      /* 기본 폰트 사용 */
    }

    if (targetIndices.length === pages.length && scope === 'all') {
      return bakeManifestToPdf({
        pages,
        sources,
        annotations: includeAnnotations ? annotations : [],
        customFont: fontBytes
      })
    }

    // 부분 페이지만 추출하여 베이킹
    const subPages = targetIndices.map((idx) => pages[idx])
    return bakeManifestToPdf({
      pages: subPages,
      sources,
      annotations: includeAnnotations
        ? annotations.filter((a) => targetIndices.includes(a.pageIndex))
        : [],
      customFont: fontBytes
    })
  }

  /** 시스템 프린터로 인쇄 */
  const handlePrint = async () => {
    setIsProcessing(true)
    try {
      const bytes = await generatePrintBytes()
      if (bytes) {
        await window.workspace.printPdf(Array.from(bytes))
        onClose()
      }
    } catch (err) {
      console.error('인쇄 실패:', err)
      alert('인쇄 준비 중 오류가 발생했습니다.')
    } finally {
      setIsProcessing(false)
    }
  }

  /** 인쇄용 PDF 파일로 내보내기 */
  const handleExportPdf = async () => {
    setIsProcessing(true)
    try {
      const bytes = await generatePrintBytes()
      if (bytes && doc) {
        const baseName = doc.fileName.replace(/\.pdf$/i, '')
        const suffix =
          scope === 'all'
            ? '_인쇄용'
            : scope === 'current'
              ? `_p${currentPage + 1}_인쇄용`
              : `_선택${targetIndices.length}쪽_인쇄용`

        await window.workspace.savePdfBytes(
          Array.from(bytes),
          `${baseName}${suffix}.pdf`
        )
        onClose()
      }
    } catch (err) {
      console.error('인쇄 내보내기 실패:', err)
      alert('인쇄 파일 내보내기 중 오류가 발생했습니다.')
    } finally {
      setIsProcessing(false)
    }
  }

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal modal--print" onClick={(e) => e.stopPropagation()}>
        <div className="modal__header">
          <h2 className="modal__title">🖨️ 인쇄 및 인쇄 내보내기</h2>
          <button className="modal__close" onClick={onClose}>
            ✕
          </button>
        </div>

        <div className="modal__body">
          {/* 인쇄 대상 범위 선택 */}
          <fieldset className="modal__fieldset">
            <legend className="modal__legend">인쇄 페이지 범위</legend>
            <div className="modal__radio-group">
              <label className="modal__radio-label">
                <input
                  type="radio"
                  name="print-scope"
                  checked={scope === 'all'}
                  onChange={() => setScope('all')}
                />
                전체 페이지 ({totalPages}쪽)
              </label>

              <label className="modal__radio-label">
                <input
                  type="radio"
                  name="print-scope"
                  checked={scope === 'current'}
                  onChange={() => setScope('current')}
                />
                현재 페이지만 ({currentPage + 1}쪽)
              </label>

              <label className="modal__radio-label">
                <input
                  type="radio"
                  name="print-scope"
                  checked={scope === 'selected'}
                  onChange={() => setScope('selected')}
                  disabled={selectedPageIndices.length <= 1}
                />
                선택된 페이지만 ({selectedPageIndices.length}쪽)
              </label>

              <label className="modal__radio-label">
                <input
                  type="radio"
                  name="print-scope"
                  checked={scope === 'range'}
                  onChange={() => setScope('range')}
                />
                페이지 범위 지정:
              </label>
              {scope === 'range' && (
                <input
                  type="text"
                  className="modal__input modal__input--inline"
                  placeholder="예: 1-3, 5, 8"
                  value={rangeText}
                  onChange={(e) => setRangeText(e.target.value)}
                  autoFocus
                />
              )}
            </div>
          </fieldset>

          {/* 인쇄 옵션 */}
          <fieldset className="modal__fieldset">
            <legend className="modal__legend">출력 옵션</legend>
            <label className="modal__checkbox-label">
              <input
                type="checkbox"
                checked={includeAnnotations}
                onChange={(e) => setIncludeAnnotations(e.target.checked)}
              />
              작성된 주석, 마킹 및 본문 텍스트 수정 사항 포함
            </label>
          </fieldset>

          {/* 요약 박스 */}
          <div className="modal__summary-box">
            <div>
              <strong>문서:</strong> {doc?.fileName ?? '선택된 문서 없음'}
            </div>
            <div>
              <strong>인쇄 대상:</strong>{' '}
              <span style={{ color: 'var(--color-accent)', fontWeight: 600 }}>
                {targetIndices.length}개 페이지
              </span>{' '}
              {targetIndices.length > 0 &&
                `(${targetIndices.slice(0, 8).map((i) => i + 1).join(', ')}${targetIndices.length > 8 ? '...' : ''}쪽)`}
            </div>
            {targetIndices.length === 0 && (
              <div style={{ color: 'var(--color-danger)', fontSize: 11, marginTop: 4 }}>
                유효한 인쇄 페이지가 없습니다. 범위를 확인해주세요.
              </div>
            )}
          </div>
        </div>

        <div className="modal__footer">
          <button className="btn" onClick={onClose} disabled={isProcessing}>
            취소
          </button>
          <button
            className="btn btn--secondary"
            onClick={handleExportPdf}
            disabled={!doc || targetIndices.length === 0 || isProcessing}
            title="인쇄 규격이 적용된 PDF 파일로 저장"
          >
            📥 인쇄용 PDF 내보내기
          </button>
          <button
            className="btn btn--primary"
            onClick={handlePrint}
            disabled={!doc || targetIndices.length === 0 || isProcessing}
            title="시스템 인쇄 대화상자 호출"
          >
            {isProcessing ? '처리 중...' : '🖨️ 프린터로 인쇄'}
          </button>
        </div>
      </div>
    </div>
  )
}

/** "1-3, 5, 8" 형태의 문자열을 0-based 인덱스 배열로 변환 */
function parsePageRanges(text: string, maxPages: number): number[] {
  const parts = text.split(',')
  const set = new Set<number>()

  for (const part of parts) {
    const trimmed = part.trim()
    if (!trimmed) continue
    if (trimmed.includes('-')) {
      const [startStr, endStr] = trimmed.split('-')
      const s = parseInt(startStr, 10) - 1
      const e = parseInt(endStr, 10) - 1
      if (!isNaN(s) && !isNaN(e)) {
        for (let i = Math.max(0, s); i <= Math.min(maxPages - 1, e); i++) {
          set.add(i)
        }
      }
    } else {
      const p = parseInt(trimmed, 10) - 1
      if (!isNaN(p) && p >= 0 && p < maxPages) {
        set.add(p)
      }
    }
  }

  return Array.from(set).sort((a, b) => a - b)
}
