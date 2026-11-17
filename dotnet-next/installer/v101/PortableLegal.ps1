# Reads legal entries directly from a pinned, already-open archive. Never extracts archive paths.
Set-StrictMode -Version Latest
function Write-PortableLegal {
 [CmdletBinding()]
 param([Parameter(Mandatory)][IO.Compression.ZipArchive]$Archive,
       [Parameter(Mandatory)][string]$OutputDirectory,
       [Parameter(Mandatory)][string]$ArchiveSha256)
 $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 $selected=[Collections.Generic.List[object]]::new()
 foreach($entry in $Archive.Entries){
  $name=$entry.FullName
  # Validate the entire archive namespace; reject ambiguous or hostile paths even though no path is extracted.
  if(!$name -or $name -match '[\\:\x00-\x1f\x7f]' -or $name.StartsWith('/') -or $name -match '(^|/)\.{1,2}(/|$)' -or $name.Contains('//')){throw "Unsafe portable archive entry: $name"}
  $canonical=$name.TrimEnd('/')
  if(!$seen.Add($canonical)){throw "Duplicate portable archive entry: $name"}
  foreach($part in $canonical.Split('/')){if($part.EndsWith('.') -or $part.EndsWith(' ')){throw "Ambiguous portable archive path: $name"}}
  if(!$entry.Name){continue}
  $kind=if($entry.Name -like 'LICENSE*' -or $entry.Name -like 'OFL-*.txt'){'license'}elseif($entry.Name -like 'NOTICE*' -or $entry.Name -like 'README*'){'notice'}else{$null}
  if($kind){
   if($entry.Length -le 0 -or $entry.Length -gt 1MB){throw "Empty/oversized portable legal entry: $name"}
   $selected.Add([pscustomobject]@{Entry=$entry;Kind=$kind})
  }
 }
 foreach($required in @('LICENSE-MIT','LICENSE-APACHE','LICENSE-document.txt','OFL-nanum-gothic.txt','NOTICE','README.md')){
  if(!($selected | Where-Object {$_.Entry.Name -ceq $required})){throw "Required official legal file missing: $required"}
 }
 $provenance=[Collections.Generic.List[object]]::new();$outputs=[Collections.Generic.List[object]]::new()
 foreach($group in @(@{Kind='license';Name='LICENSE.pdfcraft'},@{Kind='notice';Name='NOTICE.pdfcraft'})){
  $target=Join-Path $OutputDirectory $group.Name
  $out=[IO.FileStream]::new($target,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
  try {
   # Ordinal sorting avoids machine locale-dependent packaging order.
   $names=[string[]]@($selected | Where-Object {$_.Kind -eq $group.Kind} | ForEach-Object {$_.Entry.FullName})
   [Array]::Sort($names,[StringComparer]::Ordinal)
   foreach($name in $names){
    $entry=($selected | Where-Object {$_.Entry.FullName -ceq $name}).Entry
    $input=$entry.Open();$memory=[IO.MemoryStream]::new()
    try{$input.CopyTo($memory);$bytes=$memory.ToArray()}finally{$input.Dispose();$memory.Dispose()}
    if($bytes.LongLength -ne $entry.Length){throw "Archive legal length mismatch: $name"}
    $header=[Text.Encoding]::UTF8.GetBytes("===== $name =====`n")
    $out.Write($header);$offset=$out.Position
    # Preserve the exact entry bytes, including original encoding/BOM, whitespace and line endings.
    $out.Write($bytes);$out.Write([byte[]]@(10,10))
    if($out.Position -gt 1MB){throw "Combined $($group.Name) exceeds backend notice limit (1 MiB)."}
    $provenance.Add([ordered]@{entry=$name;size=$bytes.LongLength;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes));output=$group.Name;contentOffset=$offset})
   }
  }finally{$out.Dispose()}
  $outputs.Add([ordered]@{name=$group.Name;size=(Get-Item -LiteralPath $target).Length;sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash})
 }
 $receipt=[ordered]@{archiveSha256=$ArchiveSha256;selection='Every LICENSE*, OFL-*.txt, NOTICE*, README* basename throughout the archive (case-insensitive)';entries=@($provenance.ToArray());outputs=@($outputs.ToArray())}
 $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'legal-inputs.json') -Encoding utf8NoBOM
 return [pscustomobject]$receipt
}
