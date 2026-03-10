using TimeGuard.Models;
using TimeGuard.Services;
using Xunit;

namespace TimeGuard.Tests;

/// <summary>
/// Tests for MonitorService.ReloadConfig unblock behavior (Bug 1).
/// </summary>
public class MonitorServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connString;

    public MonitorServiceTests()
    {
        _dbPath     = Path.Combine(Path.GetTempPath(), $"tgmonitor_{Guid.NewGuid():N}.db");
        _connString = $"Data Source={_dbPath};";
    }

    private DatabaseService CreateDb() => new(_connString);

    private static AppConfig MakeConfig(DatabaseService db, int perAppLimit = 60, int overallLimit = 0)
    {
        var rule = new AppRule
        {
            ProcessName       = "roblox",
            DisplayName       = "Roblox",
            DailyLimitMinutes = perAppLimit,
            Enabled           = true
        };
        db.SaveRule(rule);
        var config = db.LoadConfig();
        config.OverallDailyLimitMinutes = overallLimit;
        db.SaveConfig(config);
        return db.LoadConfig();
    }

    // ── ReloadConfig unblocks per-app blocked entry ───────────────────────────

    [Fact]
    public void ReloadConfig_UnblocksEntry_WhenPerAppLimitRaised()
    {
        var db     = CreateDb();
        var config = MakeConfig(db, perAppLimit: 60);
        var today  = DateOnly.FromDateTime(DateTime.Today);

        // Seed a blocked entry at exactly the original limit
        db.UpsertUsageEntry(today, new UsageEntry
        {
            ProcessName  = "roblox",
            UsageMinutes = 60,
            Blocked      = true
        });

        var monitor = new MonitorService(db, new RulesEngine(), config);

        // Raise the per-app limit above current usage
        var updatedRule = db.GetRules()[0];
        updatedRule.DailyLimitMinutes = 90;
        db.SaveRule(updatedRule);
        var updatedConfig = db.LoadConfig();

        monitor.ReloadConfig(updatedConfig);

        var log = db.LoadLog(today);
        Assert.False(log.Entries[0].Blocked, "Entry should be unblocked after limit is raised.");
    }

    [Fact]
    public void ReloadConfig_KeepsBlocked_WhenUsageStillExceedsNewLimit()
    {
        var db     = CreateDb();
        var config = MakeConfig(db, perAppLimit: 60);
        var today  = DateOnly.FromDateTime(DateTime.Today);

        db.UpsertUsageEntry(today, new UsageEntry
        {
            ProcessName  = "roblox",
            UsageMinutes = 60,
            Blocked      = true
        });

        var monitor = new MonitorService(db, new RulesEngine(), config);

        // Raise to 60 — still at the limit, should stay blocked
        var updatedRule = db.GetRules()[0];
        updatedRule.DailyLimitMinutes = 60;
        db.SaveRule(updatedRule);

        monitor.ReloadConfig(db.LoadConfig());

        var log = db.LoadLog(today);
        Assert.True(log.Entries[0].Blocked, "Entry should remain blocked when usage still meets the limit.");
    }

    [Fact]
    public void ReloadConfig_ResetWarningSent_WhenLimitRaisedWellAboveUsage()
    {
        var db     = CreateDb();
        var config = MakeConfig(db, perAppLimit: 60);
        var today  = DateOnly.FromDateTime(DateTime.Today);

        // 56 min used — close to original 60 limit, warning already sent
        db.UpsertUsageEntry(today, new UsageEntry
        {
            ProcessName  = "roblox",
            UsageMinutes = 56,
            Blocked      = true,
            WarningSent  = true
        });

        var monitor = new MonitorService(db, new RulesEngine(), config);

        var updatedRule = db.GetRules()[0];
        updatedRule.DailyLimitMinutes = 120; // raised well above usage
        db.SaveRule(updatedRule);

        monitor.ReloadConfig(db.LoadConfig());

        var log = db.LoadLog(today);
        Assert.False(log.Entries[0].WarningSent, "WarningSent should be reset when limit is raised well above usage.");
    }

    // ── ReloadConfig unsets overall cap ───────────────────────────────────────

    [Fact]
    public void ReloadConfig_UnsetsOverallCap_WhenCapRaised()
    {
        var db     = CreateDb();
        var config = MakeConfig(db, perAppLimit: 200, overallLimit: 120);
        var today  = DateOnly.FromDateTime(DateTime.Today);

        db.UpsertUsageEntry(today, new UsageEntry
        {
            ProcessName  = "roblox",
            UsageMinutes = 120
        });

        // LoadTodayLog will set OverallCapHit = true (120 >= 120)
        var monitor = new MonitorService(db, new RulesEngine(), config);
        Assert.True(monitor.IsOverallCapHit);

        // Raise overall cap above current usage
        config.OverallDailyLimitMinutes = 180;
        db.SaveConfig(config);

        monitor.ReloadConfig(db.LoadConfig());

        Assert.False(monitor.IsOverallCapHit, "Overall cap hit flag should be cleared when cap is raised above current usage.");
    }

    [Fact]
    public void ReloadConfig_UnsetsOverallCap_WhenCapRemovedEntirely()
    {
        var db     = CreateDb();
        var config = MakeConfig(db, perAppLimit: 200, overallLimit: 120);
        var today  = DateOnly.FromDateTime(DateTime.Today);

        db.UpsertUsageEntry(today, new UsageEntry { ProcessName = "roblox", UsageMinutes = 120 });

        var monitor = new MonitorService(db, new RulesEngine(), config);
        Assert.True(monitor.IsOverallCapHit);

        config.OverallDailyLimitMinutes = 0; // remove overall cap
        db.SaveConfig(config);

        monitor.ReloadConfig(db.LoadConfig());

        Assert.False(monitor.IsOverallCapHit, "Overall cap hit flag should be cleared when overall cap is removed.");
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }
}
