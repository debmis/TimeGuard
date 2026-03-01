using TimeGuard.Models;
using TimeGuard.Services;
using Xunit;

namespace TimeGuard.Tests;

public class RulesEngineTests
{
    private static AppConfig MakeConfig(int perAppLimit = 60, int overallLimit = 0,
        string? windowStart = null, string? windowEnd = null) => new()
    {
        PasswordHash = "x", PasswordSalt = "x",
        OverallDailyLimitMinutes = overallLimit,
        Rules =
        [
            new AppRule
            {
                ProcessName          = "roblox",
                DisplayName          = "Roblox",
                DailyLimitMinutes    = perAppLimit,
                AllowedWindowStart   = windowStart,
                AllowedWindowEnd     = windowEnd,
                Enabled              = true
            }
        ]
    };

    private static DailyLog MakeLog(double usedMinutes = 0, bool blocked = false) =>
        new()
        {
            Date = DateOnly.FromDateTime(DateTime.Today),
            Entries =
            [
                new UsageEntry
                {
                    ProcessName   = "roblox",
                    UsageMinutes  = usedMinutes,
                    Blocked       = blocked
                }
            ],
            TotalUsageMinutes = usedMinutes
        };

    private readonly RulesEngine _engine = new();
    private readonly string[] _running = ["roblox"];

    // ── Per-app limit ─────────────────────────────────────────────────────────

    [Fact]
    public void NoAction_WhenUnderLimit()
    {
        var actions = _engine.Evaluate(_running, MakeLog(30), MakeConfig(60), new TimeOnly(16, 0));
        Assert.Empty(actions);
    }

    [Fact]
    public void Block_WhenAtLimit()
    {
        var actions = _engine.Evaluate(_running, MakeLog(60), MakeConfig(60), new TimeOnly(16, 0));
        Assert.Single(actions);
        Assert.Equal(RulesEngine.ActionKind.Block, actions[0].Kind);
    }

    [Fact]
    public void Warn_WhenWithin5MinutesOfLimit()
    {
        var actions = _engine.Evaluate(_running, MakeLog(56), MakeConfig(60), new TimeOnly(16, 0));
        Assert.Single(actions);
        Assert.Equal(RulesEngine.ActionKind.WarnFiveMinutes, actions[0].Kind);
    }

    [Fact]
    public void NoWarn_WhenWarnAlreadySent()
    {
        var log = MakeLog(56);
        log.Entries[0].WarningSent = true;
        var actions = _engine.Evaluate(_running, log, MakeConfig(60), new TimeOnly(16, 0));
        Assert.Empty(actions);
    }

    // ── Time window ───────────────────────────────────────────────────────────

    [Fact]
    public void Block_WhenOutsideTimeWindow()
    {
        var config = MakeConfig(windowStart: "15:00", windowEnd: "20:00");
        var actions = _engine.Evaluate(_running, MakeLog(0), config, new TimeOnly(10, 0));
        Assert.Single(actions);
        Assert.Equal(RulesEngine.ActionKind.Block, actions[0].Kind);
    }

    [Fact]
    public void NoAction_WhenInsideTimeWindow()
    {
        var config = MakeConfig(windowStart: "15:00", windowEnd: "20:00");
        var actions = _engine.Evaluate(_running, MakeLog(0), config, new TimeOnly(17, 0));
        Assert.Empty(actions);
    }

    // ── Overall daily cap ─────────────────────────────────────────────────────

    [Fact]
    public void Block_WhenOverallCapReached()
    {
        var config  = MakeConfig(overallLimit: 120);
        var log     = MakeLog(120);
        log.TotalUsageMinutes = 120;
        var actions = _engine.Evaluate(_running, log, config, new TimeOnly(16, 0));
        Assert.Single(actions);
        Assert.Equal(RulesEngine.ActionKind.Block, actions[0].Kind);
    }

    // ── Not running ───────────────────────────────────────────────────────────

    [Fact]
    public void NoAction_WhenProcessNotRunning()
    {
        var actions = _engine.Evaluate([], MakeLog(60), MakeConfig(60), new TimeOnly(16, 0));
        Assert.Empty(actions);
    }

    // ── GetRelaunched ─────────────────────────────────────────────────────────

    [Fact]
    public void Relaunched_DetectedIfBlockedAndRunning()
    {
        var relaunched = _engine.GetRelaunched(_running, MakeLog(60, blocked: true), MakeConfig());
        Assert.Single(relaunched);
        Assert.Equal("roblox", relaunched[0].ProcessName);
    }

    [Fact]
    public void Relaunched_EmptyIfNotRunning()
    {
        var relaunched = _engine.GetRelaunched([], MakeLog(60, blocked: true), MakeConfig());
        Assert.Empty(relaunched);
    }
}
