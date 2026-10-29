# 📘 더존 PDF 편집기 (TH PDF) 종합 개발지침서

현재 코드베이스(`e:\PDF Workspace`) 전체를 정밀 전수 조사하여 **구현 완료된 기능**, **겉모습만 있거나 반쪽짜리인 미완성 메뉴**, **숨어 있는 치명적 버그**, 그리고 **썸네일 크기·다중선택·선택 색상 커스터마이징 및 향후 필수 기능**을 단계별 구현 지침으로 정리한 문서입니다.

---

## 1. 최우선 과제: 썸네일 패널 UX 전면 개편

현재 `src/components/ThumbnailPanel.tsx`, `src/lib/store.ts`, `src/styles/app.css`의 썸네일 영역은 다음과 같은 한계가 있습니다.

### 1-1. 썸네일 크기 지정 (소 / 중 / 대 + 슬라이더 + 사이드바 리사이즈)
* **현재 상태**: `ThumbnailPanel.tsx:18`에 `THUMB_WIDTH = 156`으로 하드코딩되어 있으며, `app.css:21`에 `--thumbnail-width: 180px`로 고정되어 있습니다. `store.ts`에 썸네일 크기 상태조차 없습니다.
* **구현 지침**:
  1. **Store 상태 추가 (`src/lib/store.ts`)**:
     * `thumbnailSize: number` (기본값 `156`, 범위 `96px ~ 280px`, 프리셋 `소: 110` / `중: 156` / `대: 220`)
     * `sidebarWidth: number` (기본값 `220px`, 범위 `180px ~ 600px`)
     * `setThumbnailSize(size)`, `setSidebarWidth(width)` 액션 추가 및 `localStorage` 영속화
  2. **UI 컨트롤 추가 (`src/components/ThumbnailPanel.tsx`)**:
     * 사이드바 상단(탭 아래) 미니 툴바에 **크기 프리셋 버튼 (`소` / `중` / `대`)** 및 **크기 조절 슬라이더 (`input[type=range]`)** 배치
     * 썸네일 영역에서 `Ctrl + 마우스 휠`로 썸네일 크기를 즉시 확대/축소
     * 사이드바 우측 경계선에 드래그 리사이즈 핸들(`.sidebar-resizer`) 추가, 폭을 넓히면 한 줄에 여러 장(`grid-template-columns: repeat(auto-fill, minmax(var(--thumb-w), 1fr))`)이 자동 배치되도록 그리드 레이아웃 적용
  3. **렌더링 연동**:
     * `ThumbnailItem`이 `thumbnailSize * window.devicePixelRatio` 스케일로 렌더링하고, 각 가상 페이지의 실제 가로·세로 비율(`vPage.width / vPage.height`)을 스켈레톤 플레이스홀더에 반영해 레이아웃 점프 방지

### 1-2. 다중선택(Multi-Select) 방식 표준화 및 고도화
* **현재 문제점**:
  * **Shift 범위 선택 버그 (`store.ts:277-285`)**: 별도의 기준점(`selectionAnchorIndex`) 없이 단순히 `selectedPageIndices`의 마지막 원소를 기준으로 삼고, 기존 선택에 합집합(`Set`)으로 합쳐버려 표준 탐색기처럼 범위가 재설정되지 않습니다.
  * **사장된 액션**: `selectAllPages`(`store.ts:293`), `clearPageSelection`(`store.ts:298`)이 스토어에 정의만 되어 있고 UI나 단축키에 연결되어 있지 않습니다.
  * **다중 드래그 이동 불가**: 여러 페이지를 선택한 뒤 드래그하면 잡은 1페이지만 이동하고 선택이 풀립니다(`store.ts:407`).
* **구현 지침**:
  1. **앵커 기반 선택 모델 (`src/lib/store.ts`)**:
     * `selectionAnchorIndex: number` 상태 추가
     * **단일 클릭**: `selectedPageIndices = [index]`, `selectionAnchorIndex = index`, `currentPage = index + 1`
     * **Ctrl + 클릭**: `index` 토글, `selectionAnchorIndex = index`
     * **Shift + 클릭**: `selectionAnchorIndex`부터 `index`까지의 연속 범위로 **교체** (`Ctrl+Shift+클릭`일 때만 기존 선택에 범위 추가)
  2. **추가 다중선택 수단 (`src/components/ThumbnailPanel.tsx`)**:
     * **좌측 상단 체크박스 오버레이**: 각 썸네일 카드에 마우스 호버 또는 선택 시 체크박스(`☑`)를 표시해 마우스 클릭만으로도 다중 토글 선택 가능
     * **드래그 박스(Marquee / Rubber-band) 선택**: 사이드바 빈 여백에서 마우스를 드래그하면 반투명 선택 사각형이 그려지며 겹치는 썸네일 일괄 선택
     * **빠른 선택 바**: `전체` / `짝수` / `홀수` / `선택 해제` 버튼 및 범위 입력(`1-5, 8`) 지원
     * **키보드 지원**: `Ctrl+A`(전체 선택), `Esc`(선택 초기화), `↑`/`↓`/`←`/`→`(이동 및 `Shift` 연속 선택), `Delete`(선택 페이지 삭제)
  3. **다중 페이지 일괄 드래그 정렬**:
     * 선택된 페이지 중 하나를 드래그하면 선택된 모든 페이지 묶음(`selectedPageIndices`)이 함께 목표 위치로 이동하며 드래그 중 `"N개 페이지 이동"` 배지 표시

### 1-3. 선택 썸네일(페이지) 색상 및 시각적 계층 구분
* **현재 문제점 (`src/styles/app.css:636-654`)**:
  * 현재 보고 있는 페이지(`.thumbnail--active`)와 선택된 페이지(`.thumbnail--selected`)가 동일한 파란색 테두리(`var(--color-accent)`)를 사용하고 배경색은 흰색 캔버스(`#fff`)에 가려져 두 상태가 거의 구분되지 않습니다.
* **구현 지침**:
  1. **3단계 시각적 상태 분리**:
     * **① 현재 뷰어 페이지 (`active`)**: 외곽 포커스 링 + 하단 페이지 번호 캡슐 강조
     * **② 다중 선택된 페이지 (`selected`)**: 썸네일 카드 프레임(`3px solid var(--thumb-selected-color)`) + 상단 좌측 체크 배지(`✓`) + 캔버스 위 반투명 컬러 틴트 오버레이(`::after` 가상 요소)
     * **③ 현재 페이지 + 선택됨 (`active + selected`)**: 선택 틴트 + 하단 번호 캡슐 강조 + 이중 글로우 링
  2. **선택 강조 색상 사용자 지정**:
     * `store.ts`에 `selectionColor: string` 상태 추가 (프리셋: 더존 블루 `#0e6fd6`, 에메랄드 `#10b981`, 앰버 골드 `#f59e0b`, 코랄 레드 `#f43f5e`, 바이올렛 `#8b5cf6` + 커스텀 컬러 피커)

---

## 2. 리본 메뉴 & 툴바 미구현·부분구현 전수 감사 및 완성 지침

| 구분 | 메뉴 항목 | 현재 상태 | 문제점 상세 (근거 위치) | 완성 지침 |
| :--- | :--- | :---: | :--- | :--- |
| **보기** | **폭 맞춤** | ⚠️ 가짜 구현 | `MenuToolbar.tsx:825`, `useMenuActions.ts:83`에서 컨테이너 폭을 계산하지 않고 `setZoom(1.2)`로 고정 | `.viewer` 컨테이너 실측 너비 ÷ 현재 페이지 너비(`vPage.width`)로 정확한 배율 계산 |
| **보기** | **페이지 맞춤** | 🔌 누락됨 | 기존 네이티브 메뉴(`menu.ts:99`)에만 있었고 리본 탭에는 버튼조차 없음 | 리본 `보기` 탭 및 퀵 툴바에 `페이지 맞춤` 추가, 컨테이너 가로·세로 중 작은 비율로 자동 계산 |
| **파일** | **인쇄** | ❌ 반쪽 구현 | `ipc-handlers.ts:92-102`에서 임시 파일 저장 후 `shell.openPath`만 호출. 부분 범위 인쇄 시 주석 페이지 번호가 밀리는 버그(`PrintModal.tsx:75`) | 숨김 `BrowserWindow`에서 `webContents.print()` 호출 + 부분 범위 인쇄 시 주석 인덱스 리매핑 |
| **파일/도구** | **PDF 분할** | 🐛 치명 버그 | `SplitModal.tsx:56`에서 편집된 가상 매니페스트(`pages`)를 무시하고 최초 원본 바이트(`doc.bytes`)를 분할 | `bakeManifestToPdf`를 사용하여 현재 편집 상태가 반영된 가상 페이지 기준으로 분할 |
| **페이지** | **조판 통일 / 여백 자르기** | 🐛 화면 미반영 | `PageCanvas.tsx`가 `vPage.cropMargins`와 `vPage.normalizedConfig`를 렌더링에 반영하지 않아 뷰어에서 변화가 안 보임 | `PageCanvas` 및 `ThumbnailItem`에서 `cropMargins`와 `normalizedConfig`를 실시간 클리핑/스케일링 |
| **주석** | **주석 저장(Baking)** | 🐛 치명 버그 | `AnnotationLayer.tsx:211`은 좌상단 원점인데 `pdf-engine.service.ts:197-239`는 좌하단 원점 변환 없이 그대로 그려 저장 시 위아래가 뒤집힘 | `pdf-engine.service.ts`에서 `y = pageHeight - ann.y - ann.height`로 좌표계 변환 통일 |
| **주석/페이지** | **주석-페이지 연결** | 🐛 치명 버그 | 주석이 `vPage.id`가 아니라 배열 인덱스(`pageIndex`)에 묶여 있어 페이지 이동·삭제 시 엉뚱한 페이지로 밀림 | `Annotation.pageId`(`vPage.id`) 기반으로 귀속 변경 + Undo/Redo 스택 통합 |
| **주석** | **본문 편집** | 🐛 캐시 오염 | `text-editor.service.ts:27`의 `clearTextEditorCache` 미호출 및 `StatusBar.tsx:18`에 `'text-edit'` 라벨 누락(`"도구: undefined"`) | 문서 교체 시 캐시 초기화 + `StatusBar`에 `'text-edit': '본문 편집'` 추가 |
| **페이지** | **180° 반전** | ⚠️ 히스토리 중복 | `MenuToolbar.tsx:528`에서 `rotatePages`를 2번 연속 호출해 Undo를 두 번 눌러야 복구됨 | `rotatePages(indices, '180')` 단일 액션으로 처리 |
| **파일** | **드래그 & 드롭** | 🐛 문서 덮어쓰기 | 썸네일에 파일을 드롭하면 `App.tsx:146`의 전역 `window` 드롭 핸들러까지 버블링되어 문서 전체가 교체됨 | `ThumbnailPanel`의 `onDrop`에 `e.stopPropagation()` 추가 + 다중 파일 삽입 오프셋 수정 |
| **패키징** | **한글 폰트 경로** | 🐛 저장 실패 위험 | `ipc-handlers.ts:12`에서 패키징 시 `process.resourcesPath`를 참조하지 않아 한글 저장 시 에러 가능성 | `app.isPackaged ? join(process.resourcesPath, 'assets/fonts') : ...`로 경로 분기 |
| **단축키** | **단축키 모달** | ❌ 불일치 | `ShortcutsModal.tsx`에 적힌 단축키 다수가 미작동하며 입력창 포커스 가드가 없음 | 입력창 포커스 시 글로벌 단축키 예외 처리 + 모든 단축키 실제 바인딩 및 모달 동기화 |

---

## 3. 단계별 개발 로드맵

* **Phase 1 (썸네일 UX & 코어 버그 완결)**: 썸네일 크기 지정(소/중/대/슬라이더/Ctrl+휠/사이드바 리사이즈), 다중선택 고도화(체크박스/드래그박스/앵커 Shift/다중 드래그 이동), 선택 색상 지정, 치명적 버그 6종 수정
* **Phase 2 (리본 메뉴 100% 완결 & 페이지 고급 기능)**: 빈 페이지 삽입, 페이지 번호 매기기(Header/Footer), 워터마크 삽입, 실시간 크롭/조판 프리뷰, 진짜 폭/페이지 맞춤, 주석 두께·투명도·폰트 크기 조절
* **Phase 3 (실무 비즈니스 확장)**: 결재 도장/스탬프/전자서명, 이미지 삽입, 페이지 PNG/JPG 내보내기, 최근 문서 목록 및 사용자 환경설정 영속화
