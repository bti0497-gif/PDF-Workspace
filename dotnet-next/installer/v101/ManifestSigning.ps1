Set-StrictMode -Version Latest
function Write-ManifestSignature {
 [CmdletBinding()]
 param([Parameter(Mandatory)][string]$ManifestPath,
       [Parameter(Mandatory)][string]$ProtectedSigningKeyPath,
       [Parameter(Mandatory)][string]$ExpectedPublicKeySha256)
 if($ExpectedPublicKeySha256 -notmatch '^[A-Fa-f0-9]{64}$'){throw 'Expected SPKI SHA256 must be 64 hexadecimal characters.'}
 $keyPath=SafePath $ProtectedSigningKeyPath
 $private=$null;$protected=$null;$rsa=$null
 try {
  $protected=[IO.File]::ReadAllBytes($keyPath)
  if($protected.Length -gt 64KB -or $protected.Length -lt 32){throw 'Invalid protected signing key size.'}
  $entropy=[Text.Encoding]::UTF8.GetBytes('Deojon.PdfWorkspace.WPF.ReleaseSigning.v1')
  $private=[Security.Cryptography.ProtectedData]::Unprotect($protected,$entropy,[Security.Cryptography.DataProtectionScope]::CurrentUser)
  $rsa=[Security.Cryptography.RSA]::Create();$read=0
  $rsa.ImportPkcs8PrivateKey($private,[ref]$read)
  if($read -ne $private.Length -or $rsa.KeySize -ne 3072){throw 'Release signing requires exactly RSA-3072 PKCS#8 DER.'}
  [Security.Cryptography.CryptographicOperations]::ZeroMemory($private);$private=$null
  $fingerprint=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($rsa.ExportSubjectPublicKeyInfo()))
  if($fingerprint -ne $ExpectedPublicKeySha256){throw 'Signing key does not match the shell public-key fingerprint.'}
  $bytes=[IO.File]::ReadAllBytes($ManifestPath)
  $signature=$rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)
  if(!$rsa.VerifyData($bytes,$signature,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)){throw 'Detached manifest signature verification failed.'}
  $destination=$ManifestPath+'.sig'
  $output=[IO.FileStream]::new($destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
  try{$output.Write($signature);$output.Flush($true)}finally{$output.Dispose()}
  return [pscustomobject]@{algorithm='RSA-PSS-SHA256';keyBits=3072;publicKeySha256=$fingerprint;manifestSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes));signatureSha256=(Get-FileHash -LiteralPath $destination).Hash;signatureFile=[IO.Path]::GetFileName($destination)}
 } finally {
  if($private){[Security.Cryptography.CryptographicOperations]::ZeroMemory($private)}
  if($protected){[Security.Cryptography.CryptographicOperations]::ZeroMemory($protected)}
  if($rsa){$rsa.Dispose()}
 }
}
