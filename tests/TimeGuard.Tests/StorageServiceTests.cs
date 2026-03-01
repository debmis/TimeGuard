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

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }
}
