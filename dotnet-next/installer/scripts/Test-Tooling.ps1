[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$TemporaryDirectory,
 [string]$MakeNsis = "$env:LOCALAPPDATA\electron-builder\Cache\nsis-3.0.4.1\nsis-3.0.4.1-1mx3n\makensis.exe"
)
$ErrorActionPreference='Stop'
$tooling=Split-Path $PSScriptRoot -Parent
$errorsFound=@()
foreach($file in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1') {
 $tokens=$null; $parseErrors=$null
 $null=[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$parseErrors)
 $errorsFound+=@($parseErrors)
}
if($errorsFound.Count){throw ($errorsFound | Out-String)}
$stage=Join-Path ([IO.Path]::GetFullPath($TemporaryDirectory)) ('nsis-preprocess-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item (Join-Path $tooling 'README.ko.md') (Join-Path $stage 'LOCAL-TEST-README.txt')
'SetOutPath "$INSTDIR"' | Set-Content (Join-Path $stage 'payload.nsh') -Encoding utf8
$out=Join-Path $stage 'MUST-NOT-EXIST.exe'
# SAFEPPO only preprocesses. No installer EXE is created, no payload binary copied or launched.
& $MakeNsis /NOCONFIG /SAFEPPO /INPUTCHARSET UTF8 "/DOUTPUT_EXE=$out" "/DPAYLOAD=$stage" "/DTOOLING=$tooling" "/DPAYLOAD_INCLUDE=$(Join-Path $stage 'payload.nsh')" (Join-Path $tooling 'LocalTest.nsi') 2>&1 | Set-Content (Join-Path $stage 'preprocessed.nsi') -Encoding utf8
if($LASTEXITCODE -ne 0){throw "NSIS preprocess failed; inspect $stage"}
if(Test-Path -LiteralPath $out){throw 'Unexpected compiler output: preprocessing must not build an installer.'}
$template=Get-Content (Join-Path $tooling 'LocalTest.nsi') -Raw
if($template -match 'RMDir\s+/r|HKLM|Software\\Classes|RegisteredApplications|ExecShell|MUI_FINISHPAGE_RUN'){throw 'Unsafe installer behavior found.'}
if($template -notmatch 'RequestExecutionLevel user'){throw 'Installer must remain per-user.'}
# Compile the actual production template with a tiny payload and an unconditional
# abort at installer initialization. Never execute this compiler-only fixture.
. (Join-Path $PSScriptRoot 'Nsis-Payload.ps1')
$fixturePayload=Join-Path $stage 'tiny payload with spaces'
$fixtureNested=Join-Path $fixturePayload 'nested folder 한글'
New-Item -ItemType Directory -Path $fixtureNested -Force | Out-Null
Copy-Item (Join-Path $tooling 'README.ko.md') (Join-Path $fixturePayload 'LOCAL-TEST-README.txt')
'plain fixture' | Set-Content (Join-Path $fixturePayload 'plain.txt')
'spaced fixture' | Set-Content (Join-Path $fixturePayload 'name with spaces.txt')
'literal dollar fixture' | Set-Content (Join-Path $fixtureNested '한글 $literal name.txt')
$fixtureInclude=Join-Path $stage 'tiny-payload.nsh'
Write-NsisPayloadInclude -PayloadDirectory $fixturePayload -IncludePath $fixtureInclude
$generated=Get-Content -LiteralPath $fixtureInclude -Raw
if($generated -match 'File /oname="' -or !$generated.Contains('File "/oname=name with spaces.txt"')) { throw 'Incorrect /oname argument quoting regression.' }
$fixtureScript=Join-Path $stage 'compile-only-fixture.nsi'
$template.Replace('Function .onInit','Function .onInit'+[Environment]::NewLine+'  Abort "Compiler fixture only. Installation is disabled."') | Set-Content -LiteralPath $fixtureScript -Encoding utf8
$fixtureExe=Join-Path $stage 'COMPILER-FIXTURE-DO-NOT-RUN.exe'
& $MakeNsis /NOCONFIG /INPUTCHARSET UTF8 "/DOUTPUT_EXE=$(NsisCompileQuote $fixtureExe)" "/DPAYLOAD=$(NsisCompileQuote $fixturePayload)" "/DTOOLING=$(NsisCompileQuote $tooling)" "/DPAYLOAD_INCLUDE=$(NsisCompileQuote $fixtureInclude)" $fixtureScript 2>&1 | Set-Content (Join-Path $stage 'fixture-compiler.log') -Encoding utf8
if($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $fixtureExe -PathType Leaf)){throw "Tiny NSIS fixture compilation failed; inspect $stage"}
if((Get-Item -LiteralPath $fixtureExe).Length -lt 1024){throw 'Tiny compiler fixture output is unexpectedly empty.'}
$fixtureSha256=(Get-FileHash -LiteralPath $fixtureExe -Algorithm SHA256).Hash
$blocked=$false
try { & (Join-Path $PSScriptRoot 'Build-LocalTest.ps1') -PublishedAppDirectory 'unused' -PortableDirectory 'unused' -PortableArchive 'unused' -VerificationJson 'unused' -OutputDirectory $stage } catch { $blocked=$_.Exception.Message -like 'BLOCKED:*' }
if(!$blocked){throw 'Build gate did not reject missing backend approval.'}
. (Join-Path $PSScriptRoot 'Assert-BackendEvidence.ps1')
$fixtureHash='A'*64
$backendJson='{"EvidenceScope":"exact-published-dll-backend-and-offscreen","ReleasePublish":true,"BackendTestsPassed":true,"OffscreenSmokePassed":true,"NativeUiTested":false,"FidelityAccepted":false,"NativeUiLimitation":"CreateDIBSection blocked native interaction; fidelity not accepted.","PublishedShellSha256":"HASH","BackendOperations":{"open":true,"render":true,"rotate":true,"delete":true,"extract":true,"combine":true,"save-as":true,"original-preservation":true}}'
$backendJson=$backendJson.Replace('HASH',$fixtureHash)
Assert-BackendEvidence -Evidence ($backendJson | ConvertFrom-Json) -PublishedShellSha256 $fixtureHash
$readme=Get-Content (Join-Path $tooling 'README.ko.md') -Raw
$schema=[regex]::Match($readme,'(?s)```json\s*(.*?)```')
if(!$schema.Success){throw 'Documented verification schema missing.'}
$documented=$schema.Groups[1].Value | ConvertFrom-Json
$documented.PublishedShellSha256=$fixtureHash
Assert-BackendEvidence -Evidence $documented -PublishedShellSha256 $fixtureHash
$evidenceChecks=2
foreach($case in @('native-true','native-missing','native-string','fidelity-true','backend-failed','offscreen-failed','wrong-hash','operation-failed','missing-limitation','legacy-ambiguous')) {
 $candidate=$backendJson | ConvertFrom-Json
 switch($case) {
  'native-true' {$candidate.NativeUiTested=$true}
  'native-missing' {$candidate.PSObject.Properties.Remove('NativeUiTested')}
  'native-string' {$candidate.NativeUiTested='false'}
  'fidelity-true' {$candidate.FidelityAccepted=$true}
  'backend-failed' {$candidate.BackendTestsPassed=$false}
  'offscreen-failed' {$candidate.OffscreenSmokePassed=$false}
  'wrong-hash' {$candidate.PublishedShellSha256='B'*64}
  'operation-failed' {$candidate.BackendOperations.rotate=$false}
  'missing-limitation' {$candidate.NativeUiLimitation=''}
  'legacy-ambiguous' {$candidate=[pscustomobject]@{Passed=$true;ReleasePublish=$true;PublishedShellSha256=$fixtureHash}}
 }
 $rejected=$false
 try { Assert-BackendEvidence -Evidence $candidate -PublishedShellSha256 $fixtureHash } catch { $rejected=$true }
 if(!$rejected){throw "Evidence gate accepted invalid fixture: $case"}
 $evidenceChecks++
}
# Exercise cleanup only in a disposable fake LOCALAPPDATA, never the real install directory.
$fakeLocal=Join-Path $stage 'fake-localappdata'
$fakeRoot=Join-Path $fakeLocal 'DeojonPdfDotNetTest'
New-Item -ItemType Directory $fakeRoot -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'Remove-OwnedFiles.ps1') (Join-Path $fakeRoot 'Remove-OwnedFiles.ps1')
'owned' | Set-Content (Join-Path $fakeRoot 'owned.txt')
'original' | Set-Content (Join-Path $fakeRoot 'modified.txt')
$manifestFiles=@(foreach($name in @('owned.txt','modified.txt')){[ordered]@{Path=$name;Sha256=(Get-FileHash (Join-Path $fakeRoot $name)).Hash}})
[ordered]@{ProductId='DeojonPdfDotNetTest-Local-1';Files=$manifestFiles;Directories=@()} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $fakeRoot 'installed-files.json')
'user changed this' | Set-Content (Join-Path $fakeRoot 'modified.txt')
'not owned' | Set-Content (Join-Path $fakeRoot 'unknown.txt')
$start=[Diagnostics.ProcessStartInfo]::new()
$start.FileName=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
foreach($arg in @('-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',(Join-Path $fakeRoot 'Remove-OwnedFiles.ps1'))){$start.ArgumentList.Add($arg)}
$start.Environment['LOCALAPPDATA']=$fakeLocal
$process=[Diagnostics.Process]::Start($start)
if(!$process.WaitForExit(15000)){$process.Kill($true);throw 'Cleanup fixture timed out.'}
$cleanupOutput=$process.StandardOutput.ReadToEnd()+$process.StandardError.ReadToEnd()
if($process.ExitCode -ne 0){throw "Cleanup fixture failed: $cleanupOutput"}
$process.Dispose()
if((Test-Path (Join-Path $fakeRoot 'owned.txt')) -or !(Test-Path (Join-Path $fakeRoot 'modified.txt')) -or !(Test-Path (Join-Path $fakeRoot 'unknown.txt'))){throw 'Cleanup did not preserve unknown/modified files.'}
$cleanupOutput | Set-Content (Join-Path $stage 'cleanup-fixture.log')
[ordered]@{Passed=$true;Checks=@('All PowerShell files parse','NSIS safe preprocessing passes','Preprocessing produced no executable','Production NSIS template and shared generator compile tiny names-with-spaces/Korean/dollar fixture','Per-user no-association no-auto-launch static policy','Build blocked without backend approval','Truthful native-blocked backend evidence accepted; ambiguous/native/fidelity/failed/mismatched evidence rejected','Manifest cleanup removes owned unchanged fixture and preserves modified/unknown fixtures');EvidenceFixtureChecks=$evidenceChecks;PackageBuilt=$false;FixtureCompiled=$true;FixtureExecuted=$false;FixtureExecutable=$fixtureExe;FixtureSha256=$fixtureSha256;Installed=$false;Compiler=$MakeNsis;Stage=$stage} | ConvertTo-Json | Set-Content (Join-Path $stage 'tooling-check.json') -Encoding utf8
Write-Output "PASS tooling-only checks: $stage. Tiny abort-only compiler fixture built, never executed. No real application package built or installed; not installer/native QA."
