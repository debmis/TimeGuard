using System.Text.Json.Serialization;

namespace TimeGuard.Models;

/// <summary>
/// Root configuration stored in %AppData%\TimeGuard\config.json
/// </summary>
public class AppConfig
{
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>Global hotkey to open settings (e.g. "Ctrl+Alt+Shift+G")</summary>
    public string SettingsHotkey { get; set; } = "Ctrl+Alt+Shift+G";

    /// <summary>Total daily cap across ALL monitored apps, in minutes. 0 = no cap.</summary>
    public int OverallDailyLimitMinutes { get; set; } = 0;

    public List<AppRule> Rules { get; set; } = [];

    [JsonIgnore]
    public bool IsFirstRun => string.IsNullOrEmpty(PasswordHash);
}
