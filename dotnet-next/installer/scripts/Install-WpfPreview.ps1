# Windows PowerShell 5.1. No network, elevation, registry, recursive deletion or migration.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Payload, [switch]$Update, [int]$WaitPid=0, [switch]$Restart,
 [ValidateSet('','AfterStage','AfterBackup','AfterActivate')][string]$Fault='')
$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest
$identity='DeojonPdfDotNetTest3-WpfPreview'
$root=Join-Path $env:LOCALAPPDATA 'DeojonPdfDotNetTest3'
$journal=$root+'.transaction.json'; $mutex=$null; $held=$false
function NoLink([string]$p) {
 for($a=[IO.Path]::GetFullPath($p);$a;$a=Split-Path $a -Parent) {
  if((Test-Path -LiteralPath $a) -and ((Get-Item -LiteralPath $a -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw "Linked path refused: $a"}
 }
}
function Tree([string]$p) { NoLink $p; if(Test-Path -LiteralPath $p){foreach($f in Get-ChildItem -LiteralPath $p -Force -Recurse){NoLink $f.FullName}} }
function Owned([string]$base,[string]$relative) {
 if(!$relative -or $relative -match '(^|[\\/])\.{1,2}([\\/]|$)|[:*?"<>|]' -or [IO.Path]::IsPathRooted($relative) -or $relative -match '[. ]([\\/]|$)'){throw 'Unsafe ownership path'}
 $p=[IO.Path]::GetFullPath((Join-Path $base $relative)); if(!$p.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Escaped root'}; NoLink $p; return $p
}
function Hash([string]$p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash}
function Manifest([string]$base,[bool]$verify) {
 $m=Get-Content -LiteralPath (Join-Path $base 'installed-files.json') -Raw | ConvertFrom-Json
 if($m.ProductId -ne $identity -or $m.Schema -ne 1 -or $m.Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Wrong TEST3 manifest'}
 $seen=@{}; foreach($f in $m.Files){$p=Owned $base $f.Path; if($seen.ContainsKey($p) -or $f.Path -eq 'installed-files.json' -or $f.Sha256 -notmatch '^[A-Fa-f0-9]{64}$'){throw 'Invalid ownership record'}; $seen[$p]=$true; if($verify -and (!(Test-Path -LiteralPath $p -PathType Leaf) -or (Hash $p) -ne $f.Sha256)){throw "Payload hash failed: $($f.Path)"}}
 if(!$seen.ContainsKey((Join-Path $base 'DeojonPdfDotNetTest3.exe'))){throw 'Missing TEST3 executable ownership'}
 return $m
}
function SaveJournal($j){$tmp=$journal+'.new'; if(Test-Path -LiteralPath $tmp){throw 'Unresolved journal temporary file'}; $j | ConvertTo-Json | Set-Content -LiteralPath $tmp -Encoding UTF8; [IO.File]::Move($tmp,$journal)}
function Recover {
 if(!(Test-Path -LiteralPath $journal)){return}
 $j=Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json
 if($j.ProductId -ne $identity -or $j.Root -ne $root -or $j.Backup -notmatch ([regex]::Escape($root)+'\.backup-[a-f0-9]{32}$') -or $j.Stage -notmatch ([regex]::Escape($root)+'\.stage-[a-f0-9]{32}$')){throw 'Invalid recovery journal; manual review required'}
 Tree $j.Backup; Tree $j.Stage; Tree $root
 # No success was returned before journal removal. Interrupted activation is rolled back.
 if(Test-Path -LiteralPath $j.Backup){
  if(Test-Path -LiteralPath $root){[IO.Directory]::Move($root,$root+'.interrupted-'+[Guid]::NewGuid().ToString('N'))}
  [IO.Directory]::Move($j.Backup,$root)
 } elseif(!$j.HadRoot -and (Test-Path -LiteralPath $root)) {
  [IO.Directory]::Move($root,$root+'.interrupted-'+[Guid]::NewGuid().ToString('N'))
 } elseif($j.HadRoot -and !(Test-Path -LiteralPath $root)){throw 'Recovery cannot find old install; manual review required'}
 [IO.File]::Delete($journal)
}
try {
 NoLink $root; NoLink $journal; NoLink ($journal+'.new')
 if($WaitPid -gt 0){$p=Get-Process -Id $WaitPid -ErrorAction SilentlyContinue; if($p){if(!$p.WaitForExit(60000)){throw 'App did not exit within 60 seconds'}}}
 $mutex=New-Object Threading.Mutex($false,'Local\DeojonPdfDotNetTest3.Lifetime')
 try{$held=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true}
 if(!$held){throw 'TEST3 is running or another update owns the gate'}
 # Also reject non-cooperating directly launched processes of this unique product.
 if(Get-Process -Name 'DeojonPdfDotNetTest3' -ErrorAction SilentlyContinue){throw 'TEST3 is still running'}
 Recover
 $Payload=[IO.Path]::GetFullPath($Payload).TrimEnd('\'); Tree $Payload; Tree $root
 if($Payload -eq $root -or $Payload.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Payload must be external staging'}
 $new=Manifest $Payload $true; $had=Test-Path -LiteralPath $root; $old=$null
 if($Update -and !$had){throw 'UPDATE requires an existing TEST3 installation'}
 if($had){$old=Manifest $root $false; if([version]$new.Version -le [version]$old.Version){throw 'Upgrade must increase version'}}
 # Exact payload set: no files outside manifest except manifest itself.
 $paths=@{}; foreach($f in $new.Files){$paths[(Owned $Payload $f.Path)]=$true}
 foreach($f in Get-ChildItem -LiteralPath $Payload -File -Recurse -Force){if($f.Name -eq 'installed-files.json' -and $f.DirectoryName -eq $Payload){continue}; if(!$paths.ContainsKey($f.FullName)){throw 'Unowned payload file'}}
 $id=[Guid]::NewGuid().ToString('N'); $stage=$root+'.stage-'+$id; $backup=$root+'.backup-'+$id
 New-Item -ItemType Directory -Path $stage | Out-Null
 if($had){Get-ChildItem -LiteralPath $root -Force | Copy-Item -Destination $stage -Recurse -Force}
 $ownedOld=@{}; if($old){foreach($f in $old.Files){$ownedOld[$f.Path]=$f.Sha256}}
 foreach($f in $new.Files){
  $dest=Owned $stage $f.Path
  if(Test-Path -LiteralPath $dest){if(!$ownedOld.ContainsKey($f.Path) -or (Hash $dest) -ne $ownedOld[$f.Path]){throw "Unknown/modified collision preserved; update refused: $($f.Path)"}}
  New-Item -ItemType Directory -Path (Split-Path $dest) -Force | Out-Null
  Copy-Item -LiteralPath (Owned $Payload $f.Path) -Destination $dest -Force
 }
 # Obsolete unchanged files removed only in candidate; modified obsolete files retained.
 if($old){foreach($f in $old.Files){if(!($new.Files | Where-Object Path -eq $f.Path)){$p=Owned $stage $f.Path; if((Test-Path -LiteralPath $p -PathType Leaf) -and (Hash $p) -eq $f.Sha256){Remove-Item -LiteralPath $p}}}}
 Copy-Item -LiteralPath (Join-Path $Payload 'installed-files.json') -Destination $stage -Force
 $null=Manifest $stage $true
 if($Fault -eq 'AfterStage'){throw 'Injected AfterStage'}
 SaveJournal ([ordered]@{ProductId=$identity;Root=$root;Stage=$stage;Backup=$backup;HadRoot=[bool]$had})
 if($had){[IO.Directory]::Move($root,$backup)}
 if($Fault -eq 'AfterBackup'){throw 'Injected AfterBackup'}
 [IO.Directory]::Move($stage,$root)
 if($Fault -eq 'AfterActivate'){throw 'Injected AfterActivate'}
 $null=Manifest $root $true
 [IO.File]::Delete($journal) # commit point; previous complete tree remains retained for recovery
 Write-Output "COMMITTED $($new.Version); previous files retained at $backup"
 $mutex.ReleaseMutex(); $held=$false
 if($Restart){try{Start-Process -FilePath (Join-Path $root 'DeojonPdfDotNetTest3.exe') -WorkingDirectory $root | Out-Null}catch{Write-Output "COMMITTED, but restart failed: $($_.Exception.Message)"; exit 3}}
 exit 0
} catch {
 $reason=$_.Exception.Message
 if($held){try{Recover}catch{Write-Output "RECOVERY BLOCKED: $($_.Exception.Message). Retained files/journal require manual review."}}
 Write-Output "NOT COMMITTED: $reason"; exit 1
} finally {if($held){$mutex.ReleaseMutex()}; if($mutex){$mutex.Dispose()}}
