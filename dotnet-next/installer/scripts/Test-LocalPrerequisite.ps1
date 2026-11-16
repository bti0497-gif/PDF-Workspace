# Windows PowerShell 5.1 compatible. No downloads, registry or installation changes.
$ErrorActionPreference = 'Stop'
try {
    if (-not [Environment]::Is64BitOperatingSystem) { throw 'Windows x64 is required.' }
    $root = Join-Path $env:LOCALAPPDATA 'DeojonPdfDotNetTest'
    for ($p = $env:LOCALAPPDATA; $p; $p = Split-Path $p -Parent) {
        if ((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse-point install ancestors are not supported.' }
    }
    if (Test-Path -LiteralPath $root) { throw "Existing test directory must be reviewed/uninstalled first: $root" }
    $dotnet = Join-Path $env:ProgramW6432 'dotnet\dotnet.exe'
    if (!(Test-Path -LiteralPath $dotnet)) { throw 'Install Microsoft .NET Desktop Runtime 9.0 x64 manually before this local test. No runtime is bundled or downloaded.' }
    $runtimes = & $dotnet --list-runtimes
    if ($LASTEXITCODE -ne 0 -or !($runtimes -match '^Microsoft.WindowsDesktop.App 9\.0\.\d+ ') -or !($runtimes -match '^Microsoft.NETCore.App 9\.0\.\d+ ')) {
        throw 'Microsoft .NET Desktop Runtime 9.0 x64 (including NETCore 9.0) is required. SDK installation is not required. No download is performed.'
    }
    Write-Output 'Runtime prerequisite present; this does not prove PDF compatibility or supported deployment.'
    exit 0
} catch { Write-Output $_.Exception.Message; exit 1 }
