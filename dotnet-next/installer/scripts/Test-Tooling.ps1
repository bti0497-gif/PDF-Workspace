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
# Independent product identity must be consistent across all production writers/removers.
foreach($file in @('LocalTest.nsi','scripts\Build-LocalTest.ps1','scripts\Remove-OwnedFiles.ps1','scripts\Test-LocalPrerequisite.ps1')) {
 $production=Get-Content (Join-Path $tooling $file) -Raw
 if($production -match 'DeojonPdfDotNetTest(?!2)' -or $production -match 'Deojon PDF \.NET TEST(?! 2)'){throw "Prior-trial identity leaked into TEST 2 tooling: $file"}
 if($production -notmatch 'DeojonPdfDotNetTest2'){throw "Missing TEST 2 identity: $file"}
}
foreach($expected in @('DeojonPdfDotNetTest2-Local-2','Uninstall-DeojonPdfDotNetTest2.exe','Deojon PDF .NET TEST 2.lnk','0.2.0-local-test-2')) {
 if(!$template.Contains($expected)){throw "Missing independent installer identity: $expected"}
}
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
$backendJson='{"EvidenceScope":"exact-published-dll-backend-and-offscreen","ReleasePublish":true,"BackendTestsPassed":true,"OffscreenSmokePassed":true,"MainWorkspaceTestsPassed":true,"MainWorkspaceKind":"real-pdf","NativeUiTested":false,"FidelityAccepted":false,"NativeUiLimitation":"CreateDIBSection blocked native interaction; fidelity not accepted.","PublishedShellSha256":"HASH","BackendOperations":{"open":true,"render":true,"rotate":true,"delete":true,"extract":true,"combine":true,"save-as":true,"original-preservation":true}}'
$backendJson=$backendJson.Replace('HASH',$fixtureHash)
Assert-BackendEvidence -Evidence ($backendJson | ConvertFrom-Json) -PublishedShellSha256 $fixtureHash
$readme=Get-Content (Join-Path $tooling 'README.ko.md') -Raw
$schema=[regex]::Match($readme,'(?s)```json\s*(.*?)```')
if(!$schema.Success){throw 'Documented verification schema missing.'}
$documented=$schema.Groups[1].Value | ConvertFrom-Json
$documented.PublishedShellSha256=$fixtureHash
Assert-BackendEvidence -Evidence $documented -PublishedShellSha256 $fixtureHash
$evidenceChecks=2
foreach($case in @('native-true','native-missing','native-string','fidelity-true','backend-failed','offscreen-failed','wrong-hash','operation-failed','missing-limitation','legacy-ambiguous','main-failed','main-missing','sample-only')) {
 $candidate=$backendJson | ConvertFrom-Json
 switch($case) {
  'main-failed' {$candidate.MainWorkspaceTestsPassed=$false}
  'main-missing' {$candidate.PSObject.Properties.Remove('MainWorkspaceTestsPassed')}
  'sample-only' {$candidate.MainWorkspaceKind='sample'}
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
$oldFakeRoot=Join-Path $fakeLocal 'DeojonPdfDotNetTest'
New-Item -ItemType Directory $oldFakeRoot -Force | Out-Null
'old product and user data must survive' | Set-Content (Join-Path $oldFakeRoot 'old-user-data.txt')
$oldDataHash=(Get-FileHash (Join-Path $oldFakeRoot 'old-user-data.txt')).Hash
# Run prerequisite with only the old sibling present, then with the TEST 2 folder present.
$priorLocal=$env:LOCALAPPDATA
try {
 $env:LOCALAPPDATA=$fakeLocal
 $prereqShell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
 & $prereqShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-LocalPrerequisite.ps1') 2>&1 | Set-Content (Join-Path $stage 'prerequisite-old-sibling.log')
 if($LASTEXITCODE -ne 0){throw 'TEST 1 sibling incorrectly blocked TEST 2 prerequisite, or local .NET 9 runtime missing.'}
} finally { $env:LOCALAPPDATA=$priorLocal }
$fakeRoot=Join-Path $fakeLocal 'DeojonPdfDotNetTest2'
New-Item -ItemType Directory $fakeRoot -Force | Out-Null
try {
 $env:LOCALAPPDATA=$fakeLocal
 & $prereqShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-LocalPrerequisite.ps1') 2>&1 | Set-Content (Join-Path $stage 'prerequisite-existing-test2.log')
 if($LASTEXITCODE -eq 0){throw 'Existing TEST 2 directory was not rejected.'}
} finally { $env:LOCALAPPDATA=$priorLocal }
Copy-Item (Join-Path $PSScriptRoot 'Remove-OwnedFiles.ps1') (Join-Path $fakeRoot 'Remove-OwnedFiles.ps1')
'owned' | Set-Content (Join-Path $fakeRoot 'owned.txt')
'original' | Set-Content (Join-Path $fakeRoot 'modified.txt')
$manifestFiles=@(foreach($name in @('owned.txt','modified.txt')){[ordered]@{Path=$name;Sha256=(Get-FileHash (Join-Path $fakeRoot $name)).Hash}})
[ordered]@{ProductId='DeojonPdfDotNetTest2-Local-2';Files=$manifestFiles;Directories=@()} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $fakeRoot 'installed-files.json')
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
if((Get-FileHash (Join-Path $oldFakeRoot 'old-user-data.txt')).Hash -ne $oldDataHash){throw 'Prior-trial sibling data changed.'}
$cleanupOutput | Set-Content (Join-Path $stage 'cleanup-fixture.log')
[ordered]@{Passed=$true;Checks=@('TEST 2 identities separated from prior trial','Old trial sibling does not block prerequisite; existing TEST 2 folder rejects overwrite','Prior-trial sibling data unchanged after TEST 2 cleanup fixture','All PowerShell files parse','NSIS safe preprocessing passes','Preprocessing produced no executable','Production NSIS template and shared generator compile tiny names-with-spaces/Korean/dollar fixture','Per-user no-association no-auto-launch static policy','Build blocked without backend approval','Truthful native-blocked backend evidence accepted; ambiguous/native/fidelity/failed/mismatched evidence rejected','Manifest cleanup removes owned unchanged fixture and preserves modified/unknown fixtures');EvidenceFixtureChecks=$evidenceChecks;PackageBuilt=$false;FixtureCompiled=$true;FixtureExecuted=$false;FixtureExecutable=$fixtureExe;FixtureSha256=$fixtureSha256;Installed=$false;Compiler=$MakeNsis;Stage=$stage} | ConvertTo-Json | Set-Content (Join-Path $stage 'tooling-check.json') -Encoding utf8
Write-Output "PASS tooling-only checks: $stage. Tiny abort-only compiler fixture built, never executed. No real application package built or installed; not installer/native QA."
