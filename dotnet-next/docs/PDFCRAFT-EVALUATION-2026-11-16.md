# PdfCraft 후보 사전 검증 · 2026-11-16

## 범위

사용자 지시에 따라 WPF 시험판 제작·설치·릴리즈 및 PDF 기본 앱 변경 전에, PdfCraft의 별도 포터블 CLI를 원본과 분리한 작업 복사본에서 평가했다. OCR·사내망·실제 프린터·원본 덮어쓰기는 범위 밖이다. 아래는 좁은 예비 시험이지 엔진 도입 승인, 전체 호환성, 실제 GUI 작업의 성공 또는 생산 사용 적합성 판단이 아니다.

## 정확한 대상

- `storytold/pdfcraft` 공식 GitHub v0.21.1 Windows x64 portable, 크기 123,073,914 bytes, ZIP SHA-256 `dcbcc2ddc019d85cca90a8242d5e4dc380f1ff6bfb6b951a89152d1dbae14cb4`. 공식 GitHub 릴리즈 메타데이터 및 제공 `SHA256SUMS.txt`와 일치.
- 추출된 `pdfcraft.exe`, `pdfcraft-cli.exe`는 Windows Authenticode 검사에서 Valid, 서명 주체 `Learning Machines Inc`.
- `pdfcraft-cli --version`: 0.21.1. 포터블 ZIP은 사용자 artifacts, 압축 해제본과 PDF 시험 작업은 이번 세션 tmp에 둠. 설치·PDF 연결 변경 없음.
- 프로젝트 README의 소스 라이선스는 MIT 또는 Apache-2.0 선택. 재배포 전 실행파일에 포함된 제3자 라이선스·고지와 상표 규칙을 실제 패키지 기준으로 따로 검토해야 한다. 라이선스 설명은 법률 자문이 아니다.

## 자료와 보존

프로젝트 `test-output`의 PDF 3개를 각각 originals 및 work로 복사해 사용했다. 자료는 `원본.pdf`(33쪽, SHA-256 `D7DAEBE6359A1EAA2CDD1F9FC7B99624E9B210CE6D14704D099C823B7B3B8C1B`), `font-render-diagnostic.pdf`(3쪽, `F68BD152A3E89BCEBF95A95DBD5529139308C3814E42FF4CDC85350458800D68`), `6_주석_적용.pdf`(33쪽, `92D38AF80C5D8A2276424F3718519B02123F6FC732F37916B4BDEECFC4CDC0C4`). 최초 및 시험 후 originals/work 쌍의 SHA-256이 일치했다. 기존 원본은 직접 수정하지 않았다. 이 문서들만으로 모든 실제 업무 문서 유형을 대표하지 않는다.

## 실제 검사

1. PdfCraft CLI `info`/`render --page 1 --dpi 96`으로 위 3종을 열었다. 각각 33/3/33쪽, 1쪽 이미지를 생성했고 CLI 정보에 warnings 0으로 출력됐다. 자동화 정보에 표시된 annotations=0을 실제 주석 모두의 보존 검사로 간주하지 않는다.
2. `font-render-diagnostic.pdf`의 각 페이지를 PdfCraft와 독립 `aside.pdf.renderPages`에서 같은 794×1123 픽셀로 출력해 시각 비교했다. 두 도구 모두 첫 두 subset 페이지에서 글자 일부가 빠져 보였고 세 번째 전체 글꼴 페이지는 문구가 읽혔다. 기존 파일의 의도/생성 이력과 추가 기준 도구를 검토하기 전 PdfCraft만의 결함이나 원인을 확정하지 않는다. 이 결과로 모든 한국어 글꼴이 안전하다고 보지 않는다.
3. 독립 도구로 33쪽 자료의 1–3쪽 복사본을 따로 만들었다: `three-pages.pdf` SHA-256 `4EEEB37913BC336B596FA1BE5EA7420DDA481A82B1895075D9DF81A5F7A30A56`.
4. PdfCraft `edit --rotate 1:90 --delete 2`를 별도 출력 `rotate-delete.pdf`에 실행하고 독립 도구로 다시 열었다: 2쪽; 1쪽 1123×795 렌더로 회전, 2쪽은 원본 3쪽의 텍스트와 일치했다. 별도로 두 파일의 원본 3쪽/출력 2쪽을 같은 795×1123으로 독립 렌더해 892,785픽셀 모두 동일했다. 이는 이 한 페이지만의 렌더 결과다.
5. `extract --pages 3,1` 출력은 2쪽이며 독립 추출 텍스트 기준 원본 3→1 순서였다. 이 출력과 회전·삭제 출력을 `combine`한 PDF는 4쪽, 원본 3→1→회전된 1→3 순서로 독립 재열기·페이지 수 및 텍스트 대조를 수행했다. 회전된 페이지의 독립 텍스트 추출은 앞뒤 공백을 정규화해도 문자열 순서가 달랐으나 문자 383개의 출현 수는 같았다. 문자 집합 일치는 정확한 읽기 순서·보존을 뜻하지 않는다. 원인(회전 좌표계/추출기 등)은 미확정이다.
6. CLI `run doc_open ... --root <work>`로 work 내 파일을 열 수 있었고, `../originals/원본.pdf`는 디렉터리 밖이라는 오류와 종료 코드 1로 거부되었다. 이 한 경로 시험은 링크·UNC·디바이스 경로 등 전체 격리 보안을 보증하지 않으며 CLI `run`의 root 검사이지 MCP 종단 간 시험은 아니다.
7. GUI 포터블을 work의 3쪽 복사본으로 실행 요청해 PID 29064가 반환되었다. 이후 native 앱 관찰은 `target process cannot be inspected`, Windows-MCP 제어 상태는 `AI control indicator failed: OSError: CreateDIBSection failed`로 차단되었고, 뒤의 프로세스 조회에서 PID가 보이지 않았다. UI 열림·사용·저장 또는 종료 원인은 확인하지 못했다. 제어 실패를 우회하거나 임의로 시스템/다른 프로세스를 종료하지 않았다. GUI 검증은 차단으로 남는다.

## 한계 및 다음 판단

- WPF에서 PdfCraft를 UI 컨트롤로 바로 임베드할 수 있는 공식 .NET 바인딩은 이번 조사에서 확인되지 않았다. 공식 CLI/MCP 경계를 통한 별도 프로세스 후보로 평가하되 실제 WPF 연동, 수명·오류·취소·출력 데이터 검증이 필요하다.
- PdfCraft 공식 문서는 alpha 및 기존 CJK 텍스트 편집, 렌더 충실도 등 한계를 기술한다. PdfCraft CLI 테스트 통과는 네이티브 WPF PDF 엔진 채택이나 수정·저장 전체 안정성을 의미하지 않는다.
- GUI 열기/편집/저장 후 재열기, 한글 입력 및 글꼴, 주석·책갈피·양식, 크롭·병합 구조 보존, 원본 vs 새 저장본의 다른 뷰어 확인, 손상/암호/큰 파일, 반복 수행·성능·파일 접근 오류, 다중 화면, 보안 경계 등 미검증.
- PDFiumCore 등 다른 후보도 고정 버전/라이선스/배포물 고지와 동일 코퍼스·동일 렌더 기준으로 별도 검증해야 한다. 특정 엔진 선정은 아직 하지 않았다.
- 현재 WPF UI/UX 미커밋 변경과 기존 Electron 앱은 건드리지 않았다. 시험판 설치·기본 PDF 연결·릴리즈·자동 업데이트는 진행하지 않았다.


## 2026-11-16 후속: 메모리 재확인과 한글 주석 저장본

사용자 요청 후 다시 측정한 `Win32_OperatingSystem`: 전체 물리 메모리 23.38GB, 사용 가능 6.44GB(후속 확인 6.36GB). 앞선 1.52GB는 당시 시점 결과이지 현재 상태가 아니었다. 이 시점 `AsusWiFiSmartConnect` PID 6292의 작업 집합은 약 4066MB였으나 그것이 앞선 낮은 수치나 `CreateDIBSection` 오류의 원인인지는 확정하지 않았다. 임의로 다른 프로세스는 닫지 않았다. Windows-MCP는 여전히 `AI control indicator failed: OSError: CreateDIBSection failed`를 보고하여 GUI 제어 차단은 지속됐다. 사용 가능 메모리만으로 UI 문제가 해결됐다고 판단하지 않는다.

CLI 0.21.1 실제 `run --script`로 `three-pages.pdf`를 열고 1쪽 텍스트 상자에 `한글 주석 시험 ABC 123`, 2쪽에 `한글 책갈피 시험`을 추가하여 새 `korean-annotation.pdf`로 저장 후 닫고 다시 열었다. 처리 단계는 모두 ok였고, 다시 연 파일의 comment_list는 원본 한글 내용, bookmark_list는 한글 제목과 2쪽을 반환했다. 그러나 comment_add 응답 warnings는 `한` 글꼴을 표준 글꼴로 그리지 못해 텍스트는 저장되지만 외관에 ?가 나타난다고 명시했다. 독립 `aside.pdf.renderPages`로 저장본의 1쪽을 실제 렌더해 해당 주석이 `?? ?? ?? ABC 123` 형태로 보임을 확인했다. 이는 **해당 버전·텍스트 상자 주석·기본 설정·해당 복사본**에서의 표시 실패다. 다른 형태의 주석이나 일반 PDF 본문 표시, 다른 폰트 설정 전체가 불가능하다는 증거는 아니다.

입력 `three-pages.pdf` SHA-256은 시험 전후 `4EEEB37913BC336B596FA1BE5EA7420DDA481A82B1895075D9DF81A5F7A30A56`로 유지됐고, 출력 `korean-annotation.pdf` SHA-256은 `961A2351CF8A3E57F077F08FBD03A135D83DD87738B606D3857076212E76C40F`이다. 결과 데이터 보존과 실제 그림 표시를 별도 판정한다. 이 결함은 한국어 주석/텍스트를 다룰 후보로 검토할 때 중요한 보류 항목이다. 코드 연결·설치·릴리즈·기본 연결 변경은 하지 않았다.


### 별도 한글 본문 수정 제한 시험

같은 PdfCraft 0.21.1 CLI에서 글꼴 진단용 `font-render-diagnostic.pdf`의 3쪽 `subset=false` 전체 `NanumGothic-1848` 문단을 조회하고 `서울휴게소`를 `부산휴게소`로 변경해 별도 `korean-body-edited.pdf`로 저장·재열었다. CLI는 해당 글꼴과 수정 문장을 보고했고, 독립 `aside.pdf.extractText`에서 변경 문자열이 있고 이전 문자열이 없음을 확인했다. 독립 `aside.pdf.renderPages` 794×1123에서 수정된 한글이 읽히는 것을 확인했다. 단순 1줄과 특정 포함 글꼴·문자열의 제한 시험으로, PDF 전반의 한글 편집·폰트, 표나 주변 레이아웃, 모든 글리프 및 다른 앱의 원본 보존까지 증명하지 않는다. GUI 편집은 시험하지 않았다.

입력 SHA-256 `F68BD152A3E89BCEBF95A95DBD5529139308C3814E42FF4CDC85350458800D68` 유지. 출력 SHA-256 `4906DEC3C11E501A5DFC500FF3525D5A3077BAD5A0530AEE239B73E7321D68F0`. 앞서 관찰한 텍스트 상자 주석의 글꼴 경고 및 물음표 출력과 별개의 기능·증거다.
