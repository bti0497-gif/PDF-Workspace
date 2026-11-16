Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
Name "PDF Workspace WPF TEST3"
OutFile "${OUTPUT_EXE}"
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\DeojonPdfDotNetTest3"
ShowInstDetails show
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${PAYLOAD}\LOCAL-TEST-README.txt"
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_LANGUAGE "Korean"
Var Args
Var UpdateArg
Var WaitPid
Var PS
Function .onInit
 SetShellVarContext current
 ${GetParameters} $Args
 StrCpy $UpdateArg ""
 ${GetOptions} $Args "/UPDATE" $0
 ${IfNot} ${Errors}
  StrCpy $UpdateArg "-Update"
 ${EndIf}
 ${GetOptions} $Args "/RESTART" $0
 ${IfNot} ${Errors}
  StrCpy $UpdateArg "$UpdateArg -Restart"
 ${EndIf}
 ${GetOptions} $Args "/WAITPID=" $WaitPid
 ${If} ${Errors}
  StrCpy $WaitPid "0"
 ${EndIf}
 ; Digits only, disallow PowerShell argument injection.
 StrLen $1 $WaitPid
 ${If} $1 > 10
  SetErrorLevel 1
  Abort
 ${EndIf}
 StrCpy $2 0
 loop:
  StrCpy $3 $WaitPid 1 $2
  StrCmp $3 "" done
  StrCmp $3 "0" valid
  StrCmp $3 "1" valid
  StrCmp $3 "2" valid
  StrCmp $3 "3" valid
  StrCmp $3 "4" valid
  StrCmp $3 "5" valid
  StrCmp $3 "6" valid
  StrCmp $3 "7" valid
  StrCmp $3 "8" valid
  StrCmp $3 "9" valid
  SetErrorLevel 1
  Abort
 valid:
  IntOp $2 $2 + 1
  Goto loop
 done:
 IfSilent 0 interactive
 ${If} $UpdateArg == ""
  SetErrorLevel 1
  Abort
 ${EndIf}
 interactive:
 InitPluginsDir
 StrCpy $PS "$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
 File /oname=$PLUGINSDIR\Install-WpfPreview.ps1 "${TOOLING}\scripts\Install-WpfPreview.ps1"
FunctionEnd
Section
 ; Extract only to NSIS private temporary storage, never directly into installed tree.
 StrCpy $INSTDIR "$PLUGINSDIR\payload"
 !include "${PAYLOAD_INCLUDE}"
 nsExec::ExecToStack '"$PS" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\Install-WpfPreview.ps1" -Payload "$PLUGINSDIR\payload" $UpdateArg -WaitPid $WaitPid'
 Pop $0
 Pop $1
 DetailPrint "$1"
 ${If} $0 != 0
  SetErrorLevel 1
  IfSilent +2 0
  MessageBox MB_ICONSTOP "Update failed or restart requires attention. No success is assumed.$\r$\n$1"
  Abort
 ${EndIf}
 ${If} $UpdateArg == ""
  CreateDirectory "$SMPROGRAMS\PDF Workspace WPF TEST3"
  CreateShortcut "$SMPROGRAMS\PDF Workspace WPF TEST3\PDF Workspace WPF TEST3.lnk" "$LOCALAPPDATA\DeojonPdfDotNetTest3\DeojonPdfDotNetTest3.exe"
  ${If} ${Errors}
   SetErrorLevel 1
   Abort "Files committed, shortcut creation failed."
  ${EndIf}
 ${EndIf}
 SetErrorLevel 0
SectionEnd
