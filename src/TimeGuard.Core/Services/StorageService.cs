using System.Text.Json;
using System.Text.Json.Serialization;
using TimeGuard.Models;

namespace TimeGuard.Services;

/// <summary>
/// Reads and writes config.json and daily log files under %AppData%\TimeGuard\.
/// All public methods are thread-safe.
/// </summary>
public class StorageService
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TimeGuard");

    private static readonly string ConfigPath = Path.Combine(DataDir, "config.json");
    private static readonly string LogsDir    = Path.Combine(DataDir, "logs");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _lock = new();

    public StorageService()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogsDir);
    }

    // ── Config ────────────────────────────────────────────────────────────────

    public AppConfig LoadConfig()
    {
        lock (_lock)
        {
            if (!File.Exists(ConfigPath))
                return new AppConfig();

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
        }
    }

    public void SaveConfig(AppConfig config)
    {
        lock (_lock)
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOpts));
        }
    }

    // ── Daily Log ─────────────────────────────────────────────────────────────

    public DailyLog LoadTodayLog()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return LoadLog(today);
    }

    public DailyLog LoadLog(DateOnly date)
    {
        lock (_lock)
        {
            var path = LogPath(date);
            if (!File.Exists(path))
                return new DailyLog { Date = date };

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<DailyLog>(json, JsonOpts)
                   ?? new DailyLog { Date = date };
        }
    }

    public void SaveLog(DailyLog log)
    {
        lock (_lock)
        {
            File.WriteAllText(LogPath(log.Date), JsonSerializer.Serialize(log, JsonOpts));
        }
    }

    // ── History ───────────────────────────────────────────────────────────────

    /// <summary>Returns all daily logs sorted newest-first (for the settings UI history view).</summary>
    public IReadOnlyList<DailyLog> LoadRecentLogs(int days = 30)
    {
        var result = new List<DailyLog>();
        var today = DateOnly.FromDateTime(DateTime.Now);

        for (int i = 0; i < days; i++)
        {
            var date = today.AddDays(-i);
            var path = LogPath(date);
            if (File.Exists(path))
                result.Add(LoadLog(date));
        }
        return result;
    }

    private static string LogPath(DateOnly date) =>
        Path.Combine(LogsDir, $"{date:yyyy-MM-dd}.json");
}
