using System.Diagnostics;
using TimeGuard.Models;

namespace TimeGuard.Services;

/// <summary>
/// Background polling loop. Every 5 seconds it:
///   1. Enumerates running processes
///   2. Accumulates usage time in today's DailyUsage rows
///   3. Tracks time-since-last-break per session
///   4. Calls RulesEngine to get actions
///   5. Fires events so App.xaml.cs can show popups and kill processes
/// </summary>
public sealed class MonitorService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly RulesEngine     _rules;
    private readonly CancellationTokenSource _cts = new();

    private AppConfig _config;
    private DailyLog  _log;

    // processName (lowercase) → (sessionDbId, timeSinceBreakMins)
    private readonly Dictionary<string, (int SessionId, double TimeSinceBreak)> _sessions = new();

    public event Action<string, string>? BlockRequested;      // (processName, displayName)
    public event Action<string, string>? WarnRequested;       // (processName, displayName)
    public event Action<string, string, int>? BreakRequested; // (processName, displayName, sessionId)

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const double PollMinutes = 5.0 / 60.0;

    public MonitorService(DatabaseService db, RulesEngine rules, AppConfig config)
    {
        _db     = db;
        _rules  = rules;
        _config = config;
        _log    = db.LoadTodayLog();
    }

    public void Start() => Task.Run(() => RunLoop(_cts.Token));

    public void ReloadConfig(AppConfig config) => _config = config;

    /// <summary>Called by App.xaml.cs after the break overlay is dismissed.</summary>
    public void OnBreakCompleted(string processName)
    {
        var key = processName.ToLowerInvariant();
        if (_sessions.TryGetValue(key, out var s))
        {
            _db.ResetBreakTimer(s.SessionId);
            _sessions[key] = (s.SessionId, 0);
        }
    }

    private async Task RunLoop(CancellationToken ct)
    {
        var lastDate = DateOnly.FromDateTime(DateTime.Now);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);

                if (today != lastDate)
                {
                    _log = _db.LoadTodayLog();
                    CloseAllSessions();
                    lastDate = today;
                }

                var runningNames = GetMonitoredRunningProcessNames();
                var now          = TimeOnly.FromDateTime(DateTime.Now);

                AccumulateUsage(runningNames);

                var breakTimers = _sessions.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.TimeSinceBreak);
                var actions = _rules.Evaluate(runningNames, _log, _config, now, breakTimers);

                foreach (var action in actions)
                {
                    var entry = _log.GetOrCreate(action.ProcessName);

                    if (action.Kind == RulesEngine.ActionKind.Block)
                    {
                        entry.Blocked = true;
                        CloseSession(action.ProcessName);
                        _db.UpsertUsageEntry(today, entry);
                        BlockRequested?.Invoke(action.ProcessName, action.DisplayName);
                    }
                    else if (action.Kind == RulesEngine.ActionKind.WarnFiveMinutes)
                    {
                        entry.WarningSent = true;
                        _db.UpsertUsageEntry(today, entry);
                        WarnRequested?.Invoke(action.ProcessName, action.DisplayName);
                    }
                    else if (action.Kind == RulesEngine.ActionKind.BreakDue)
                    {
                        if (_sessions.TryGetValue(action.ProcessName.ToLowerInvariant(), out var s))
                            BreakRequested?.Invoke(action.ProcessName, action.DisplayName, s.SessionId);
                    }
                }

                foreach (var (procName, displayName) in _rules.GetRelaunched(runningNames, _log, _config))
                    BlockRequested?.Invoke(procName, displayName);

                _log.TotalUsageMinutes = _log.Entries.Sum(e => e.UsageMinutes);
                if (_config.OverallDailyLimitMinutes > 0 &&
                    _log.TotalUsageMinutes >= _config.OverallDailyLimitMinutes)
                    _log.OverallCapHit = true;
            }
            catch (Exception ex)
            {
                File.AppendAllText(
                    Path.Combine(DatabaseService.DataDir, "error.log"),
                    $"[{DateTime.Now:O}] {ex}\n");
            }

            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }
    }

    private IReadOnlyList<string> GetMonitoredRunningProcessNames()
    {
        var monitored = new HashSet<string>(
            _config.Rules.Where(r => r.Enabled)
                         .Select(r => r.ProcessName.ToLowerInvariant()));

        return Process.GetProcesses()
            .Select(p => p.ProcessName.ToLowerInvariant())
            .Where(monitored.Contains)
            .Distinct()
            .ToList();
    }

    private void AccumulateUsage(IReadOnlyList<string> runningNames)
    {
        var runningSet = new HashSet<string>(runningNames);
        var today      = DateOnly.FromDateTime(DateTime.Now);

        foreach (var name in runningNames)
            if (!_sessions.ContainsKey(name))
                _sessions[name] = (_db.OpenSession(name), 0);

        foreach (var name in _sessions.Keys.ToList())
            if (!runningSet.Contains(name))
                CloseSession(name);

        foreach (var name in runningNames)
        {
            if (_log.IsBlocked(name)) continue;

            var entry = _log.GetOrCreate(name);
            entry.UsageMinutes += PollMinutes;

            if (_sessions.TryGetValue(name, out var s))
            {
                var newBreak = s.TimeSinceBreak + PollMinutes;
                _sessions[name] = (s.SessionId, newBreak);
                _db.UpdateSession(s.SessionId, newBreak);
            }

            _db.UpsertUsageEntry(today, entry);
        }
    }

    private void CloseSession(string processName)
    {
        if (!_sessions.TryGetValue(processName, out var s)) return;
        _db.CloseSession(s.SessionId, s.TimeSinceBreak);
        _sessions.Remove(processName);
    }

    private void CloseAllSessions()
    {
        foreach (var name in _sessions.Keys.ToList())
            CloseSession(name);
    }

    public void Dispose()
    {
        CloseAllSessions();
        _cts.Cancel();
        _cts.Dispose();
    }
}
