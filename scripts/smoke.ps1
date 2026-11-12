$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$exe = Join-Path $repoRoot 'dist/win-x64/FortniteChestTracker.exe'
if (-not (Test-Path $exe)) { throw 'No existe el ejecutable publicado.' }
$isolated = Join-Path ([IO.Path]::GetTempPath()) ('FCT-Smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $isolated | Out-Null
try {
    # The actual downloadable artifact is ONLY the EXE. No adjacent DLLs may be needed.
    $copy = Join-Path $isolated 'FortniteChestTracker.exe'
    Copy-Item -LiteralPath $exe -Destination $copy
    if (@(Get-ChildItem -File $isolated).Count -ne 1) { throw 'La prueba no esta aislada.' }
    Write-Host 'Ejecutando las pruebas desde el ejecutable aislado ...'
    $browserFixture = Join-Path $isolated 'dashboard-browser-test.html'
    $env:FCT_DASHBOARD_TEST_OUTPUT = $browserFixture
    try {
        # WinExe does not open a console; wait for its actual process exit code.
        $self = Start-Process -FilePath $copy -ArgumentList '--self-test' -Wait -PassThru
        if ($self.ExitCode -ne 0) { throw "El EXE aislado no pasa las pruebas (exit $($self.ExitCode))." }
    }
    finally {
        Remove-Item Env:FCT_DASHBOARD_TEST_OUTPUT -ErrorAction SilentlyContinue
    }
    if (-not (Test-Path $browserFixture)) { throw 'No se ha generado el HTML de prueba del navegador.' }
    node (Join-Path $PSScriptRoot 'browser-smoke.cjs') $browserFixture
    if ($LASTEXITCODE -ne 0) { throw 'El navegador no muestra las estadisticas interactivas.' }
    $data = Join-Path $isolated 'data'
    $replays = Join-Path $isolated 'demos'
    New-Item -ItemType Directory -Path $replays | Out-Null
    $onceArgs = '--once --no-browser --demos "' + $replays + '" --data "' + $data + '"'
    $once = Start-Process -FilePath $copy -ArgumentList $onceArgs -Wait -PassThru
    if ($once.ExitCode -ne 0) { throw "El arranque de la aplicacion ha fallado (exit $($once.ExitCode))." }
    if (-not (Test-Path (Join-Path $data 'dashboard.html'))) { throw 'No se ha generado dashboard.html.' }
    if (-not (Test-Path (Join-Path $data 'tracker.db'))) { throw 'No se ha generado tracker.db.' }
    # Start in watch mode and check the process remains running instead of flashing and exiting.
    $watch = Join-Path $isolated 'watch-data'
    $watchArgs = '--watch --headless --no-browser --demos "' + $replays + '" --data "' + $watch + '"'
    $process = Start-Process -FilePath $copy -ArgumentList $watchArgs -PassThru -WorkingDirectory $isolated
    try {
        Start-Sleep -Seconds 4
        $process.Refresh()
        if ($process.HasExited) { throw "La aplicacion se cierra al iniciar (exit $($process.ExitCode))." }
        $liveDashboard = Join-Path $watch 'dashboard.html'
        if (-not (Test-Path $liveDashboard)) { throw 'El modo vigilancia no genera el dashboard.' }
        $liveHtml = Get-Content -LiteralPath $liveDashboard -Raw
        if ($liveHtml -notmatch '"state":"watching"') {
            throw 'El monitor no ha actualizado su estado activo en el dashboard.'
        }
    }
    finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
    Write-Host 'OK: EXE autonomo, SQLite, dashboard y vigilancia comprobados.' -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $isolated -Recurse -Force -ErrorAction SilentlyContinue
}
