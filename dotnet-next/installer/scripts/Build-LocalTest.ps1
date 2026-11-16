[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$PublishedAppDirectory,
 [Parameter(Mandatory)][string]$PortableDirectory,
 [Parameter(Mandatory)][string]$PortableArchive,
 [Parameter(Mandatory)][string]$VerificationJson,
 [Parameter(Mandatory)][string]$OutputDirectory,
 [string]$MakeNsis = "$env:LOCALAPPDATA\electron-builder\Cache\nsis-3.0.4.1\nsis-3.0.4.1-1mx3n\makensis.exe",
 [switch]$ReleasePublishConfirmed,
 [switch]$BackendTestsComplete,
 [switch]$BuildInstaller
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Deliberately no dotnet publish, restore, install, network or application launch.
if (!$ReleasePublishConfirmed -or !$BackendTestsComplete -or !$BuildInstaller) {
 throw 'BLOCKED: explicit -ReleasePublishConfirmed -BackendTestsComplete -BuildInstaller and passing exact-published-DLL backend/offscreen evidence are all required. No package was built.'
}
$tooling = Split-Path $PSScriptRoot -Parent
$foundation = Split-Path $tooling -Parent
$project = Split-Path $foundation -Parent
function Full([string]$path) { [IO.Path]::GetFullPath($path) }
function SafeTree([string]$path) {
 $full = Full $path
 for ($p=$full; $p; $p=Split-Path $p -Parent) {
  if ((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked path refused: $p" }
 }
 if (Test-Path -LiteralPath $full -PathType Container) {
  if (Get-ChildItem -LiteralPath $full -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Linked payload entries are unsupported.' }
 }
 return $full
}
$publish=SafeTree $PublishedAppDirectory; $portable=SafeTree $PortableDirectory; $output=SafeTree $OutputDirectory
if ($output.StartsWith((Full $project)+'\',[StringComparison]::OrdinalIgnoreCase) -or $output -eq (Full $project)) { throw 'OutputDirectory must be the external artifacts directory, not project source.' }
if ($portable.StartsWith((Full $project)+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Official portable input must remain outside the project.' }
$evidence=Get-Content -LiteralPath $VerificationJson -Raw | ConvertFrom-Json
. (Join-Path $PSScriptRoot 'Assert-BackendEvidence.ps1')
Assert-BackendEvidence -Evidence $evidence -PublishedShellSha256 (Get-FileHash -LiteralPath (Join-Path $publish 'Workspace.Shell.dll')).Hash
foreach ($required in @('Workspace.Shell.exe','Workspace.Shell.dll','Workspace.Shell.runtimeconfig.json','Workspace.Shell.deps.json','modules.json','Modules\Workspace.FoundationModule.dll')) {
 if (!(Test-Path -LiteralPath (Join-Path $publish $required) -PathType Leaf)) { throw "Published application incomplete: $required" }
}
$apphost=[IO.File]::ReadAllBytes((Join-Path $publish 'Workspace.Shell.exe'))
if($apphost.Length -lt 128){throw 'Invalid published apphost.'}
$pe=[BitConverter]::ToInt32($apphost,0x3c)
if($pe -lt 0 -or $pe+6 -gt $apphost.Length -or [BitConverter]::ToUInt32($apphost,$pe) -ne 0x4550 -or [BitConverter]::ToUInt16($apphost,$pe+4) -ne 0x8664){throw 'Published apphost must be Windows x64 PE.'}
$runtime=Get-Content -LiteralPath (Join-Path $publish 'Workspace.Shell.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtime.runtimeOptions.tfm -ne 'net9.0' -or !($runtime.runtimeOptions.frameworks | Where-Object { $_.name -eq 'Microsoft.WindowsDesktop.App' -and $_.version -like '9.0.*' })) { throw 'Expected framework-dependent net9.0 WindowsDesktop publish.' }
$source=Get-Content -LiteralPath (Join-Path $foundation 'src\Workspace.Shell\PdfViewing\ReadOnlyPdfSession.cs') -Raw
$match=[regex]::Match($source,'PinnedHash\s*=\s*"([A-Fa-f0-9]{64})"')
if (!$match.Success) { throw 'Cannot read the application PdfCraft hash pin.' }
$expected=$match.Groups[1].Value
$cli=Join-Path $portable 'pdfcraft-cli.exe'
if ((Get-FileHash -LiteralPath $cli -Algorithm SHA256).Hash -ne $expected) { throw 'PdfCraft CLI hash differs from application pin.' }
$sig=Get-AuthenticodeSignature -LiteralPath $cli
if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Learning Machines Inc') { throw 'PdfCraft Authenticode verification failed.' }
$officialZip='DCBCC2DDC019D85CCA90A8242D5E4DC380F1FF6BFB6B951A89152D1DBAE14CB4'
if ((Get-FileHash -LiteralPath $PortableArchive -Algorithm SHA256).Hash -ne $officialZip) { throw 'Archive hash differs from official evaluated 0.21.1 Windows x64 portable.' }
if (!(Test-Path -LiteralPath $MakeNsis)) { throw 'Local NSIS compiler missing. Nothing is downloaded.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$buildId=(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$stage=Join-Path $output ('.installer-staging-'+$buildId)
$payload=Join-Path $stage 'payload'; New-Item -ItemType Directory -Path $payload -Force | Out-Null
# Retain staging and receipts for inspection; never recursively clean user-supplied directories.
Get-ChildItem -LiteralPath $publish -Force | Copy-Item -Destination $payload -Recurse
Rename-Item -LiteralPath (Join-Path $payload 'Workspace.Shell.exe') -NewName 'DeojonPdfDotNetTest2.exe'
if ((Get-FileHash -LiteralPath (Join-Path $payload 'Workspace.Shell.dll')).Hash -ne $evidence.PublishedShellSha256) { throw 'Staged Shell DLL changed after backend evidence validation.' }
$dependency=Join-Path $payload 'Dependencies\PdfCraft\0.21.1'
if (Test-Path -LiteralPath $dependency) { throw 'Publish unexpectedly already contains PdfCraft; refusing ambiguous provenance.' }
New-Item -ItemType Directory -Path $dependency -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead((Full $PortableArchive))
try {
 $cliEntries=@($zip.Entries | Where-Object { $_.FullName -match '(^|/)pdfcraft-cli\.exe$' })
 if ($cliEntries.Count -ne 1) { throw 'Unexpected portable archive layout.' }
 $prefix=$cliEntries[0].FullName.Substring(0,$cliEntries[0].FullName.Length-'pdfcraft-cli.exe'.Length)
 foreach ($entry in $zip.Entries) {
  if (!$entry.FullName.StartsWith($prefix) -or !$entry.Name) { continue }
  $relative=$entry.FullName.Substring($prefix.Length).Replace('/','\')
  if ($relative -eq 'pdfcraft.exe') { continue } # GUI is not a dependency of the local CLI boundary.
  if ($relative -match '(^|\\)\.\.(\\|$)' -or [IO.Path]::IsPathRooted($relative) -or $relative.Contains(':')) { throw 'Unsafe archive entry.' }
  $origin=Join-Path $portable $relative
  if (!(Test-Path -LiteralPath $origin -PathType Leaf)) { throw "Missing official portable file: $relative" }
  $stream=$entry.Open(); try { $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
  if ((Get-FileHash -LiteralPath $origin -Algorithm SHA256).Hash -ne $hash) { throw "Portable file differs from official archive: $relative" }
  $target=Join-Path $dependency $relative; New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
  Copy-Item -LiteralPath $origin -Destination $target
 }
} finally { $zip.Dispose() }
foreach ($notice in @('LICENSE-MIT','LICENSE-APACHE','LICENSE-document.txt','NOTICE','README.md','OFL-nanum-gothic.txt')) {
 if (!(Test-Path -LiteralPath (Join-Path $dependency $notice))) { throw "Required legal notice missing: $notice" }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Remove-OwnedFiles.ps1') -Destination $payload
Copy-Item -LiteralPath (Join-Path $tooling 'README.ko.md') -Destination (Join-Path $payload 'LOCAL-TEST-README.txt')
[ordered]@{EvidenceScope=$evidence.EvidenceScope;PublishedShellSha256=$evidence.PublishedShellSha256;BackendTestsPassed=$true;OffscreenSmokePassed=$true;MainWorkspaceTestsPassed=$true;MainWorkspaceKind=$evidence.MainWorkspaceKind;NativeUiTested=$false;FidelityAccepted=$false;NativeUiLimitation=$evidence.NativeUiLimitation} | ConvertTo-Json | Set-Content (Join-Path $payload 'EXPERIMENTAL-ACCEPTANCE-SCOPE.json') -Encoding utf8
$files=@(Get-ChildItem -LiteralPath $payload -Recurse -File | ForEach-Object { [ordered]@{Path=[IO.Path]::GetRelativePath($payload,$_.FullName);Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
$dirs=@(Get-ChildItem -LiteralPath $payload -Recurse -Directory | ForEach-Object { [IO.Path]::GetRelativePath($payload,$_.FullName) })
[ordered]@{ProductId='DeojonPdfDotNetTest2-Local-2';BuildId=$buildId;Files=$files;Directories=$dirs} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $payload 'installed-files.json') -Encoding utf8
. (Join-Path $PSScriptRoot 'Nsis-Payload.ps1')
$include=Join-Path $stage 'payload.nsh'
Write-NsisPayloadInclude -PayloadDirectory $payload -IncludePath $include
$installer=Join-Path $output ('DeojonPdfDotNetTest2-local-'+$buildId+'.exe')
& $MakeNsis /NOCONFIG /INPUTCHARSET UTF8 "/DOUTPUT_EXE=$(NsisCompileQuote $installer)" "/DPAYLOAD=$(NsisCompileQuote $payload)" "/DTOOLING=$(NsisCompileQuote $tooling)" "/DPAYLOAD_INCLUDE=$(NsisCompileQuote $include)" (Join-Path $tooling 'LocalTest.nsi') 2>&1 | Tee-Object (Join-Path $stage 'compiler.log')
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $installer)) { throw 'NSIS build failed. Stage retained for inspection.' }
[ordered]@{ProductId='DeojonPdfDotNetTest2-Local-2';Installer=$installer;Sha256=(Get-FileHash -LiteralPath $installer).Hash;PdfCraftSha256=$expected;ArchiveSha256=$officialZip;VerificationSha256=(Get-FileHash -LiteralPath $VerificationJson).Hash;SourcePinSha256=(Get-FileHash -LiteralPath (Join-Path $foundation 'src\Workspace.Shell\PdfViewing\ReadOnlyPdfSession.cs')).Hash;Stage=$stage;Installed=$false;EvidenceScope=$evidence.EvidenceScope;BackendTestsPassed=$true;OffscreenSmokePassed=$true;MainWorkspaceTestsPassed=$true;MainWorkspaceKind=$evidence.MainWorkspaceKind;NativeUiTested=$false;FidelityAccepted=$false;NativeUiLimitation=$evidence.NativeUiLimitation;OtherPcSupport=$false} | ConvertTo-Json | Set-Content ($installer+'.receipt.json') -Encoding utf8
Write-Output "Built only: $installer. Backend/offscreen evidence only. Native UI and fidelity NOT accepted. NOT installed, launched, signed or released."
