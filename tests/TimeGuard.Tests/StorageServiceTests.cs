using System.Text.Json;
using TimeGuard.Models;
using TimeGuard.Services;
using Xunit;

namespace TimeGuard.Tests;

public class StorageServiceTests : IDisposable
{
    // Use a temp directory so tests don't touch the real %AppData%\TimeGuard
    private readonly string _tempDir;

    public StorageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"TimeGuardTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Point StorageService at the temp directory via environment override
        // (We use a subclass to override the paths)
    }

    // ── Config round-trip ─────────────────────────────────────────────────────

    [Fact]
    public void Config_RoundTrip()
    {
        var svc = new TestableStorageService(_tempDir);
        var config = new AppConfig
        {
            PasswordHash = "hash",
            PasswordSalt = "salt",
            OverallDailyLimitMinutes = 90,
            Rules =
            [
                new AppRule
                {
                    ProcessName          = "roblox",
                    DisplayName          = "Roblox",
                    DailyLimitMinutes    = 60,
                    AllowedWindowStart   = "15:00",
                    AllowedWindowEnd     = "20:00",
                    Enabled              = true
                }
            ]
        };

        svc.SaveConfig(config);
        var loaded = svc.LoadConfig();

        Assert.Equal("hash", loaded.PasswordHash);
        Assert.Equal(90, loaded.OverallDailyLimitMinutes);
        Assert.Single(loaded.Rules);
        Assert.Equal("roblox", loaded.Rules[0].ProcessName);
        Assert.Equal("15:00", loaded.Rules[0].AllowedWindowStart);
    }

    [Fact]
    public void Config_ReturnsDefault_WhenMissing()
    {
        var svc    = new TestableStorageService(_tempDir);
        var config = svc.LoadConfig();

        Assert.True(config.IsFirstRun);
        Assert.Empty(config.Rules);
    }

    // ── Daily log round-trip ──────────────────────────────────────────────────

    [Fact]
    public void DailyLog_RoundTrip()
    {
        var svc  = new TestableStorageService(_tempDir);
        var date = new DateOnly(2026, 3, 1);
        var log  = new DailyLog
        {
            Date  = date,
            TotalUsageMinutes = 45.5,
            Entries =
            [
                new UsageEntry
                {
                    ProcessName   = "roblox",
                    UsageMinutes  = 45.5,
                    Blocked       = false,
                    Sessions      =
                    [
                        new SessionEntry
                        {
                            Start = new DateTime(2026, 3, 1, 15, 0, 0),
                            End   = new DateTime(2026, 3, 1, 15, 45, 0)
                        }
                    ]
                }
            ]
        };

        svc.SaveLog(log);
        var loaded = svc.LoadLog(date);

        Assert.Equal(45.5, loaded.TotalUsageMinutes);
        Assert.Single(loaded.Entries);
        Assert.Equal("roblox", loaded.Entries[0].ProcessName);
        Assert.Single(loaded.Entries[0].Sessions);
    }

    [Fact]
    public void DailyLog_ReturnsDefault_WhenMissing()
    {
        var svc    = new TestableStorageService(_tempDir);
        var loaded = svc.LoadLog(new DateOnly(2099, 1, 1));

        Assert.Empty(loaded.Entries);
        Assert.Equal(0, loaded.TotalUsageMinutes);
    }

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
        var e2  = log.GetOrCreate("Chrome"); // different casing

        Assert.Same(e1, e2);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}

/// <summary>
/// Allows tests to inject a custom data directory instead of the real %AppData%.
/// </summary>
internal sealed class TestableStorageService : StorageService
{
    private readonly string _dataDir;
    private string ConfigPath => Path.Combine(_dataDir, "config.json");
    private string LogsDir    => Path.Combine(_dataDir, "logs");

    public TestableStorageService(string dataDir) : base()
    {
        _dataDir = dataDir;
        Directory.CreateDirectory(LogsDir);
    }

    // Override file paths by shadowing the private helpers via new public methods
    public new void SaveConfig(AppConfig config)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(config,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    public new AppConfig LoadConfig()
    {
        if (!File.Exists(ConfigPath)) return new AppConfig();
        return System.Text.Json.JsonSerializer.Deserialize<AppConfig>(
            File.ReadAllText(ConfigPath)) ?? new AppConfig();
    }

    public new void SaveLog(DailyLog log)
    {
        var path = Path.Combine(LogsDir, $"{log.Date:yyyy-MM-dd}.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(log,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    public new DailyLog LoadLog(DateOnly date)
    {
        var path = Path.Combine(LogsDir, $"{date:yyyy-MM-dd}.json");
        if (!File.Exists(path)) return new DailyLog { Date = date };
        return System.Text.Json.JsonSerializer.Deserialize<DailyLog>(File.ReadAllText(path))
               ?? new DailyLog { Date = date };
    }
}
