param([Parameter(Mandatory)][string]$TemporaryDirectory)
$ErrorActionPreference='Stop'
$base=Join-Path $TemporaryDirectory ('wpf-upgrade-'+[guid]::NewGuid().ToString('N')); New-Item -ItemType Directory $base | Out-Null
$engine=Join-Path $PSScriptRoot 'Install-WpfPreview.ps1'
$ps=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$local=Join-Path $base 'local'; New-Item -ItemType Directory $local | Out-Null
$root=Join-Path $local 'DeojonPdfDotNetTest3'; $prior=$env:LOCALAPPDATA
function Assert($v,$why){if(!$v){throw $why}}
function Payload($v){$p=Join-Path $base $v; New-Item -ItemType Directory $p | Out-Null; "fixture $v" | Set-Content (Join-Path $p 'DeojonPdfDotNetTest3.exe'); 'old' | Set-Content (Join-Path $p 'obsolete.txt'); $files=@(Get-ChildItem $p -File | ForEach-Object{@{Path=$_.Name;Sha256=(Get-FileHash $_.FullName).Hash}}); @{Schema=1;ProductId='DeojonPdfDotNetTest3-WpfPreview';Version=$v;Files=$files}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $p 'installed-files.json'); return $p}
function Run($p,$fault='', $success=$true){$a=@('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$engine,'-Payload',$p); if($fault){$a+=@('-Fault',$fault)}; & $ps @a | Add-Content (Join-Path $base 'runs.log'); Assert (($LASTEXITCODE -eq 0) -eq $success) "Unexpected result $LASTEXITCODE fault=$fault"}
try {
 $env:LOCALAPPDATA=$local; $p1=Payload '0.3.0'; $p2=Payload '0.3.1'
 New-Item -ItemType Directory (Join-Path $local 'DeojonPdfDotNetTest2')|Out-Null; 'TEST2 untouched'|Set-Content (Join-Path $local 'DeojonPdfDotNetTest2\keep.txt')
 Run $p1
 'user PDF bytes'|Set-Content (Join-Path $root 'user.pdf'); $userHash=(Get-FileHash (Join-Path $root 'user.pdf')).Hash
 $oldHash=(Get-FileHash (Join-Path $root 'DeojonPdfDotNetTest3.exe')).Hash
 foreach($f in @('AfterStage','AfterBackup','AfterActivate')){Run $p2 $f $false; Assert ((Get-FileHash (Join-Path $root 'DeojonPdfDotNetTest3.exe')).Hash -eq $oldHash) "Rollback failed $f"; Assert (!(Test-Path ($root+'.transaction.json'))) 'Unresolved journal'}
 'modified app'|Set-Content (Join-Path $root 'DeojonPdfDotNetTest3.exe'); Run $p2 '' $false; Assert ((Get-Content (Join-Path $root 'DeojonPdfDotNetTest3.exe')) -eq 'modified app') 'Modified file lost'; Copy-Item (Join-Path $p1 'DeojonPdfDotNetTest3.exe') $root -Force
 $m=New-Object Threading.Mutex($false,'Local\DeojonPdfDotNetTest3.Lifetime'); $null=$m.WaitOne(0); try{Run $p2 '' $false}finally{$m.ReleaseMutex();$m.Dispose()}
 Run $p2; Assert ((Get-FileHash (Join-Path $root 'user.pdf')).Hash -eq $userHash) 'User document lost'; Run $p1 '' $false
 Assert ((Get-Content (Join-Path $local 'DeojonPdfDotNetTest2\keep.txt')) -eq 'TEST2 untouched') 'TEST2 changed'
 $bad=Payload '0.3.2'; 'tampered'|Set-Content (Join-Path $bad 'DeojonPdfDotNetTest3.exe'); Run $bad '' $false
 $traversal=Payload '0.3.3'; $j=Get-Content (Join-Path $traversal 'installed-files.json') -Raw|ConvertFrom-Json; $j.Files[0].Path='..\escaped.txt'; $j|ConvertTo-Json -Depth 5|Set-Content (Join-Path $traversal 'installed-files.json'); Run $traversal '' $false
 [ordered]@{Passed=$true;Checks=@('fresh fake install','upgrade preserves user document','rollback AfterStage/AfterBackup/AfterActivate','modified collision refused and preserved','running-app gate refused','downgrade rejected','tampered payload refused','traversal refused','TEST2 untouched');InstalledOnDesktop=$false;Stage=$base}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $base 'result.json')
 Write-Output "PASS: $base"
}finally{$env:LOCALAPPDATA=$prior}
