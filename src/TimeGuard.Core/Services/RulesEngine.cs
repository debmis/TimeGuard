using TimeGuard.Models;

namespace TimeGuard.Services;

/// <summary>
/// Pure evaluation logic — no side effects, fully testable.
/// Given the current state, returns a list of actions the monitor should take.
/// </summary>
public class RulesEngine
{
    public enum ActionKind { Block, WarnFiveMinutes }

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
        TimeOnly now)
    {
        var actions = new List<RuleAction>();
        var running = new HashSet<string>(
            runningProcessNames.Select(p => p.ToLowerInvariant()));

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
        foreach (var rule in config.Rules.Where(r => r.Enabled))
        {
            var key = rule.ProcessName.ToLowerInvariant();
            if (!running.Contains(key)) continue;

            var entry = log.GetOrCreate(rule.ProcessName);
            if (entry.Blocked) continue; // already handled

            // Time-of-day window violation → immediate block
            if (rule.HasTimeWindow && !rule.IsWithinAllowedWindow(now))
            {
                actions.Add(new RuleAction(ActionKind.Block, rule.ProcessName, rule.DisplayName,
                    $"{rule.DisplayName} is not allowed at this time (allowed: {rule.AllowedWindowStart}–{rule.AllowedWindowEnd})."));
                continue;
            }

            if (rule.HasDailyLimit)
            {
                var used = entry.UsageMinutes;

                if (used >= rule.DailyLimitMinutes)
                {
                    actions.Add(new RuleAction(ActionKind.Block, rule.ProcessName, rule.DisplayName,
                        $"Daily limit of {rule.DailyLimitMinutes} min reached for {rule.DisplayName}."));
                }
                else
                {
                    var remaining = rule.DailyLimitMinutes - used;
                    if (remaining <= WarningThresholdMinutes && !entry.WarningSent)
                        actions.Add(new RuleAction(ActionKind.WarnFiveMinutes, rule.ProcessName,
                            rule.DisplayName, $"{rule.DisplayName} has ~{remaining:F0} minutes left today."));
                }
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
