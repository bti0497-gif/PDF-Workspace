# Windows PowerShell 5.1 compatible. Deletes only unchanged, manifest-listed files.
$ErrorActionPreference = 'Stop'
try {
    $root = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'DeojonPdfDotNetTest2'))
    if ([IO.Path]::GetFullPath($PSScriptRoot) -ne $root) { throw 'Unexpected uninstall directory.' }
    function Assert-NoLink([string]$Path) {
        for ($p = $Path; $p; $p = Split-Path $p -Parent) {
            if ((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked path refused: $p" }
        }
    }
    Assert-NoLink $root
    $manifestPath = Join-Path $root 'installed-files.json'; Assert-NoLink $manifestPath
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.ProductId -ne 'DeojonPdfDotNetTest2-Local-2') { throw 'Incorrect uninstall manifest identity.' }
    function Resolve-Owned([string]$Relative) {
        if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':') -or $Relative -match '(^|[\\/])\.\.([\\/]|$)') { throw 'Unsafe manifest path.' }
        $full = [IO.Path]::GetFullPath((Join-Path $root $Relative))
        if (!$full.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escaped installation.' }
        Assert-NoLink $full
        return $full
    }
    # Validate every record before making any change.
    foreach ($file in $manifest.Files) { $null = Resolve-Owned $file.Path; if ($file.Sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Invalid manifest hash.' } }
    foreach ($dir in $manifest.Directories) { $null = Resolve-Owned $dir }
    function File-Sha256([string]$Path) {
        $stream = [IO.File]::OpenRead($Path); $algorithm = [Security.Cryptography.SHA256]::Create()
        try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','') }
        finally { $algorithm.Dispose(); $stream.Dispose() }
    }
    $preserved = 0
    foreach ($file in $manifest.Files) {
        $path = Resolve-Owned $file.Path
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            if ((File-Sha256 $path) -eq $file.Sha256) { Remove-Item -LiteralPath $path -Force }
            else { $preserved++; Write-Output "Preserved modified file: $($file.Path)" }
        }
    }
    foreach ($dir in @($manifest.Directories | Sort-Object Length -Descending)) {
        $path = Resolve-Owned $dir
        if ((Test-Path -LiteralPath $path -PathType Container) -and !(Get-ChildItem -LiteralPath $path -Force | Select-Object -First 1)) { Remove-Item -LiteralPath $path }
    }
    Write-Output "Known unchanged files removed. Modified files preserved: $preserved. Unrecognized user files are never enumerated for deletion."
    exit 0
} catch { Write-Output $_.Exception.Message; exit 1 }
