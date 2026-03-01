## Build, Test, and Run

```bash
# Build
dotnet build

# Run app
dotnet run --project src/TimeGuard.App

# Run all tests
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~RulesEngineTests"

# Run a single test method
dotnet test --filter "FullyQualifiedName~RulesEngineTests.Block_WhenAtLimit"

# Publish single-file .exe
dotnet publish src/TimeGuard.App -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/
```

## Architecture

**Two-project split:** `TimeGuard.Core` (no WPF dependency — models + services) and `TimeGuard.App` (WPF entry point + UI). Tests only reference `TimeGuard.Core`.

**Monitor loop** (`MonitorService`) runs on a background `Task`, polling every 5 seconds. It accumulates session time into today's `DailyLog` and fires events (`BlockRequested`, `WarnRequested`, `BreakRequested`) back to the UI thread. `App.xaml.cs` handles those events — it kills the process and shows the appropriate popup, or shows the `BreakOverlay`.

**Pure evaluation:** `RulesEngine.Evaluate()` has no side effects — it takes current state and returns a list of `RuleAction` records. This makes it fully unit-testable without mocking.

**Storage:** All data lives in `%AppData%\TimeGuard\timeguard.db` (SQLite). `DatabaseMigrator` creates the schema on first run. `DatabaseService` (Dapper) handles all reads/writes — no flat files.

**No tray icon, no main window.** The app runs as `OutputType=WinExe` with `ShutdownMode=OnExplicitShutdown`. A hidden zero-size helper window hosts the Win32 `RegisterHotKey` WndProc.

## Key Conventions

- Process names are stored and compared **lowercase without `.exe`** throughout (e.g., `"roblox"` not `"Roblox.exe"`).
- `AppRule.IsWithinAllowedWindow(TimeOnly)` is the single source of truth for time-window logic. Do not duplicate that logic elsewhere.
- `DailyLog.GetOrCreate(processName)` is the only way to access usage entries — it handles case-insensitive lookup and creates missing entries.
- `RulesEngine` never reads from disk or kills processes. All side effects live in `MonitorService` (DB writes) and `App.xaml.cs` (process kill + UI).
- New WPF windows should set `ShowInTaskbar="False"` and use the shared resource styles (`PrimaryButton`, `SecondaryButton`, color brushes) defined in `App.xaml`.
- Password hashing uses PBKDF2/SHA-256 via `PasswordHelper` — never store plaintext or use a weaker hash.
- The `GlobalHotkeyHelper` must be disposed on app exit to release the Win32 hotkey handle.
- `DatabaseService` is the single persistence layer — never bypass it with direct SQLite calls from UI code.
- When `UseWindowsForms=true` is set in the App .csproj (needed for `Screen.AllScreens`), always qualify ambiguous types: `System.Windows.MessageBox`, `System.Windows.Input.KeyEventArgs`, `System.Windows.Application`.
