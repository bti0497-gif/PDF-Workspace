# Shared by real packaging and the tiny compiler regression fixture.
function NsisCompileQuote([string]$value) {
 if ($value -match '[\r\n"]') { throw 'Unsupported NSIS path characters.' }
 if ($value.Contains('${')) { throw 'NSIS macro-like path characters are unsupported.' }
 $value
}
function NsisQuote([string]$value) { (NsisCompileQuote $value).Replace('$','$$') }
function Write-NsisPayloadInclude {
 [CmdletBinding()]
 param([Parameter(Mandatory)][string]$PayloadDirectory,[Parameter(Mandatory)][string]$IncludePath)
 $lines=foreach($file in Get-ChildItem -LiteralPath $PayloadDirectory -Recurse -File | Sort-Object FullName) {
  $relative=[IO.Path]::GetRelativePath($PayloadDirectory,$file.FullName)
  $dir=Split-Path $relative -Parent
  'SetOutPath "$INSTDIR'+$(if($dir){'\'+(NsisQuote $dir)}else{''})+'"'
  # Quote the whole /oname= argument, not merely its value (NSIS File grammar).
  'File "/oname='+(NsisQuote $file.Name)+'" "'+(NsisCompileQuote $file.FullName)+'"'
 }
 $lines | Set-Content -LiteralPath $IncludePath -Encoding utf8
}
