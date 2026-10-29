/**
 * 문서 속성 및 메타데이터 편집 모달 (Document Properties / Metadata Modal)
 * - 파일 정보, 전체 페이지 수, 현재 페이지 판형(mm & pt) 표시
 * - 제목(Title), 작성자(Author), 주제(Subject), 키워드(Keywords), 생성 프로그램(Creator) 조회 및 편집
 * - 적용 시 지연 베이킹(Deferred Baking) 파이프라인에 주입되어 최종 PDF에 영구 보존
 */
import { useState } from 'react'
import { useStore, type DocumentMetadata } from '../../lib/store'

interface Props {
  onClose: () => void
}

export function MetadataModal({ onClose }: Props) {
  const doc = useStore((s) => s.document)
  const pages = useStore((s) => s.pages)
  const currentPage = useStore((s) => s.currentPage)
  const currentMetadata = useStore((s) => s.metadata)
  const setDocumentMetadata = useStore((s) => s.setDocumentMetadata)

  const [title, setTitle] = useState(currentMetadata?.title ?? '')
  const [author, setAuthor] = useState(currentMetadata?.author ?? '')
  const [subject, setSubject] = useState(currentMetadata?.subject ?? '')
  const [keywords, setKeywords] = useState(currentMetadata?.keywords ?? '')
  const [creator, setCreator] = useState(currentMetadata?.creator ?? '더존 PDF 편집기 (Douzone PDF Editor)')

  const [savedSuccess, setSavedSuccess] = useState(false)

  // 현재 페이지 규격 계산 (포인트 -> mm 변환)
  const vPage = pages[currentPage]
  const ptWidth = vPage?.width ?? 595.28 // A4 기본값
  const ptHeight = vPage?.height ?? 841.89
  const mmWidth = Math.round(ptWidth * 0.352778)
  const mmHeight = Math.round(ptHeight * 0.352778)

  const handleSave = () => {
    const meta: DocumentMetadata = {
      title: title.trim(),
      author: author.trim(),
      subject: subject.trim(),
      keywords: keywords.trim(),
      creator: creator.trim()
    }

    setDocumentMetadata(meta)
    setSavedSuccess(true)
    setTimeout(() => {
      onClose()
    }, 400)
  }

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div
        className="modal"
        onMouseDown={(e) => e.stopPropagation()}
        style={{ width: 560, maxWidth: '92vw' }}
      >
        {/* 헤더 */}
        <div
          className="modal__header"
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            paddingBottom: 12,
            borderBottom: '1px solid var(--color-border)'
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span style={{ fontSize: 20 }}>ℹ️</span>
            <h2 className="modal__title" style={{ margin: 0, fontSize: 16 }}>
              문서 속성 (Metadata)
            </h2>
          </div>
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
        </div>

        {/* 바디 */}
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: 16,
            padding: '16px 0',
            maxHeight: '75vh',
            overflowY: 'auto'
          }}
        >
          {/* 기본 파일 정보 요약 카드 */}
          <div
            style={{
              background: 'var(--color-bg-elevated)',
              padding: 12,
              borderRadius: 8,
              border: '1px solid var(--color-border)',
              display: 'grid',
              gridTemplateColumns: 'repeat(2, 1fr)',
              gap: 8,
              fontSize: 12
            }}
          >
            <div>
              <span style={{ color: 'var(--color-text-muted)', display: 'block' }}>파일명</span>
              <strong style={{ overflow: 'hidden', textOverflow: 'ellipsis', display: 'block' }}>
                {doc?.fileName ?? '문서 없음'}
              </strong>
            </div>
            <div>
              <span style={{ color: 'var(--color-text-muted)', display: 'block' }}>전체 페이지 수</span>
              <strong>{pages.length || doc?.pageCount || 0} 페이지</strong>
            </div>
            <div style={{ gridColumn: 'span 2' }}>
              <span style={{ color: 'var(--color-text-muted)', display: 'block' }}>파일 경로</span>
              <span
                style={{
                  color: 'var(--color-text)',
                  fontSize: 11,
                  wordBreak: 'break-all',
                  display: 'block'
                }}
              >
                {doc?.filePath ?? '저장되지 않음 (새 문서)'}
              </span>
            </div>
            <div style={{ gridColumn: 'span 2' }}>
              <span style={{ color: 'var(--color-text-muted)', display: 'block' }}>
                현재 페이지 규격 (P.{currentPage + 1})
              </span>
              <strong>
                {mmWidth} × {mmHeight} mm ({Math.round(ptWidth)} × {Math.round(ptHeight)} pt)
              </strong>
            </div>
          </div>

          {/* 메타데이터 편집 필드 */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            <div>
              <label style={{ fontSize: 13, fontWeight: 600, display: 'block', marginBottom: 4 }}>
                문서 제목 (Title)
              </label>
              <input
                type="text"
                className="input"
                style={{ width: '100%', padding: '8px 10px', fontSize: 13 }}
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                placeholder="예: 2026년 상반기 사업계획서"
              />
            </div>

            <div>
              <label style={{ fontSize: 13, fontWeight: 600, display: 'block', marginBottom: 4 }}>
                작성자 (Author)
              </label>
              <input
                type="text"
                className="input"
                style={{ width: '100%', padding: '8px 10px', fontSize: 13 }}
                value={author}
                onChange={(e) => setAuthor(e.target.value)}
                placeholder="예: 홍길동 팀장"
              />
            </div>

            <div>
              <label style={{ fontSize: 13, fontWeight: 600, display: 'block', marginBottom: 4 }}>
                주제 / 부서 (Subject)
              </label>
              <input
                type="text"
                className="input"
                style={{ width: '100%', padding: '8px 10px', fontSize: 13 }}
                value={subject}
                onChange={(e) => setSubject(e.target.value)}
                placeholder="예: 경영기획본부 전략기획팀"
              />
            </div>

            <div>
              <label style={{ fontSize: 13, fontWeight: 600, display: 'block', marginBottom: 4 }}>
                키워드 (Keywords, 쉼표로 구분)
              </label>
              <input
                type="text"
                className="input"
                style={{ width: '100%', padding: '8px 10px', fontSize: 13 }}
                value={keywords}
                onChange={(e) => setKeywords(e.target.value)}
                placeholder="예: 예산, 더존, 기획, 사업계획"
              />
            </div>

            <div>
              <label style={{ fontSize: 13, fontWeight: 600, display: 'block', marginBottom: 4 }}>
                생성 프로그램 (Creator / Producer)
              </label>
              <input
                type="text"
                className="input"
                style={{ width: '100%', padding: '8px 10px', fontSize: 13 }}
                value={creator}
                onChange={(e) => setCreator(e.target.value)}
                placeholder="더존 PDF 편집기"
              />
            </div>
          </div>
        </div>

        {/* 액션 버튼 */}
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
          <button type="button" className="btn btn--secondary" onClick={onClose}>
            취소
          </button>
          <button
            type="button"
            className="btn btn--primary"
            onClick={handleSave}
            style={{ minWidth: 100 }}
          >
            {savedSuccess ? '✓ 적용 완료!' : '속성 저장'}
          </button>
        </div>
      </div>
    </div>
  )
}
