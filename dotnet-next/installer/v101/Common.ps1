# Shared strictly enumerated NSIS include generation. No wildcard File/Delete/RMDir /r.
Set-StrictMode -Version Latest
function SafePath([string]$p) {
 $p=[IO.Path]::GetFullPath($p)
 for($q=$p;$q;$q=Split-Path $q -Parent){if((Test-Path -LiteralPath $q) -and ((Get-Item -LiteralPath $q -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw "Reparse path refused: $q"}}
 return $p
}
function RelativePath([string]$p) {
 if(!$p -or $p -match '(^|[\\/])\.\.?([\\/]|$)|[:\r\n"$*?<>|]' -or [IO.Path]::IsPathRooted($p) -or $p.EndsWith('.') -or $p.EndsWith(' ')){throw "Unsafe relative path: $p"}
 return $p.Replace('/','\')
}
function NsisQuote([string]$p){if($p -match '[\r\n"$]'){throw "Unsupported NSIS characters: $p"};return $p}
function Write-PayloadIncludes([string]$Payload,[string]$Stage){
 $checks=[Collections.Generic.List[string]]::new();$install=[Collections.Generic.List[string]]::new();$remove=[Collections.Generic.List[string]]::new();$dirs=[Collections.Generic.HashSet[string]]::new()
 foreach($f in Get-ChildItem -LiteralPath $Payload -File -Recurse | Sort-Object FullName){
  $relative=RelativePath ([IO.Path]::GetRelativePath($Payload,$f.FullName));$dir=Split-Path $relative -Parent
  $install.Add('SetOutPath "$INSTDIR'+$(if($dir){'\'+$dir}else{''})+'"')
  $install.Add('File "/oname='+(NsisQuote $f.Name)+'" "'+(NsisQuote $f.FullName)+'"')
  if($remove.Count -eq 0){
   $install.Add('!ifdef FIXTURE')
   $install.Add('!ifdef FAIL_AFTER_FIRST')
   $install.Add('SetErrorLevel 44')
   $install.Add('Abort "Injected candidate extraction failure."')
   $install.Add('!endif')
   $install.Add('!endif')
  }
  $checks.Add('System::Call ''kernel32::GetFileAttributesW(w "$INSTDIR\'+$relative+'") i.r0''')
  $checks.Add('${If} $0 != -1')
  $checks.Add('IntOp $0 $0 & 0x400')
  $checks.Add('${If} $0 != 0')
  $checks.Add('SetErrorLevel 22')
  $checks.Add('Abort "Linked payload paths are not supported."')
  $checks.Add('${EndIf}')
  $checks.Add('${EndIf}')
  $remove.Add('Delete "$INSTDIR\'+$relative+'"')
  for($d=$dir;$d;$d=Split-Path $d -Parent){[void]$dirs.Add($d)}
 }
 foreach($d in $dirs | Sort-Object Length -Descending){$remove.Add('RMDir "$INSTDIR\'+$d+'"')}
 foreach($d in $dirs){
  $checks.Add('System::Call ''kernel32::GetFileAttributesW(w "$INSTDIR\'+$d+'") i.r0''')
  $checks.Add('${If} $0 != -1')
  $checks.Add('IntOp $0 $0 & 0x400')
  $checks.Add('${If} $0 != 0')
  $checks.Add('SetErrorLevel 22')
  $checks.Add('Abort "Linked payload directories are not supported."')
  $checks.Add('${EndIf}')
  $checks.Add('${EndIf}')
 }
 $checks | Set-Content (Join-Path $Stage 'check.nsh') -Encoding utf8
 $install | Set-Content (Join-Path $Stage 'install.nsh') -Encoding utf8
 $remove | Set-Content (Join-Path $Stage 'remove.nsh') -Encoding utf8
}


