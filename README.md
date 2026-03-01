# TimeGuard

A lightweight Windows parental screen-time manager that runs silently at startup, enforces per-app and overall daily usage limits, restricts apps to allowed time-of-day windows, warns the user 5 minutes before expiry, kills processes on limit expiry, and blocks relaunches for the rest of the day. Limits reset at midnight.

## Features

- Invisible background service — no taskbar entry, no tray icon
- Per-app daily time limits + overall daily cap
- Time-of-day allowed windows per app
- 5-minute warning popup before expiry
- Blocks and prevents relaunch once limit is hit (friendly "Time's up" message)
- Parent-only password-protected settings panel (opened via global hotkey)
- Usage history stored as daily JSON log files
- Launches automatically with Windows via registry (`HKCU\Run`)

## Tech Stack

- C# / .NET 8, WPF (Windows only)
- No database — plain JSON files in `%AppData%\TimeGuard\`
- xUnit for unit tests

## Project Structure

```
TimeGuard/
├── src/
│   ├── TimeGuard.Core/       # Models + Services (no WPF dependency)
│   │   ├── Models/           # AppConfig, AppRule, DailyLog, UsageEntry, SessionEntry
│   │   └── Services/         # StorageService, MonitorService, RulesEngine
│   └── TimeGuard.App/        # WPF entry point
│       ├── Helpers/          # PasswordHelper, StartupHelper, GlobalHotkeyHelper
│       └── UI/               # All WPF windows (App.xaml, popups, settings)
└── tests/
    └── TimeGuard.Tests/      # xUnit tests for RulesEngine + StorageService
```

## Build & Run

```bash
# Build entire solution
dotnet build

# Run the app (debug)
dotnet run --project src/TimeGuard.App

# Run all tests
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~RulesEngineTests"

# Run a single test method
dotnet test --filter "FullyQualifiedName~RulesEngineTests.Block_WhenAtLimit"

# Publish as single self-contained .exe
dotnet publish src/TimeGuard.App -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/
```

## First Run

On first launch, a setup wizard prompts for a parent password. After setup, the app runs silently in the background. Open settings at any time with the default hotkey **Ctrl+Alt+Shift+G**.

## Data Files

| Path | Purpose |
|---|---|
| `%AppData%\TimeGuard\config.json` | App rules, password hash, overall cap |
| `%AppData%\TimeGuard\logs\YYYY-MM-DD.json` | Daily usage log per calendar day |
| `%AppData%\TimeGuard\error.log` | Background loop error log |
