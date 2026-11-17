Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
Name "더죤 PDF Workspace"
!ifdef APP_ICON
 !define MUI_ICON "${APP_ICON}"
 !define MUI_UNICON "${APP_ICON}"
!endif
OutFile "${OUTPUT_EXE}"
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\DeojonPdfWpf"
!define PRODUCT_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfWpf"
VIProductVersion "1.0.1.0"
VIAddVersionKey /LANG=1033 "ProductName" "더죤 PDF Workspace"
VIAddVersionKey /LANG=1033 "FileDescription" "더죤 PDF Workspace"
VIAddVersionKey /LANG=1033 "FileVersion" "1.0.1"
VIAddVersionKey /LANG=1033 "ProductVersion" "1.0.1"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Deojon Environmental Technology"
SetCompressor /SOLID lzma
Var FinalDir
Var CandidateDir
Var HadInstall
ShowInstDetails show
ShowUninstDetails show
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!macro CheckStopped PREFIX
Function ${PREFIX}CheckStopped
 IfFileExists "$INSTDIR\Workspace.App.exe" 0 done
 System::Call 'kernel32::CreateFileW(w "$INSTDIR\Workspace.App.exe", i 0x40000000, i 0, p 0, i 3, i 0, p 0) p.r0'
 ${If} $0 == -1
  IfSilent +2
  MessageBox MB_OK "더죤 PDF Workspace를 종료한 후 다시 시도하세요. 문서는 변경되지 않습니다."
  SetErrorLevel 20
  Abort
 ${EndIf}
 System::Call 'kernel32::CloseHandle(p r0)'
 done:
FunctionEnd
!macroend
!insertmacro CheckStopped ""
!insertmacro CheckStopped "un."
!macro GuardDestination PREFIX
Function ${PREFIX}GuardDestination
 StrCpy $1 "$INSTDIR"
 loop:
 System::Call 'kernel32::GetFileAttributesW(w r1) i.r0'
 ${If} $0 != -1
  IntOp $0 $0 & 0x400
  ${If} $0 != 0
   SetErrorLevel 22
   Abort "Linked install paths are not supported."
  ${EndIf}
 ${EndIf}
 ${GetParent} "$1" $2
 StrCmp $1 $2 done
 StrCmp $2 "" done
 StrCpy $1 $2
 Goto loop
 done:
FunctionEnd
!macroend
!include "FileFunc.nsh"
!macro CreateProductShortcuts DESKTOP_ROOT MENU_ROOT
 SetOutPath "$INSTDIR"
 CreateShortcut "${MENU_ROOT}\더죤 PDF Workspace.lnk" "$INSTDIR\Workspace.App.exe" "" "$INSTDIR\Workspace.App.exe" 0
 CreateShortcut "${DESKTOP_ROOT}\더죤 PDF Workspace.lnk" "$INSTDIR\Workspace.App.exe" "" "$INSTDIR\Workspace.App.exe" 0
!macroend
!insertmacro GuardDestination ""
!insertmacro GuardDestination "un."
Function .onInit
 SetShellVarContext current
!ifndef FIXTURE
 ; No arbitrary destination, migration, legacy registry reads, or elevated install.
 StrCpy $INSTDIR "$LOCALAPPDATA\Programs\DeojonPdfWpf"
!endif
FunctionEnd
Section "Shell"
 Call GuardDestination
 Call CheckStopped
 StrCpy $FinalDir "$INSTDIR"
 StrCpy $CandidateDir "$INSTDIR.staging"
 InitPluginsDir
 SetOutPath "$PLUGINSDIR"
 File "/oname=CoreDeployment.ps1" "${__FILEDIR__}\CoreDeployment.ps1"
 nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\CoreDeployment.ps1" -Mode Recover -InstallDirectory "$FinalDir" -CandidateDirectory "$CandidateDir"'
 Pop $0
 Pop $1
 ${If} $0 != 0
  DetailPrint "$1"
  SetErrorLevel 25
  Abort "Deployment recovery could not complete. Existing data was preserved. Close the application and retry."
 ${EndIf}
 StrCpy $HadInstall 0
 IfFileExists "$FinalDir\shell-files.json" 0 +2
 StrCpy $HadInstall 1
!ifndef FIXTURE
 ${If} $HadInstall == 0
  IfFileExists "$DESKTOP\더죤 PDF Workspace.lnk" shortcut_collision
  IfFileExists "$SMPROGRAMS\더죤 PDF Workspace.lnk" shortcut_collision
 ${EndIf}
!endif
 StrCpy $INSTDIR "$CandidateDir"
 Call GuardDestination
 SetOverwrite off
 ClearErrors
 !include "${STAGE}\install.nsh"
 IfErrors failed
 WriteUninstaller "$INSTDIR\Uninstall.exe"
 IfErrors failed
 SetOutPath "$TEMP"
 nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\CoreDeployment.ps1" -Mode Commit -InstallDirectory "$FinalDir" -CandidateDirectory "$CandidateDir"'
 Pop $0
 Pop $1
 ${If} $0 != 0
  DetailPrint "$1"
!ifdef FIXTURE
  FileOpen $2 "$FinalDir.fixture-error.txt" w
  FileWrite $2 "Exit=$0 $1"
  FileClose $2
!endif
  Goto failed
 ${EndIf}
 StrCpy $INSTDIR "$FinalDir"
!ifndef FIXTURE
 ${If} $HadInstall == 0
 WriteRegStr HKCU "${PRODUCT_KEY}" "DisplayName" "더죤 PDF Workspace"
 WriteRegStr HKCU "${PRODUCT_KEY}" "DisplayVersion" "1.0.1"
 WriteRegStr HKCU "${PRODUCT_KEY}" "InstallLocation" "$INSTDIR"
 WriteRegStr HKCU "${PRODUCT_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
 WriteRegStr HKCU "${PRODUCT_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
 WriteRegDWORD HKCU "${PRODUCT_KEY}" "NoModify" 1
 WriteRegDWORD HKCU "${PRODUCT_KEY}" "NoRepair" 1
 WriteRegStr HKCU "${PRODUCT_KEY}" "DisplayIcon" '$\"$INSTDIR\Workspace.App.exe$\",0'
 !insertmacro CreateProductShortcuts "$DESKTOP" "$SMPROGRAMS"
 ${EndIf}
!endif
!ifdef FIXTURE_SHORTCUT_ROOT
 CreateDirectory "${FIXTURE_SHORTCUT_ROOT}\Desktop"
 CreateDirectory "${FIXTURE_SHORTCUT_ROOT}\StartMenu"
 !insertmacro CreateProductShortcuts "${FIXTURE_SHORTCUT_ROOT}\Desktop" "${FIXTURE_SHORTCUT_ROOT}\StartMenu"
!endif
 Goto done
!ifndef FIXTURE
 shortcut_collision:
 SetErrorLevel 26
 Abort "같은 이름의 기존 바로가기가 있습니다. 기존 앱과 바로가기는 변경하지 않았습니다."
!endif
 failed:
 SetErrorLevel 21
 Abort "Setup could not complete. Existing files were not replaced; staging was preserved."
 done:
SectionEnd
Section "Uninstall"
 SetShellVarContext current
 Call un.GuardDestination
 !include "${STAGE}\check.nsh"
 Call un.CheckStopped
 !include "${STAGE}\remove.nsh"
 Delete "$INSTDIR\Uninstall.exe"
!ifdef FIXTURE_SHORTCUT_ROOT
 Delete "${FIXTURE_SHORTCUT_ROOT}\Desktop\더죤 PDF Workspace.lnk"
 Delete "${FIXTURE_SHORTCUT_ROOT}\StartMenu\더죤 PDF Workspace.lnk"
!endif
!ifndef FIXTURE
 Delete "$SMPROGRAMS\더죤 PDF Workspace.lnk"
 Delete "$DESKTOP\더죤 PDF Workspace.lnk"
 DeleteRegKey HKCU "${PRODUCT_KEY}"
!endif
 ; Never recurse: unknown files, documents, and independently downloaded modules survive.
 RMDir "$INSTDIR"
SectionEnd




