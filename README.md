# TimeGuard

A lightweight Windows parental screen-time manager that runs silently at startup, enforces per-app and overall daily usage limits, restricts apps to allowed time-of-day windows, warns the user 5 minutes before expiry, kills processes on limit expiry, and blocks relaunches for the rest of the day. Supports mandatory break schedules. Limits reset at midnight.

## Features

- Invisible background service — no taskbar entry, no tray icon
- Per-app daily time limits + overall daily cap
- Time-of-day allowed windows per app
- Mandatory break schedules per app (e.g. 5-min break every 30 min)
- 5-minute warning popup before expiry
- Fullscreen break overlay that counts down and blocks all input
- Blocks and prevents relaunch once limit is hit (friendly "Time's up" message)
- History dashboard with 7-day bar chart (OxyPlot) and per-day drilldown
- Parent-only password-protected settings panel (opened via global hotkey)
- Usage history stored in SQLite (`%AppData%\TimeGuard\timeguard.db`)
- Launches automatically with Windows via registry (`HKCU\Run`)

## Tech Stack

- C# / .NET 8, WPF (Windows only)
- SQLite via `Microsoft.Data.Sqlite` + `Dapper`
- OxyPlot.Wpf for the history dashboard chart
- xUnit for unit tests

## Project Structure

```
TimeGuard/
├── src/
│   ├── TimeGuard.Core/       # Models + Services (no WPF dependency)
│   │   ├── Models/           # AppConfig, AppRule, DailyLog, UsageEntry
│   │   └── Services/         # DatabaseService, DatabaseMigrator, MonitorService, RulesEngine
│   └── TimeGuard.App/        # WPF entry point
│       ├── Helpers/          # PasswordHelper, StartupHelper, GlobalHotkeyHelper
│       └── UI/               # App.xaml, SettingsWindow, RuleEditWindow, DashboardWindow,
│                             #   BreakOverlay, ProcessPickerWindow, popups
└── tests/
    └── TimeGuard.Tests/      # xUnit tests for RulesEngine + DatabaseService
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

## Data Storage (SQLite)

All data lives in `%AppData%\TimeGuard\timeguard.db`.

| Table | Purpose |
|---|---|
| `Settings` | Key-value store — password hash, hotkey, overall daily cap |
| `AppRules` | Per-app rules: limit, time window, break schedule, enabled flag |
| `DailyUsage` | Per-app usage in minutes per calendar day, blocked flag |
| `Sessions` | Running session tracking used for break timer accumulation |

Schema is created automatically by `DatabaseMigrator` on first run.

## Break Schedule

Each app rule can optionally set:
- **Break every N minutes** — how often a break is required
- **Break duration M minutes** — how long the break must last

When the break is due the app is not killed, but a fullscreen countdown overlay appears that covers all monitors and cannot be dismissed. The break timer resets automatically when the overlay closes.
