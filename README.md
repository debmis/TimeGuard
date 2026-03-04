# 🛡️ TimeGuard

> **Quietly protect your kids' screen time — without the arguments.**

A lightweight Windows parental screen-time manager that runs silently in the background, enforces per-app daily limits, restricts apps to allowed time-of-day windows, enforces mandatory break schedules, and blocks relaunches once limits are hit. Limits reset at midnight. 🌙

---

## ⬇️ Download & Install

### [📦 Download TimeGuard-win-x64.zip](https://github.com/debmis/TimeGuard/raw/master/release/TimeGuard-win-x64.zip)

**Requires:** [.NET 8 Desktop Runtime (Windows x64)](https://dotnet.microsoft.com/download/dotnet/8.0) — free, one-time install.

### 🚀 Quick Start

1. Install **.NET 8 Desktop Runtime** from the link above
2. Download and extract **TimeGuard-win-x64.zip** to a permanent folder, e.g.:
   ```
   C:\Program Files\TimeGuard\
   ```
3. Run **TimeGuard.exe** — a setup wizard will appear to set your parent password 🔐
4. That's it! TimeGuard registers itself to **start automatically with Windows** and runs silently in the background 🎉

> ⚠️ Pick your permanent folder *before* first run. Moving the exe later breaks the auto-start entry (re-register via Settings → Security tab).

### ⌨️ Opening Settings

Press **Ctrl + Alt + Shift + G** at any time to open the parent settings panel (password required).

---

## ✨ Features

- 👻 **Invisible** — no taskbar entry, no tray icon, totally silent
- ⏱️ **Per-app daily time limits** + an overall daily cap across all apps
- 🕐 **Time-of-day windows** — allow apps only between certain hours
- ☕ **Mandatory break schedules** per app (e.g. 5-min break every 30 min)
- ⚠️ **5-minute warning popup** before a limit expires
- 🖥️ **Fullscreen break overlay** with countdown that covers all monitors and blocks all input
- 🚫 **Blocks relaunches** once the limit is hit — with a friendly "Time's up" message
- 📊 **History dashboard** with 7-day bar chart and per-day drilldown
- 🔒 **Password-protected settings** panel opened via global hotkey
- 💾 All usage history stored locally in SQLite (`%AppData%\TimeGuard\timeguard.db`)
- 🪟 Auto-launches with Windows via registry (`HKCU\Run`)

---

## 🔧 Tech Stack

- C# / .NET 8, WPF (Windows only)
- SQLite via `Microsoft.Data.Sqlite` + `Dapper`
- OxyPlot.Wpf for the history dashboard chart
- xUnit for unit tests

## 🗂️ Project Structure

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

---

## 🛠️ Build & Run

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

# Publish as single-file framework-dependent .exe
dotnet publish src/TimeGuard.App -r win-x64 --no-self-contained -p:PublishSingleFile=true -c Release -o publish/
```

## 🗄️ Data Storage (SQLite)

All data lives in `%AppData%\TimeGuard\timeguard.db`.

| Table | Purpose |
|---|---|
| `Settings` | Key-value store — password hash, hotkey, overall daily cap |
| `AppRules` | Per-app rules: limit, time window, break schedule, enabled flag |
| `DailyUsage` | Per-app usage in minutes per calendar day, blocked flag |
| `Sessions` | Running session tracking used for break timer accumulation |

Schema is created automatically by `DatabaseMigrator` on first run.

## ☕ Break Schedule

Each app rule can optionally configure:
- **Break every N minutes** — how often a break is required
- **Break duration M minutes** — how long the break must last

When a break is due, the app isn't killed — instead a fullscreen countdown overlay appears that covers all monitors and cannot be dismissed. The break timer resets automatically when the overlay closes.
