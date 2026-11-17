$ErrorActionPreference = 'Stop'
$work = Join-Path $PSScriptRoot ('checks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
try {
  '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content (Join-Path $work 'NuGet.Config')
  '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><AssemblyName>Distribution.Checks</AssemblyName><Version>1.0.0</Version><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="../ModuleDistributionService.cs"/><Compile Include="../ModuleResourcePolicy.cs"/><Compile Include="../ManifestSignaturePolicy.cs"/><Compile Include="Program.cs"/></ItemGroup></Project>' | Set-Content (Join-Path $work 'Checks.csproj')
  New-Item -ItemType Directory (Join-Path $work 'fixture') | Out-Null
  '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework><AssemblyName>Distribution.Checks</AssemblyName><Version>1.0.0</Version></PropertyGroup></Project>' | Set-Content (Join-Path $work 'fixture/Fixture.csproj')
  'public class Fixture {}' | Set-Content (Join-Path $work 'fixture/Fixture.cs')
  & dotnet build (Join-Path $work 'fixture/Fixture.csproj') --nologo --verbosity quiet
  if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed' }
  & dotnet build (Join-Path $work 'fixture/Fixture.csproj') --nologo --verbosity quiet -p:Version=1.0.1 -o (Join-Path $work 'fixture-v2')
  if ($LASTEXITCODE -ne 0) { throw 'Updated fixture build failed' }
  Copy-Item (Join-Path $PSScriptRoot 'Checks.cs.txt') (Join-Path $work 'Program.cs')
  & dotnet run --project (Join-Path $work 'Checks.csproj') -- $work
  if ($LASTEXITCODE -ne 0) { throw 'Distribution checks failed' }
} finally {
  # This fresh generated harness is wholly owned by this invocation, not the module cache.
  Remove-Item $work -Recurse -Force
}
