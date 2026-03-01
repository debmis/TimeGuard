using System.Diagnostics;
using TimeGuard.Models;

namespace TimeGuard.Services;

/// <summary>
/// Background polling loop. Every 5 seconds it:
///   1. Enumerates running processes
///   2. Accumulates usage time in today's log
///   3. Calls RulesEngine to get actions
///   4. Fires events so the UI layer (App.xaml.cs) can show popups and kill processes
/// </summary>
public sealed class MonitorService : IDisposable
{
    private readonly StorageService _storage;
    private readonly RulesEngine    _rules;
    private readonly CancellationTokenSource _cts = new();

    private AppConfig _config;
    private DailyLog  _log;

    // Key = processName (lowercase), Value = time we first saw it running this tick-cycle
    private readonly Dictionary<string, DateTime> _sessionStarts = new();

    // Events raised on the calling thread via SynchronizationContext
    public event Action<string, string>? BlockRequested;   // (processName, displayName)
    public event Action<string, string>? WarnRequested;    // (processName, displayName)

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public MonitorService(StorageService storage, RulesEngine rules, AppConfig config)
    {
        _storage = storage;
        _rules   = rules;
        _config  = config;
        _log     = storage.LoadTodayLog();
    }

    public void Start() =>
        Task.Run(() => RunLoop(_cts.Token));

    public void ReloadConfig(AppConfig config)
    {
        _config = config;
    }

    private async Task RunLoop(CancellationToken ct)
    {
        var lastDate = DateOnly.FromDateTime(DateTime.Now);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);

                // ── Midnight rollover ─────────────────────────────────────────
                if (today != lastDate)
                {
                    _log = _storage.LoadTodayLog(); // fresh log for new day
                    _sessionStarts.Clear();
                    lastDate = today;
                }

                var runningNames = GetMonitoredRunningProcessNames();
                var now          = TimeOnly.FromDateTime(DateTime.Now);

                // ── Accumulate usage ──────────────────────────────────────────
                AccumulateUsage(runningNames);

                // ── Evaluate rules ────────────────────────────────────────────
                var actions = _rules.Evaluate(runningNames, _log, _config, now);

                foreach (var action in actions)
                {
                    if (action.Kind == RulesEngine.ActionKind.Block)
                    {
                        var entry = _log.GetOrCreate(action.ProcessName);
                        entry.Blocked = true;
                        CloseSession(action.ProcessName);
                        BlockRequested?.Invoke(action.ProcessName, action.DisplayName);
                    }
                    else if (action.Kind == RulesEngine.ActionKind.WarnFiveMinutes)
                    {
                        var entry = _log.GetOrCreate(action.ProcessName);
                        entry.WarningSent = true;
                        WarnRequested?.Invoke(action.ProcessName, action.DisplayName);
                    }
                }

                // ── Re-kill relaunched blocked apps ───────────────────────────
                var relaunched = _rules.GetRelaunched(runningNames, _log, _config);
                foreach (var (procName, displayName) in relaunched)
                    BlockRequested?.Invoke(procName, displayName);

                // ── Update overall total ──────────────────────────────────────
                _log.TotalUsageMinutes = _log.Entries.Sum(e => e.UsageMinutes);
                if (_config.OverallDailyLimitMinutes > 0 &&
                    _log.TotalUsageMinutes >= _config.OverallDailyLimitMinutes)
                    _log.OverallCapHit = true;

                _storage.SaveLog(_log);
            }
            catch (Exception ex)
            {
                // Log to a file but never crash the background loop
                File.AppendAllText(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "TimeGuard", "error.log"),
                    $"[{DateTime.Now:O}] {ex}\n");
            }

            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }
    }

    private IReadOnlyList<string> GetMonitoredRunningProcessNames()
    {
        var monitoredNames = new HashSet<string>(
            _config.Rules.Where(r => r.Enabled)
                         .Select(r => r.ProcessName.ToLowerInvariant()));

        return Process.GetProcesses()
            .Select(p => p.ProcessName.ToLowerInvariant())
            .Where(monitoredNames.Contains)
            .Distinct()
            .ToList();
    }

    private void AccumulateUsage(IReadOnlyList<string> runningNames)
    {
        var runningSet = new HashSet<string>(runningNames);
        var tick       = DateTime.Now;

        // Start new sessions for newly seen processes
        foreach (var name in runningNames)
            if (!_sessionStarts.ContainsKey(name))
            {
                _sessionStarts[name] = tick;
                var entry = _log.GetOrCreate(name);
                entry.Sessions.Add(new Models.SessionEntry { Start = tick });
            }

        // Close sessions for processes that have exited
        foreach (var name in _sessionStarts.Keys.ToList())
            if (!runningSet.Contains(name))
                CloseSession(name);

        // Add elapsed time since last poll for running sessions
        foreach (var name in runningNames)
        {
            if (_log.IsBlocked(name)) continue;
            var entry = _log.GetOrCreate(name);
            // Usage is derived from session end times on save; just update the open session
            var open = entry.Sessions.LastOrDefault(s => s.End is null);
            if (open is not null)
                entry.UsageMinutes = entry.Sessions.Sum(s => s.ElapsedMinutes);
        }
    }

    private void CloseSession(string processName)
    {
        if (!_sessionStarts.ContainsKey(processName)) return;
        _sessionStarts.Remove(processName);

        var entry = _log.GetOrCreate(processName);
        var open  = entry.Sessions.LastOrDefault(s => s.End is null);
        if (open is not null)
            open.End = DateTime.Now;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
