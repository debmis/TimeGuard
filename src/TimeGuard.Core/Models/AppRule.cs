namespace TimeGuard.Models;

/// <summary>
/// A rule for a single monitored application.
/// </summary>
public class AppRule
{
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

    public bool Enabled { get; set; } = true;

    // ── helpers ──────────────────────────────────────────────────────────────

    public bool HasTimeWindow =>
        AllowedWindowStart is not null && AllowedWindowEnd is not null;

    public bool HasDailyLimit => DailyLimitMinutes > 0;

    /// <summary>Returns true if the current time falls inside the allowed window.</summary>
    public bool IsWithinAllowedWindow(TimeOnly now)
    {
        if (!HasTimeWindow) return true;

        var start = TimeOnly.Parse(AllowedWindowStart!);
        var end   = TimeOnly.Parse(AllowedWindowEnd!);

        // Supports windows that don't cross midnight (e.g. 15:00–20:00)
        return now >= start && now <= end;
    }
}
