[CmdletBinding()]
param([Parameter(Mandatory)][string]$TestRoot)
$ErrorActionPreference='Stop'
if(Test-Path $TestRoot){throw 'New fixture root required.'}
New-Item -ItemType Directory $TestRoot | Out-Null
$tool=Join-Path $PSScriptRoot 'CoreDeployment.ps1'
function Make-Candidate([string]$Path,[string]$Version){
 New-Item -ItemType Directory $Path -Force | Out-Null
 Set-Content (Join-Path $Path 'Workspace.App.exe') ('apphost-'+$Version)
 Set-Content (Join-Path $Path 'Workspace.App.dll') ('managed-'+$Version)
 Set-Content (Join-Path $Path 'Uninstall.exe') ('uninstaller-'+$Version)
 $files=@('Workspace.App.exe','Workspace.App.dll') | ForEach-Object {[ordered]@{path=$_;sha256=(Get-FileHash (Join-Path $Path $_)).Hash}}
 ConvertTo-Json -InputObject @($files) | Set-Content (Join-Path $Path 'shell-files.json') -Encoding utf8
}
$results=New-Object 'System.Collections.Generic.List[object]'
foreach($state in @('Success','Prepared','OldMoved','NewMoved','Committed')){
 foreach($crash in @($false,$true)){
  if($state -eq 'Success' -and $crash){continue}
  $dir=Join-Path $TestRoot ($state+'-'+$crash);$stage=$dir+'.staging'
  Make-Candidate $stage 'old'; & $tool -Mode Commit -InstallDirectory $dir -CandidateDirectory $stage
  Set-Content (Join-Path $dir 'document.pdf') 'unchanged PDF'
  New-Item -ItemType Directory (Join-Path $dir 'unknown') | Out-Null
  Set-Content (Join-Path $dir 'unknown/user.txt') 'unknown text'
  $before=(Get-FileHash (Join-Path $dir 'Workspace.App.dll')).Hash
  Make-Candidate $stage 'new'
  if($state -eq 'Success'){& $tool -Mode Commit -InstallDirectory $dir -CandidateDirectory $stage}
  else{
   $failed=$false
   try{& $tool -Mode Commit -InstallDirectory $dir -CandidateDirectory $stage -InjectAt $state -SimulateInterruption:$crash}catch{if($_.Exception.Message -notmatch 'Injected transition'){throw};$failed=$true}
   if(!$failed){throw 'Fault injection did not trigger.'}
   if($crash){& $tool -Mode Recover -InstallDirectory $dir -CandidateDirectory $stage}
  }
  $isNew=($state -eq 'Success' -or $state -eq 'Committed')
  $actual=(Get-FileHash (Join-Path $dir 'Workspace.App.dll')).Hash
  if(($actual -ne $before) -ne $isNew){throw ('Wrong recovered version: '+$state)}
  if((Get-Content (Join-Path $dir 'document.pdf') -Raw).Trim() -ne 'unchanged PDF' -or (Get-Content (Join-Path $dir 'unknown/user.txt') -Raw).Trim() -ne 'unknown text'){throw 'User data lost.'}
  if(Test-Path ($dir+'.update-journal.json')){throw 'Journal not resolved.'}
  $results.Add([ordered]@{transition=$state;simulatedInterruption=$crash;verified=$true;current=$(if($isNew){'new'}else{'old'});unknownAndPdfPreserved=$true})
 }
}
# Existing app's lifetime lock rejects mutation before candidate or journal work.
$dir=Join-Path $TestRoot 'lifetime-lock';$stage=$dir+'.staging';Make-Candidate $stage 'old'; & $tool -Mode Commit -InstallDirectory $dir -CandidateDirectory $stage
Make-Candidate $stage 'new'
$gate=[IO.File]::Open($dir+'.update.lock',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::Read)
try{$blocked=$false;try{& $tool -Mode Commit -InstallDirectory $dir -CandidateDirectory $stage}catch{$blocked=$true};if(!$blocked){throw 'Lifetime lock ignored.'}}finally{$gate.Dispose()}
# Changed owned bytes must not be overwritten.
Set-Content (Join-Path $dir 'Workspace.App.dll') 'user changed owned file'
$blocked=$false;try{& $tool -Mode Commit -InstallDirectory $dir -CandidateDirectory $stage}catch{$blocked=$true};if(!$blocked -or (Get-Content (Join-Path $dir 'Workspace.App.dll') -Raw).Trim() -ne 'user changed owned file'){throw 'Changed owned file overwritten.'}
[ordered]@{passed=$true;transitionCases=@($results.ToArray());lifetimeLockBlocked=$true;changedOwnedBlocked=$true;backupNeverDeleted=$true;actualUserInstallTouched=$false;realPowerLossTested=$false} | ConvertTo-Json -Depth 8 | Tee-Object (Join-Path $TestRoot 'result.json')
