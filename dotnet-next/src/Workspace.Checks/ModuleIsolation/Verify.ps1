$ErrorActionPreference='Stop'
$r=$PSScriptRoot
foreach($v in 1,2) {
    dotnet build "$r/Fixture$v/Probe$v.csproj" -o "$r/bin/fixture$v" -v:q
    if($LASTEXITCODE -ne 0) { throw "Fixture $v build failed" }
}
dotnet build "$r/WpfFixture/WpfFixture.csproj" -o "$r/bin/wpf" -v:q
if($LASTEXITCODE -ne 0) { throw 'WPF fixture build failed' }
dotnet run --project "$r/IsolationChecks.csproj" -- "$r/bin/fixture1" "$r/bin/fixture2" "$r/bin/wpf"
if($LASTEXITCODE -ne 0) { throw 'Isolation checks failed' }
