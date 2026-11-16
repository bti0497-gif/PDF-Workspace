Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"
Name "Deojon PDF .NET TEST 2 (local PC only)"
OutFile "${OUTPUT_EXE}"
VIProductVersion "0.2.0.0"
VIAddVersionKey /LANG=1042 "ProductName" "Deojon PDF .NET TEST 2"
VIAddVersionKey /LANG=1042 "FileDescription" "Deojon PDF .NET TEST 2 local trial installer"
VIAddVersionKey /LANG=1042 "FileVersion" "0.2.0.0"
VIAddVersionKey /LANG=1042 "ProductVersion" "0.2.0-local-test-2"
VIAddVersionKey /LANG=1042 "LegalCopyright" "Local experimental build"
InstallDir "$LOCALAPPDATA\DeojonPdfDotNetTest2"
RequestExecutionLevel user
SetCompressor /SOLID lzma
ShowInstDetails show
ShowUninstDetails show
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${PAYLOAD}\LOCAL-TEST-README.txt"
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Korean"
Var PowerShell
Function .onInit
  IfSilent 0 +3
    MessageBox MB_ICONSTOP "Silent installation is disabled for this local test."
    Abort
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "Windows x64 is required."
    Abort
  ${EndIf}
  SetShellVarContext current
  StrCpy $INSTDIR "$LOCALAPPDATA\DeojonPdfDotNetTest2"
  StrCpy $PowerShell "$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
  InitPluginsDir
  File /oname=$PLUGINSDIR\Test-LocalPrerequisite.ps1 "${TOOLING}\scripts\Test-LocalPrerequisite.ps1"
  nsExec::ExecToStack '"$PowerShell" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\Test-LocalPrerequisite.ps1"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Local test prerequisite failed:$\r$\n$1$\r$\nNo runtime download or installation was attempted."
    Abort
  ${EndIf}
  MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION ".NET 9 support ends on 2026-11-10. TEST 2 installs separately from TEST 1. Experimental local-PC package: real-PDF main-workspace automated/backend/offscreen checks only. Native interactive UI and document/Korean fidelity remain unverified. Not production or redistribution. No default PDF association is changed." IDOK +2
    Abort
FunctionEnd
Section "Local test"
  SetShellVarContext current
  StrCpy $INSTDIR "$LOCALAPPDATA\DeojonPdfDotNetTest2"
  IfFileExists "$INSTDIR\." 0 +3
    MessageBox MB_ICONSTOP "Test directory already exists. Nothing will be overwritten."
    Abort
  !include "${PAYLOAD_INCLUDE}"
  WriteUninstaller "$INSTDIR\Uninstall-DeojonPdfDotNetTest2.exe"
  CreateDirectory "$SMPROGRAMS\Deojon PDF .NET TEST 2"
  CreateShortcut "$SMPROGRAMS\Deojon PDF .NET TEST 2\Deojon PDF .NET TEST 2.lnk" "$INSTDIR\DeojonPdfDotNetTest2.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfDotNetTest2-Local-2" "DisplayName" "Deojon PDF .NET TEST 2 (local only)"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfDotNetTest2-Local-2" "DisplayVersion" "0.2.0-local-test-2"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfDotNetTest2-Local-2" "UninstallString" '$\"$INSTDIR\Uninstall-DeojonPdfDotNetTest2.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfDotNetTest2-Local-2" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfDotNetTest2-Local-2" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfDotNetTest2-Local-2" "NoRepair" 1
SectionEnd
Function un.onInit
  IfSilent 0 +3
    MessageBox MB_ICONSTOP "Use interactive uninstall for this local test."
    Abort
  SetShellVarContext current
  StrCpy $INSTDIR "$LOCALAPPDATA\DeojonPdfDotNetTest2"
  StrCpy $PowerShell "$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
FunctionEnd
Section "Uninstall"
  nsExec::ExecToStack '"$PowerShell" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Remove-OwnedFiles.ps1"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Safe uninstall stopped. Files/registration retained for inspection:$\r$\n$1"
    Abort
  ${EndIf}
  Delete "$INSTDIR\installed-files.json"
  Delete "$INSTDIR\Uninstall-DeojonPdfDotNetTest2.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\Deojon PDF .NET TEST 2\Deojon PDF .NET TEST 2.lnk"
  RMDir "$SMPROGRAMS\Deojon PDF .NET TEST 2"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfDotNetTest2-Local-2"
  MessageBox MB_OK "Removed known unchanged test files. Unrecognized or modified files, if any, remain. No document/default-app settings were changed."
SectionEnd
