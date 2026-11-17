[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Recover','Commit')][string]$Mode,
 [Parameter(Mandatory)][string]$InstallDirectory,
 [Parameter(Mandatory)][string]$CandidateDirectory,
 [ValidateSet('None','Prepared','OldMoved','NewMoved','Committed')][string]$InjectAt='None',
 [switch]$SimulateInterruption)
# Windows PowerShell 5.1-compatible transactional directory deployment. No downloads or recursive deletion.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\');$candidate=[IO.Path]::GetFullPath($CandidateDirectory).TrimEnd('\')
if($candidate -ne $root+'.staging'){throw 'Candidate must be the exact adjacent staging directory.'}
$journal=$root+'.update-journal.json';$lockPath=$root+'.update.lock'
$handles=New-Object 'System.Collections.Generic.List[System.IDisposable]'
function Guard([string]$Path){
 for($p=[IO.Path]::GetFullPath($Path);$p;$p=Split-Path $p -Parent){if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked deployment path refused.'}}
 if(Test-Path -LiteralPath $Path -PathType Container){foreach($e in Get-ChildItem -LiteralPath $Path -Force -Recurse){if($e.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked deployment entry refused.'}}}
}
function Relative([string]$Base,[string]$Path){$Path.Substring($Base.Length+1)}
# Avoid module auto-loading differences in NSIS's 32-bit Windows PowerShell child.
function Get-FileHash([string]$LiteralPath,[string]$Algorithm='SHA256'){
 $sha=[Security.Cryptography.SHA256]::Create();$stream=[IO.File]::OpenRead($LiteralPath)
 try{return [pscustomobject]@{Hash=([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','')}}finally{$stream.Dispose();$sha.Dispose()}
}
function Inventory([string]$Path){
 Guard $Path
 if(!(Test-Path -LiteralPath $Path -PathType Container)){throw 'Deployment tree missing.'}
 @(Get-ChildItem -LiteralPath $Path -Force -File -Recurse | Sort-Object FullName | ForEach-Object {[pscustomobject]@{path=(Relative $Path $_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
}
function Matches([string]$Path,$Expected){
 if(!(Test-Path -LiteralPath $Path -PathType Container)){return $false}
 $actual=@(Inventory $Path);$wanted=@($Expected)
 if($actual.Count -ne $wanted.Count){return $false}
 foreach($file in $actual){$found=@($wanted | Where-Object {$_.path -ceq $file.path -and $_.sha256 -eq $file.sha256});if($found.Count -ne 1){return $false}}
 return $true
}
function Write-Journal($Value){
 $temp=$journal+'.new';Guard $temp
 if(Test-Path -LiteralPath $temp){throw 'Unresolved journal temporary file.'}
 $bytes=[Text.Encoding]::UTF8.GetBytes(($Value | ConvertTo-Json -Depth 12 -Compress))
 $stream=New-Object IO.FileStream($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
 try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
 if(Test-Path -LiteralPath $journal){[IO.File]::Replace($temp,$journal,$journal+'.previous-'+[Guid]::NewGuid().ToString('N'))}else{[IO.File]::Move($temp,$journal)}
}
function Recover-Journal {
 # A complete .new journal is durable intent if power failed before first rename/replace.
 if(Test-Path -LiteralPath ($journal+'.new')){
  if(Test-Path -LiteralPath $journal){[IO.File]::Move($journal+'.new',$journal+'.abandoned-'+[Guid]::NewGuid().ToString('N'))}
  else{[IO.File]::Move($journal+'.new',$journal)}
 }
 if(!(Test-Path -LiteralPath $journal)){return}
 $j=Get-Content -LiteralPath $journal -Raw -Encoding UTF8 | ConvertFrom-Json
 if($j.product -ne 'PDF-Workspace-WPF' -or $j.root -ne $root -or $j.candidate -ne $candidate -or $j.backup -notmatch ('^'+[regex]::Escape($root)+'\.backup-[a-f0-9]{32}$') -or $j.failed -notmatch ('^'+[regex]::Escape($root)+'\.failed-[a-f0-9]{32}$')){throw 'Invalid deployment journal; no recovery mutation allowed.'}
 foreach($p in @($j.backup,$j.failed,$root,$candidate)){Guard $p}
 if($j.state -eq 'Committed'){
  if(!(Matches $root $j.newFiles)){throw 'Committed deployment changed; recovery blocked.'}
  [IO.File]::Move($journal,$journal+'.completed-'+[Guid]::NewGuid().ToString('N'));return
 }
 if($j.state -notin @('Prepared','OldMoved','NewMoved','RollingBack')){throw 'Unknown journal state.'}
 $j.state='RollingBack';Write-Journal $j
 if($j.hadOld){
  if(Test-Path -LiteralPath $j.backup){
   if(!(Matches $j.backup $j.oldFiles)){throw 'Backup changed; recovery blocked.'}
   if(Test-Path -LiteralPath $root){
    if(!(Matches $root $j.newFiles) -or (Test-Path -LiteralPath $j.failed)){throw 'Unexpected live tree; recovery blocked.'}
    [IO.Directory]::Move($root,$j.failed)
   }
   [IO.Directory]::Move($j.backup,$root)
  }elseif(!(Matches $root $j.oldFiles)){throw 'Original installation unavailable; recovery blocked.'}
 }elseif(Test-Path -LiteralPath $root){
  if(!(Matches $root $j.newFiles) -or (Test-Path -LiteralPath $j.failed)){throw 'Unexpected initial tree; recovery blocked.'}
  [IO.Directory]::Move($root,$j.failed)
 }
 # Retain candidate and failed bytes for provenance. Never destroy an unverified directory.
 if(Test-Path -LiteralPath $candidate){[IO.Directory]::Move($candidate,$candidate+'.abandoned-'+[Guid]::NewGuid().ToString('N'))}
 [IO.File]::Move($journal,$journal+'.rolledback-'+[Guid]::NewGuid().ToString('N'))
}
function Lock-Tree([string]$Path){
 if(!(Test-Path -LiteralPath $Path)){return}
 Guard $Path
 foreach($file in Get-ChildItem -LiteralPath $Path -Force -File -Recurse){
  # App lifetime/update lock prevents launches; deny writes but permit verification reads and rename.
  $access=[IO.FileAccess]::Read;$share=[IO.FileShare]::Read -bor [IO.FileShare]::Delete

  $handles.Add((New-Object IO.FileStream($file.FullName,[IO.FileMode]::Open,$access,$share)))
 }
}
function Check-Manifest([string]$Path,[switch]$Exact){
 $manifest=Join-Path $Path 'shell-files.json'
 if(!(Test-Path -LiteralPath $manifest)){throw 'Owned shell manifest missing; upgrade cannot safely proceed.'}
 $items=Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json;$names=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
 foreach($f in $items){
  if(!$f.path -or [IO.Path]::IsPathRooted($f.path) -or $f.path -match '(^|[\\/])\.\.?([\\/]|$)|[:\x00-\x1f]' -or !$names.Add($f.path)){throw 'Unsafe owned manifest.'}
  $p=Join-Path $Path $f.path;Guard $p
  if(!(Test-Path -LiteralPath $p -PathType Leaf) -or (Get-FileHash -LiteralPath $p).Hash -ne $f.sha256){throw 'Owned shell file changed or missing; no replacement allowed.'}
 }
 foreach($required in @('Workspace.App.exe','Workspace.App.dll')){if(!$names.Contains($required)){throw 'Required shell file missing from ownership manifest.'}}
 [void]$names.Add('shell-files.json');[void]$names.Add('Uninstall.exe')
 if($Exact){foreach($f in Inventory $Path){if(!$names.Contains($f.path)){throw 'Unexpected candidate payload.'}}}
 return ,$names
}
function Transition([string]$State){
 $script:j.state=$State;Write-Journal $script:j
 if($InjectAt -eq $State){if($SimulateInterruption){$script:interrupted=$true};throw ('Injected transition: '+$State)}
}
Guard $root;Guard $candidate;Guard $journal;Guard $lockPath
$parent=Split-Path $root -Parent;if(!(Test-Path -LiteralPath $parent)){[void][IO.Directory]::CreateDirectory($parent)}
$gate=$null;$interrupted=$false;$j=$null
try {
 $gate=New-Object IO.FileStream($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 Recover-Journal
 if($Mode -eq 'Recover'){
  if(Test-Path -LiteralPath $candidate){[IO.Directory]::Move($candidate,$candidate+'.abandoned-'+[Guid]::NewGuid().ToString('N'))}
  return
 }
 Lock-Tree $root
 $newOwned=Check-Manifest $candidate -Exact
 $hadOld=Test-Path -LiteralPath $root -PathType Container
 $oldFiles=@()
 if($hadOld){
  $oldOwned=Check-Manifest $root
  $oldFiles=@(Inventory $root)
  foreach($dir in Get-ChildItem -LiteralPath $root -Force -Directory -Recurse){$target=Join-Path $candidate (Relative $root $dir.FullName);if(!(Test-Path -LiteralPath $target)){[void][IO.Directory]::CreateDirectory($target)}}
  foreach($file in $oldFiles){if(!$oldOwned.Contains($file.path)){
   if($newOwned.Contains($file.path) -or (Test-Path -LiteralPath (Join-Path $candidate $file.path))){throw 'Unknown user file collides with candidate; replacement blocked.'}
   Copy-Item -LiteralPath (Join-Path $root $file.path) -Destination (Join-Path $candidate $file.path)
   if((Get-FileHash -LiteralPath (Join-Path $candidate $file.path)).Hash -ne $file.sha256){throw 'User file copy changed.'}
  }}
 }
 Lock-Tree $candidate
 $newFiles=@(Inventory $candidate)
 # Windows refuses directory renames while descendant handles remain open.
 # Release snapshot handles but retain the exclusive application lifetime gate.
 foreach($handle in $handles){$handle.Dispose()};$handles.Clear()
 $id=[Guid]::NewGuid().ToString('N')
 $j=[pscustomobject]@{product='PDF-Workspace-WPF';root=$root;candidate=$candidate;backup=$root+'.backup-'+$id;failed=$root+'.failed-'+$id;hadOld=$hadOld;oldFiles=$oldFiles;newFiles=$newFiles;state='Prepared'}
 Transition 'Prepared'
 if($hadOld){if(!(Matches $root $oldFiles)){throw 'Installed files changed before switch.'};[IO.Directory]::Move($root,$j.backup)}
 Transition 'OldMoved'
 [IO.Directory]::Move($candidate,$root)
 Transition 'NewMoved'
 if(!(Matches $root $newFiles)){throw 'Published candidate verification failed.'}
 Transition 'Committed'
 [IO.File]::Move($journal,$journal+'.completed-'+$id)
} catch {
 $failure=$_.Exception.Message
 foreach($handle in $handles){$handle.Dispose()};$handles.Clear()
 if(!$interrupted -and $gate -and (Test-Path -LiteralPath $journal)){
  try{Recover-Journal}catch{throw ('Deployment failed; recovery remains blocked: '+$_.Exception.Message)}
 }
 throw $failure
} finally {
 foreach($handle in $handles){$handle.Dispose()}
 if($gate){$gate.Dispose()}
}


