$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'bootstrap.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src/FortniteChestTracker/FortniteChestTracker.csproj'
$destination = Join-Path $repoRoot 'dist/win-x64'
dotnet publish $project -c Release -r win-x64 --self-contained true -o $destination -p:PublishSingleFile=true -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Ejecutable: $destination/FortniteChestTracker.exe" -ForegroundColor Green
