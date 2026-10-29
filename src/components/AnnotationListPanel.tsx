/**
 * 사이드바 주석 및 마크업 목록 패널 (Comments & Markup List Panel)
 * - 전체 주석을 페이지 번호 순으로 정렬하여 표시
 * - 페이지 배지, 주석 종류 아이콘/한글명, 색상 인디케이터, 내용 미리보기
 * - 클릭 시 해당 페이지로 즉시 이동 및 주석 선택 하이라이트
 * - 개별 삭제(×) 및 일괄 지우기 지원
 */
import { useStore } from '../lib/store'
import type { AnnotationType } from '../../electron/shared/types'

const TYPE_CONFIG: Record<AnnotationType, { label: string; icon: string }> = {
  highlight: { label: '형광펜', icon: '🖍️' },
  rectangle: { label: '사각형', icon: '▭' },
  textbox: { label: '텍스트', icon: 'T' },
  drawing: { label: '펜 그리기', icon: '✎' },
  stamp: { label: '도장/서명', icon: '💮' }
}

export function AnnotationListPanel() {
  const annotations = useStore((s) => s.annotations)
  const selectedAnnotationId = useStore((s) => s.selectedAnnotationId)
  const setCurrentPage = useStore((s) => s.setCurrentPage)
  const selectAnnotation = useStore((s) => s.selectAnnotation)
  const removeAnnotation = useStore((s) => s.removeAnnotation)
  const clearAnnotations = useStore((s) => s.clearAnnotations)

  const sortedAnnotations = [...annotations].sort((a, b) => {
    if (a.pageIndex !== b.pageIndex) return a.pageIndex - b.pageIndex
    return a.y - b.y
  })

  return (
    <div
      className="annotation-list-panel"
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        overflow: 'hidden',
        fontSize: 12,
        color: 'var(--color-text)'
      }}
    >
      {/* 헤더: 카운트 및 일괄 삭제 */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '10px 12px',
          borderBottom: '1px solid var(--color-border)',
          background: 'var(--color-bg-elevated)',
          flexShrink: 0
        }}
      >
        <span style={{ fontWeight: 600, fontSize: 12 }}>
          💬 주석 및 마크업 ({annotations.length})
        </span>

        {annotations.length > 0 && (
          <button
            type="button"
            onClick={() => {
              if (window.confirm('문서의 모든 주석을 삭제하시겠습니까?')) {
                clearAnnotations()
              }
            }}
            style={{
              background: 'transparent',
              border: 'none',
              color: '#e5534b',
              fontSize: 11,
              cursor: 'pointer',
              padding: '2px 6px',
              borderRadius: 3
            }}
            title="문서의 모든 주석 일괄 삭제"
          >
            전체 지우기
          </button>
        )}
      </div>

      {/* 목록 본문 */}
      <div style={{ flex: 1, overflowY: 'auto', padding: 8 }}>
        {sortedAnnotations.length === 0 ? (
          <div
            style={{
              padding: '32px 16px',
              textAlign: 'center',
              color: 'var(--color-text-muted)',
              lineHeight: 1.6
            }}
          >
            <div style={{ fontSize: 24, marginBottom: 8 }}>💬</div>
            <div>작성된 주석이 없습니다.</div>
            <div style={{ fontSize: 11, marginTop: 4 }}>
              상단 도구에서 형광펜, 사각형, 텍스트 상자, 마스킹 등을 추가해보세요.
            </div>
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            {sortedAnnotations.map((ann) => {
              const isSelected = ann.id === selectedAnnotationId
              const config = TYPE_CONFIG[ann.type] || { label: '주석', icon: '📌' }

              return (
                <div
                  key={ann.id}
                  onClick={() => {
                    setCurrentPage(ann.pageIndex)
                    selectAnnotation(ann.id)
                  }}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    padding: '8px 10px',
                    borderRadius: 6,
                    border: isSelected
                      ? '1px solid var(--color-accent)'
                      : '1px solid var(--color-border)',
                    background: isSelected
                      ? 'var(--color-bg-elevated)'
                      : 'rgba(255, 255, 255, 0.03)',
                    cursor: 'pointer',
                    transition: 'all 0.15s ease'
                  }}
                  title={`P.${ann.pageIndex + 1} 페이지로 이동 및 선택`}
                >
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: 8,
                      minWidth: 0,
                      flex: 1
                    }}
                  >
                    {/* 페이지 배지 */}
                    <span
                      style={{
                        padding: '1px 5px',
                        borderRadius: 3,
                        background: 'rgba(0, 0, 0, 0.15)',
                        border: '1px solid var(--color-border)',
                        fontSize: 10,
                        fontWeight: 600,
                        flexShrink: 0
                      }}
                    >
                      P.{ann.pageIndex + 1}
                    </span>

                    {/* 색상 도트 */}
                    <span
                      style={{
                        width: 8,
                        height: 8,
                        borderRadius: '50%',
                        backgroundColor: ann.color || '#000000',
                        border: '1px solid rgba(0,0,0,0.2)',
                        flexShrink: 0
                      }}
                    />

                    {/* 아이콘 및 텍스트 정보 */}
                    <div style={{ minWidth: 0, flex: 1 }}>
                      <div
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          gap: 4,
                          fontWeight: 600,
                          fontSize: 12
                        }}
                      >
                        <span>{config.icon}</span>
                        <span>{config.label}</span>
                      </div>

                      {/* 텍스트 미리보기 또는 크기 정보 */}
                      <div
                        style={{
                          fontSize: 11,
                          color: 'var(--color-text-muted)',
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap',
                          marginTop: 2
                        }}
                      >
                        {ann.text ? (
                          `"${ann.text}"`

                        ) : (
                          `${Math.round(ann.width)}×${Math.round(ann.height)}pt`
                        )}
                      </div>
                    </div>
                  </div>

                  {/* 개별 주석 삭제 버튼 */}
                  <button
                    type="button"
                    onClick={(e) => {
                      e.stopPropagation()
                      removeAnnotation(ann.id)
                    }}
                    style={{
                      background: 'transparent',
                      border: 'none',
                      color: 'var(--color-text-muted)',
                      fontSize: 14,
                      cursor: 'pointer',
                      padding: '2px 6px',
                      borderRadius: 3,
                      marginLeft: 4
                    }}
                    title="주석 삭제 (Delete)"
                  >
                    ×
                  </button>
                </div>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}
