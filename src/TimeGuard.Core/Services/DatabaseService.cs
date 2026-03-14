using Dapper;
using Microsoft.Data.Sqlite;
using TimeGuard.Models;

namespace TimeGuard.Services;

/// <summary>
/// All persistence operations via SQLite + Dapper.
/// Replaces the flat-file StorageService.
/// Thread-safe — uses a single connection string; SQLite WAL handles concurrency.
/// </summary>
public class DatabaseService
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TimeGuard");

    private readonly string _connectionString;

    public DatabaseService(string? connectionString = null)
    {
        Directory.CreateDirectory(DataDir);
        _connectionString = connectionString
            ?? $"Data Source={Path.Combine(DataDir, "timeguard.db")};";

        new DatabaseMigrator(_connectionString).Migrate();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    public string? GetSetting(string key)
    {
        using var conn = Open();
        return conn.QuerySingleOrDefault<string>(
            "SELECT Value FROM Settings WHERE Key = @key", new { key });
    }

    public void SetSetting(string key, string value)
    {
        using var conn = Open();
        conn.Execute(
            "INSERT INTO Settings(Key, Value) VALUES(@key, @value) " +
            "ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value",
            new { key, value });
    }

    // Convenience wrappers for the AppConfig fields stored in Settings
    public AppConfig LoadConfig()
    {
        return new AppConfig
        {
            PasswordHash             = GetSetting("PasswordHash") ?? string.Empty,
            PasswordSalt             = GetSetting("PasswordSalt") ?? string.Empty,
            SettingsHotkey           = GetSetting("SettingsHotkey") ?? "Ctrl+Alt+Shift+G",
            OverallDailyLimitMinutes = int.TryParse(GetSetting("OverallDailyLimitMinutes"), out var cap) ? cap : 0,
            Rules                    = GetRules()
        };
    }

    public void SaveConfig(AppConfig config)
    {
        SetSetting("PasswordHash",             config.PasswordHash);
        SetSetting("PasswordSalt",             config.PasswordSalt);
        SetSetting("SettingsHotkey",           config.SettingsHotkey);
        SetSetting("OverallDailyLimitMinutes", config.OverallDailyLimitMinutes.ToString());
        // Rules are saved separately via SaveRule / DeleteRule
    }

    // ── App Rules ─────────────────────────────────────────────────────────────

    public List<AppRule> GetRules()
    {
        using var conn = Open();
        var rules = conn.Query<AppRule>("""
            SELECT Id, ProcessName, DisplayName,
                   DailyLimitMins  AS DailyLimitMinutes,
                   WindowStart     AS AllowedWindowStart,
                   WindowEnd       AS AllowedWindowEnd,
                   BreakEveryMins  AS BreakEveryMinutes,
                    BreakDurationMins AS BreakDurationMinutes,
                   Enabled
            FROM AppRules ORDER BY DisplayName
            """).ToList();

        var scheduleLookup = conn.Query<AppRuleDayScheduleRow>("""
            SELECT RuleId,
                   DayOfWeek,
                   DailyLimitMins AS DailyLimitMinutes,
                   WindowStart    AS AllowedWindowStart,
                   WindowEnd      AS AllowedWindowEnd
            FROM AppRuleDaySchedules
            ORDER BY RuleId, DayOfWeek
            """)
            .GroupBy(row => row.RuleId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.ToSchedule()).ToList());

        foreach (var rule in rules)
        {
            if (scheduleLookup.TryGetValue(rule.Id, out var schedules))
                rule.SetWeekSchedule(schedules);
            else
                rule.SetWeekSchedule(rule.GetWeekSchedule());
        }

        return rules;
    }

    public void SaveRule(AppRule rule)
    {
        using var conn = Open();
        using var tx   = conn.BeginTransaction();

        var schedules      = GetSchedulesForPersistence(rule);
        var legacySchedule = GetLegacyScheduleForPersistence(schedules);

        if (rule.Id == 0)
        {
            rule.Id = conn.QuerySingle<int>("""
                INSERT INTO AppRules(ProcessName, DisplayName, DailyLimitMins,
                    WindowStart, WindowEnd, BreakEveryMins, BreakDurationMins, Enabled)
                VALUES(@ProcessName, @DisplayName, @DailyLimitMinutes,
                    @AllowedWindowStart, @AllowedWindowEnd, @BreakEveryMinutes, @BreakDurationMinutes, @Enabled);
                SELECT last_insert_rowid();
                """, new
            {
                rule.ProcessName,
                rule.DisplayName,
                DailyLimitMinutes  = legacySchedule.DailyLimitMinutes,
                AllowedWindowStart = legacySchedule.AllowedWindowStart,
                AllowedWindowEnd   = legacySchedule.AllowedWindowEnd,
                rule.BreakEveryMinutes,
                rule.BreakDurationMinutes,
                rule.Enabled
            }, tx);
        }
        else
        {
            conn.Execute("""
                UPDATE AppRules SET
                    ProcessName       = @ProcessName,
                    DisplayName       = @DisplayName,
                    DailyLimitMins    = @DailyLimitMinutes,
                    WindowStart       = @AllowedWindowStart,
                    WindowEnd         = @AllowedWindowEnd,
                    BreakEveryMins    = @BreakEveryMinutes,
                    BreakDurationMins = @BreakDurationMinutes,
                    Enabled           = @Enabled
                WHERE Id = @Id
                """, new
            {
                rule.Id,
                rule.ProcessName,
                rule.DisplayName,
                DailyLimitMinutes  = legacySchedule.DailyLimitMinutes,
                AllowedWindowStart = legacySchedule.AllowedWindowStart,
                AllowedWindowEnd   = legacySchedule.AllowedWindowEnd,
                rule.BreakEveryMinutes,
                rule.BreakDurationMinutes,
                rule.Enabled
            }, tx);
        }

        conn.Execute("DELETE FROM AppRuleDaySchedules WHERE RuleId = @ruleId",
            new { ruleId = rule.Id }, tx);

        conn.Execute("""
            INSERT INTO AppRuleDaySchedules(RuleId, DayOfWeek, DailyLimitMins, WindowStart, WindowEnd)
            VALUES(@RuleId, @DayOfWeek, @DailyLimitMinutes, @AllowedWindowStart, @AllowedWindowEnd)
            """,
            schedules.Select(schedule => new
            {
                RuleId             = rule.Id,
                DayOfWeek          = (int)schedule.DayOfWeek,
                schedule.DailyLimitMinutes,
                schedule.AllowedWindowStart,
                schedule.AllowedWindowEnd
            }),
            tx);

        tx.Commit();
    }

    public void DeleteRule(int id)
    {
        using var conn = Open();
        using var tx   = conn.BeginTransaction();
        conn.Execute("DELETE FROM AppRuleDaySchedules WHERE RuleId = @id", new { id }, tx);
        conn.Execute("DELETE FROM AppRules WHERE Id = @id", new { id }, tx);
        tx.Commit();
    }

    // ── Daily Usage ───────────────────────────────────────────────────────────

    public DailyLog LoadTodayLog() => LoadLog(DateOnly.FromDateTime(DateTime.Now));

    public DailyLog LoadLog(DateOnly date)
    {
        using var conn = Open();
        var dateStr = date.ToString("yyyy-MM-dd");

        var entries = conn.Query<UsageEntry>("""
            SELECT Id, ProcessName, UsageMins AS UsageMinutes,
                   Blocked, WarningSent
            FROM DailyUsage WHERE Date = @dateStr
            """, new { dateStr }).ToList();

        var totalMins = entries.Sum(e => e.UsageMinutes);
        var config    = LoadConfig();
        var capHit    = config.OverallDailyLimitMinutes > 0
                        && totalMins >= config.OverallDailyLimitMinutes;

        return new DailyLog
        {
            Date              = date,
            Entries           = entries,
            TotalUsageMinutes = totalMins,
            OverallCapHit     = capHit
        };
    }

    public void UpsertUsageEntry(DateOnly date, UsageEntry entry)
    {
        using var conn = Open();
        conn.Execute("""
            INSERT INTO DailyUsage(Date, ProcessName, UsageMins, Blocked, WarningSent)
            VALUES(@date, @ProcessName, @UsageMinutes, @Blocked, @WarningSent)
            ON CONFLICT(Date, ProcessName) DO UPDATE SET
                UsageMins   = excluded.UsageMins,
                Blocked     = excluded.Blocked,
                WarningSent = excluded.WarningSent
            """, new
        {
            date = date.ToString("yyyy-MM-dd"),
            entry.ProcessName,
            entry.UsageMinutes,
            entry.Blocked,
            entry.WarningSent
        });
    }

    // ── Sessions ──────────────────────────────────────────────────────────────

    public int OpenSession(string processName, string windowTitle = "", bool isPassive = false)
    {
        using var conn = Open();
        return conn.QuerySingle<int>("""
            INSERT INTO Sessions(ProcessName, StartTime, TimeSinceBreakMins, WindowTitle, IsPassive)
            VALUES(@processName, @startTime, 0, @windowTitle, @isPassive);
            SELECT last_insert_rowid();
            """, new { processName, startTime = DateTime.Now.ToString("o"), windowTitle, isPassive = isPassive ? 1 : 0 });
    }

    public void UpdateSession(int sessionId, double timeSinceBreakMins)
    {
        using var conn = Open();
        conn.Execute("""
            UPDATE Sessions SET TimeSinceBreakMins = @timeSinceBreakMins
            WHERE Id = @sessionId
            """, new { sessionId, timeSinceBreakMins });
    }

    public void UpdateSessionTitle(int sessionId, string windowTitle)
    {
        using var conn = Open();
        conn.Execute("UPDATE Sessions SET WindowTitle = @windowTitle WHERE Id = @sessionId",
            new { sessionId, windowTitle });
    }

    public void CloseSession(int sessionId, double timeSinceBreakMins = 0)
    {
        using var conn = Open();
        conn.Execute("""
            UPDATE Sessions SET EndTime = @endTime, TimeSinceBreakMins = @timeSinceBreakMins
            WHERE Id = @sessionId
            """, new { sessionId, endTime = DateTime.Now.ToString("o"), timeSinceBreakMins });
    }

    public void ResetBreakTimer(int sessionId)
    {
        using var conn = Open();
        conn.Execute("UPDATE Sessions SET TimeSinceBreakMins = 0 WHERE Id = @sessionId",
            new { sessionId });
    }

    // ── History (for dashboard) ───────────────────────────────────────────────

    public IReadOnlyList<DailyLog> LoadRecentLogs(int days = 30)
    {
        var result = new List<DailyLog>();
        var today  = DateOnly.FromDateTime(DateTime.Now);
        for (int i = 0; i < days; i++)
            result.Add(LoadLog(today.AddDays(-i)));
        return result;
    }

    /// <summary>Returns per-app daily totals for the last N days, for chart rendering.</summary>
    public IReadOnlyList<(DateOnly Date, string ProcessName, double UsageMins, bool IsPassive)> LoadChartData(int days = 7)
    {
        using var conn  = Open();
        var cutoff = DateOnly.FromDateTime(DateTime.Now).AddDays(-(days - 1))
                             .ToString("yyyy-MM-dd");
        // Join with AppRules to determine if a process has a rule
        return conn.Query<(string Date, string ProcessName, double UsageMins, int IsPassive)>("""
            SELECT d.Date, d.ProcessName, d.UsageMins,
                   CASE WHEN r.ProcessName IS NULL THEN 1 ELSE 0 END AS IsPassive
            FROM DailyUsage d
            LEFT JOIN AppRules r ON lower(r.ProcessName) = lower(d.ProcessName) AND r.Enabled = 1
            WHERE d.Date >= @cutoff AND d.UsageMins > 0
            ORDER BY d.Date, d.ProcessName
            """, new { cutoff })
            .Select(r => (DateOnly.Parse(r.Date), r.ProcessName, r.UsageMins, r.IsPassive == 1))
            .ToList();
    }

    /// <summary>Returns all sessions that started or were active on the given date, ordered by start time.</summary>
    public IReadOnlyList<SessionSegment> LoadSessionsForDay(DateOnly date)
    {
        using var conn = Open();
        var dateStr     = date.ToString("yyyy-MM-dd");
        var nextDateStr = date.AddDays(1).ToString("yyyy-MM-dd");
        return conn.Query<SessionSegment>("""
            SELECT ProcessName,
                   COALESCE(WindowTitle, '') AS WindowTitle,
                   StartTime,
                   COALESCE(EndTime, @nextDateStr) AS EndTime,
                   IsPassive
            FROM Sessions
            WHERE substr(StartTime, 1, 10) = @dateStr
               OR (substr(StartTime, 1, 10) < @dateStr AND (EndTime IS NULL OR substr(EndTime, 1, 10) >= @dateStr))
            ORDER BY StartTime
            """, new { dateStr, nextDateStr }).ToList();
    }

    /// <summary>Deletes passive sessions older than the given number of days.</summary>
    public void PurgeOldPassiveSessions(int days = 7)
    {
        using var conn = Open();
        var cutoff = DateTime.Now.AddDays(-days).ToString("o");
        conn.Execute("""
            DELETE FROM Sessions
            WHERE IsPassive = 1 AND StartTime < @cutoff
            """, new { cutoff });
    }

    /// <summary>Returns distinct process names seen in Sessions within the last N days (excluding ruled apps).</summary>
    public IReadOnlyList<string> GetRecentlySeenProcesses(int days = 7)
    {
        using var conn = Open();
        var cutoff = DateTime.Now.AddDays(-days).ToString("o");
        return conn.Query<string>("""
            SELECT DISTINCT lower(s.ProcessName)
            FROM Sessions s
            WHERE s.IsPassive = 1 AND s.StartTime >= @cutoff
              AND NOT EXISTS (
                  SELECT 1 FROM AppRules r WHERE lower(r.ProcessName) = lower(s.ProcessName)
              )
            ORDER BY 1
            """, new { cutoff }).ToList();
    }

    private static List<AppRuleDaySchedule> GetSchedulesForPersistence(AppRule rule)
    {
        if (rule.DaySchedules.Count == 0)
            return BuildUniformSchedules(rule);

        if (ShouldOverwriteSchedulesFromLegacyFields(rule))
            return BuildUniformSchedules(rule);

        return rule.GetWeekSchedule();
    }

    private static bool ShouldOverwriteSchedulesFromLegacyFields(AppRule rule)
    {
        var hasLegacyWindow = rule.AllowedWindowStart is not null || rule.AllowedWindowEnd is not null;

        if (!rule.TryGetUniformSchedule(out var uniform))
            return rule.DailyLimitMinutes > 0 || hasLegacyWindow;

        return rule.DailyLimitMinutes != uniform.DailyLimitMinutes ||
               !string.Equals(rule.AllowedWindowStart, uniform.AllowedWindowStart, StringComparison.Ordinal) ||
               !string.Equals(rule.AllowedWindowEnd, uniform.AllowedWindowEnd, StringComparison.Ordinal);
    }

    private static List<AppRuleDaySchedule> BuildUniformSchedules(AppRule rule)
    {
        return new AppRule().GetWeekSchedule()
            .Select(schedule => new AppRuleDaySchedule
            {
                DayOfWeek          = schedule.DayOfWeek,
                DailyLimitMinutes  = rule.DailyLimitMinutes,
                AllowedWindowStart = rule.AllowedWindowStart,
                AllowedWindowEnd   = rule.AllowedWindowEnd
            })
            .ToList();
    }

    private static AppRuleDaySchedule GetLegacyScheduleForPersistence(List<AppRuleDaySchedule> schedules)
    {
        var first     = schedules[0];
        var isUniform = schedules.All(schedule =>
            schedule.DailyLimitMinutes == first.DailyLimitMinutes &&
            string.Equals(schedule.AllowedWindowStart, first.AllowedWindowStart, StringComparison.Ordinal) &&
            string.Equals(schedule.AllowedWindowEnd, first.AllowedWindowEnd, StringComparison.Ordinal));

        return isUniform
            ? new AppRuleDaySchedule
            {
                DayOfWeek          = first.DayOfWeek,
                DailyLimitMinutes  = first.DailyLimitMinutes,
                AllowedWindowStart = first.AllowedWindowStart,
                AllowedWindowEnd   = first.AllowedWindowEnd
            }
            : new AppRuleDaySchedule { DayOfWeek = DayOfWeek.Monday };
    }
}

file sealed class AppRuleDayScheduleRow
{
    public int RuleId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    public int DailyLimitMinutes { get; set; }

    public string? AllowedWindowStart { get; set; }

    public string? AllowedWindowEnd { get; set; }

    public AppRuleDaySchedule ToSchedule()
    {
        return new AppRuleDaySchedule
        {
            DayOfWeek          = DayOfWeek,
            DailyLimitMinutes  = DailyLimitMinutes,
            AllowedWindowStart = AllowedWindowStart,
            AllowedWindowEnd   = AllowedWindowEnd
        };
    }
}
