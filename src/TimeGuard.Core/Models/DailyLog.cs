namespace TimeGuard.Models;

/// <summary>
/// One continuous session of a monitored process being active.
/// </summary>
public class SessionEntry
{
    public int Id { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public DateTime Start { get; set; }
    public DateTime? End { get; set; }   // null while session is still running

    /// <summary>Minutes of play accumulated since the last break (resets after each break).</summary>
    public double TimeSinceBreakMinutes { get; set; } = 0;

    public double ElapsedMinutes =>
        (End ?? DateTime.Now).Subtract(Start).TotalMinutes;
}

/// <summary>
/// Per-app usage for a single calendar day.
/// </summary>
public class UsageEntry
{
    public int Id { get; set; }
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Accumulated usage in minutes (sum of all sessions).</summary>
    public double UsageMinutes { get; set; } = 0;

    /// <summary>True once the process has been killed and blocked for today.</summary>
    public bool Blocked { get; set; } = false;

    /// <summary>True once the 5-minute warning has been shown (avoid repeated popups).</summary>
    public bool WarningSent { get; set; } = false;
}

/// <summary>
/// Aggregated daily state — composed in memory from the DB, not stored as a single row.
/// </summary>
public class DailyLog
{
    public DateOnly Date { get; set; }

    /// <summary>Combined usage across all monitored apps (for overall cap check).</summary>
    public double TotalUsageMinutes { get; set; } = 0;

    /// <summary>True once the overall daily cap has been hit.</summary>
    public bool OverallCapHit { get; set; } = false;

    public List<UsageEntry> Entries { get; set; } = [];

    // ── helpers ──────────────────────────────────────────────────────────────

    public UsageEntry GetOrCreate(string processName)
    {
        var entry = Entries.FirstOrDefault(e =>
            e.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            entry = new UsageEntry { ProcessName = processName.ToLowerInvariant() };
            Entries.Add(entry);
        }
        return entry;
    }

    public bool IsBlocked(string processName) =>
        OverallCapHit ||
        Entries.Any(e => e.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase) && e.Blocked);
}
