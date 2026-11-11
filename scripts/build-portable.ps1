$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'bootstrap.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src/FortniteChestTracker/FortniteChestTracker.csproj'
$destination = Join-Path $repoRoot 'dist/win-x64-portable'

# Standard .NET folder deployment rather than a self-extracting single-file app.
# Dependencies are individually hashable for security review.
dotnet publish $project -c Release -r win-x64 --self-contained true -o $destination -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$manifest = Join-Path $destination 'SHA256SUMS.txt'
$lines = @(
    Get-ChildItem -LiteralPath $destination -File -Recurse |
        Where-Object { $_.FullName -ne $manifest } |
        Sort-Object FullName |
        ForEach-Object {
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $relative = [IO.Path]::GetRelativePath($destination, $_.FullName).Replace('\', '/')
            "$hash  $relative"
        }
)
$lines | Set-Content -LiteralPath $manifest -Encoding utf8
Write-Host "Publicado en carpeta estándar con checksums: $destination" -ForegroundColor Green
