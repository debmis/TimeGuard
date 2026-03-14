using TimeGuard.Models;
using TimeGuard.Services;
using Xunit;

namespace TimeGuard.Tests;

public class RulesEngineTests
{
    private static readonly DayOfWeek[] OrderedDays =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];

    private static AppConfig MakeConfig(int perAppLimit = 60, int overallLimit = 0,
        string? windowStart = null, string? windowEnd = null, Action<AppRule>? configureRule = null)
    {
        var rule = new AppRule
        {
            ProcessName        = "roblox",
            DisplayName        = "Roblox",
            DailyLimitMinutes  = perAppLimit,
            AllowedWindowStart = windowStart,
            AllowedWindowEnd   = windowEnd,
            Enabled            = true
        };
        configureRule?.Invoke(rule);

        return new AppConfig
        {
            PasswordHash             = "x",
            PasswordSalt             = "x",
            OverallDailyLimitMinutes = overallLimit,
            Rules = [rule]
        };
    }

    private static DailyLog MakeLog(double usedMinutes = 0, bool blocked = false, DateOnly? date = null) =>
        new()
        {
            Date = date ?? DateOnly.FromDateTime(DateTime.Today),
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

    private static List<AppRuleDaySchedule> MakeWeekSchedule(int defaultLimit = 0,
        string? defaultStart = null, string? defaultEnd = null)
    {
        return OrderedDays
            .Select(day => new AppRuleDaySchedule
            {
                DayOfWeek          = day,
                DailyLimitMinutes  = defaultLimit,
                AllowedWindowStart = defaultStart,
                AllowedWindowEnd   = defaultEnd
            })
            .ToList();
    }

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

    // ── Already-blocked entry skipped ─────────────────────────────────────────

    [Fact]
    public void NoBlock_WhenEntryAlreadyBlocked()
    {
        // If the entry is already blocked, Evaluate should not emit a second Block action
        var actions = _engine.Evaluate(_running, MakeLog(60, blocked: true), MakeConfig(60), new TimeOnly(16, 0));
        Assert.Empty(actions);
    }

    // ── Break schedule ────────────────────────────────────────────────────────

    [Fact]
    public void BreakDue_WhenTimeSinceBreakExceedsInterval()
    {
        var config = new AppConfig
        {
            PasswordHash = "x", PasswordSalt = "x",
            Rules =
            [
                new AppRule
                {
                    ProcessName          = "roblox",
                    DisplayName          = "Roblox",
                    DailyLimitMinutes    = 120,
                    BreakEveryMinutes    = 30,
                    BreakDurationMinutes = 5,
                    Enabled              = true
                }
            ]
        };

        var breakTimers = new Dictionary<string, double> { ["roblox"] = 30 };
        var actions = _engine.Evaluate(_running, MakeLog(30), config, new TimeOnly(16, 0), breakTimers);

        Assert.Single(actions);
        Assert.Equal(RulesEngine.ActionKind.BreakDue, actions[0].Kind);
    }

    [Fact]
    public void NoBreakDue_WhenTimeSinceBreakBelowInterval()
    {
        var config = new AppConfig
        {
            PasswordHash = "x", PasswordSalt = "x",
            Rules =
            [
                new AppRule
                {
                    ProcessName          = "roblox",
                    DisplayName          = "Roblox",
                    DailyLimitMinutes    = 120,
                    BreakEveryMinutes    = 30,
                    BreakDurationMinutes = 5,
                    Enabled              = true
                }
            ]
        };

        var breakTimers = new Dictionary<string, double> { ["roblox"] = 20 };
        var actions = _engine.Evaluate(_running, MakeLog(20), config, new TimeOnly(16, 0), breakTimers);
        Assert.Empty(actions);
    }

    [Fact]
    public void Block_WhenWeekdaySpecificLimitReached()
    {
        var wednesday = new DateOnly(2026, 3, 4);
        var config = MakeConfig(configureRule: rule =>
        {
            var schedule = MakeWeekSchedule(defaultLimit: 60);
            schedule.First(s => s.DayOfWeek == DayOfWeek.Wednesday).DailyLimitMinutes = 30;
            rule.SetWeekSchedule(schedule);
        });

        var actions = _engine.Evaluate(_running, MakeLog(30, date: wednesday), config, new TimeOnly(13, 0));

        Assert.Single(actions);
        Assert.Equal(RulesEngine.ActionKind.Block, actions[0].Kind);
        Assert.Contains("30 min", actions[0].Reason);
    }

    [Fact]
    public void Block_WhenOutsideWeekdaySpecificWindow()
    {
        var wednesday = new DateOnly(2026, 3, 4);
        var config = MakeConfig(configureRule: rule =>
        {
            var schedule = MakeWeekSchedule(defaultLimit: 0);
            var wednesdaySchedule = schedule.First(s => s.DayOfWeek == DayOfWeek.Wednesday);
            wednesdaySchedule.AllowedWindowStart = "12:00";
            wednesdaySchedule.AllowedWindowEnd   = "14:00";
            rule.SetWeekSchedule(schedule);
        });

        var actions = _engine.Evaluate(_running, MakeLog(0, date: wednesday), config, new TimeOnly(15, 0));

        Assert.Single(actions);
        Assert.Equal(RulesEngine.ActionKind.Block, actions[0].Kind);
        Assert.Contains("12:00-14:00", actions[0].Reason);
    }

    [Fact]
    public void NoAction_WhenAnotherDayHasShorterLimit()
    {
        var monday = new DateOnly(2026, 3, 2);
        var config = MakeConfig(configureRule: rule =>
        {
            var schedule = MakeWeekSchedule(defaultLimit: 60);
            schedule.First(s => s.DayOfWeek == DayOfWeek.Wednesday).DailyLimitMinutes = 30;
            rule.SetWeekSchedule(schedule);
        });

        var actions = _engine.Evaluate(_running, MakeLog(45, date: monday), config, new TimeOnly(13, 0));

        Assert.Empty(actions);
    }
}
