$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dependency = Join-Path $repoRoot 'third_party/ChestCounter'
$locked = '1f387987737cfd606611d5b8d294d5db04af9eab'
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Instala Git antes de continuar: https://git-scm.com/' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Instala .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0' }
if (-not (Test-Path (Join-Path $dependency '.git'))) {
    New-Item -ItemType Directory -Path (Split-Path $dependency) -Force | Out-Null
    git clone --filter=blob:none --no-checkout 'https://github.com/LED-N/Fortnite-ChestCounter.git' $dependency
    if ($LASTEXITCODE -ne 0) { throw 'No se ha podido descargar el codigo fuente del lector.' }
}
git -C $dependency fetch --depth=1 origin $locked
if ($LASTEXITCODE -ne 0) { throw 'No se ha podido descargar el commit fijado del lector.' }
git -C $dependency checkout --force --detach $locked
if ($LASTEXITCODE -ne 0) { throw 'No se ha podido fijar la version exacta del lector.' }
$actual = (git -C $dependency rev-parse HEAD).Trim()
if ($actual -ne $locked) { throw "La version del lector no coincide: $actual" }
Write-Host "Codigo fuente verificado en el commit $actual" -ForegroundColor Green
