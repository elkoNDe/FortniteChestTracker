$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'bootstrap.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src/FortniteChestTracker/FortniteChestTracker.csproj'
# Keep CI test output visible even though the user-facing release is WinExe (no console).
dotnet run --project $project -p:OutputType=Exe -- --self-test
exit $LASTEXITCODE
