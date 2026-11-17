[CmdletBinding()]
param([Parameter(Mandatory)][string]$MakeNsis,[Parameter(Mandatory)][string]$TestRoot,[string]$AppIconPath)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Common.ps1"
$root=SafePath $TestRoot
if(Test-Path $root){throw 'Fixture root must not exist.'}
$stage=Join-Path $root 'build';$payload=Join-Path $stage 'payload';$install=Join-Path $root 'isolated install';$exe=Join-Path $root 'fixture.exe'
$key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DeojonPdfWpf'
$shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) '더죤 PDF Workspace.lnk'
# Snapshot production metadata read-only; fixture never writes it.
$beforeRegistry=if(Test-Path $key){Get-ItemProperty $key | ConvertTo-Json -Depth 5 -Compress}else{'absent'}
$beforeShortcut=if(Test-Path $shortcut){(Get-FileHash $shortcut).Hash}else{'absent'}
New-Item -ItemType Directory (Join-Path $payload 'nested 한글') | Out-Null
Set-Content (Join-Path $payload 'Workspace.App.exe') 'fixture only, never executed'
Set-Content (Join-Path $payload 'nested 한글/file with spaces.txt') 'owned'
Set-Content (Join-Path $payload 'Workspace.App.dll') 'managed fixture, never loaded'
$owned=@(Get-ChildItem $payload -File -Recurse | ForEach-Object {[ordered]@{path=[IO.Path]::GetRelativePath($payload,$_.FullName);sha256=(Get-FileHash $_.FullName).Hash}})
ConvertTo-Json -InputObject $owned | Set-Content (Join-Path $payload 'shell-files.json') -Encoding utf8
Write-PayloadIncludes $payload $stage
$shortcutRoot=Join-Path $root 'isolated shortcuts'
$iconArgs=@()
if($AppIconPath){$iconArgs=@('/DAPP_ICON='+(NsisQuote (SafePath $AppIconPath)))}
& $MakeNsis /NOCONFIG /INPUTCHARSET UTF8 /DFIXTURE @iconArgs "/DFIXTURE_SHORTCUT_ROOT=$(NsisQuote $shortcutRoot)" "/DOUTPUT_EXE=$(NsisQuote $exe)" "/DSTAGE=$(NsisQuote $stage)" "$PSScriptRoot/Shell.nsi" 2>&1 | Set-Content (Join-Path $stage 'compile.log')
if($LASTEXITCODE -ne 0){throw 'Fixture compile failed.'}
function Run-Setup([string]$Path,[string]$Destination){$p=Start-Process -FilePath $Path -ArgumentList @('/S',"/D=$Destination") -PassThru -Wait;return $p.ExitCode}
if((Run-Setup $exe $install) -ne 0 -or !(Test-Path (Join-Path $install 'nested 한글/file with spaces.txt')) -or (Test-Path ($install+'.staging'))){throw 'Atomic initial publication failed.'}
$version=[Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
if($version.ProductName -ne '더죤 PDF Workspace' -or $version.FileDescription -ne '더죤 PDF Workspace'){throw 'Installer visible branding mismatch.'}
$wsh=New-Object -ComObject WScript.Shell
foreach($folder in @('Desktop','StartMenu')){
 $linkPath=Join-Path (Join-Path $shortcutRoot $folder) '더죤 PDF Workspace.lnk'
 if(!(Test-Path -LiteralPath $linkPath)){throw 'Required branded shortcut missing.'}
 $link=$wsh.CreateShortcut($linkPath)
 if($link.TargetPath -ne (Join-Path $install 'Workspace.App.exe') -or $link.IconLocation -ne ((Join-Path $install 'Workspace.App.exe')+',0')){throw 'Shortcut target/icon mismatch.'}
 [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)
}
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($wsh)
Set-Content (Join-Path $install 'user document.pdf') 'untouched document sentinel'
Set-Content (Join-Path $install 'unknown.txt') 'unknown sentinel'
$baseline=@(Get-ChildItem $install -Recurse -File | Sort-Object FullName | ForEach-Object {(Get-FileHash $_.FullName).Hash}) -join ','
$lock=[IO.File]::Open((Join-Path $install 'Workspace.App.exe'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
try {if((Run-Setup $exe $install) -ne 20){throw 'Locked-shell gate failed.'}}finally{$lock.Dispose()}
if((Run-Setup $exe $install) -ne 0){throw 'Transactional upgrade after app exit failed.'}
$after=@(Get-ChildItem $install -Recurse -File | Sort-Object FullName | ForEach-Object {(Get-FileHash $_.FullName).Hash}) -join ','
if($baseline -ne $after){throw 'Rejected upgrade changed installed files.'}
# Inject a first-file extraction failure in a separate fresh candidate, never an existing app.
$failureExe=Join-Path $root 'failure-fixture.exe'
& $MakeNsis /NOCONFIG /INPUTCHARSET UTF8 /DFIXTURE @iconArgs /DFAIL_AFTER_FIRST "/DOUTPUT_EXE=$(NsisQuote $failureExe)" "/DSTAGE=$(NsisQuote $stage)" "$PSScriptRoot/Shell.nsi" 2>&1 | Set-Content (Join-Path $stage 'failure-compile.log')
if($LASTEXITCODE -ne 0){throw 'Failure fixture compile failed.'}
$failed=Join-Path $root 'failed install'
if((Run-Setup $failureExe $failed) -ne 44 -or (Test-Path $failed) -or !(Test-Path ($failed+'.staging'))){throw 'Incomplete candidate was published.'}
$orphanBefore=@(Get-ChildItem ($failed+'.staging') -File -Recurse | ForEach-Object {(Get-FileHash $_.FullName).Hash}) -join ','
if((Run-Setup $exe $failed) -ne 0){throw 'Interrupted staging recovery and fresh installation failed.'}
if(!(Get-ChildItem $root -Directory | Where-Object {$_.Name -like 'failed install.staging.abandoned-*'})){throw 'Interrupted staging was not preserved.'}
$p=Start-Process -FilePath (Join-Path $install 'Uninstall.exe') -ArgumentList @('/S',"_?=$install") -PassThru -Wait
if($p.ExitCode -ne 0 -or (Test-Path (Join-Path $install 'Workspace.App.exe'))){throw 'Exact-owned removal failed.'}
if((Get-Content (Join-Path $install 'user document.pdf') -Raw).Trim() -ne 'untouched document sentinel' -or (Get-Content (Join-Path $install 'unknown.txt') -Raw).Trim() -ne 'unknown sentinel'){throw 'Unknown files damaged.'}
$afterRegistry=if(Test-Path $key){Get-ItemProperty $key | ConvertTo-Json -Depth 5 -Compress}else{'absent'}
$afterShortcut=if(Test-Path $shortcut){(Get-FileHash $shortcut).Hash}else{'absent'}
if($beforeRegistry -ne $afterRegistry -or $beforeShortcut -ne $afterShortcut){throw 'Existing product metadata changed.'}
[ordered]@{compiled=$true;unicodeProductMetadataVerified=$true;desktopAndStartMenuShortcutTargetAndIconVerified=$true;initialCandidatePublished=$true;lockedShellBlocked=$true;transactionalUpgradeSucceeded=$true;failureAfterFirstCandidateFile=$true;interruptedStageRecoveredAndPreserved=$true;unknownAndPdfPreserved=$true;existingRegistryAndShortcutPreserved=$true;coreUpgradeSupported=$true;upgradeSuccess='passed';journalRecovery='see Test-CoreDeployment transition matrix';interactiveGuiTested=$false;realAppInstalled=$false;root=$root} | ConvertTo-Json | Tee-Object (Join-Path $root 'fixture-result.json')



