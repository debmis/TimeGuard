# TimeGuard Bonus Tasks — Feature Spec

## 1. Overview

**Bonus Tasks** lets parents configure challenges that children can complete to earn extra screen time on a per-app basis. A blocked child sees a "Earn More Time" button on the block popup; they launch the Bonus app, complete tasks (math, writing, physical), and the main app automatically unlocks additional minutes.

The feature lives in a **separate `TimeGuard.Bonus` executable** that shares the existing SQLite database via a new schema extension — no changes to `MonitorService`'s core logic, minimal changes to the main app.

---

## 2. User Stories

| Actor | Story |
|---|---|
| Parent | I configure bonus task chains for specific apps (e.g. "Roblox bonus tasks") |
| Parent | I set how many minutes each task earns and how many times per day it can be completed |
| Parent | I build rich multi-step challenges: solve problem 1 → earn 5 min → solve problem 2 → earn 5 more min |
| Child | When blocked from an app, I see "Earn More Time" and can complete tasks to keep playing |
| Child | I solve a math problem, write a short paragraph, or do jumping jacks and the app validates my work automatically |
| System | When validation passes, minutes are granted to the blocked app without any parent interaction |

---

## 3. Architecture

### 3.1 Two-process design

```
TimeGuard.exe  (existing)
  │  writes BlockedEvent to DB
  │  reads & applies BonusGrants on each 5s MonitorService poll
  └──► launches TimeGuard.Bonus.exe <processName>
         │  shows task UI
         │  runs validation
         └──► writes BonusGrant to DB
```

- **TimeGuard.Bonus.exe** — new WPF WinExe project (`src/TimeGuard.Bonus`)
- **Communication** — shared SQLite DB, new tables only; no sockets, no named pipes
- **Launch** — `TimeGuard.exe` spawns `TimeGuard.Bonus.exe roblox` when `OnBlockRequested` fires (one instance at a time; if already open, just bring to front via mutex check)
- **Apply** — `MonitorService.Tick()` queries `BonusGrants WHERE applied=0` and calls `DailyLog.AddBonusMinutes(processName, minutes)` then marks applied=1

### 3.2 Dependency rules

| Project | May reference |
|---|---|
| `TimeGuard.Core` | no change — gains new DB tables + `BonusService` |
| `TimeGuard.App` | gains `BonusService` call in `OnBlockRequested`; launches Bonus exe |
| `TimeGuard.Bonus` | references `TimeGuard.Core` only |
| `TimeGuard.UITests` | can test both apps independently |

---

## 4. Data Model

### 4.1 New DB tables (added by `DatabaseMigrator` v2)

```sql
-- A chain is a named sequence of tasks assigned to one or more apps
CREATE TABLE BonusChains (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    DisplayName     TEXT    NOT NULL,
    ProcessName     TEXT    NOT NULL,  -- lowercase, no .exe; '*' = all apps
    MaxPerDay       INTEGER NOT NULL DEFAULT 1,  -- max full-chain completions per calendar day
    IsEnabled       INTEGER NOT NULL DEFAULT 1
);

-- Each step in a chain
CREATE TABLE BonusTasks (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ChainId         INTEGER NOT NULL REFERENCES BonusChains(Id) ON DELETE CASCADE,
    StepOrder       INTEGER NOT NULL,  -- 1-based; tasks unlock in order
    DisplayName     TEXT    NOT NULL,
    TaskType        TEXT    NOT NULL,  -- 'Math' | 'Writing' | 'Physical' | 'MultipleChoice'
    ConfigJson      TEXT    NOT NULL,  -- task-type-specific config (see §5)
    BonusMinutes    INTEGER NOT NULL   -- minutes granted on completion of this step
);

-- One row per grant; MonitorService applies and marks applied=1
CREATE TABLE BonusGrants (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ProcessName     TEXT    NOT NULL,
    TaskId          INTEGER NOT NULL REFERENCES BonusTasks(Id),
    MinutesGranted  INTEGER NOT NULL,
    GrantedAt       TEXT    NOT NULL,  -- ISO-8601 UTC
    Applied         INTEGER NOT NULL DEFAULT 0,
    SubmissionHash  TEXT    NULL       -- SHA-256 of trimmed lowercase text; NULL for non-writing tasks
);

-- Per-chain progress today (resets at midnight like DailyUsage)
CREATE TABLE BonusChainProgress (
    ChainId         INTEGER NOT NULL REFERENCES BonusChains(Id) ON DELETE CASCADE,
    Date            TEXT    NOT NULL,  -- YYYY-MM-DD
    CompletionsToday INTEGER NOT NULL DEFAULT 0,
    NextStepOrder   INTEGER NOT NULL DEFAULT 1,
    PRIMARY KEY (ChainId, Date)
);
```

### 4.2 `DailyLog` change

Add `BonusMinutesEarned INTEGER NOT NULL DEFAULT 0` column to `DailyUsage`.  
`MonitorService` calculates available time as:

```
available = rule.DailyLimitMinutes + bonusMinutesEarned - minutesUsedToday
```

---

## 5. Task Types

### 5.1 Math Challenge

**Config JSON:**
```json
{
  "difficulty": "medium",        // easy | medium | hard
  "operationTypes": ["+", "-", "*"],
  "requiredCorrect": 3,          // must get N correct to earn minutes
  "totalProblems": 5             // show N problems, pick N correct from them
}
```

**Validation:** Server-side (in-process) arithmetic. Problems generated at runtime using `System.Random` seeded from UTC date so problems change daily. Answer must match exactly (integers) or within ±0.01 (floats).

**UI:** Fullscreen overlay (like BreakOverlay), one problem at a time, numeric keyboard input, progress indicator "2 / 3 correct".

---

### 5.2 Writing Prompt

**Config JSON:**
```json
{
  "prompt": "Write 3 sentences about what you did outside today.",
  "minWords": 30,
  "requiredKeywords": [],        // optional: ["outside", "played"] — at least 1 must appear
  "language": "en"
}
```

**Validation:**  
1. Word count ≥ `minWords`  
2. Any `requiredKeywords` present (case-insensitive)  
3. Not a repeat of yesterday's submission (simple hash check against stored submissions)

**UI:** Multi-line `TextBox`, live word count display, "Submit" button activates once threshold met.

---

### 5.3 Physical Activity (Webcam)

**Config JSON:**
```json
{
  "activityType": "jumping_jacks",  // jumping_jacks | squats | arm_circles
  "targetReps": 20,
  "holdSeconds": 0,                  // for plank/hold activities
  "cameraIndex": 0
}
```

**Detection approach (V1 — motion heuristics):**  
- Capture frames at 15 fps via `AForge.Video.DirectShow`  
- Background subtraction to detect motion magnitude  
- Count peaks in the motion signal (low → high → low = 1 rep)  
- Debounce: minimum 0.5s between counted reps  
- Progress bar + rep counter displayed over live camera feed  

**Detection approach (V2 — pose estimation):**  
- Add `Microsoft.ML.OnnxRuntime` + MoveNet ONNX model  
- Detect key joint angles per activity type for reliable rep counting  
- V2 shipped as a separate `TimeGuard.Bonus.Vision` package, swapped in via DI

**UI:** Fullscreen with live camera preview, large rep counter, cancel button (goes back, no grant), countdown "3-2-1-Go!" before start.

**Privacy:** Camera feed is processed in-memory only; zero frames persisted to disk.

---

### 5.4 Multiple Choice Quiz *(future)*

JSON-configurable questions with 4 options. Included in schema today, UI deferred to V2.

---

## 6. Rich Chain Rules

Example parent configuration in the DB:

```
BonusChain: "Roblox Brain Boost" → ProcessName = "roblox", MaxPerDay = 2
  Step 1: Math (easy, 3/5 correct)  → +5 min
  Step 2: Writing (30 words)         → +5 min
  Step 3: Physical (20 jumping jacks)→ +10 min
```

**Rules:**
- Steps unlock sequentially — step 2 only appears after step 1 is passed in the same session  
- Partial completion is allowed — if kid quits after step 1, they keep the 5 min already granted  
- `BonusChainProgress.NextStepOrder` persists across app restarts (kid can come back)  
- `CompletionsToday` tracks full-chain completions against `MaxPerDay`; partial completions don't count  
- If `MaxPerDay` is reached, tasks are greyed out with a "Come back tomorrow" message  

---

## 7. UX Flow

### 7.1 Block event → Bonus app launch

```
MonitorService fires BlockRequested
  → App.xaml.cs shows BlockedPopup (existing)
  → BlockedPopup gains "💡 Earn More Time" button (new)
  → Button click: App.xaml.cs launches TimeGuard.Bonus.exe roblox
      (mutex-guarded; second click brings existing window to front)
```

### 7.2 Bonus app screens

```
1. Task Menu
   ┌──────────────────────────────────────┐
   │ 🎮 Earn More Roblox Time             │
   │                                      │
   │ "Roblox Brain Boost"  (2/2 today ✅) │
   │  Step 1: Math            [+5 min] ✅ │
   │  Step 2: Writing         [+5 min] ▶  │  ← current step
   │  Step 3: Jumping Jacks  [+10 min] 🔒 │
   │                                      │
   │          [Start Step 2]              │
   └──────────────────────────────────────┘

2. Task Execution (type-specific fullscreen UI)

3. Result Screen
   ┌──────────────────────────────────────┐
   │ ✅ +5 minutes earned!                 │
   │    Roblox time extended to 8:32 PM   │
   │                                      │
   │  [Continue to Step 3]  [Close]       │
   └──────────────────────────────────────┘
```

### 7.3 Main app applies grant

On the next `MonitorService` 5-second tick:
- Query `BonusGrants WHERE ProcessName='roblox' AND Applied=0`
- Add `MinutesGranted` to `DailyUsage.BonusMinutesEarned`
- Mark `Applied=1`
- MonitorService re-evaluates — process is no longer over-limit — no longer blocks relaunch

---

## 8. Parent Configuration (SettingsWindow extension)

Add a **"⭐ Bonus Tasks"** tab to the existing `SettingsWindow`:

- List of `BonusChains` for all processes  
- Per chain: edit name, assign to process, set `MaxPerDay`  
- Per task step: type picker, config form (dynamic based on type), bonus minutes, reorder/delete  
- "Test Task" button previews the task without granting time  
- "Add Chain" wizard: guided 3-step setup for first-time parents  

---

## 9. Security & Anti-Cheat

| Threat | Mitigation |
|---|---|
| Kid edits `BonusGrants` in SQLite directly | Parent password required to open `timeguard.db` folder (NTFS permissions set by installer) — this is a known limitation; full tamper-proofing requires a service process |
| Kid fast-clicks through tasks | Tasks are fullscreen modal with a minimum engagement time (math: 2s per problem; writing: real-time word count; physical: webcam timer) |
| Kid covers camera for physical tasks | Motion magnitude threshold must be met for ≥80% of the target duration; all-black or static frames restart the rep counter |
| Writing tasks submitted with gibberish | Three-layer local validation (no LLM): ① dictionary word-coverage ratio, ② Flesch-Kincaid readability score, ③ sentence count ≥ 1; Phase 4 adds `WeCantSpell.Hunspell` spell-check ratio for stronger detection |
| Repeating yesterday's writing | SHA-256 hash of trimmed lowercase submission stored in `BonusGrants.SubmissionHash` |

---

## 10. Implementation Phases

### Phase 1 — Foundation
- DB schema migration (new tables)  
- `BonusService` in `TimeGuard.Core` — CRUD for chains/tasks, progress tracking, grant application  
- `MonitorService` integration (apply grants on tick)  
- `BlockedPopup` "Earn More Time" button  
- `TimeGuard.Bonus` skeleton app with task menu screen  

### Phase 2 — Task types
- Math challenge (self-contained, no dependencies)  
- Writing prompt (self-contained)  
- Physical activity V1 — motion detection via `AForge.Video.DirectShow`  

### Phase 3 — Parent configuration
- "Bonus Tasks" tab in SettingsWindow  
- Chain wizard  
- "Test Task" preview  

### Phase 4 — Polish & V2 enhancements
- Pose estimation (MoveNet ONNX) for accurate rep counting  
- Multiple-choice quiz task type  
- Daily summary in Dashboard: "earned X bonus minutes"  
- Enhanced writing validation: `WeCantSpell.Hunspell` spell-check ratio (replaces FK-only heuristics)  

---

## 11. New NuGet Dependencies

| Package | Used by | Purpose |
|---|---|---|
| `AForge.Video.DirectShow` | `TimeGuard.Bonus` | Webcam capture for physical tasks |
| `AForge.Vision` | `TimeGuard.Bonus` | Motion detection / background subtraction |
| `Microsoft.ML.OnnxRuntime` *(Phase 4)* | `TimeGuard.Bonus.Vision` | Pose estimation |
| `WeCantSpell.Hunspell` *(Phase 4)* | `TimeGuard.Bonus` | Spell-check ratio for writing validation |

### Writing validation detail (Phase 2 — no external dependencies)

Three checks applied in order; all must pass to accept a submission:

1. **Dictionary word-coverage** — bundle a 10k common English word list as an embedded resource (~50 KB). Score = `recognized / total`; threshold 65%. Rejects pure gibberish (`"asdfg asdfg"` scores 0%).
2. **Flesch-Kincaid readability** — pure arithmetic on syllable/word/sentence counts. Requires score ≥ 30 (roughly 5th-grade level and above). Catches run-on walls of text with no punctuation and all-caps rants.
3. **Sentence count** — at least 1 sentence-ending character (`.`, `?`, `!`) must be present. Prevents single-word spam that passes word count.

Phase 4 adds `WeCantSpell.Hunspell` as a 4th check: if > 40% of words fail spell-check, reject. Catches random-word-sequence attacks that happen to use real dictionary words spaced apart.

---

## 12. Resolved Design Decisions

| # | Question | Decision |
|---|---|---|
| 1 | Offline writing validation without LLM? | ✅ Three-layer local heuristics: dictionary coverage + Flesch-Kincaid + sentence count. No model needed. Phase 4 adds Hunspell spell-check as 4th layer. |
| 2 | Cross-device parent approval (Manual Approval task type)? | ✅ In scope. Phase 4 adds a "Manual Approval" task type that pushes a webhook/Pushover notification to parent's phone; parent approves or rejects from their device. |
| 3 | Hard daily bonus minutes cap? | ✅ Yes — global `MaxBonusMinutesPerDay` setting (default 60 min). Configurable by parent in SettingsWindow. `BonusService.CanGrant()` checks remaining headroom before allowing a grant. |
| 4 | Physical task camera overlay on multiple monitors? | ✅ Spans all monitors (fullscreen, like BreakOverlay). Uses `System.Windows.Forms.Screen.AllScreens` to compute combined bounds, same pattern as existing BreakOverlay. |
