<#
.SYNOPSIS
    Watches for .cs and .xaml file changes and re-runs the TimeGuard UI tests automatically.

.DESCRIPTION
    Monitors src/ and tests/ directories for changes. On each save, waits 1 second
    for any burst of changes to settle, then runs `dotnet test tests/TimeGuard.UITests`.
    Prints PASS in green or FAIL in red. Press Ctrl+C to stop.

.PARAMETER Password
    Your TimeGuard parent password. Passed to the tests via TIMEGUARD_TEST_PASSWORD
    so they can unlock the settings window. If omitted, defaults to "test123" (suitable
    for a fresh DB where the tests seeded that password themselves).

.EXAMPLE
    .\watch.ps1 -Password myRealPassword
    .\watch.ps1 -Project tests\TimeGuard.Tests   # watch unit tests instead
#>

param(
    [string]$Project  = "tests\TimeGuard.UITests",
    [string]$WatchDir = $PSScriptRoot,
    [string]$Password = ""
)

$ErrorActionPreference = "Continue"

if ($Password) {
    $env:TIMEGUARD_TEST_PASSWORD = $Password
    Write-Host "🔑 Using supplied password." -ForegroundColor DarkGray
} elseif ($env:TIMEGUARD_TEST_PASSWORD) {
    Write-Host "🔑 Using TIMEGUARD_TEST_PASSWORD from environment." -ForegroundColor DarkGray
} else {
    Write-Host "🔑 No password supplied — tests will seed 'test123' into the temp DB." -ForegroundColor DarkGray
}

Write-Host "👁  Watching for changes in $WatchDir" -ForegroundColor Cyan
Write-Host "   Project: $Project" -ForegroundColor Cyan
Write-Host "   Press Ctrl+C to stop.`n" -ForegroundColor Cyan

# Build once up front so --no-build works in the watch loop (faster)
Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Building solution..." -ForegroundColor Yellow
dotnet build "$WatchDir" -v q 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Initial build failed — fix errors then re-run watch.ps1" -ForegroundColor Red
    exit 1
}
Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Build OK. Watching...`n" -ForegroundColor Green

# ── File system watcher ──────────────────────────────────────────────────────
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path                = $WatchDir
$watcher.IncludeSubdirectories = $true
$watcher.EnableRaisingEvents = $true
$watcher.NotifyFilter        = [System.IO.NotifyFilters]::LastWrite -bor
                               [System.IO.NotifyFilters]::FileName

# Thread-safe queue to collect change events
$queue = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()

$handler = {
    $path = $Event.SourceEventArgs.FullPath
    if ($path -match '\.(cs|xaml)$') {
        $queue.Enqueue($path)
    }
}

$changedSub = Register-ObjectEvent $watcher Changed -Action $handler
$renamedSub = Register-ObjectEvent $watcher Created -Action $handler

# ── Main loop ────────────────────────────────────────────────────────────────
try {
    $lastRunTime = [DateTime]::MinValue

    while ($true) {
        $item = $null
        if ($queue.TryDequeue([ref]$item)) {
            # Debounce: wait 1 s for any burst to settle, drain the rest of the queue
            Start-Sleep -Milliseconds 1000
            $drained = $null
            while ($queue.TryDequeue([ref]$drained)) { }

            # Avoid double-trigger within 2 s
            if (([DateTime]::Now - $lastRunTime).TotalSeconds -lt 2) {
                continue
            }
            $lastRunTime = [DateTime]::Now

            $shortPath = $item.Replace($WatchDir, "").TrimStart('\')
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Changed: $shortPath" -ForegroundColor Cyan
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Building..." -ForegroundColor Yellow

            # Incremental build
            dotnet build "$WatchDir" -v q 2>&1 | Out-Null
            if ($LASTEXITCODE -ne 0) {
                Write-Host "[$(Get-Date -Format 'HH:mm:ss')] ❌ BUILD FAILED" -ForegroundColor Red
                continue
            }

            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Running tests..." -ForegroundColor Yellow
            $output = dotnet test "$WatchDir\$Project" --no-build --logger "console;verbosity=normal" 2>&1

            if ($LASTEXITCODE -eq 0) {
                Write-Host "[$(Get-Date -Format 'HH:mm:ss')] ✅ PASS`n" -ForegroundColor Green
            }
            else {
                Write-Host "[$(Get-Date -Format 'HH:mm:ss')] ❌ FAIL" -ForegroundColor Red
                Write-Host ($output | Select-String "Failed|Error|Exception" | Out-String) -ForegroundColor Red
                Write-Host ""
            }
        }
        Start-Sleep -Milliseconds 100
    }
}
finally {
    Unregister-Event -SourceIdentifier $changedSub.Name -ErrorAction SilentlyContinue
    Unregister-Event -SourceIdentifier $renamedSub.Name -ErrorAction SilentlyContinue
    $watcher.EnableRaisingEvents = $false
    $watcher.Dispose()
    Write-Host "`nWatcher stopped." -ForegroundColor Cyan
}
