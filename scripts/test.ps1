$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'bootstrap.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src/FortniteChestTracker/FortniteChestTracker.csproj'
# Keep CI test output visible even though the user-facing release is WinExe (no console).
$priorConsoleTest = $env:FCT_CONSOLE_TEST
try {
    $env:FCT_CONSOLE_TEST = '1'
    dotnet run --project $project -- --self-test
    $testExit = $LASTEXITCODE
}
finally {
    if ($null -eq $priorConsoleTest) {
        Remove-Item Env:FCT_CONSOLE_TEST -ErrorAction SilentlyContinue
    } else {
        $env:FCT_CONSOLE_TEST = $priorConsoleTest
    }
}
exit $testExit
