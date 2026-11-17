[CmdletBinding()]
param([Parameter(Mandatory)][string]$TestRoot)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/ManifestSigning.ps1"
$root=SafePath $TestRoot
if(Test-Path $root){throw 'TestRoot must be new.'}
$output=Join-Path $root 'public-artifacts';$secret=Join-Path $root 'private-fixture'
New-Item -ItemType Directory $output,$secret | Out-Null
$rsa=[Security.Cryptography.RSA]::Create(3072);$private=$null;$protected=$null
try {
 $private=$rsa.ExportPkcs8PrivateKey()
 $entropy=[Text.Encoding]::UTF8.GetBytes('Deojon.PdfWorkspace.WPF.ReleaseSigning.v1')
 $protected=[Security.Cryptography.ProtectedData]::Protect($private,$entropy,[Security.Cryptography.DataProtectionScope]::CurrentUser)
 $keyPath=Join-Path $secret 'fixture.dpapi';[IO.File]::WriteAllBytes($keyPath,$protected)
 $fingerprint=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($rsa.ExportSubjectPublicKeyInfo()))
 $manifest=Join-Path $output 'wpf-modules.json';[IO.File]::WriteAllText($manifest,'{"product":"PDF-Workspace-WPF","version":"1.0.1","api":1,"modules":[]}',[Text.UTF8Encoding]::new($false))
 $receipt=Write-ManifestSignature $manifest $keyPath $fingerprint
 $bytes=[IO.File]::ReadAllBytes($manifest);$signature=[IO.File]::ReadAllBytes($manifest+'.sig')
 $public=[Security.Cryptography.RSA]::Create();$read=0
 try {
  $public.ImportSubjectPublicKeyInfo($rsa.ExportSubjectPublicKeyInfo(),[ref]$read)
  if(!$public.VerifyData($bytes,$signature,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)){throw 'Public-only verification failed.'}
  $bytes[0]=$bytes[0] -bxor 1
  if($public.VerifyData($bytes,$signature,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)){throw 'Tampered manifest accepted.'}
 }finally{$public.Dispose()}
 $bad=Join-Path $root 'wrong-key-manifest.json';Copy-Item $manifest $bad
 $rejected=$false
 try{[void](Write-ManifestSignature $bad $keyPath ('0'*64))}catch{if($_.Exception.Message -notmatch 'fingerprint'){throw};$rejected=$true}
 if(!$rejected -or (Test-Path ($bad+'.sig'))){throw 'Wrong public-key fingerprint was accepted.'}
 $receipt | ConvertTo-Json | Set-Content (Join-Path $output 'signing-receipt.json') -Encoding utf8
 # Public artifacts contain neither the full private DER nor the DPAPI blob, nor private PEM.
 foreach($file in Get-ChildItem $output -File){
  $data=[IO.File]::ReadAllBytes($file.FullName)
  foreach($needle in @($private,$protected)){
   for($i=0;$i -le $data.Length-$needle.Length;$i++){
    $equal=$true;for($j=0;$j -lt $needle.Length;$j++){if($data[$i+$j] -ne $needle[$j]){$equal=$false;break}}
    if($equal){throw 'Private signing material leaked into public artifact.'}
   }
  }
  if([Text.Encoding]::UTF8.GetString($data).Contains('PRIVATE KEY')){throw 'Private PEM leaked into artifact.'}
 }
 [ordered]@{passed=$true;rsaBits=3072;publicOnlyPssSha256Verified=$true;tamperRejected=$true;wrongFingerprintRejected=$true;publicArtifactsContainNoPrivateKey=$true;protectedKeyRemoved=$true;published=$false} | ConvertTo-Json | Tee-Object (Join-Path $root 'test-result.json')
} finally {
 if($private){[Security.Cryptography.CryptographicOperations]::ZeroMemory($private)}
 if($protected){[Security.Cryptography.CryptographicOperations]::ZeroMemory($protected)}
 $rsa.Dispose()
 # Only the generated fixture key, never the parent release key.
 if(Test-Path (Join-Path $secret 'fixture.dpapi')){Remove-Item -LiteralPath (Join-Path $secret 'fixture.dpapi')}
}
