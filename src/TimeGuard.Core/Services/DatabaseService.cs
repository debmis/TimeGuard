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
        return conn.Query<AppRule>("""
            SELECT Id, ProcessName, DisplayName,
                   DailyLimitMins  AS DailyLimitMinutes,
                   WindowStart     AS AllowedWindowStart,
                   WindowEnd       AS AllowedWindowEnd,
                   BreakEveryMins  AS BreakEveryMinutes,
                   BreakDurationMins AS BreakDurationMinutes,
                   Enabled
            FROM AppRules ORDER BY DisplayName
            """).ToList();
    }

    public void SaveRule(AppRule rule)
    {
        using var conn = Open();
        if (rule.Id == 0)
        {
            rule.Id = conn.QuerySingle<int>("""
                INSERT INTO AppRules(ProcessName, DisplayName, DailyLimitMins,
                    WindowStart, WindowEnd, BreakEveryMins, BreakDurationMins, Enabled)
                VALUES(@ProcessName, @DisplayName, @DailyLimitMinutes,
                    @AllowedWindowStart, @AllowedWindowEnd, @BreakEveryMinutes, @BreakDurationMinutes, @Enabled);
                SELECT last_insert_rowid();
                """, rule);
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
                """, rule);
        }
    }

    public void DeleteRule(int id)
    {
        using var conn = Open();
        conn.Execute("DELETE FROM AppRules WHERE Id = @id", new { id });
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

    public int OpenSession(string processName)
    {
        using var conn = Open();
        return conn.QuerySingle<int>("""
            INSERT INTO Sessions(ProcessName, StartTime, TimeSinceBreakMins)
            VALUES(@processName, @startTime, 0);
            SELECT last_insert_rowid();
            """, new { processName, startTime = DateTime.Now.ToString("o") });
    }

    public void UpdateSession(int sessionId, double timeSinceBreakMins)
    {
        using var conn = Open();
        conn.Execute("""
            UPDATE Sessions SET TimeSinceBreakMins = @timeSinceBreakMins
            WHERE Id = @sessionId
            """, new { sessionId, timeSinceBreakMins });
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
    public IReadOnlyList<(DateOnly Date, string ProcessName, double UsageMins)> LoadChartData(int days = 7)
    {
        using var conn  = Open();
        var cutoff = DateOnly.FromDateTime(DateTime.Now).AddDays(-(days - 1))
                             .ToString("yyyy-MM-dd");
        return conn.Query<(string Date, string ProcessName, double UsageMins)>("""
            SELECT Date, ProcessName, UsageMins
            FROM DailyUsage
            WHERE Date >= @cutoff AND UsageMins > 0
            ORDER BY Date, ProcessName
            """, new { cutoff })
            .Select(r => (DateOnly.Parse(r.Date), r.ProcessName, r.UsageMins))
            .ToList();
    }
}
