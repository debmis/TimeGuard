namespace TimeGuard.Models;

/// <summary>
/// A rule for a single monitored application.
/// </summary>
public class AppRule
{
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

    /// <summary>Returns true if the current time falls inside the allowed window.</summary>
    public bool IsWithinAllowedWindow(TimeOnly now)
    {
        if (!HasTimeWindow) return true;

        var start = TimeOnly.Parse(AllowedWindowStart!);
        var end   = TimeOnly.Parse(AllowedWindowEnd!);

        return now >= start && now <= end;
    }
}
