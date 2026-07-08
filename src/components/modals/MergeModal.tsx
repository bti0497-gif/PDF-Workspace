/**
 * PDF 합치기 모달
 * - 여러 PDF 파일을 선택하고 순서를 조정한 뒤 하나로 병합
 */
import { useState } from 'react'
import { mergePdfs } from '../../lib/pdf-operations'
import { useStore } from '../../lib/store'
import { clearRenderCache } from '../../hooks/usePdfRender'

interface Props {
  onClose: () => void
}

interface MergeFile {
  path: string
  name: string
}

export function MergeModal({ onClose }: Props) {
  const [files, setFiles] = useState<MergeFile[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const loadFromBytes = useStore((s) => s.document) // 현재 문서 참조용 (미사용이지만 컨텍스트)
  void loadFromBytes

  const pickFiles = async () => {
    setError(null)
    const paths = await window.workspace.openMultiple()
    if (paths.length === 0) return
    const entries: MergeFile[] = paths.map((p) => ({
      path: p,
      name: p.split(/[\\/]/).pop() ?? p
    }))
    setFiles((prev) => [...prev, ...entries])
  }

  const move = (index: number, dir: -1 | 1) => {
    setFiles((prev) => {
      const next = [...prev]
      const target = index + dir
      if (target < 0 || target >= next.length) return prev
      ;[next[index], next[target]] = [next[target], next[index]]
      return next
    })
  }

  const remove = (index: number) => {
    setFiles((prev) => prev.filter((_, i) => i !== index))
  }

  const doMerge = async () => {
    if (files.length < 2) {
      setError('2개 이상의 파일을 선택하세요.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      // 각 파일 읽기
      const dataArrays: Uint8Array[] = []
      for (const f of files) {
        const data = await window.workspace.readFile(f.path)
        dataArrays.push(new Uint8Array(data))
      }
      const merged = await mergePdfs(dataArrays)
      const savedPath = await window.workspace.savePdfBytes(
        Array.from(merged),
        '병합된문서.pdf'
      )
      if (savedPath) {
        // 병합 결과를 뷰어에 로드
        const { loadPdfFromBytes } = await import('../../hooks/usePdfDocument')
        const meta = await loadPdfFromBytes(
          merged,
          savedPath.split(/[\\/]/).pop()!,
          savedPath
        )
        clearRenderCache()
        useStore.getState().setDocument(meta)
        onClose()
      }
    } catch (err) {
      console.error(err)
      setError('합치기 중 오류가 발생했습니다.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div className="modal" onMouseDown={(e) => e.stopPropagation()}>
        <div className="modal__header">
          <span>PDF 합치기</span>
          <button className="modal__close" onClick={onClose} aria-label="닫기">
            ×
          </button>
        </div>
        <div className="modal__body">
          <p style={{ marginBottom: 12, color: 'var(--color-text-muted)' }}>
            합칠 PDF 파일들을 선택하고 순서를 조정하세요. 위쪽부터 아래로 이어 붙입니다.
          </p>

          <div className="form-row">
            <button className="btn btn--primary" onClick={() => void pickFiles()}>
              + 파일 추가
            </button>
          </div>

          {files.length > 0 && (
            <ul className="file-list">
              {files.map((f, i) => (
                <li key={`${f.path}-${i}`} className="file-list__item">
                  <span className="file-list__index">{i + 1}.</span>
                  <span className="file-list__name" title={f.path}>
                    {f.name}
                  </span>
                  <div className="file-list__actions">
                    <button
                      className="file-list__btn"
                      onClick={() => move(i, -1)}
                      disabled={i === 0}
                      title="위로"
                    >
                      ↑
                    </button>
                    <button
                      className="file-list__btn"
                      onClick={() => move(i, 1)}
                      disabled={i === files.length - 1}
                      title="아래로"
                    >
                      ↓
                    </button>
                    <button
                      className="file-list__btn"
                      onClick={() => remove(i)}
                      title="제거"
                    >
                      ×
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          )}

          {error && (
            <p style={{ color: 'var(--color-danger)', marginTop: 10 }}>{error}</p>
          )}
        </div>
        <div className="modal__footer">
          <button className="btn" onClick={onClose} disabled={busy}>
            취소
          </button>
          <button className="btn btn--primary" onClick={() => void doMerge()} disabled={busy || files.length < 2}>
            {busy ? '합치는 중...' : '합치기'}
          </button>
        </div>
      </div>
    </div>
  )
}
