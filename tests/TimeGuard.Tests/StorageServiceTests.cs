using TimeGuard.Models;
using TimeGuard.Services;
using Xunit;

namespace TimeGuard.Tests;

public class DatabaseServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connString;

    public DatabaseServiceTests()
    {
        _dbPath     = Path.Combine(Path.GetTempPath(), $"tgtest_{Guid.NewGuid():N}.db");
        _connString = $"Data Source={_dbPath};";
    }

    private DatabaseService CreateSvc() => new(_connString);

    // ── Settings ──────────────────────────────────────────────────────────────

    [Fact]
    public void Settings_RoundTrip()
    {
        var svc = CreateSvc();
        svc.SetSetting("TestKey", "TestValue");
        Assert.Equal("TestValue", svc.GetSetting("TestKey"));
    }

    [Fact]
    public void Settings_ReturnsNull_WhenMissing()
    {
        var svc = CreateSvc();
        Assert.Null(svc.GetSetting("NonExistent"));
    }

    // ── App Rules ─────────────────────────────────────────────────────────────

    [Fact]
    public void AppRules_InsertAndRetrieve()
    {
        var svc  = CreateSvc();
        var rule = new AppRule
        {
            ProcessName          = "roblox",
            DisplayName          = "Roblox",
            DailyLimitMinutes    = 60,
            AllowedWindowStart   = "15:00",
            AllowedWindowEnd     = "20:00",
            BreakEveryMinutes    = 30,
            BreakDurationMinutes = 5,
            Enabled              = true
        };

        svc.SaveRule(rule);
        var rules = svc.GetRules();

        Assert.Single(rules);
        Assert.Equal("roblox",      rules[0].ProcessName);
        Assert.Equal(60,            rules[0].DailyLimitMinutes);
        Assert.Equal("15:00",       rules[0].AllowedWindowStart);
        Assert.Equal(30,            rules[0].BreakEveryMinutes);
        Assert.Equal(5,             rules[0].BreakDurationMinutes);
    }

    [Fact]
    public void AppRules_Update()
    {
        var svc  = CreateSvc();
        var rule = new AppRule { ProcessName = "roblox", DisplayName = "Roblox", Enabled = true };
        svc.SaveRule(rule);

        var saved = svc.GetRules()[0];
        saved.DailyLimitMinutes = 120;
        svc.SaveRule(saved);

        Assert.Equal(120, svc.GetRules()[0].DailyLimitMinutes);
    }

    [Fact]
    public void AppRules_Delete()
    {
        var svc  = CreateSvc();
        var rule = new AppRule { ProcessName = "roblox", DisplayName = "Roblox", Enabled = true };
        svc.SaveRule(rule);
        var id = svc.GetRules()[0].Id;

        svc.DeleteRule(id);
        Assert.Empty(svc.GetRules());
    }

    // ── Daily Usage ───────────────────────────────────────────────────────────

    [Fact]
    public void DailyUsage_UpsertAndLoad()
    {
        var svc   = CreateSvc();
        var date  = new DateOnly(2026, 3, 1);
        var entry = new UsageEntry
        {
            ProcessName  = "roblox",
            UsageMinutes = 45.5,
            Blocked      = false
        };

        svc.UpsertUsageEntry(date, entry);
        var log = svc.LoadLog(date);

        Assert.Single(log.Entries);
        Assert.Equal(45.5, log.Entries[0].UsageMinutes);
    }

    [Fact]
    public void DailyUsage_Upsert_UpdatesExisting()
    {
        var svc   = CreateSvc();
        var date  = new DateOnly(2026, 3, 1);
        var entry = new UsageEntry { ProcessName = "roblox", UsageMinutes = 30 };

        svc.UpsertUsageEntry(date, entry);
        entry.UsageMinutes = 60;
        svc.UpsertUsageEntry(date, entry);

        Assert.Equal(60, svc.LoadLog(date).Entries[0].UsageMinutes);
    }

    [Fact]
    public void DailyLog_ReturnsEmpty_WhenNoData()
    {
        var svc = CreateSvc();
        var log = svc.LoadLog(new DateOnly(2099, 1, 1));
        Assert.Empty(log.Entries);
    }

    // ── GetOrCreate helpers ───────────────────────────────────────────────────

    [Fact]
    public void GetOrCreate_CreatesEntry_WhenMissing()
    {
        var log   = new DailyLog { Date = DateOnly.FromDateTime(DateTime.Today) };
        var entry = log.GetOrCreate("chrome");

        Assert.Equal("chrome", entry.ProcessName);
        Assert.Single(log.Entries);
    }

    [Fact]
    public void GetOrCreate_ReturnsSame_WhenExists()
    {
        var log = new DailyLog { Date = DateOnly.FromDateTime(DateTime.Today) };
        var e1  = log.GetOrCreate("chrome");
        var e2  = log.GetOrCreate("Chrome");

        Assert.Same(e1, e2);
    }

    // ── Passive session tracking ──────────────────────────────────────────────

    [Fact]
    public void PassiveSession_StoredAndRetrieved()
    {
        var svc       = CreateSvc();
        var sessionId = svc.OpenSession("notepad", "Untitled - Notepad", isPassive: true);
        svc.CloseSession(sessionId);

        var today    = DateOnly.FromDateTime(DateTime.Now);
        var segments = svc.LoadSessionsForDay(today);

        Assert.Single(segments);
        Assert.Equal("notepad",               segments[0].ProcessName);
        Assert.Equal("Untitled - Notepad",    segments[0].WindowTitle);
        Assert.Equal(1,                       segments[0].IsPassive);
    }

    [Fact]
    public void UpdateSessionTitle_UpdatesTitle()
    {
        var svc       = CreateSvc();
        var sessionId = svc.OpenSession("chrome", "New Tab", isPassive: true);
        svc.UpdateSessionTitle(sessionId, "YouTube - Chrome");
        svc.CloseSession(sessionId);

        var segments = svc.LoadSessionsForDay(DateOnly.FromDateTime(DateTime.Now));
        Assert.Equal("YouTube - Chrome", segments[0].WindowTitle);
    }

    [Fact]
    public void PurgeOldPassiveSessions_RemovesOldRows()
    {
        var svc = CreateSvc();
        // Manually insert an old passive session via ADO.NET
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(_connString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Sessions(ProcessName, StartTime, EndTime, TimeSinceBreakMins, WindowTitle, IsPassive)
            VALUES('oldapp', @old, @old, 0, 'Old App', 1)
            """;
        cmd.Parameters.AddWithValue("@old", DateTime.Now.AddDays(-10).ToString("o"));
        cmd.ExecuteNonQuery();

        svc.PurgeOldPassiveSessions(7);

        var segments = svc.LoadSessionsForDay(DateOnly.FromDateTime(DateTime.Now.AddDays(-10)));
        Assert.Empty(segments);
    }

    [Fact]
    public void GetRecentlySeenProcesses_ReturnsPassiveOnly()
    {
        var svc = CreateSvc();
        // Add a rule for chrome
        svc.SaveRule(new AppRule { ProcessName = "chrome", DisplayName = "Chrome", Enabled = true });

        svc.OpenSession("chrome",  "YouTube",        isPassive: false);
        svc.OpenSession("notepad", "Untitled",        isPassive: true);
        svc.OpenSession("mspaint", "Untitled - Paint",isPassive: true);

        var recent = svc.GetRecentlySeenProcesses(7);
        Assert.DoesNotContain("chrome",  recent);
        Assert.Contains("notepad",  recent);
        Assert.Contains("mspaint",  recent);
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }
}
