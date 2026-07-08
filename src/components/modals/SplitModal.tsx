/**
 * PDF 나누기 모달
 * - 페이지 범위를 지정해 여러 PDF로 분할
 * - 현재 열린 문서 기준으로 동작
 */
import { useState } from 'react'
import { splitPdf } from '../../lib/pdf-operations'
import { useStore } from '../../lib/store'

interface Props {
  onClose: () => void
}

export function SplitModal({ onClose }: Props) {
  const doc = useStore((s) => s.document)
  const [rangesText, setRangesText] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [resultMsg, setResultMsg] = useState<string | null>(null)

  const pageCount = doc?.pageCount ?? 0
  const baseName = (doc?.fileName ?? '문서').replace(/\.pdf$/i, '')

  /** "1-3, 5, 7-9" 형식을 [start,end][] (0-base) 로 파싱 */
  const parseRanges = (text: string): [number, number][] => {
    const parts = text
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean)
    const ranges: [number, number][] = []
    for (const part of parts) {
      const m = part.match(/^(\d+)\s*-\s*(\d+)$/)
      if (m) {
        const a = parseInt(m[1], 10)
        const b = parseInt(m[2], 10)
        if (a > 0 && b >= a) ranges.push([a - 1, b - 1])
      } else {
        const single = parseInt(part, 10)
        if (!isNaN(single) && single > 0) ranges.push([single - 1, single - 1])
      }
    }
    return ranges
  }

  const doSplit = async () => {
    if (!doc) return
    setError(null)
    setResultMsg(null)
    const ranges = parseRanges(rangesText)
    if (ranges.length === 0) {
      setError('올바른 페이지 범위를 입력하세요. 예: 1-3, 5, 7-9')
      return
    }
    setBusy(true)
    try {
      const results = await splitPdf(doc.bytes, ranges)
      let savedCount = 0
      for (let i = 0; i < results.length; i++) {
        const [start, end] = ranges[i]
        const name =
          start === end
            ? `${baseName}_p${start + 1}.pdf`
            : `${baseName}_p${start + 1}-${end + 1}.pdf`
        const path = await window.workspace.savePdfBytes(Array.from(results[i]), name)
        if (path) savedCount++
      }
      setResultMsg(`${savedCount}개 파일이 저장되었습니다.`)
      if (savedCount > 0) onClose()
    } catch (err) {
      console.error(err)
      setError('나누기 중 오류가 발생했습니다.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div className="modal" onMouseDown={(e) => e.stopPropagation()}>
        <div className="modal__header">
          <span>PDF 나누기</span>
          <button className="modal__close" onClick={onClose} aria-label="닫기">
            ×
          </button>
        </div>
        <div className="modal__body">
          {!doc ? (
            <p>먼저 PDF를 여세요.</p>
          ) : (
            <>
              <p style={{ marginBottom: 12, color: 'var(--color-text-muted)' }}>
                현재 문서: <strong>{doc.fileName}</strong> (총 {pageCount}페이지)
              </p>
              <div className="form-row">
                <label className="form-label">페이지 범위</label>
                <input
                  className="form-input"
                  value={rangesText}
                  onChange={(e) => setRangesText(e.target.value)}
                  placeholder={`예: 1-3, 5, 7-${pageCount}`}
                  autoFocus
                />
              </div>
              <p style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>
                쉼표로 구분된 범위를 입력하세요. 각 범위가 별도 PDF로 저장됩니다.
              </p>

              {error && (
                <p style={{ color: 'var(--color-danger)', marginTop: 10 }}>{error}</p>
              )}
              {resultMsg && (
                <p style={{ color: '#4caf50', marginTop: 10 }}>{resultMsg}</p>
              )}
            </>
          )}
        </div>
        <div className="modal__footer">
          <button className="btn" onClick={onClose} disabled={busy}>
            취소
          </button>
          <button
            className="btn btn--primary"
            onClick={() => void doSplit()}
            disabled={busy || !doc}
          >
            {busy ? '나누는 중...' : '나누기'}
          </button>
        </div>
      </div>
    </div>
  )
}
