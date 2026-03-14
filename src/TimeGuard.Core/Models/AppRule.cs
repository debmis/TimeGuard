using System.Globalization;

namespace TimeGuard.Models;

/// <summary>
/// A rule for a single monitored application.
/// </summary>
public class AppRule
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

    public int Id { get; set; }

    /// <summary>Process name to match (case-insensitive, without .exe)</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Human-friendly label shown in popups and the settings UI</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Max daily usage in minutes. 0 = no per-app limit.</summary>
    public int DailyLimitMinutes { get; set; } = 0;

    /// <summary>Earliest time app may be used (null = no restriction). Format: "HH:mm"</summary>
    public string? AllowedWindowStart { get; set; }

    /// <summary>Latest time app may be used (null = no restriction). Format: "HH:mm"</summary>
    public string? AllowedWindowEnd { get; set; }

    public List<AppRuleDaySchedule> DaySchedules { get; set; } = [];

    /// <summary>Take a break every N minutes of play. 0 = no breaks.</summary>
    public int BreakEveryMinutes { get; set; } = 0;

    /// <summary>How long the forced break lasts in minutes.</summary>
    public int BreakDurationMinutes { get; set; } = 0;

    public bool Enabled { get; set; } = true;

    // ── helpers ──────────────────────────────────────────────────────────────

    public bool HasTimeWindow =>
        AllowedWindowStart is not null && AllowedWindowEnd is not null;

    public bool HasDailyLimit => DailyLimitMinutes > 0;

    public bool HasBreakSchedule => BreakEveryMinutes > 0 && BreakDurationMinutes > 0;

    public string WeeklyLimitSummary
    {
        get
        {
            var schedules = GetWeekSchedule();
            if (TryGetUniformSchedule(out var uniform))
                return uniform.DailyLimitMinutes > 0
                    ? $"{uniform.DailyLimitMinutes} min every day"
                    : "No limit";

            return string.Join(", ",
                schedules
                    .Where(s => s.DailyLimitMinutes > 0)
                    .Select(s => $"{AbbreviateDay(s.DayOfWeek)} {s.DailyLimitMinutes}"));
        }
    }

    public string WeeklyWindowSummary
    {
        get
        {
            var schedules = GetWeekSchedule();
            if (TryGetUniformSchedule(out var uniform))
                return uniform.HasTimeWindow
                    ? $"{uniform.AllowedWindowStart}-{uniform.AllowedWindowEnd} daily"
                    : "Any time";

            var windows = schedules
                .Where(s => s.HasTimeWindow)
                .Select(s => $"{AbbreviateDay(s.DayOfWeek)} {s.AllowedWindowStart}-{s.AllowedWindowEnd}")
                .ToList();

            return windows.Count > 0 ? string.Join(", ", windows) : "Any time";
        }
    }

    /// <summary>Returns true if the current time falls inside the allowed window.</summary>
    public bool IsWithinAllowedWindow(TimeOnly now)
    {
        if (!HasTimeWindow) return true;

        var start = TimeOnly.Parse(AllowedWindowStart!);
        var end   = TimeOnly.Parse(AllowedWindowEnd!);

        return now >= start && now <= end;
    }

    public AppRuleDaySchedule GetScheduleForDay(DayOfWeek dayOfWeek)
    {
        var existing = DaySchedules.FirstOrDefault(s => s.DayOfWeek == dayOfWeek);
        return existing ?? CreateFallbackSchedule(dayOfWeek);
    }

    public List<AppRuleDaySchedule> GetWeekSchedule()
    {
        return OrderedDays
            .Select(GetScheduleForDay)
            .Select(CloneSchedule)
            .ToList();
    }

    public void SetWeekSchedule(IEnumerable<AppRuleDaySchedule> schedules)
    {
        DaySchedules = schedules
            .Select(CloneSchedule)
            .OrderBy(s => Array.IndexOf(OrderedDays, s.DayOfWeek))
            .ToList();

        SyncLegacyFieldsFromSchedules();
    }

    public void SyncLegacyFieldsFromSchedules()
    {
        if (!TryGetUniformSchedule(out var uniform))
        {
            DailyLimitMinutes  = 0;
            AllowedWindowStart = null;
            AllowedWindowEnd   = null;
            return;
        }

        DailyLimitMinutes  = uniform.DailyLimitMinutes;
        AllowedWindowStart = uniform.AllowedWindowStart;
        AllowedWindowEnd   = uniform.AllowedWindowEnd;
    }

    public bool HasTimeWindowOn(DayOfWeek dayOfWeek) => GetScheduleForDay(dayOfWeek).HasTimeWindow;

    public bool HasDailyLimitOn(DayOfWeek dayOfWeek) => GetScheduleForDay(dayOfWeek).HasDailyLimit;

    public bool IsWithinAllowedWindow(DayOfWeek dayOfWeek, TimeOnly now) =>
        GetScheduleForDay(dayOfWeek).IsWithinAllowedWindow(now);

    public bool TryGetUniformSchedule(out AppRuleDaySchedule schedule)
    {
        var schedules = GetWeekSchedule();
        var first     = schedules[0];
        var isUniform = schedules.All(s =>
            s.DailyLimitMinutes == first.DailyLimitMinutes &&
            string.Equals(s.AllowedWindowStart, first.AllowedWindowStart, StringComparison.Ordinal) &&
            string.Equals(s.AllowedWindowEnd, first.AllowedWindowEnd, StringComparison.Ordinal));

        schedule = CloneSchedule(first);
        return isUniform;
    }

    private AppRuleDaySchedule CreateFallbackSchedule(DayOfWeek dayOfWeek)
    {
        return new AppRuleDaySchedule
        {
            DayOfWeek          = dayOfWeek,
            DailyLimitMinutes  = DailyLimitMinutes,
            AllowedWindowStart = AllowedWindowStart,
            AllowedWindowEnd   = AllowedWindowEnd
        };
    }

    private static AppRuleDaySchedule CloneSchedule(AppRuleDaySchedule schedule)
    {
        return new AppRuleDaySchedule
        {
            DayOfWeek          = schedule.DayOfWeek,
            DailyLimitMinutes  = schedule.DailyLimitMinutes,
            AllowedWindowStart = schedule.AllowedWindowStart,
            AllowedWindowEnd   = schedule.AllowedWindowEnd
        };
    }

    private static string AbbreviateDay(DayOfWeek dayOfWeek) =>
        CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(dayOfWeek);
}
