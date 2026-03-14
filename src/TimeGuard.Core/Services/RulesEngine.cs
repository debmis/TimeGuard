using TimeGuard.Models;

namespace TimeGuard.Services;

/// <summary>
/// Pure evaluation logic — no side effects, fully testable.
/// Given the current state, returns a list of actions the monitor should take.
/// </summary>
public class RulesEngine
{
    public enum ActionKind { Block, WarnFiveMinutes, BreakDue }

    public record RuleAction(ActionKind Kind, string ProcessName, string DisplayName, string Reason);

    private const double WarningThresholdMinutes = 5.0;

    /// <summary>
    /// Evaluates all rules and returns the actions that need to be taken RIGHT NOW.
    /// Called on every monitor tick.
    /// </summary>
    public IReadOnlyList<RuleAction> Evaluate(
        IEnumerable<string> runningProcessNames,
        DailyLog log,
        AppConfig config,
        TimeOnly now,
        IReadOnlyDictionary<string, double>? breakTimers = null)
    {
        var actions = new List<RuleAction>();
        var running = new HashSet<string>(
            runningProcessNames.Select(p => p.ToLowerInvariant()));
        breakTimers ??= new Dictionary<string, double>();

        // ── Check overall cap ─────────────────────────────────────────────────
        if (config.OverallDailyLimitMinutes > 0 && !log.OverallCapHit)
        {
            if (log.TotalUsageMinutes >= config.OverallDailyLimitMinutes)
            {
                // Block every monitored app that is currently running
                foreach (var rule in config.Rules.Where(r => r.Enabled))
                {
                    if (running.Contains(rule.ProcessName.ToLowerInvariant()))
                        actions.Add(new RuleAction(ActionKind.Block, rule.ProcessName, rule.DisplayName,
                            $"Overall daily limit of {config.OverallDailyLimitMinutes} min reached."));
                }
                return actions; // overall cap supersedes per-app checks
            }

            // Warn if overall cap is within 5 minutes
            var remaining = config.OverallDailyLimitMinutes - log.TotalUsageMinutes;
            if (remaining <= WarningThresholdMinutes)
            {
                foreach (var rule in config.Rules.Where(r => r.Enabled))
                {
                    if (running.Contains(rule.ProcessName.ToLowerInvariant()))
                    {
                        var entry = log.GetOrCreate(rule.ProcessName);
                        if (!entry.WarningSent)
                            actions.Add(new RuleAction(ActionKind.WarnFiveMinutes, rule.ProcessName,
                                rule.DisplayName, $"Overall daily limit is almost reached."));
                    }
                }
            }
        }

        // ── Per-app rules ─────────────────────────────────────────────────────
        var currentDay = log.Date.DayOfWeek;
        foreach (var rule in config.Rules.Where(r => r.Enabled))
        {
            var key = rule.ProcessName.ToLowerInvariant();
            if (!running.Contains(key)) continue;

            var entry = log.GetOrCreate(rule.ProcessName);
            if (entry.Blocked) continue; // already handled
            var daySchedule = rule.GetScheduleForDay(currentDay);

            // Time-of-day window violation → immediate block
            if (daySchedule.HasTimeWindow && !daySchedule.IsWithinAllowedWindow(now))
            {
                actions.Add(new RuleAction(ActionKind.Block, rule.ProcessName, rule.DisplayName,
                    $"{rule.DisplayName} is not allowed at this time on {currentDay} (allowed: {daySchedule.AllowedWindowStart}-{daySchedule.AllowedWindowEnd})."));
                continue;
            }

            if (daySchedule.HasDailyLimit)
            {
                var used = entry.UsageMinutes;

                if (used >= daySchedule.DailyLimitMinutes)
                {
                    actions.Add(new RuleAction(ActionKind.Block, rule.ProcessName, rule.DisplayName,
                        $"Daily limit of {daySchedule.DailyLimitMinutes} min reached for {rule.DisplayName} on {currentDay}."));
                }
                else
                {
                    var remaining = daySchedule.DailyLimitMinutes - used;
                    if (remaining <= WarningThresholdMinutes && !entry.WarningSent)
                        actions.Add(new RuleAction(ActionKind.WarnFiveMinutes, rule.ProcessName,
                            rule.DisplayName, $"{rule.DisplayName} has ~{remaining:F0} minutes left today."));
                }
            }

            // Break schedule check — uses TimeSinceBreakMinutes passed in via context
            if (rule.HasBreakSchedule &&
                breakTimers.TryGetValue(key, out var sinceBreak) &&
                sinceBreak >= rule.BreakEveryMinutes)
            {
                actions.Add(new RuleAction(ActionKind.BreakDue, rule.ProcessName, rule.DisplayName,
                    $"Break time! {rule.BreakDurationMinutes} min break required for {rule.DisplayName}."));
            }
        }

        return actions;
    }

    /// <summary>
    /// Returns process names that are blocked for today and currently running
    /// (so the monitor can re-kill them if the child relaunched them).
    /// </summary>
    public IReadOnlyList<(string ProcessName, string DisplayName)> GetRelaunched(
        IEnumerable<string> runningProcessNames,
        DailyLog log,
        AppConfig config)
    {
        var running = new HashSet<string>(
            runningProcessNames.Select(p => p.ToLowerInvariant()));

        var result = new List<(string, string)>();

        if (log.OverallCapHit)
        {
            foreach (var rule in config.Rules.Where(r => r.Enabled))
                if (running.Contains(rule.ProcessName.ToLowerInvariant()))
                    result.Add((rule.ProcessName, rule.DisplayName));
            return result;
        }

        foreach (var entry in log.Entries.Where(e => e.Blocked))
            if (running.Contains(entry.ProcessName.ToLowerInvariant()))
            {
                var rule = config.Rules.FirstOrDefault(r =>
                    r.ProcessName.Equals(entry.ProcessName, StringComparison.OrdinalIgnoreCase));
                result.Add((entry.ProcessName, rule?.DisplayName ?? entry.ProcessName));
            }

        return result;
    }
}
