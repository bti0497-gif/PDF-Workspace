param([string]$OutputDirectory = (Join-Path $PSScriptRoot '.test-results'), [string]$BuildRoot = '')
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Push-Location $PSScriptRoot
try {
    $buildArguments = @('build','Workspace.sln','-c','Debug','--nologo')
    $shellDirectory = Join-Path $PSScriptRoot 'src/Workspace.Shell/bin/Debug/net9.0-windows'
    $checksDirectory = Join-Path $PSScriptRoot 'src/Workspace.Checks/bin/Debug/net9.0'
    if ($BuildRoot) {
        $BuildRoot = [IO.Path]::GetFullPath($BuildRoot)
        $buildArguments += "-p:WorkspaceBuildRoot=$BuildRoot"
        $shellDirectory = Join-Path $BuildRoot 'Workspace.Shell/Debug/net9.0-windows'
        $checksDirectory = Join-Path $BuildRoot 'Workspace.Checks/Debug/net9.0'
    }
    & dotnet @buildArguments | Tee-Object (Join-Path $OutputDirectory 'build.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Foundation build failed' }
    dotnet (Join-Path $checksDirectory 'Workspace.Checks.dll') | Tee-Object (Join-Path $OutputDirectory 'checks.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Foundation checks failed' }
    dotnet (Join-Path $shellDirectory 'Workspace.Shell.dll') --smoke $OutputDirectory
    if ($LASTEXITCODE -ne 0) {
        if (Test-Path (Join-Path $OutputDirectory 'failure.txt')) { Get-Content (Join-Path $OutputDirectory 'failure.txt') }
        throw 'WPF module smoke check failed'
    }
    $result = Get-Content (Join-Path $OutputDirectory 'smoke.json') -Raw | ConvertFrom-Json
    if (!$result.Passed) { throw 'Missing successful WPF verification result' }
    Write-Output 'PASS: build, command/loader checks, offscreen WPF module/view checks. Not PDF or physical-display acceptance.'
} finally { Pop-Location }
