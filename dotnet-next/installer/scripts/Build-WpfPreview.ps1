[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishedAppDirectory,[Parameter(Mandatory)][string]$PortableDirectory,
 [Parameter(Mandatory)][string]$PortableArchive,[Parameter(Mandatory)][string]$VerificationJson,
 [Parameter(Mandatory)][string]$OutputDirectory,[string]$Version='0.3.0',
 [string]$MakeNsis="$env:LOCALAPPDATA\electron-builder\Cache\nsis-3.0.4.1\nsis-3.0.4.1-1mx3n\makensis.exe",
 [switch]$ReleasePublishConfirmed,[switch]$BackendTestsComplete,[switch]$BuildInstaller)
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid version'}
# Reuse existing offline, pinned-CLI/archive/license/evidence validation unchanged.
# This creates a legacy compiler artifact as intermediate ONLY; neither executable is run.
$args2=@{PublishedAppDirectory=$PublishedAppDirectory;PortableDirectory=$PortableDirectory;PortableArchive=$PortableArchive;VerificationJson=$VerificationJson;OutputDirectory=$OutputDirectory;MakeNsis=$MakeNsis;ReleasePublishConfirmed=$ReleasePublishConfirmed;BackendTestsComplete=$BackendTestsComplete;BuildInstaller=$BuildInstaller}
& (Join-Path $PSScriptRoot 'Build-LocalTest.ps1') @args2
$receipt=Get-ChildItem -LiteralPath $OutputDirectory -Filter 'DeojonPdfDotNetTest2-local-*.exe.receipt.json' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if(!$receipt){throw 'Validated staging receipt missing'}
$r=Get-Content $receipt.FullName -Raw | ConvertFrom-Json
$source=Join-Path $r.Stage 'payload'; $payload=Join-Path $r.Stage 'wpf-preview-payload'
New-Item -ItemType Directory -Path $payload | Out-Null
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $payload -Recurse
Rename-Item (Join-Path $payload 'DeojonPdfDotNetTest2.exe') 'DeojonPdfDotNetTest3.exe'
Remove-Item -LiteralPath (Join-Path $payload 'Remove-OwnedFiles.ps1'),(Join-Path $payload 'installed-files.json')
$files=@(Get-ChildItem -LiteralPath $payload -Recurse -File | ForEach-Object{[ordered]@{Path=[IO.Path]::GetRelativePath($payload,$_.FullName);Sha256=(Get-FileHash $_.FullName).Hash}})
[ordered]@{Schema=1;ProductId='DeojonPdfDotNetTest3-WpfPreview';Version=$Version;Files=$files} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $payload 'installed-files.json') -Encoding utf8
. (Join-Path $PSScriptRoot 'Nsis-Payload.ps1')
$include=Join-Path $r.Stage 'wpf-preview-payload.nsh'; Write-NsisPayloadInclude $payload $include
$installer=Join-Path $OutputDirectory "PDF-Workspace-WPF-$Version-win-x64-setup.exe"
if(Test-Path -LiteralPath $installer){throw 'Refusing overwrite of existing versioned installer'}
$tooling=Split-Path $PSScriptRoot -Parent
& $MakeNsis /NOCONFIG /INPUTCHARSET UTF8 "/DOUTPUT_EXE=$(NsisCompileQuote $installer)" "/DPAYLOAD=$(NsisCompileQuote $payload)" "/DTOOLING=$(NsisCompileQuote $tooling)" "/DPAYLOAD_INCLUDE=$(NsisCompileQuote $include)" (Join-Path $tooling 'WpfPreview.nsi')
if($LASTEXITCODE -ne 0 -or !(Test-Path $installer)){throw 'TEST3 compilation failed'}
[ordered]@{schema=1;channel='wpf-preview';repository='bti0497-gif/PDF-Workspace';version=$Version;tag="wpf-v$Version";installer=[ordered]@{name=[IO.Path]::GetFileName($installer);size=(Get-Item $installer).Length;sha256=(Get-FileHash $installer).Hash.ToLowerInvariant()}} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'wpf-update.json') -Encoding utf8
Write-Output "Built TEST3 only: $installer; not installed, signed, uploaded or published."
