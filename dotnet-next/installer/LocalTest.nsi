Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"
Name "Deojon PDF .NET TEST (local PC only)"
OutFile "${OUTPUT_EXE}"
InstallDir "$LOCALAPPDATA\DeojonPdfDotNetTest"
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
  StrCpy $INSTDIR "$LOCALAPPDATA\DeojonPdfDotNetTest"
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
  MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION ".NET 9 support ended 2026-11-10. Experimental local-PC package: backend/offscreen checks only. Native interactive UI and document/Korean fidelity are NOT accepted (CreateDIBSection blocks native testing). Not production or redistribution. No default PDF association is changed." IDOK +2
    Abort
FunctionEnd
Section "Local test"
  SetShellVarContext current
  StrCpy $INSTDIR "$LOCALAPPDATA\DeojonPdfDotNetTest"
  IfFileExists "$INSTDIR\." 0 +3
    MessageBox MB_ICONSTOP "Test directory already exists. Nothing will be overwritten."
    Abort
  !include "${PAYLOAD_INCLUDE}"
  WriteUninstaller "$INSTDIR\Uninstall-DeojonPdfDotNetTest.exe"
  CreateDirectory "$SMPROGRAMS\Deojon PDF .NET TEST"
  CreateShortcut "$SMPROGRAMS\Deojon PDF .NET TEST\Deojon PDF .NET TEST.lnk" "$INSTDIR\DeojonPdfDotNetTest.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfDotNetTest-Local-1" "DisplayName" "Deojon PDF .NET TEST (local only)"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfDotNetTest-Local-1" "UninstallString" '$\"$INSTDIR\Uninstall-DeojonPdfDotNetTest.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfDotNetTest-Local-1" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfDotNetTest-Local-1" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfDotNetTest-Local-1" "NoRepair" 1
SectionEnd
Function un.onInit
  IfSilent 0 +3
    MessageBox MB_ICONSTOP "Use interactive uninstall for this local test."
    Abort
  SetShellVarContext current
  StrCpy $INSTDIR "$LOCALAPPDATA\DeojonPdfDotNetTest"
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
  Delete "$INSTDIR\Uninstall-DeojonPdfDotNetTest.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\Deojon PDF .NET TEST\Deojon PDF .NET TEST.lnk"
  RMDir "$SMPROGRAMS\Deojon PDF .NET TEST"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfDotNetTest-Local-1"
  MessageBox MB_OK "Removed known unchanged test files. Unrecognized or modified files, if any, remain. No document/default-app settings were changed."
SectionEnd
