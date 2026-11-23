[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$PublishDirectory,
 [Parameter(Mandatory)][string]$InputManifest,
 [Parameter(Mandatory)][string]$VerificationJson,
 [string]$OfficialCliZip,
 [string]$ExistingEngineDir,
 [Parameter(Mandatory)][string]$OutputDirectory,
 [Parameter(Mandatory)][string]$MakeNsis,
 [string]$ProtectedSigningKeyPath,
 [string]$ExpectedPublicKeySha256,
 [string]$AppIconPath=(Join-Path $PSScriptRoot '../../src/Workspace.App/Assets/deojon-pdf-workspace.ico'),
 [string]$ProductVersion='1.0.2',
 [switch]$DevelopmentNet9
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/PortableLegal.ps1"
. "$PSScriptRoot/ManifestSigning.ps1"
# Packaging and optional detached manifest signing only. Never restore/publish/download/install/Authenticode-sign/launch/upload/tag/commit.
if($ProductVersion -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid ProductVersion.'}
$releaseTag="wpf-v$ProductVersion"
$publish=SafePath $PublishDirectory;$output=SafePath $OutputDirectory
if(Test-Path -LiteralPath $output){throw 'OutputDirectory must be new; existing files are never overwritten.'}
$icon=SafePath $AppIconPath
if(!(Test-Path -LiteralPath $icon -PathType Leaf)){throw 'The approved application icon is required before packaging.'}
$iconBytes=[IO.File]::ReadAllBytes($icon)
if($iconBytes.Length -lt 22 -or [BitConverter]::ToUInt16($iconBytes,0) -ne 0 -or [BitConverter]::ToUInt16($iconBytes,2) -ne 1 -or [BitConverter]::ToUInt16($iconBytes,4) -lt 1){throw 'Invalid Windows ICO asset.'}
$hasZip=![string]::IsNullOrWhiteSpace($OfficialCliZip)
$hasExistingEngine=![string]::IsNullOrWhiteSpace($ExistingEngineDir)
if($hasZip -eq $hasExistingEngine){throw 'Supply exactly one of -OfficialCliZip or -ExistingEngineDir.'}
$hasSigningKey=![string]::IsNullOrWhiteSpace($ProtectedSigningKeyPath)
$hasPublicKeySha=![string]::IsNullOrWhiteSpace($ExpectedPublicKeySha256)
if($hasSigningKey -ne $hasPublicKeySha){throw 'Supply both -ProtectedSigningKeyPath and -ExpectedPublicKeySha256, or omit both for unsigned manifest.'}
$signManifest=$hasSigningKey
$key=$null
if($signManifest){
 $key=SafePath $ProtectedSigningKeyPath
 $sourceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
 foreach($forbidden in @($output,$publish,$sourceRoot)){
  if($key -eq $forbidden -or $key.StartsWith($forbidden.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Protected signing key must be outside source, publish and output trees.'}
 }
 if($ExpectedPublicKeySha256 -notmatch '^[A-Fa-f0-9]{64}$'){throw 'Expected public-key SHA256 required.'}
}
$spec=Get-Content -LiteralPath $InputManifest -Raw | ConvertFrom-Json
$proof=Get-Content -LiteralPath $VerificationJson -Raw | ConvertFrom-Json
if($spec.product -ne 'PDF-Workspace-WPF' -or $spec.version -ne $ProductVersion -or $spec.api -ne 1){throw 'Incorrect product/version/API.'}
if($proof.passed -ne $true -or $proof.inputManifestSha256 -ne (Get-FileHash -LiteralPath $InputManifest).Hash){throw 'Exact input-manifest QA receipt required.'}
foreach($gate in @('shellOnly','startupOwnedModals','moduleIntegration','backend','offscreen','coreUpdateLease')){if($proof.checks.$gate -ne $true){throw "Missing QA gate: $gate"}}
$files=@($spec.shellFiles)
$required=@('Workspace.App.exe','Workspace.App.dll','Workspace.App.deps.json','Workspace.App.runtimeconfig.json','Workspace.Contracts.dll','Workspace.Runtime.dll','Workspace.Desktop.Contracts.dll')
foreach($r in $required){if($r -notin $files.path){throw "Missing shell include: $r"}}
$names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($f in $files){
 $rel=RelativePath $f.path
 if(!$names.Add($rel)){throw "Duplicate shell include $rel"}
 if($rel -match '(?i)(Modules[\\/]|PdfCraft|PdfEngine|ViewerModule|SidebarModule|FoundationModule|\.pdb$)'){throw "Feature/debug file in shell: $rel"}
 if($rel -match '^Workspace\..*\.dll$' -and $rel -notin $required){throw "Non-shell Workspace assembly: $rel"}
 $p=SafePath (Join-Path $publish $rel)
 if($f.sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-FileHash -LiteralPath $p).Hash -ne $f.sha256){throw "Unverified shell input: $rel"}
}
$runtime=Get-Content (Join-Path $publish 'Workspace.App.runtimeconfig.json') -Raw | ConvertFrom-Json
$ro=$runtime.runtimeOptions
if($DevelopmentNet9){if($ro.tfm -ne 'net9.0'){throw 'Development mode requires net9.0.'}}
else {
 if($ro.tfm -ne 'net10.0' -or !$ro.PSObject.Properties['includedFrameworks']){throw 'Formal release requires supported .NET10 self-contained Windows Desktop publish. global.json remains unchanged.'}
 foreach($r in @('coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll','PresentationFramework.dll')){if($r -notin $files.path){throw "Missing self-contained runtime file: $r"}}
 if(!($ro.includedFrameworks | Where-Object {$_.name -eq 'Microsoft.WindowsDesktop.App' -and $_.version -like '10.0.*'})){throw 'Missing .NET10 Windows Desktop runtime.'}
}
foreach($r in @('Workspace.App.dll','Workspace.Contracts.dll','Workspace.Runtime.dll','Workspace.Desktop.Contracts.dll')){if([Reflection.AssemblyName]::GetAssemblyName((Join-Path $publish $r)).Version.ToString() -ne "$ProductVersion.0"){throw "Shell contract assembly not $ProductVersion.0: $r"}}
$zipPin='DCBCC2DDC019D85CCA90A8242D5E4DC380F1FF6BFB6B951A89152D1DBAE14CB4'
$cliPin='32C94DF8EABB995DE8B24B078BF4D51F84A2CEE8B42C868B660017F7E5BBF8D0'
$licensePin='D229F45E1D077B59AC060A91424278B8E921B8F84942A5E9A0CA51D5874B7BE8'
$noticePin='8115BAC8114EC13229FAEEAB5A3BD28F948167FB8251FC04FB35AB9A122D3AC2'
$zip=$null
$existingEngine=$null
if($hasZip){
 $zip=SafePath $OfficialCliZip
 if((Get-FileHash -LiteralPath $zip).Hash -ne $zipPin){throw 'Official PdfCraft 0.21.1 Windows x64 ZIP pin mismatch.'}
} else {
 $existingEngine=SafePath $ExistingEngineDir
 if(!(Test-Path -LiteralPath $existingEngine -PathType Container)){throw 'ExistingEngineDir not found.'}
 $pinMap=@{'pdfengine-pdfcraft-cli.exe'=$cliPin;'pdfengine-LICENSE.pdfcraft'=$licensePin;'pdfengine-NOTICE.pdfcraft'=$noticePin}
 foreach($entry in $pinMap.GetEnumerator()){
  $srcPath=SafePath (Join-Path $existingEngine $entry.Key)
  if(!(Test-Path -LiteralPath $srcPath -PathType Leaf)){throw "Missing pinned engine/legal file in ExistingEngineDir: $($entry.Key)"}
  if((Get-FileHash -LiteralPath $srcPath).Hash -ne $entry.Value){throw "Pinned hash mismatch in ExistingEngineDir: $($entry.Key)"}
 }
}
[void](SafePath $MakeNsis)
if(!(Test-Path -LiteralPath $MakeNsis -PathType Leaf)){throw 'Cached NSIS required; no download performed.'}
New-Item -ItemType Directory $output | Out-Null
$stage=Join-Path $output 'build';$payload=Join-Path $stage 'shell';$assets=Join-Path $output 'assets'
New-Item -ItemType Directory $payload,$assets | Out-Null
foreach($f in $files){$target=Join-Path $payload $f.path;New-Item -ItemType Directory (Split-Path $target) -Force | Out-Null;Copy-Item -LiteralPath (Join-Path $publish $f.path) $target;if((Get-FileHash $target).Hash -ne $f.sha256){throw 'Staged shell hash mismatch.'}}
$spec.shellFiles | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $payload 'shell-files.json') -Encoding utf8
$prefix="https://github.com/bti0497-gif/PDF-Workspace/releases/download/$releaseTag/"
$modules=[Collections.Generic.List[object]]::new();$ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($m in $spec.modules){
 if($m.id -notmatch '^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)*$' -or !$ids.Add($m.id)){throw 'Invalid/duplicate module ID.'}
 $p=SafePath $m.path;$an=[Reflection.AssemblyName]::GetAssemblyName($p)
 if($an.Name -ne $m.assemblyName -or $an.Version.ToString() -ne "$($m.version).0" -or $m.version -notmatch '^\d+\.\d+\.\d+$'){throw "Invalid module identity: $($m.id)"}
 if((Get-FileHash -LiteralPath $p).Hash -ne $m.sha256){throw 'Module bytes differ from verified manifest.'}
 $asset="$($m.assemblyName)-$($m.version).dll";Copy-Item -LiteralPath $p (Join-Path $assets $asset)
 $modules.Add([ordered]@{id=$m.id;version=$m.version;url=$prefix+$asset;size=(Get-Item $p).Length;sha256=$m.sha256;assemblyName=$m.assemblyName;region=$m.region;type=$m.type;files=@()})
}
$engine=@($modules | Where-Object {$_.assemblyName -eq 'Workspace.PdfEngine'})
if($engine.Count -ne 1){throw 'Exactly one PdfEngine module required.'}
$legal=$null
$recordedZipPin=$null
if($hasZip){
 $recordedZipPin=$zipPin
 # Hash and consume the SAME read-locked archive handle, not a later reopened path.
 $zipStream=[IO.FileStream]::new($zip,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 $z=$null
 try {
  $sha=[Security.Cryptography.SHA256]::Create()
  try{$streamHash=([BitConverter]::ToString($sha.ComputeHash($zipStream))).Replace('-','')}finally{$sha.Dispose()}
  if($streamHash -ne $zipPin){throw 'Opened official archive pin mismatch.'}
  $zipStream.Position=0
  $z=[IO.Compression.ZipArchive]::new($zipStream,[IO.Compression.ZipArchiveMode]::Read,$true)
  $legal=Write-PortableLegal -Archive $z -OutputDirectory $stage -ArchiveSha256 $zipPin
  $cli=@($z.Entries | Where-Object {$_.Name -eq 'pdfcraft-cli.exe'})
  if($cli.Count -ne 1){throw 'Ambiguous official CLI archive.'}
  $raw=Join-Path $stage 'pdfcraft-cli.exe';[IO.Compression.ZipFileExtensions]::ExtractToFile($cli[0],$raw)
  if((Get-FileHash $raw).Hash -ne $cliPin){throw 'CLI pin mismatch.'}
  $sig=Get-AuthenticodeSignature -LiteralPath $raw
  if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Learning Machines Inc'){throw 'CLI publisher signature invalid.'}
 } finally {if($z){$z.Dispose()};$zipStream.Dispose()}
} else {
 $reusedOutputs=[Collections.Generic.List[object]]::new()
 foreach($pair in @(@{Src='pdfengine-pdfcraft-cli.exe';Dst='pdfcraft-cli.exe';Pin=$cliPin},@{Src='pdfengine-LICENSE.pdfcraft';Dst='LICENSE.pdfcraft';Pin=$licensePin},@{Src='pdfengine-NOTICE.pdfcraft';Dst='NOTICE.pdfcraft';Pin=$noticePin})){
  $srcPath=SafePath (Join-Path $existingEngine $pair.Src)
  $dstPath=Join-Path $stage $pair.Dst
  Copy-Item -LiteralPath $srcPath $dstPath
  if((Get-FileHash -LiteralPath $dstPath).Hash -ne $pair.Pin){throw "Copied pinned file hash mismatch: $($pair.Dst)"}
  $reusedOutputs.Add([ordered]@{name=$pair.Dst;size=(Get-Item -LiteralPath $dstPath).Length;sha256=$pair.Pin})
 }
 $raw=Join-Path $stage 'pdfcraft-cli.exe'
 $sig=Get-AuthenticodeSignature -LiteralPath $raw
 if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Learning Machines Inc'){throw 'CLI publisher signature invalid.'}
 $legal=[ordered]@{source='existingEngineDir';existingEngineDir=$existingEngine;outputs=@($reusedOutputs.ToArray())}
}
foreach($name in @('pdfcraft-cli.exe','LICENSE.pdfcraft','NOTICE.pdfcraft')){
 $p=Join-Path $stage $name;$asset="$($engine[0].id)-$($engine[0].version)-$name"
 Copy-Item -LiteralPath $p (Join-Path $assets $asset)
 $engine[0].files+= [ordered]@{kind=$(if($name.EndsWith('.exe')){'engine'}else{'notice'});name=$name;url=$prefix+$asset;size=(Get-Item $p).Length;sha256=(Get-FileHash $p).Hash}
}
foreach($m in $modules){foreach($a in @($m)+@($m.files)){if($a.size -le 0 -or $a.size -gt 256MB){throw 'Asset exceeds backend size limits.'}}}
$manifestJson=[ordered]@{product='PDF-Workspace-WPF';version=$ProductVersion;api=1;modules=@($modules.ToArray())} | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText((Join-Path $assets 'wpf-modules.json'),$manifestJson,[Text.UTF8Encoding]::new($false))
$manifestSigning = if($signManifest){
 $sigResult=Write-ManifestSignature -ManifestPath (Join-Path $assets 'wpf-modules.json') -ProtectedSigningKeyPath $key -ExpectedPublicKeySha256 $ExpectedPublicKeySha256
 [ordered]@{manifestSigned=$true;algorithm=$sigResult.algorithm;keyBits=$sigResult.keyBits;publicKeySha256=$sigResult.publicKeySha256;manifestSha256=$sigResult.manifestSha256;signatureSha256=$sigResult.signatureSha256;signatureFile=$sigResult.signatureFile}
} else {
 [ordered]@{manifestSigned=$false;algorithm='none';manifestSha256=(Get-FileHash -LiteralPath (Join-Path $assets 'wpf-modules.json')).Hash}
}
Write-PayloadIncludes $payload $stage
$exe=Join-Path $assets $(if($DevelopmentNet9){"DeojonPdfWpf-$ProductVersion-DEVELOPMENT-net9.exe"}else{'더죤 PDF Workspace.exe'})
& $MakeNsis /NOCONFIG /INPUTCHARSET UTF8 "/DPRODUCT_VERSION=$(NsisQuote $ProductVersion)" "/DOUTPUT_EXE=$(NsisQuote $exe)" "/DSTAGE=$(NsisQuote $stage)" "/DAPP_ICON=$(NsisQuote $icon)" "$PSScriptRoot/Shell.nsi" 2>&1 | Tee-Object (Join-Path $stage 'compiler.log')
if($LASTEXITCODE -ne 0 -or !(Test-Path $exe)){throw 'NSIS compilation failed.'}
$hashes=@(Get-ChildItem $assets -File | Sort-Object Name | ForEach-Object {[ordered]@{name=$_.Name;size=$_.Length;sha256=(Get-FileHash $_.FullName).Hash}})
[ordered]@{product='PDF-Workspace-WPF';version=$ProductVersion;tag=$releaseTag;electronLatest=$false;releaseType='parent-decides';displayName='더죤 PDF Workspace';installerIconSha256=(Get-FileHash -LiteralPath $icon).Hash;formalReleaseEligible=(!$DevelopmentNet9);coreUpgradeSupported=$true;existingInstallPolicy='Journaled directory switch; verified backup retained; unchanged owned manifest and app lease required';manifestSigned=$signManifest;manifestSigning=$manifestSigning;installerSignature=(Get-AuthenticodeSignature $exe).Status.ToString();inputManifestSha256=(Get-FileHash $InputManifest).Hash;verificationSha256=(Get-FileHash $VerificationJson).Hash;cliZipSha256=$recordedZipPin;cliSha256=$cliPin;legalInputs=$legal;assets=$hashes;installed=$false;published=$false} | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $output 'receipt.json') -Encoding utf8
Write-Output ("Built only: $exe. " + $(if($signManifest){'Detached manifest signed.'}else{'Unsigned manifest (HTTPS + SHA-256).'}) + ' No install, upload or Authenticode signing performed.')



