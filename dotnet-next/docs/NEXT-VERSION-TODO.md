# 다음 버전 작업 목록 (1.0.2)

1. **[소스 반영 · 검증 완료] 본문 스크롤과 축소판 선택 동기화**: `PdfDocumentService`의 `document.navigate` 처리에서 단일 선택 이하일 때 `SelectedPages`를 함께 갱신하고, `SidebarView.ApplyDocument`에서 드래그·크기 조절·스크롤 복원 중이 아닐 때 현재 페이지 카드를 `BringIntoView()`로 표시하도록 수정했다.
2. **[소스 반영 · 실기 확인 대기] 시작 시 최대화**: 프로그램을 시작할 때 창이 항상 최대화 상태로 열리도록 `Workspace.App/MainWindow.xaml` 및 `Workspace.Shell/MainWindow.xaml`에 `WindowState="Maximized"`를 적용했다. 실제 실행 화면에서의 확인이 필요하다.
3. **[소스 반영 · 검증 완료] 파일·홈 메뉴의 열기/저장 중복 정리**: 열기·저장·다른 이름으로 저장·문서 닫기는 `파일` 탭에 두고, `홈` 탭에서 중복된 `파일` 그룹(열기/저장)을 제거했다.
4. **[소스 반영 · 검증 완료] 실행 취소 메뉴 위치 정리**: `홈` 탭의 `편집` 그룹에 `실행 취소`·`다시 실행`을 배치해 메뉴 구성을 정리했다.
5. **[소스 반영 · 단위/오프스크린 검증 완료] 축소판 패널 빈 공간 클릭 선택 해제**: `SidebarView`에서 `ScrollViewer` 뷰포트 전체 높이를 클릭 가능 영역(`surface.MinHeight`)으로 유지하고, 드래그·수정키 없는 빈 공간 클릭 시 선택을 해제하도록 수정했으며 `ClearSelectionAsync()` 및 `Sidebar.Checks` 검증을 추가했다.
6. **[소스 반영] 페이지 추출 시 기본 파일 이름 자동 지정**: `MainWindow.Modular.cs`(`DefaultExtractFileName`) 및 `PdfWorkspaceView.cs`에서 추출 저장 대화상자 기본 파일 이름을 `<문서명>_<페이지범위>페이지.pdf` 형식으로 자동 채우도록 수정했다.
7. **[소스 반영] 설치된 모듈 DLL이 최신일 때 모듈 설치 창 생략**: `ModuleDistributionService.PrepareAsync`에서 동일 디렉터리 중복 재검증을 제거하고, `MainWindow.Modular.cs`(`StartModularAsync`)에서 `NoChange`/`Offline` 등 신규 설치·업데이트·다운로드가 필요 없는 경우 `ModuleSetupWindow`를 띄우지 않고 상태 표시줄 안내 후 즉시 문서를 열도록 수정했다.
8. **[소스 반영 · 검증 완료] 열기 모달 PDF 드래그 앤 드롭 지원**: `StartupOpenWindow`에 `AllowDrop`, 안내 문구, `DragEnter`/`PreviewDragOver`/`Drop` 처리 및 `TryDropFiles` 헬퍼를 추가하고 `StartupOpenChecks`에 검증을 추가했다.
9. **[소스 반영 · 검증 완료] 1.0.2 버전 반영**: `Directory.Build.props`, `Workspace.App.csproj`, `MainWindow.xaml`, `ModuleDistributionService.ShellVersion` 및 수정 모듈 버전을 `1.0.2` 기준으로 갱신했다(`ReleaseDiscovery` 검색 기준선은 `1.0.1` 유지).

위 항목은 `1.0.2` 빌드에 반영되었으며, 실제 설치 환경에서의 실기 동작 확인을 진행한다.
