[CmdletBinding()]
param([Parameter(Mandatory)][string]$TestRoot,[string]$OfficialCliZip)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/PortableLegal.ps1"
$root=SafePath $TestRoot
if(Test-Path -LiteralPath $root){throw 'TestRoot must be new.'}
New-Item -ItemType Directory $root | Out-Null
function Create-Fixture([string]$Path,[string[]]$Extra){
 $z=[IO.Compression.ZipFile]::Open($Path,[IO.Compression.ZipArchiveMode]::Create)
 try{
  foreach($name in @('portable/LICENSE-MIT','portable/LICENSE-APACHE','portable/LICENSE-document.txt','portable/OFL-nanum-gothic.txt','portable/NOTICE','portable/README.md','portable/OFL-unseen-font-a.txt','portable/fonts/OFL-unseen-font-b.txt','portable/vendor/LICENSE-extra','portable/vendor/NOTICE-extra.txt','portable/vendor/README-extra.txt','portable/not-legal.bin')+$Extra){
   $entry=$z.CreateEntry($name);$stream=$entry.Open()
   try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes("$name`r`n한글 full original text`n  spaces preserved  `r`n");$stream.Write($bytes)}finally{$stream.Dispose()}
  }
 }finally{$z.Dispose()}
}
function Verify-Archive([string]$Path,[string]$Name,[string]$ExpectedHash){
 $output=Join-Path $root $Name;New-Item -ItemType Directory $output | Out-Null
 $stream=[IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read);$z=$null
 try{
  $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))
  if($ExpectedHash -and $hash -ne $ExpectedHash){throw 'Official ZIP pin mismatch.'}
  $stream.Position=0;$z=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Read,$true)
  $receipt=Write-PortableLegal -Archive $z -OutputDirectory $output -ArchiveSha256 $hash
  $expected=@($z.Entries | Where-Object {$_.Name -match '^(LICENSE.*|OFL-.*\.txt|NOTICE.*|README.*)$'})
  if($receipt.entries.Count -ne $expected.Count){throw 'Legal entry coverage mismatch.'}
  foreach($entry in $expected){
   $record=@($receipt.entries | Where-Object {$_.entry -ceq $entry.FullName})
   if($record.Count -ne 1){throw "Missing or duplicate legal entry: $($entry.FullName)"}
   $r=$record[0];$input=$entry.Open();try{$originalHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($input))}finally{$input.Dispose()}
   $bundle=[IO.File]::ReadAllBytes((Join-Path $output $r.output));$preserved=[byte[]]::new($r.size)
   [Array]::Copy($bundle,$r.contentOffset,$preserved,0,$r.size)
   if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($preserved)) -ne $originalHash -or $r.sha256 -ne $originalHash){throw "Original legal bytes changed: $($entry.FullName)"}
   $header=[Text.Encoding]::UTF8.GetBytes("===== $($entry.FullName) =====`n");$actualHeader=[byte[]]::new($header.Length)
   [Array]::Copy($bundle,$r.contentOffset-$header.Length,$actualHeader,0,$header.Length)
   if([Convert]::ToHexString($actualHeader) -ne [Convert]::ToHexString($header)){throw 'Original-name header mismatch.'}
  }
  return [ordered]@{name=$Name;archiveSha256=$hash;legalEntries=$expected.Count;fontLicenses=@($expected | Where-Object {$_.Name -like 'OFL-*.txt'}).Count;everyOriginalByteAndHeaderVerified=$true;outputs=$receipt.outputs}
 }finally{if($z){$z.Dispose()};$stream.Dispose()}
}
$results=[Collections.Generic.List[object]]::new()
$fixture=Join-Path $root 'fixture.zip';Create-Fixture $fixture @();$results.Add((Verify-Archive $fixture 'fixture-output' ''))
foreach($case in @(@{Name='traversal';Entry='portable/../LICENSE-escape'},@{Name='absolute';Entry='/LICENSE-absolute'},@{Name='backslash';Entry='portable\LICENSE-ambiguous'},@{Name='duplicate';Entry='portable/LICENSE-MIT'})){
 $path=Join-Path $root ($case.Name+'.zip');Create-Fixture $path @($case.Entry)
 $rejected=$false
 try{[void](Verify-Archive $path ($case.Name+'-output') '')}catch{if($_.Exception.Message -notmatch 'Unsafe portable archive entry|Duplicate portable archive entry'){throw};$rejected=$true}
 if(!$rejected){throw "Unsafe archive was accepted: $($case.Name)"}
 $results.Add([ordered]@{name=$case.Name;rejected=$true})
}
if($OfficialCliZip){$results.Add((Verify-Archive (SafePath $OfficialCliZip) 'official-output' 'DCBCC2DDC019D85CCA90A8242D5E4DC380F1FF6BFB6B951A89152D1DBAE14CB4'))}
[ordered]@{passed=$true;results=@($results.ToArray());published=$false} | ConvertTo-Json -Depth 10 | Tee-Object (Join-Path $root 'test-result.json')
