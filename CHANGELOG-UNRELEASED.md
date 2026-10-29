# 미배포 변경: 빈 화면과 다중 페이지 스크롤

현재 게시된 릴리즈/패키지 버전은 1.0.5입니다. 아래 변경은 소스에 반영했으며 기존 1.0.5 설치파일을 수정하거나 새 설치파일을 게시하지 않았습니다. 다음 배포는 별도 새 버전 번호와 릴리즈 절차를 사용해야 합니다.

## 빈 문서 화면

- 시작/문서 닫기 시 리본을 자동으로 접고 빈 썸네일 사이드바를 표시하지 않습니다.
- 문서가 없을 때 메뉴 탭은 시작 화면을 밀지 않는 임시 펼침 메뉴로 동작합니다.
- 문서용 리본 고정 설정은 보존합니다. 문서를 열면 고정 모드는 홈 리본을 복원하고 자동 숨김 모드는 접힌 상태를 유지합니다.
- 작은 화면의 최근 문서 목록은 위쪽이 잘리지 않도록 스크롤할 수 있습니다.
- 외곽 html/body/root는 스크롤하지 않도록 하고, 페이지·썸네일 이동은 해당 패널 내부만 스크롤합니다. 보고된 실제 모니터 전환 잘림은 시험창에서 재현되지 않았으므로 원인이 확정됐다고 주장하지 않습니다.

## 다중 페이지 스크롤

- 프레임마다 전체 페이지 DOM을 측정하던 경로를 행 단위 이진 탐색으로 변경했습니다. 단일/양면 및 서로 다른 페이지 높이를 처리합니다.
- 썸네일 콜백을 안정화하여 현재 페이지가 바뀔 때 모든 썸네일이 다시 렌더링되지 않게 했습니다.
- 썸네일은 사이드바 주변 600px 범위에서만 캔버스를 유지합니다. 화면 밖에서는 렌더 작업을 취소하고, 돌아오면 다시 그립니다.
- 캔버스와 자리표시자의 종횡비를 같게 유지하여 로딩·해제 시 행 높이가 바뀌지 않도록 했습니다.
- 비트맵 캐시는 기존 32개 상한에 추정 RGBA 96 MiB 상한을 추가했습니다. 이는 캐시 비트맵만의 한도이며 앱 전체 RAM/GPU 메모리 한도가 아닙니다. 원본·출력 화질은 낮추지 않습니다.
- 오래된 텍스트 레이어 작업을 취소하고, 캐시 초기화·렌더 취소 뒤 늦게 도착한 비트맵은 해제합니다. DPR을 캐시 키에 포함했습니다.

## 재현 및 검사

기본 검사: `node node_modules/typescript/bin/tsc --noEmit`, `node scripts/run-regressions.cjs`, `node node_modules/electron-vite/bin/electron-vite.js build`.

`scroll-index-regression.cjs`는 2,400개 혼합 크기/단일·양면 조건을 기존 전체 탐색과 비교합니다. 2,000페이지 모형의 최대 페이지 측정 수는 단일 11회, 양면 20회였습니다. 이는 연산량 검사이지 실제 FPS 개선률이 아닙니다.

UI 검사는 개발용 Vite 서버 `http://127.0.0.1:5874/`와 별도 임시 Electron user-data 폴더를 사용합니다. 서버 시작 예:

```powershell
node --input-type=module -e "const {createServer}=await import('vite');const s=await createServer({configFile:false,root:'src',publicDir:'../public',esbuild:{jsx:'automatic'},server:{host:'127.0.0.1',port:5874,strictPort:true}});await s.listen();"
```

다른 터미널에서 각 명령의 `<isolated-temp-dir>`를 서로 다른 승인된 임시 경로로 바꾸어 실행합니다:

```powershell
node node_modules/electron/cli.js scripts/empty-layout-regression.cjs <isolated-temp-dir>
node node_modules/electron/cli.js scripts/header-layout-regression.cjs <isolated-temp-dir>
node node_modules/electron/cli.js scripts/large-document-scroll-regression.cjs <isolated-temp-dir>
```

결과는 무시되는 `test-output/`의 동명 JSON에 기록됩니다. 빈 화면 검사는 문서 열기/실제 닫기, 리본 설정 보존, 최근 목록 및 12개 크기·배율 조건을 포함합니다. 대형 문서 검사는 생성한 240페이지 혼합 크기 PDF, 단일/양면 스크롤, 캔버스 수, 썸네일 재방문·다열 높이 안정성, Ctrl/Shift 선택 및 드래그 시작을 검사합니다. 숨긴 Electron 창에서 애니메이션 시간을 제외하도록 smooth scrolling을 비활성화했습니다. 사용자의 원본 PDF, 실제 휠 지연/FPS, 실제 모니터 이동 및 설치·업데이트 실행은 이 검사로 검증되지 않습니다.

## 이번 작업의 확인 결과

- 타입 검사, 전체 소스 회귀 검사, Electron 앱 빌드 통과. 새 설치본 생성/설치/배포는 하지 않았습니다.
- 빈 화면·리본 생명주기 검사 및 12개 크기/배율 메뉴 검사 통과.
- 240페이지 시험에서 연속 보기의 프레임별 페이지 측정 최대 8회, 본문 캔버스 최대 3개; 양면 보기에서 각각 14회, 6개였습니다.
- 1열 썸네일 조건에서 캔버스 최대 12개를 유지했습니다. 3열 전환, 스크롤 높이 안정성, 재방문 재렌더, Ctrl/Shift 선택, 선택을 보존하는 컨텍스트 메뉴·드래그 시작 검사도 통과했습니다.
- 스크롤/선택 후 PDF의 수정 상태는 false였고, 검사 중 수집한 렌더 오류는 없었습니다. 이 결과를 사용자의 원본 파일 체감 속도나 실제 휠 애니메이션 검증으로 확대하지 않습니다.

## 다음 작업 시작점

1. 실제 사용자 PDF로 휠 스크롤 체감과 빠른 페이지 이동을 확인하고, 생성한 240페이지 시험 결과와 구분해 기록합니다.
2. 실제 모니터 간 이동·DPI 변경 시 메뉴 잘림 여부를 확인합니다.
3. 배포 요청을 받으면 새 버전으로 설치본·업데이트를 검증하고 릴리즈합니다. 이번 커밋·푸시에서는 태그나 릴리즈를 만들지 않습니다.
