$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$published = Join-Path $repoRoot 'dist/win-x64-portable'
$manifest = Join-Path $published 'SHA256SUMS.txt'
$exe = Join-Path $published 'FortniteChestTracker.exe'

if (-not (Test-Path $exe) -or -not (Test-Path $manifest)) {
    throw 'La distribución estándar o su manifiesto SHA256 no existen.'
}
if (@(Get-ChildItem -LiteralPath $published -File).Count -lt 4) {
    throw 'La distribución estándar debería contener EXE y dependencias .NET por separado.'
}

# Confirm each published file matches its checksum before running it.
$entries = @(Get-Content -LiteralPath $manifest | Where-Object { $_.Trim() -ne '' })
foreach ($line in $entries) {
    if ($line -notmatch '^([a-fA-F0-9]{64})  (.+)$') {
        throw "Línea de hash inválida: $line"
    }
    $file = Join-Path $published $Matches[2]
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Falta $file" }
    $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if ($actual -ne $Matches[1]) { throw "SHA256 incorrecto en $file" }
}

$isolated = Join-Path ([IO.Path]::GetTempPath()) ('FCT-Portable-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $isolated | Out-Null
try {
    $work = Join-Path $isolated 'application'
    New-Item -ItemType Directory -Path $work | Out-Null
    Copy-Item -Path (Join-Path $published '*') -Destination $work -Recurse -Force
    $copy = Join-Path $work 'FortniteChestTracker.exe'

    & $copy --self-test
    if ($LASTEXITCODE -ne 0) { throw "Han fallado los tests del EXE estándar: $LASTEXITCODE" }

    $replays = Join-Path $isolated 'replays'
    $data = Join-Path $isolated 'data'
    New-Item -ItemType Directory -Path $replays | Out-Null
    & $copy --once --no-browser --demos $replays --data $data
    if ($LASTEXITCODE -ne 0) { throw "Ha fallado el arranque estándar: $LASTEXITCODE" }
    if (-not (Test-Path (Join-Path $data 'tracker.db')) -or
        -not (Test-Path (Join-Path $data 'dashboard.html'))) {
        throw 'La distribución estándar no genera sus datos correctamente.'
    }

    Write-Host 'OK: distribución multiarchivo verificada, checksums correctos y arranque funcional.' -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $isolated -Recurse -Force -ErrorAction SilentlyContinue
}
