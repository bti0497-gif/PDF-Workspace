# 더존 PDF .NET TEST · 로컬 설치 도구

## 현재 범위와 승인 경계

이 폴더는 기존 Electron 앱과 다른 **로컬 PC 시험 설치 도구**만 제공합니다. 기능 개발/통합 완료, 설치 파일 제작 완료, 설치·제거 QA, 다른 PC 배포 적합성을 뜻하지 않습니다. 자동 업데이트, 네트워크 다운로드, SDK/런타임 설치, 기본 PDF 연결 변경, 파일 연결 등록은 없습니다. 설치 후 앱을 자동 실행하지 않습니다.

**사용자가 승인한 로컬 실험판입니다. 네이티브 UI 미검증을 숨기지 않고, 실제 게시 DLL의 백엔드 기능 검사와 오프스크린 smoke가 통과하면 시험 패키지를 만들 수 있습니다.** CreateDIBSection 오류로 네이티브 입력 검사는 차단되어 있으며 다음 버전 과제로 남습니다. 이것은 실제 UI 종단 간 성공, 모든 PDF/한국어 표시 충실도, 운영 품질 승인이 아닙니다.

Build-LocalTest.ps1은 기본적으로 차단됩니다. `-ReleasePublishConfirmed -BackendTestsComplete -BuildInstaller`를 모두 지정하고 아래의 범위가 명확한 증거를 제출해야 합니다. `NativeUiTested=false`, `FidelityAccepted=false`는 필수 JSON 불리언이며 누락·문자열·true는 거부합니다. 스위치는 담당자의 명시적 판단이며, 증거 자체가 자동으로 진실을 증명하는 것은 아닙니다.

## 런타임·지원 한계

이 PC의 기존 Windows x64/.NET 9 framework-dependent 환경에 한정합니다. Microsoft.WindowsDesktop.App 9.0 및 Microsoft.NETCore.App 9.0 x64가 실제 설치되어 있어야 합니다. 설치 시작 시 x64 dotnet --list-runtimes로 검사하고 없으면 중단하여 정확한 선행 조건을 안내합니다. SDK는 필요 없으며 이 도구는 런타임을 내려받거나 설치하지 않습니다.

**.NET 9 STS 지원 종료일은 2026-11-10입니다.** 지원 종료 후 보안 수정·공식 기술 지원이 제공되지 않으므로 운영/외부 배포 기반으로 권장하지 않습니다. 이 PC의 한정 시험을 허용한 것과 지원되는 제품 배포는 별개입니다. 추후 지원 중인 LTS 전환은 별도 승인·재검증 대상입니다.

공식 근거: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core 및 https://devblogs.microsoft.com/dotnet/dotnet-sts-releases-supported-for-24-months/

## 다른 제품과 분리되는 설치 식별자

- 제품: Deojon PDF .NET TEST (local PC only)
- 실행파일: DeojonPdfDotNetTest.exe (공식 apphost 이름만 별도로 변경, 원래 Workspace.Shell.dll 유지)
- 고정 설치 경로: `%LOCALAPPDATA%\DeojonPdfDotNetTest`
- 시작 메뉴: 현재 사용자 `Deojon PDF .NET TEST`
- 제거 키: HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfDotNetTest-Local-1
- 관리자 승격 없음. 자동 실행 없음. `/S` 무인 설치/제거 차단.
- 기존 설치 폴더가 있으면 덮어쓰지 않고 중단합니다. 디렉터리 변경 화면이나 기존 앱 경로 사용을 제공하지 않습니다.

## 1. 도구 자체 검사만 수행

PowerShell 7에서 다음 명령은 스크립트 구문, NSIS `/SAFEPPO` 전처리, 승인 누락 차단 및 **작은 테스트 payload의 실제 NSIS 컴파일**을 검사합니다. 실제 앱 패키지는 만들지 않습니다. 공백·한글·달러 기호가 있는 파일명으로 생산용 payload 생성 함수를 검사하고, 설치 초기화에서 무조건 중단하도록 만든 `COMPILER-FIXTURE-DO-NOT-RUN.exe`만 고유 임시 폴더에 생성합니다. 이 테스트 EXE도 실행하거나 설치하지 않습니다. 이전 실패 staging/로그는 보존합니다.

```powershell
& .\scripts\Test-Tooling.ps1 -TemporaryDirectory '<별도 임시 폴더>'
```

기본 NSIS 경로는 이 PC에서 확인된 electron-builder 캐시의 3.0.4.1 컴파일러입니다. `-MakeNsis`로 기존 로컬 컴파일러만 지정할 수 있습니다. 다운로드하지 않습니다. 전처리만으로는 NSIS File 명령 문법이 검증되지 않으므로 작은 fixture의 실제 컴파일을 반드시 수행합니다. 생성 문법은 `File "/oname=name with spaces.txt" "원본 경로"`이며 `File /oname="..."`가 아닙니다. 작은 fixture 통과는 실제 전체 패키지 컴파일·설치·제거 QA를 대신하지 않습니다.

## 2. 정확한 게시 DLL의 백엔드·오프스크린 검사 후 실험판 생성

입력은 이미 Release로 게시한 **전체 폴더**입니다. 도구는 publish/build/restore를 하지 않습니다. 제공자는 Release 및 win-x64 게시 결과임을 확인해야 합니다. 모듈 DLL, deps/runtimeconfig, apphost가 필요하며 net9.0 framework-dependent 구성을 검사합니다.

`-VerificationJson`은 담당자가 **정확한 Release 게시 DLL에서 백엔드 기능 및 오프스크린 smoke를 실행한 후** 작성하는 다음 형식입니다. 모든 BackendOperations의 true는 백엔드 경로·출력/보존 검사에만 해당하며 버튼·키보드·실제 창의 성공을 의미하지 않습니다. render=true는 렌더 처리/출력 검사를 뜻하고 시각 충실도 전체 승인과 다릅니다. 읽기 전용 smoke.json 또는 과거의 모호한 Passed/Operations 형식만으로 통과시킬 수 없습니다. PublishedShellSha256은 제공한 Release 게시 폴더의 실제 Workspace.Shell.dll SHA-256이어야 합니다. 검사 로그/코퍼스/원본 보존 근거는 별도로 보관하십시오.

```json
{
  "EvidenceScope": "exact-published-dll-backend-and-offscreen",
  "ReleasePublish": true,
  "BackendTestsPassed": true,
  "OffscreenSmokePassed": true,
  "NativeUiTested": false,
  "FidelityAccepted": false,
  "NativeUiLimitation": "CreateDIBSection 오류로 실제 UI 입력 검사가 차단됨. 네이티브 동작·한국어/PDF 충실도는 미승인.",
  "PublishedShellSha256": "실제로 검사한 게시 DLL의 SHA256",
  "BackendOperations": {
    "open": true, "render": true, "rotate": true, "delete": true,
    "extract": true, "combine": true, "save-as": true,
    "original-preservation": true
  }
}
```

```powershell
& .\scripts\Build-LocalTest.ps1 `
 -PublishedAppDirectory '<기존 Release publish 폴더>' `
 -PortableDirectory '<프로젝트 밖 공식 0.21.1 portable 압축 해제 폴더>' `
 -PortableArchive '<검증된 공식 Windows x64 portable ZIP>' `
 -VerificationJson '<정확한 게시 DLL 백엔드·오프스크린 증거 JSON>' `
 -OutputDirectory '<Aside artifacts 절대 경로>' `
 -ReleasePublishConfirmed -BackendTestsComplete -BuildInstaller
```

패키지의 EXPERIMENTAL-ACCEPTANCE-SCOPE.json, 외부 receipt, 설치 전 경고에 백엔드/오프스크린만 통과했고 네이티브 UI·충실도는 미승인임을 표시합니다.

출력은 프로젝트 밖 artifacts 경로만 허용합니다. 고유한 staging, 설치 EXE, SHA-256 및 근거 해시 receipt를 남깁니다. 실패 staging은 조사용으로 보존합니다. 설치 파일 실행·업로드·태그·릴리즈·서명은 수행하지 않습니다. 실행파일 버전 문자열만으로 Release임을 판정하지 않으며, 이 시험 도구는 전체 빌드 provenance 시스템이 아닙니다.

## PdfCraft 출처·고지

공식 0.21.1 Windows x64 portable ZIP SHA-256:
`dcbcc2ddc019d85cca90a8242d5e4dc380f1ff6bfb6b951a89152d1dbae14cb4`

CLI는 앱의 ReadOnlyPdfSession.cs PinnedHash와 비교하고 Authenticode Valid/Learning Machines Inc도 확인합니다. ZIP에서 CLI를 찾아 모든 공식 파일의 내용을 압축 해제 입력과 대조합니다. GUI pdfcraft.exe만 제외하고 CLI, 모든 LICENSE/LICENCE/NOTICE/OFL/README, portable 설명, 모델·귀속 고지를 원래 상대 경로로 보존합니다. 원본 폴더의 PdfCraftData처럼 ZIP에 없는 실행 로그·복구 데이터는 복사하지 않습니다. 모델 포함은 OCR 지원 구현을 의미하지 않습니다. Authenticode 상태를 오프라인에서 확인하지 못하면 우회하지 않고 중단합니다.

앱의 packaged dependency 위치는 `Dependencies\PdfCraft\0.21.1\pdfcraft-cli.exe`입니다. `PDFWORKSPACE_PDFCRAFT_CLI` 환경변수가 설정되어 있으면 앱이 외부 경로를 우선할 수 있으므로 패키지 검사는 별도 환경에서 해야 합니다. 이 설치 도구는 사용자 환경변수를 변경하지 않습니다.

고지를 모두 포함해도 법률 검토나 모든 재배포 의무 충족을 보증하지 않습니다. 공식 portable 설명에 있는 연결 등록 예제는 단순 동봉 문서이며 설치 도구는 실행하지 않습니다.

## 안전한 제거와 잔여 파일

설치 시 파일별 상대 경로·SHA-256 및 알려진 폴더를 installed-files.json으로 기록합니다. 제거는 고정 설치 루트/제품 ID/경로 이탈/링크를 먼저 검사한 후 **해시가 같은 알려진 파일만** 삭제합니다. 수정된 파일·미등록 사용자 문서는 보존합니다. 알려진 빈 폴더만 비재귀 삭제하며 `RMDir /r` 또는 사용자 경로에 대한 와일드카드 재귀 삭제가 없습니다. 목록/해시가 손상되면 중단합니다. 메타데이터·자체 제거 프로그램·정확한 전용 바로가기만 별도로 정리합니다. 같은 사용자에 의한 악의적 스크립트/manifest 변조를 방어하는 보안 샌드박스는 아닙니다.

설치 중 실패/강제 종료 복구, 실행 중 파일 잠금, 제거 재시도, 실제 UAC·다중 사용자·백신·다른 PC, 서명/SmartScreen 및 한국어 렌더 충실도는 아직 검증하지 않았습니다. 실험판 생성 전 정확한 게시 DLL의 백엔드 기능·원본 보존·오프스크린 검사를 확인하고, 네이티브 UI와 충실도 미검증은 명시적으로 유지합니다. 설치/실행/제거의 실제 동작은 별도 확인 대상이며 백엔드 통과로 간주하지 않습니다.
