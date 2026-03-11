<#
.SYNOPSIS
    Publishes TimeGuard as a self-contained single-file exe and packages it
    as release/TimeGuard-win-x64.zip.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -SkipTests
#>

param(
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "`n[1/4] Building solution..." -ForegroundColor Cyan
dotnet build "$root" -c Release -v q
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; exit 1 }

if (-not $SkipTests) {
    Write-Host "[2/4] Running unit tests..." -ForegroundColor Cyan
    dotnet test "$root\tests\TimeGuard.Tests" -c Release --no-build --logger "console;verbosity=normal"
    if ($LASTEXITCODE -ne 0) { Write-Host "Tests failed." -ForegroundColor Red; exit 1 }
} else {
    Write-Host "[2/4] Skipping tests (-SkipTests)." -ForegroundColor DarkGray
}

Write-Host "[3/4] Publishing single-file win-x64..." -ForegroundColor Cyan
$publishDir = Join-Path $root "publish"
dotnet publish "$root\src\TimeGuard.App" `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o "$publishDir" `
    -v q
if ($LASTEXITCODE -ne 0) { Write-Host "Publish failed." -ForegroundColor Red; exit 1 }

Write-Host "[4/4] Packaging release/TimeGuard-win-x64.zip..." -ForegroundColor Cyan
$releaseDir = Join-Path $root "release"
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
$zipPath = Join-Path $releaseDir "TimeGuard-win-x64.zip"
# IncludeNativeLibrariesForSelfExtract bundles all native DLLs into the exe.
# DebugType=None suppresses PDBs. Zip contains only TimeGuard.exe.
Compress-Archive -Path "$publishDir\TimeGuard.exe" -DestinationPath $zipPath -Force

$sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "`n✅ Done!  release\TimeGuard-win-x64.zip  ($sizeMB MB)" -ForegroundColor Green
