/**
 * 스마트 플라이아웃 리본 & 통합 툴바 (Smart Flyout Ribbon & Integrated Quick Toolbar)
 * - 상부 메뉴 탭 클릭 시: 카테고리별 직관적인 대형 아이콘과 설명이 담긴 리본 메뉴 플라이아웃 전개
 * - 마우스가 떠나거나 외부 클릭 시: 부드럽게 닫히며 모든 기능이 통합된 버튼식 툴바 표시
 * - 📌 핀 고정 토글 지원 (상시 펼침 모드 ↔ 자동 숨김 플라이아웃 모드)
 * - 가상 매니페스트(0ms) 비파괴 조작 및 지연 베이킹 완벽 연동
 */
import { useState, useRef, useEffect } from 'react'
import { useStore } from '../lib/store'
import { usePdfDocument } from '../hooks/usePdfDocument'
import { useMenuActions, extractSelectedPagesAction } from '../hooks/useMenuActions'
import appIcon from '../assets/app-icon.png'
import type { ToolType } from '../lib/store'

interface Props {
  onOpenModal: (
    modal: 'merge' | 'split' | 'about' | 'shortcuts' | 'normalize' | 'crop' | 'print'
  ) => void
}

type RibbonTab = 'file' | 'home' | 'page' | 'annotate' | 'layout' | 'view'

const COLOR_PRESETS = [
  { color: '#ffeb3b', label: '형광 노랑' },
  { color: '#e5534b', label: '경고 빨강' },
  { color: '#0e6fd6', label: '포인트 파랑' },
  { color: '#4caf50', label: '성공 초록' },
  { color: '#1e1e1e', label: '진한 검정' }
]

const INTEGRATED_TOOLS: { id: ToolType; label: string; icon: string }[] = [
  { id: 'select', label: '선택 (V)', icon: '▦' },
  { id: 'text-edit', label: '본문 편집', icon: '✍️' },
  { id: 'highlight', label: '형광펜', icon: '█' },
  { id: 'rectangle', label: '사각형', icon: '▭' },
  { id: 'textbox', label: '텍스트', icon: 'T' },
  { id: 'drawing', label: '그리기', icon: '✎' }
]

export function MenuToolbar({ onOpenModal }: Props) {
  const doc = useStore((s) => s.document)
  const pages = useStore((s) => s.pages)
  const currentPage = useStore((s) => s.currentPage)
  const selectedPageIndices = useStore((s) => s.selectedPageIndices)
  const activeTool = useStore((s) => s.activeTool)
  const toolColor = useStore((s) => s.toolColor)
  const zoom = useStore((s) => s.zoom)
  const thumbnailsVisible = useStore((s) => s.thumbnailsVisible)
  const isDirty = useStore((s) => s.isDirty)
  const history = useStore((s) => s.history)
  const viewMode = useStore((s) => s.viewMode)
  const selectedAnnotationId = useStore((s) => s.selectedAnnotationId)

  const undo = useStore((s) => s.undo)
  const redo = useStore((s) => s.redo)
  const setViewMode = useStore((s) => s.setViewMode)
  const setActiveTool = useStore((s) => s.setActiveTool)
  const setToolColor = useStore((s) => s.setToolColor)
  const zoomIn = useStore((s) => s.zoomIn)
  const zoomOut = useStore((s) => s.zoomOut)
  const resetZoom = useStore((s) => s.resetZoom)
  const setZoom = useStore((s) => s.setZoom)
  const toggleThumbnails = useStore((s) => s.toggleThumbnails)
  const toggleTheme = useStore((s) => s.toggleTheme)
  const rotatePages = useStore((s) => s.rotatePages)
  const deletePages = useStore((s) => s.deletePages)
  const duplicatePages = useStore((s) => s.duplicatePages)
  const removeAnnotation = useStore((s) => s.removeAnnotation)
  const openSearchBar = useStore((s) => s.openSearchBar)
  const setDocument = useStore((s) => s.setDocument)

  const { openFile } = usePdfDocument()
  const { handleMenuEvent } = useMenuActions()

  // 리본 상태: 활성 탭 및 고정 여부
  const [activeTab, setActiveTab] = useState<RibbonTab | null>(null)
  const [isPinned, setIsPinned] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)
  const leaveTimerRef = useRef<NodeJS.Timeout | null>(null)

  const pageCount = pages.length > 0 ? pages.length : doc?.pageCount ?? 0
  const targetIndices =
    selectedPageIndices.length > 0 ? selectedPageIndices : [currentPage]

  // 마우스 진입 시 닫기 타이머 취소
  const handleMouseEnter = () => {
    if (leaveTimerRef.current) {
      clearTimeout(leaveTimerRef.current)
      leaveTimerRef.current = null
    }
  }

  // 마우스가 메뉴 및 리본 영역을 떠날 때 부드럽게 닫기 (고정되지 않은 경우)
  const handleMouseLeave = () => {
    if (isPinned) return
    if (activeTab) {
      leaveTimerRef.current = setTimeout(() => {
        setActiveTab(null)
      }, 220)
    }
  }

  // 상단 메뉴 탭 클릭 시 열기 / 토글
  const handleTabClick = (tab: RibbonTab) => {
    if (activeTab === tab) {
      setActiveTab(null)
    } else {
      setActiveTab(tab)
    }
  }

  // 리본이 이미 열려 있는 상태에서 다른 탭에 마우스를 올리면 즉시 전환 (Office 스타일)
  const handleTabMouseEnter = (tab: RibbonTab) => {
    handleMouseEnter()
    if (activeTab !== null && activeTab !== tab) {
      setActiveTab(tab)
    }
  }

  // 리본 내 기능 실행 후 (고정 모드가 아니면) 리본 닫기
  const runAction = (action: () => void | Promise<unknown>) => {
    void action()
    if (!isPinned) {
      setActiveTab(null)
    }
  }

  // 바깥 클릭 및 ESC 키로 리본 닫기
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && activeTab && !isPinned) {
        setActiveTab(null)
      }
    }
    const onPointerDown = (e: MouseEvent) => {
      if (
        !isPinned &&
        activeTab &&
        containerRef.current &&
        !containerRef.current.contains(e.target as Node)
      ) {
        setActiveTab(null)
      }
    }
    window.addEventListener('keydown', onKeyDown)
    window.addEventListener('pointerdown', onPointerDown)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('pointerdown', onPointerDown)
    }
  }, [activeTab, isPinned])

  // 저장 트리거
  const handleSave = () => {
    window.dispatchEvent(new CustomEvent('trigger-save'))
  }

  return (
    <header
      className={`smart-header ${isPinned ? 'smart-header--pinned' : ''}`}
      ref={containerRef}
      onMouseEnter={handleMouseEnter}
      onMouseLeave={handleMouseLeave}
    >
      {/* ── 1. 상단 메뉴 탭 행 (Menu Tabs Bar) ── */}
      <div className="menu-bar">
        <div className="menu-bar__brand" title="더존 PDF 편집기">
          <img src={appIcon} alt="더존 PDF 편집기" className="menu-bar__logo-img" />
          <span className="menu-bar__logo-text">더존 PDF 편집기</span>
        </div>

        <nav className="menu-bar__tabs" aria-label="메인 메뉴">
          <button
            className={`menu-tab-btn ${activeTab === 'file' ? 'menu-tab-btn--active' : ''}`}
            onClick={() => handleTabClick('file')}
            onMouseEnter={() => handleTabMouseEnter('file')}
          >
            📁 파일
          </button>
          <button
            className={`menu-tab-btn ${activeTab === 'home' ? 'menu-tab-btn--active' : ''}`}
            onClick={() => handleTabClick('home')}
            onMouseEnter={() => handleTabMouseEnter('home')}
          >
            🏠 홈
          </button>
          <button
            className={`menu-tab-btn ${activeTab === 'page' ? 'menu-tab-btn--active' : ''}`}
            onClick={() => handleTabClick('page')}
            onMouseEnter={() => handleTabMouseEnter('page')}
          >
            📄 페이지
          </button>
          <button
            className={`menu-tab-btn ${activeTab === 'annotate' ? 'menu-tab-btn--active' : ''}`}
            onClick={() => handleTabClick('annotate')}
            onMouseEnter={() => handleTabMouseEnter('annotate')}
          >
            🖍️ 주석
          </button>
          <button
            className={`menu-tab-btn ${activeTab === 'layout' ? 'menu-tab-btn--active' : ''}`}
            onClick={() => handleTabClick('layout')}
            onMouseEnter={() => handleTabMouseEnter('layout')}
          >
            📐 조판/도구
          </button>
          <button
            className={`menu-tab-btn ${activeTab === 'view' ? 'menu-tab-btn--active' : ''}`}
            onClick={() => handleTabClick('view')}
            onMouseEnter={() => handleTabMouseEnter('view')}
          >
            👁️ 보기
          </button>
        </nav>

        <div className="menu-bar__spacer" />

        {/* 상단 우측 빠른 실행 및 문서 상태 */}
        <div className="menu-bar__right">
          <button
            className="menu-bar__quick-btn"
            title="본문 검색 (Ctrl+F)"
            onClick={() => openSearchBar()}
            disabled={!doc}
          >
            🔍 찾기
          </button>
          <button
            className="menu-bar__quick-btn"
            title="단축키 안내 (F1)"
            onClick={() => onOpenModal('shortcuts')}
          >
            ⌨ 단축키
          </button>
          <button
            className="menu-bar__quick-btn"
            title="테마 전환 (다크/라이트)"
            onClick={toggleTheme}
          >
            🌓 테마
          </button>
          {doc && (
            <span
              className="menu-bar__doc-title"
              title={doc.filePath ?? '저장되지 않은 새 문서'}
            >
              {isDirty ? '● ' : ''}
              {doc.fileName}
            </span>
          )}
        </div>
      </div>

      {/* ── 2. 펼쳐진 리본 메뉴 플라이아웃 (Flyout Ribbon Drawer) ── */}
      {activeTab !== null && (
        <div className="ribbon-flyout" role="region" aria-label="리본 메뉴 도구 상자">
          <div className="ribbon-flyout__inner">
            {/* 📁 파일 탭 리본 */}
            {activeTab === 'file' && (
              <>
                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className="ribbon-card"
                      title="PDF 파일 열기 (Ctrl+O)"
                      onClick={() => runAction(() => openFile())}
                    >
                      <span className="ribbon-card__icon">📂</span>
                      <span className="ribbon-card__label">열기</span>
                      <span className="ribbon-card__sub">Ctrl+O</span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="문서 저장 (Ctrl+S)"
                      onClick={() => runAction(handleSave)}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">💾</span>
                      <span className="ribbon-card__label">저장</span>
                      <span className="ribbon-card__sub">Ctrl+S</span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="다른 이름으로 저장 (Ctrl+Shift+S)"
                      onClick={() => runAction(() => handleMenuEvent('file:saveAs'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">📤</span>
                      <span className="ribbon-card__label">다른 이름</span>
                      <span className="ribbon-card__sub">새 복사본</span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="인쇄 및 인쇄 내보내기 (Ctrl+P)"
                      onClick={() => runAction(() => onOpenModal('print'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">🖨️</span>
                      <span className="ribbon-card__label">인쇄</span>
                      <span className="ribbon-card__sub">Ctrl+P</span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="문서 닫기"
                      onClick={() => runAction(() => setDocument(null))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">✕</span>
                      <span className="ribbon-card__label">닫기</span>
                      <span className="ribbon-card__sub">작업 종료</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">열기 및 저장</div>
                </div>

                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className="ribbon-card"
                      title="선택한 페이지만 새 PDF로 내보내기"
                      onClick={() =>
                        runAction(() => extractSelectedPagesAction(targetIndices))
                      }
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">📑</span>
                      <span className="ribbon-card__label">선택 추출</span>
                      <span className="ribbon-card__sub">
                        {targetIndices.length}개 페이지
                      </span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="여러 PDF 파일을 하나로 결합"
                      onClick={() => runAction(() => onOpenModal('merge'))}
                    >
                      <span className="ribbon-card__icon">⊕</span>
                      <span className="ribbon-card__label">PDF 합치기</span>
                      <span className="ribbon-card__sub">문서 병합</span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="지정한 범위로 PDF 나누기"
                      onClick={() => runAction(() => onOpenModal('split'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">⊘</span>
                      <span className="ribbon-card__label">PDF 분할</span>
                      <span className="ribbon-card__sub">파일 나누기</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">내보내기 및 분할/병합</div>
                </div>

                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className="ribbon-card"
                      title="앱 정보"
                      onClick={() => runAction(() => onOpenModal('about'))}
                    >
                      <span className="ribbon-card__icon">ℹ️</span>
                      <span className="ribbon-card__label">프로그램 정보</span>
                      <span className="ribbon-card__sub">v1.0 Pro</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">도움말</div>
                </div>
              </>
            )}

            {/* 🏠 홈 탭 리본 */}
            {activeTab === 'home' && (
              <>
                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className="ribbon-card"
                      title="실행 취소 (Ctrl+Z)"
                      onClick={() => runAction(undo)}
                      disabled={!doc || history.past.length === 0}
                    >
                      <span className="ribbon-card__icon">↶</span>
                      <span className="ribbon-card__label">실행 취소</span>
                      <span className="ribbon-card__sub">Ctrl+Z</span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="다시 실행 (Ctrl+Shift+Z)"
                      onClick={() => runAction(redo)}
                      disabled={!doc || history.future.length === 0}
                    >
                      <span className="ribbon-card__icon">↷</span>
                      <span className="ribbon-card__label">다시 실행</span>
                      <span className="ribbon-card__sub">Ctrl+Y</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">작업 내역</div>
                </div>

                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className="ribbon-card"
                      title="본문 텍스트 검색 (Ctrl+F)"
                      onClick={() => runAction(() => openSearchBar())}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">🔍</span>
                      <span className="ribbon-card__label">본문 찾기</span>
                      <span className="ribbon-card__sub">Ctrl+F</span>
                    </button>
                    <button
                      className={`ribbon-card ${viewMode === 'continuous' ? 'ribbon-card--active' : ''}`}
                      title="연속 세로 스크롤 / 단일 페이지 전환"
                      onClick={() =>
                        runAction(() =>
                          setViewMode(
                            viewMode === 'single' ? 'continuous' : 'single'
                          )
                        )
                      }
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">
                        {viewMode === 'single' ? '📄' : '📜'}
                      </span>
                      <span className="ribbon-card__label">
                        {viewMode === 'single' ? '단일 페이지' : '연속 스크롤'}
                      </span>
                      <span className="ribbon-card__sub">보기 모드</span>
                    </button>
                    <button
                      className={`ribbon-card ${thumbnailsVisible ? 'ribbon-card--active' : ''}`}
                      title="좌측 사이드 패널 토글 (F4)"
                      onClick={() => runAction(toggleThumbnails)}
                    >
                      <span className="ribbon-card__icon">▥</span>
                      <span className="ribbon-card__label">사이드 패널</span>
                      <span className="ribbon-card__sub">썸네일/목차</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">탐색 및 화면</div>
                </div>

                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className={`ribbon-card ${activeTool === 'select' ? 'ribbon-card--active' : ''}`}
                      title="객체 선택 및 조작"
                      onClick={() => runAction(() => setActiveTool('select'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">▦</span>
                      <span className="ribbon-card__label">선택</span>
                      <span className="ribbon-card__sub">객체 이동</span>
                    </button>
                    <button
                      className={`ribbon-card ${activeTool === 'text-edit' ? 'ribbon-card--active' : ''}`}
                      title="본문 텍스트 직접 편집 (클릭하여 오타/내용 수정)"
                      onClick={() => runAction(() => setActiveTool('text-edit'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">✍️</span>
                      <span className="ribbon-card__label">본문 편집</span>
                      <span className="ribbon-card__sub">오타 직접 수정</span>
                    </button>
                    <button
                      className={`ribbon-card ${activeTool === 'highlight' ? 'ribbon-card--active' : ''}`}
                      title="텍스트 형광펜 하이라이트"
                      onClick={() => runAction(() => setActiveTool('highlight'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">█</span>
                      <span className="ribbon-card__label">형광펜</span>
                      <span className="ribbon-card__sub">텍스트 강조</span>
                    </button>
                    <button
                      className={`ribbon-card ${activeTool === 'textbox' ? 'ribbon-card--active' : ''}`}
                      title="텍스트 상자 추가"
                      onClick={() => runAction(() => setActiveTool('textbox'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">T</span>
                      <span className="ribbon-card__label">텍스트</span>
                      <span className="ribbon-card__sub">글상자 입력</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">빠른 마킹 도구</div>
                </div>
              </>
            )}

            {/* 📄 페이지 탭 리본 */}
            {activeTab === 'page' && (
              <>
                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className="ribbon-card"
                      title="선택 페이지를 반시계 방향으로 90° 회전"
                      onClick={() =>
                        runAction(() => rotatePages(targetIndices, 'left'))
                      }
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">↺</span>
                      <span className="ribbon-card__label">왼쪽 90°</span>
                      <span className="ribbon-card__sub">반시계 회전</span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="선택 페이지를 시계 방향으로 90° 회전"
                      onClick={() =>
                        runAction(() => rotatePages(targetIndices, 'right'))
                      }
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">↻</span>
                      <span className="ribbon-card__label">오른쪽 90°</span>
                      <span className="ribbon-card__sub">시계 회전</span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="선택 페이지 180° 상하 반전"
                      onClick={() =>
                        runAction(() => {
                          rotatePages(targetIndices, 'right')
                          rotatePages(targetIndices, 'right')
                        })
                      }
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">🔄</span>
                      <span className="ribbon-card__label">180° 반전</span>
                      <span className="ribbon-card__sub">상하 뒤집기</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">페이지 회전 (비파괴 0ms)</div>
                </div>

                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className="ribbon-card"
                      title="선택된 페이지 즉시 복제"
                      onClick={() =>
                        runAction(() => duplicatePages(targetIndices))
                      }
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">📋</span>
                      <span className="ribbon-card__label">페이지 복제</span>
                      <span className="ribbon-card__sub">사본 생성</span>
                    </button>
                    <button
                      className="ribbon-card ribbon-card--danger"
                      title="선택된 페이지 삭제 (Del)"
                      onClick={() => runAction(() => deletePages(targetIndices))}
                      disabled={!doc || pages.length <= targetIndices.length}
                    >
                      <span className="ribbon-card__icon">🗑️</span>
                      <span className="ribbon-card__label">페이지 삭제</span>
                      <span className="ribbon-card__sub">
                        {targetIndices.length}개 선택
                      </span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="선택 페이지만 새 파일로 추출"
                      onClick={() =>
                        runAction(() => extractSelectedPagesAction(targetIndices))
                      }
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">📑</span>
                      <span className="ribbon-card__label">선택 추출</span>
                      <span className="ribbon-card__sub">새 PDF로 저장</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">페이지 편집 및 정리</div>
                </div>

                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className="ribbon-card"
                      title="A4 / Letter / A3 등 표준 판형으로 규격 일치화"
                      onClick={() => runAction(() => onOpenModal('normalize'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">⛶</span>
                      <span className="ribbon-card__label">조판 통일</span>
                      <span className="ribbon-card__sub">A4 규격화</span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="상하좌우 mm 단위 여백 절단"
                      onClick={() => runAction(() => onOpenModal('crop'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">✂</span>
                      <span className="ribbon-card__label">여백 자르기</span>
                      <span className="ribbon-card__sub">스캔 테두리 제거</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">규격 및 여백 조판</div>
                </div>
              </>
            )}

            {/* 🖍️ 주석 탭 리본 */}
            {activeTab === 'annotate' && (
              <>
                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className={`ribbon-card ${activeTool === 'select' ? 'ribbon-card--active' : ''}`}
                      title="주석 선택, 8방향 리사이즈, 드래그 이동"
                      onClick={() => runAction(() => setActiveTool('select'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">▦</span>
                      <span className="ribbon-card__label">객체 선택</span>
                      <span className="ribbon-card__sub">이동/크기조절</span>
                    </button>
                    <button
                      className={`ribbon-card ${activeTool === 'text-edit' ? 'ribbon-card--active' : ''}`}
                      title="본문 텍스트 직접 편집 (클릭하여 오타/내용 수정)"
                      onClick={() => runAction(() => setActiveTool('text-edit'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">✍️</span>
                      <span className="ribbon-card__label">본문 편집</span>
                      <span className="ribbon-card__sub">직접 수정</span>
                    </button>
                    <button
                      className="ribbon-card ribbon-card--danger"
                      title="선택된 주석 삭제 (Delete)"
                      onClick={() =>
                        runAction(() => {
                          if (selectedAnnotationId) {
                            removeAnnotation(selectedAnnotationId)
                          }
                        })
                      }
                      disabled={!doc || !selectedAnnotationId}
                    >
                      <span className="ribbon-card__icon">🗑️</span>
                      <span className="ribbon-card__label">주석 삭제</span>
                      <span className="ribbon-card__sub">Delete</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">주석 제어</div>
                </div>

                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className={`ribbon-card ${activeTool === 'highlight' ? 'ribbon-card--active' : ''}`}
                      title="형광펜 하이라이트"
                      onClick={() => runAction(() => setActiveTool('highlight'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">█</span>
                      <span className="ribbon-card__label">형광펜</span>
                      <span className="ribbon-card__sub">텍스트 강조</span>
                    </button>
                    <button
                      className={`ribbon-card ${activeTool === 'rectangle' ? 'ribbon-card--active' : ''}`}
                      title="사각형 박스 그리기"
                      onClick={() => runAction(() => setActiveTool('rectangle'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">▭</span>
                      <span className="ribbon-card__label">사각형</span>
                      <span className="ribbon-card__sub">박스 표시</span>
                    </button>
                    <button
                      className={`ribbon-card ${activeTool === 'textbox' ? 'ribbon-card--active' : ''}`}
                      title="텍스트 글상자 추가"
                      onClick={() => runAction(() => setActiveTool('textbox'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">🔤</span>
                      <span className="ribbon-card__label">텍스트</span>
                      <span className="ribbon-card__sub">글자 입력</span>
                    </button>
                    <button
                      className={`ribbon-card ${activeTool === 'drawing' ? 'ribbon-card--active' : ''}`}
                      title="자유형 펜 드로잉"
                      onClick={() => runAction(() => setActiveTool('drawing'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">✎</span>
                      <span className="ribbon-card__label">자유형 펜</span>
                      <span className="ribbon-card__sub">손글씨 그리기</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">마킹 및 도형 도구</div>
                </div>

                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <div className="ribbon-palette">
                      {COLOR_PRESETS.map((p) => (
                        <button
                          key={p.color}
                          className={`ribbon-palette__chip ${toolColor === p.color ? 'ribbon-palette__chip--active' : ''}`}
                          style={{ backgroundColor: p.color }}
                          title={p.label}
                          onClick={() => setToolColor(p.color)}
                        />
                      ))}
                      <label className="ribbon-palette__custom" title="사용자 지정 색상">
                        <input
                          type="color"
                          value={toolColor}
                          onChange={(e) => setToolColor(e.target.value)}
                        />
                        <span className="ribbon-palette__custom-icon">🎨</span>
                      </label>
                    </div>
                  </div>
                  <div className="ribbon-group__title">색상 팔레트</div>
                </div>
              </>
            )}

            {/* 📐 조판/도구 탭 리본 */}
            {activeTab === 'layout' && (
              <>
                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className="ribbon-card"
                      title="여러 PDF 문서를 순서대로 결합"
                      onClick={() => runAction(() => onOpenModal('merge'))}
                    >
                      <span className="ribbon-card__icon">⊕</span>
                      <span className="ribbon-card__label">PDF 합치기</span>
                      <span className="ribbon-card__sub">다중 파일 결합</span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="페이지 범위별로 파일 분할"
                      onClick={() => runAction(() => onOpenModal('split'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">⊘</span>
                      <span className="ribbon-card__label">PDF 나누기</span>
                      <span className="ribbon-card__sub">범위 분리</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">결합 및 분할</div>
                </div>

                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className="ribbon-card"
                      title="A4 / Letter / A3 등 표준 판형으로 규격 일치화"
                      onClick={() => runAction(() => onOpenModal('normalize'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">⛶</span>
                      <span className="ribbon-card__label">조판 규격화</span>
                      <span className="ribbon-card__sub">A4 통일 & 여백 맞춤</span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="스캔 테두리 및 불필요한 여백 절단"
                      onClick={() => runAction(() => onOpenModal('crop'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">✂</span>
                      <span className="ribbon-card__label">여백 자르기</span>
                      <span className="ribbon-card__sub">상하좌우 크롭</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">출력 및 인쇄 조판</div>
                </div>
              </>
            )}

            {/* 👁️ 보기 탭 리본 */}
            {activeTab === 'view' && (
              <>
                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className="ribbon-card"
                      title="축소 (Ctrl+-)"
                      onClick={() => runAction(zoomOut)}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">🔍－</span>
                      <span className="ribbon-card__label">축소</span>
                      <span className="ribbon-card__sub">Ctrl+-</span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="실제 100% 원본 크기 (Ctrl+0)"
                      onClick={() => runAction(resetZoom)}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">100%</span>
                      <span className="ribbon-card__label">실제 크기</span>
                      <span className="ribbon-card__sub">{Math.round(zoom * 100)}%</span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="확대 (Ctrl+=)"
                      onClick={() => runAction(zoomIn)}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">🔍＋</span>
                      <span className="ribbon-card__label">확대</span>
                      <span className="ribbon-card__sub">Ctrl+=</span>
                    </button>
                    <button
                      className="ribbon-card"
                      title="너비에 맞추기"
                      onClick={() => runAction(() => setZoom(1.2))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">↔</span>
                      <span className="ribbon-card__label">폭 맞춤</span>
                      <span className="ribbon-card__sub">가로 맞춤</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">배율 조정</div>
                </div>

                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className={`ribbon-card ${viewMode === 'single' ? 'ribbon-card--active' : ''}`}
                      title="단일 페이지 한 장씩 보기"
                      onClick={() => runAction(() => setViewMode('single'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">📄</span>
                      <span className="ribbon-card__label">단일 페이지</span>
                      <span className="ribbon-card__sub">1쪽씩 넘김</span>
                    </button>
                    <button
                      className={`ribbon-card ${viewMode === 'continuous' ? 'ribbon-card--active' : ''}`}
                      title="세로 연속 스크롤로 보기"
                      onClick={() => runAction(() => setViewMode('continuous'))}
                      disabled={!doc}
                    >
                      <span className="ribbon-card__icon">📜</span>
                      <span className="ribbon-card__label">연속 스크롤</span>
                      <span className="ribbon-card__sub">가상화 부드러운 스크롤</span>
                    </button>
                    <button
                      className={`ribbon-card ${thumbnailsVisible ? 'ribbon-card--active' : ''}`}
                      title="좌측 썸네일 & 북마크 사이드바 (F4)"
                      onClick={() => runAction(toggleThumbnails)}
                    >
                      <span className="ribbon-card__icon">▥</span>
                      <span className="ribbon-card__label">사이드 패널</span>
                      <span className="ribbon-card__sub">F4 단축키</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">문서 표시 모드</div>
                </div>

                <div className="ribbon-group">
                  <div className="ribbon-group__content">
                    <button
                      className="ribbon-card"
                      title="다크 / 라이트 테마 전환"
                      onClick={() => runAction(toggleTheme)}
                    >
                      <span className="ribbon-card__icon">🌓</span>
                      <span className="ribbon-card__label">테마 전환</span>
                      <span className="ribbon-card__sub">다크/라이트</span>
                    </button>
                  </div>
                  <div className="ribbon-group__title">인터페이스 테마</div>
                </div>
              </>
            )}

            {/* 리본 우측 제어: 📌 핀 고정 토글 & ✕ 닫기 버튼 */}
            <div className="ribbon-flyout__controls">
              <button
                className={`ribbon-pin-btn ${isPinned ? 'ribbon-pin-btn--pinned' : ''}`}
                title={
                  isPinned
                    ? '리본 자동 숨김 모드로 전환 (마우스가 떠나면 닫힘)'
                    : '리본 상시 고정 모드로 전환 (항상 열림)'
                }
                onClick={() => setIsPinned(!isPinned)}
              >
                📌 {isPinned ? '고정됨' : '고정'}
              </button>
              <button
                className="ribbon-close-btn"
                title="리본 닫기 (Esc)"
                onClick={() => setActiveTab(null)}
              >
                ✕
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ── 3. 모든 기능이 통합된 버튼식 툴바 (Integrated Quick Toolbar) ── */}
      <div className="toolbar toolbar--integrated">
        {/* 열기 / 저장 */}
        <div className="toolbar__group">
          <button
            className="toolbar__btn"
            title="PDF 열기 (Ctrl+O)"
            onClick={() => void openFile()}
          >
            📂 열기
          </button>
          <button
            className="toolbar__btn"
            title="문서 저장 (Ctrl+S)"
            onClick={handleSave}
            disabled={!doc}
          >
            💾 저장
          </button>
          <button
            className="toolbar__btn"
            title="인쇄 및 인쇄 내보내기 (Ctrl+P)"
            onClick={() => onOpenModal('print')}
            disabled={!doc}
          >
            🖨️ 인쇄
          </button>
        </div>

        <div className="toolbar__divider" />

        {/* 실행 취소 / 다시 실행 */}
        <div className="toolbar__group">
          <button
            className="toolbar__btn toolbar__btn--icon"
            title="실행 취소 (Ctrl+Z)"
            onClick={undo}
            disabled={!doc || history.past.length === 0}
          >
            ↶
          </button>
          <button
            className="toolbar__btn toolbar__btn--icon"
            title="다시 실행 (Ctrl+Shift+Z)"
            onClick={redo}
            disabled={!doc || history.future.length === 0}
          >
            ↷
          </button>
        </div>

        <div className="toolbar__divider" />

        {/* 페이지 이동 및 번호 표시 */}
        <div className="toolbar__group">
          <NavButton dir="prev" />
          <span className="toolbar__page-info">
            {doc ? `${currentPage + 1} / ${pageCount}` : '-'}
          </span>
          <NavButton dir="next" />
        </div>

        <div className="toolbar__divider" />

        {/* 줌 확대/축소 */}
        <div className="toolbar__group toolbar__zoom">
          <button
            className="toolbar__btn toolbar__btn--icon"
            title="축소 (Ctrl+-)"
            onClick={zoomOut}
            disabled={!doc}
          >
            －
          </button>
          <button
            className="toolbar__btn toolbar__zoom-value"
            title="실제 크기 (Ctrl+0)"
            onClick={resetZoom}
            disabled={!doc}
          >
            {Math.round(zoom * 100)}%
          </button>
          <button
            className="toolbar__btn toolbar__btn--icon"
            title="확대 (Ctrl+=)"
            onClick={zoomIn}
            disabled={!doc}
          >
            ＋
          </button>
        </div>

        <div className="toolbar__divider" />

        {/* 핵심 주석 도구 */}
        <div className="toolbar__group">
          {INTEGRATED_TOOLS.map((t) => (
            <button
              key={t.id}
              className={`toolbar__btn ${activeTool === t.id ? 'toolbar__btn--active' : ''}`}
              title={t.label}
              onClick={() => setActiveTool(t.id)}
              disabled={!doc}
            >
              <span aria-hidden>{t.icon}</span> {t.label.split(' ')[0]}
            </button>
          ))}
          <input
            className="toolbar__color-input"
            type="color"
            value={toolColor}
            onChange={(e) => setToolColor(e.target.value)}
            title="마킹/주석 색상"
          />
        </div>

        <div className="toolbar__divider" />

        {/* 보기 및 빠른 조판 작업 */}
        <div className="toolbar__group">
          <button
            className={`toolbar__btn ${viewMode === 'continuous' ? 'toolbar__btn--active' : ''}`}
            title={
              viewMode === 'single'
                ? '연속 세로 스크롤로 전환'
                : '단일 페이지 보기로 전환'
            }
            onClick={() =>
              setViewMode(viewMode === 'single' ? 'continuous' : 'single')
            }
            disabled={!doc}
          >
            {viewMode === 'single' ? '📄 단일' : '📜 연속'}
          </button>
          <button
            className={`toolbar__btn ${thumbnailsVisible ? 'toolbar__btn--active' : ''}`}
            title="사이드바 패널 (F4)"
            onClick={toggleThumbnails}
          >
            ▥ 패널
          </button>
          <button
            className="toolbar__btn"
            title="PDF 합치기"
            onClick={() => onOpenModal('merge')}
          >
            ⊕ 합치기
          </button>
          <button
            className="toolbar__btn"
            title="PDF 나누기"
            onClick={() => onOpenModal('split')}
            disabled={!doc}
          >
            ⊘ 나누기
          </button>
          <button
            className="toolbar__btn"
            title="조판 크기 일치화 (A4/Letter 통일)"
            onClick={() => onOpenModal('normalize')}
            disabled={!doc}
          >
            ⛶ 조판
          </button>
          <button
            className="toolbar__btn"
            title="여백 자르기 (Crop & Trim)"
            onClick={() => onOpenModal('crop')}
            disabled={!doc}
          >
            ✂ 자르기
          </button>
        </div>

        <div className="toolbar__spacer" />

        {/* 파일명 */}
        {doc && (
          <span
            className="toolbar__title"
            title={doc.filePath ?? '저장되지 않음'}
          >
            {isDirty ? '● ' : ''}
            {doc.fileName}
          </span>
        )}
      </div>
    </header>
  )
}

/** 페이지 이동 버튼 */
function NavButton({ dir }: { dir: 'prev' | 'next' }) {
  const doc = useStore((s) => s.document)
  const pages = useStore((s) => s.pages)
  const currentPage = useStore((s) => s.currentPage)
  const setCurrentPage = useStore((s) => s.setCurrentPage)

  const pageCount = pages.length > 0 ? pages.length : doc?.pageCount ?? 0
  const disabled =
    !doc || (dir === 'prev' ? currentPage === 0 : currentPage >= pageCount - 1)

  return (
    <button
      className="toolbar__btn toolbar__btn--icon"
      title={dir === 'prev' ? '이전 페이지 (←)' : '다음 페이지 (→)'}
      disabled={disabled}
      onClick={() =>
        setCurrentPage(dir === 'prev' ? currentPage - 1 : currentPage + 1)
      }
    >
      {dir === 'prev' ? '◀' : '▶'}
    </button>
  )
}
