using System.Globalization;

namespace TimeGuard.Models;

public class AppRuleDaySchedule
{
    public DayOfWeek DayOfWeek { get; set; }

    public int DailyLimitMinutes { get; set; } = 0;

    public string? AllowedWindowStart { get; set; }

    public string? AllowedWindowEnd { get; set; }

    public string DayLabel =>
        CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(DayOfWeek);

    public bool HasTimeWindow =>
        AllowedWindowStart is not null && AllowedWindowEnd is not null;

    public bool HasDailyLimit => DailyLimitMinutes > 0;

    public bool IsWithinAllowedWindow(TimeOnly now)
    {
        if (!HasTimeWindow) return true;

        var start = TimeOnly.Parse(AllowedWindowStart!);
        var end   = TimeOnly.Parse(AllowedWindowEnd!);

        return now >= start && now <= end;
    }
}
