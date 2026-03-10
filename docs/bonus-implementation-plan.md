# TimeGuard.Bonus — Implementation Plan

## Overview

**TimeGuard.Bonus** is a companion app (`TimeGuard.Bonus.exe`) that lives alongside `TimeGuard.exe` in the same install folder. When a child is blocked from an app, a **"💡 Earn More Time"** button appears on the block popup. Clicking it launches the Bonus app, where the child completes tasks (math, writing, physical activity) to earn extra minutes. The two apps communicate exclusively through the shared SQLite database — no sockets or pipes.

Full feature spec: [bonus-spec.md](./bonus-spec.md)

---

## Architecture

```
TimeGuard.exe  (existing)
  │  writes Blocked=true to DailyUsage
  │  reads BonusGrants on each 5s MonitorService tick → unblocks entry
  └──► launches TimeGuard.Bonus.exe <processName>
         │  shows task menu → task UI → writes BonusGrant to DB
         └──► DB: BonusGrants row with Applied=0

MonitorService (5s tick)
  └─ ApplyPendingGrants()
       ├─ UsageEntry.BonusMinutesEarned += grant.MinutesGranted
       ├─ mark Applied=1
       └─ if UsageMinutes < DailyLimitMinutes + BonusMinutesEarned → unblock
```

### Dependency rules

| Project | May reference |
|---|---|
| `TimeGuard.Core` | no external change — gains Bonus tables + `BonusService` |
| `TimeGuard.App` | gains launch logic; minimal changes to `BlockedPopup` + `App.xaml.cs` |
| `TimeGuard.Bonus` | references `TimeGuard.Core` only |
| `TimeGuard.Bonus.Tests` | references `TimeGuard.Core` + `TimeGuard.Bonus` services |

### Tech stack for physical activity

| Concern | Library |
|---|---|
| Camera frame capture | `OpenCvSharp4` + `OpenCvSharp4.runtime.win` |
| Pose estimation | MoveNet Lightning ONNX (~3.9 MB) via `Microsoft.ML.OnnxRuntime` |
| WPF preview | `OpenCvSharp4.Extensions` → `BitmapSource` |

**Why this split:** OpenCV is the right tool for camera I/O; OnnxRuntime is the right tool for model inference. Keeps each library doing what it does best.

#### MoveNet Lightning details
- Input: `float32 [1, 192, 192, 3]` — RGB normalised 0–1
- Output: `float32 [1, 1, 17, 3]` — 17 keypoints, each `(y, x, confidence)`
- Keypoint indices used:

| Index | Landmark |
|---|---|
| 5, 6 | Left / Right shoulder |
| 7, 8 | Left / Right elbow |
| 9, 10 | Left / Right wrist |
| 11, 12 | Left / Right hip |
| 13, 14 | Left / Right knee |
| 15, 16 | Left / Right ankle |

#### Rep counting per activity

| Activity | Joints used | Cycle definition |
|---|---|---|
| `jumping_jacks` | wrist → shoulder → hip angle | arms up (> 150°) ↔ arms down (< 60°) |
| `squats` | hip → knee → ankle angle | standing (> 160°) ↔ squat (< 100°) |
| `arm_circles` | wrist position relative to shoulder | full 360° orbital path |

---

## New / Changed Files

### `TimeGuard.Core` changes

| File | Change |
|---|---|
| `Services/DatabaseMigrator.cs` | Add v2 migration: 4 new tables + `BonusMinutesEarned` column on `DailyUsage` |
| `Services/DatabaseService.cs` | Add Bonus CRUD methods (chains, tasks, grants, progress) |
| `Services/BonusService.cs` | **NEW** — chain/task CRUD, `LoadProgress`, `RecordGrant`, `ApplyPendingGrants`, `CanGrant` |
| `Services/MonitorService.cs` | Call `ApplyPendingGrants` on each tick; unblock entry when bonus pushes usage below limit |
| `Services/RulesEngine.cs` | Effective limit = `DailyLimitMinutes + BonusMinutesEarned`; `UsageEntry` gains `BonusMinutesEarned` |
| `Models/AppConfig.cs` | Add `MaxBonusMinutesPerDay` (default 60) |
| `Models/DailyLog.cs` — `UsageEntry` | Add `BonusMinutesEarned` property |

### `TimeGuard.App` changes

| File | Change |
|---|---|
| `UI/BlockedPopup.xaml(.cs)` | Add "💡 Earn More Time" button; hidden when no chain exists for this process |
| `UI/App.xaml.cs` | `OnBlockRequested` launches `TimeGuard.Bonus.exe <processName>` (mutex-guarded) |
| `UI/SettingsWindow.xaml(.cs)` | Add "⭐ Bonus Tasks" tab *(Phase 4)* |

### `TimeGuard.Bonus` — new project (`src/TimeGuard.Bonus/`)

```
App.xaml.cs                   single-instance mutex, parse processName CLI arg
UI/TaskMenuWindow             chain list + step progress; entry point
UI/MathTaskWindow             fullscreen overlay, one problem at a time
UI/WritingTaskWindow          multiline TextBox, live word count
UI/PhysicalTaskWindow         live camera + keypoint overlay + rep counter
UI/ResultWindow               "+N minutes earned!" with Continue/Close
Services/PoseDetector.cs      OpenCV VideoCapture → OnnxRuntime MoveNet → keypoint events
Services/RepCounter.cs        joint-angle cycle detection
Services/MathValidator.cs     date-seeded problem generation + answer checking
Services/WritingValidator.cs  word count + Flesch-Kincaid + sentence count + hash dedup
```

### `TimeGuard.Bonus.Tests` — new project (`tests/TimeGuard.Bonus.Tests/`)

Unit tests for all validator and service logic (no UI, no camera).

---

## DB Schema (v2 additions)

```sql
ALTER TABLE DailyUsage ADD COLUMN BonusMinutesEarned REAL NOT NULL DEFAULT 0;

CREATE TABLE BonusChains (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    DisplayName  TEXT    NOT NULL,
    ProcessName  TEXT    NOT NULL,   -- lowercase no .exe; '*' = all apps
    MaxPerDay    INTEGER NOT NULL DEFAULT 1,
    IsEnabled    INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE BonusTasks (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    ChainId      INTEGER NOT NULL REFERENCES BonusChains(Id) ON DELETE CASCADE,
    StepOrder    INTEGER NOT NULL,
    DisplayName  TEXT    NOT NULL,
    TaskType     TEXT    NOT NULL,   -- 'Math' | 'Writing' | 'Physical' | 'MultipleChoice'
    ConfigJson   TEXT    NOT NULL,
    BonusMinutes INTEGER NOT NULL
);

CREATE TABLE BonusGrants (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    ProcessName    TEXT    NOT NULL,
    TaskId         INTEGER NOT NULL REFERENCES BonusTasks(Id),
    MinutesGranted INTEGER NOT NULL,
    GrantedAt      TEXT    NOT NULL,  -- ISO-8601 UTC
    Applied        INTEGER NOT NULL DEFAULT 0,
    SubmissionHash TEXT    NULL
);

CREATE TABLE BonusChainProgress (
    ChainId           INTEGER NOT NULL REFERENCES BonusChains(Id) ON DELETE CASCADE,
    Date              TEXT    NOT NULL,
    CompletionsToday  INTEGER NOT NULL DEFAULT 0,
    NextStepOrder     INTEGER NOT NULL DEFAULT 1,
    PRIMARY KEY (ChainId, Date)
);
```

---

## Task Type Config JSON

### Math
```json
{
  "difficulty": "medium",
  "operationTypes": ["+", "-", "*"],
  "requiredCorrect": 3,
  "totalProblems": 5
}
```
Validation: server-side arithmetic, UTC-date-seeded `System.Random` so problems change daily. Integer answers must match exactly; floats within ±0.01.

### Writing
```json
{
  "prompt": "Write 3 sentences about what you did outside today.",
  "minWords": 30,
  "requiredKeywords": [],
  "language": "en"
}
```
Validation (three layers, no external deps):
1. Word count ≥ `minWords`
2. Flesch-Kincaid readability score ≥ 30
3. At least 1 sentence-ending character (`.`, `?`, `!`)
4. SHA-256 hash not matching yesterday's submission

### Physical
```json
{
  "activityType": "jumping_jacks",
  "targetReps": 20,
  "holdSeconds": 0,
  "cameraIndex": 0
}
```
Detection: OpenCV frame grab → MoveNet keypoints → joint-angle cycle counting. Camera feed in-memory only; nothing persisted.

---

## Implementation Phases & Task List

### Phase 1 — Foundation

| ID | Task | Depends on |
|---|---|---|
| `p1-db-schema` | DB schema v2 (4 new tables + BonusMinutesEarned) | — |
| `p1-bonus-service` | BonusService in TimeGuard.Core | `p1-db-schema` |
| `p1-monitor-grants` | MonitorService: apply grants on tick + unblock | `p1-bonus-service` |
| `p1-rules-engine` | RulesEngine: effective limit includes BonusMinutesEarned | `p1-bonus-service` |
| `p1-blocked-popup` | BlockedPopup "💡 Earn More Time" button + launch logic | `p1-bonus-service` |
| `p1-bonus-skeleton` | TimeGuard.Bonus project: App.xaml.cs + TaskMenuWindow | `p1-blocked-popup` |

### Phase 2 — Math + Writing Tasks

| ID | Task | Depends on |
|---|---|---|
| `p2-math` | MathValidator + MathTaskWindow | `p1-bonus-skeleton` |
| `p2-writing` | WritingValidator + WritingTaskWindow | `p1-bonus-skeleton` |

### Phase 3 — Physical Activity

| ID | Task | Depends on |
|---|---|---|
| `p3-pose-detector` | PoseDetector: OpenCV capture + OnnxRuntime MoveNet | `p1-bonus-skeleton` |
| `p3-rep-counter` | RepCounter: joint-angle cycle detection | `p3-pose-detector` |
| `p3-physical-ui` | PhysicalTaskWindow: fullscreen camera + overlay | `p3-rep-counter` |

### Phase 4 — Parent Configuration

| ID | Task | Depends on |
|---|---|---|
| `p4-settings-tab` | "⭐ Bonus Tasks" tab in SettingsWindow | `p2-math`, `p2-writing`, `p3-physical-ui` |

### Phase 5 — Tests + CI

| ID | Task | Depends on |
|---|---|---|
| `p5-tests-ci` | TimeGuard.Bonus.Tests + build.yml second artifact | `p4-settings-tab` |

---

## Distribution

`TimeGuard.Bonus` cannot be single-file (OpenCV native DLL ~40 MB). It is published as a folder and zipped separately:

- `TimeGuard-win-x64.zip` — main app (single file, self-contained) — existing
- `TimeGuard-Bonus-win-x64.zip` — bonus app folder — added in Phase 5

Both zips are extracted to the same install directory so `TimeGuard.exe` can find `TimeGuard.Bonus.exe` via `Path.Combine(AppContext.BaseDirectory, "TimeGuard.Bonus.exe")`.

---

## Security Notes

- DB tamper protection is NTFS-permission-based (known limitation; acceptable for home use)
- Minimum engagement times: math 2s/problem, writing real-time word count, physical webcam timer
- Writing dedup via SHA-256 of trimmed lowercase submission stored in `BonusGrants.SubmissionHash`
- Camera: zero frames persisted to disk
- `BonusService.CanGrant()` enforces `MaxBonusMinutesPerDay` global cap before writing any grant
