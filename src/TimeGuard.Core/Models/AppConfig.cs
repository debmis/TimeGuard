namespace TimeGuard.Models;

/// <summary>
/// In-memory representation of app configuration loaded from the SQLite Settings table.
/// </summary>
public class AppConfig
{
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string SettingsHotkey { get; set; } = "Ctrl+Alt+Shift+G";
    public int OverallDailyLimitMinutes { get; set; } = 0;
    public List<AppRule> Rules { get; set; } = [];

    public bool IsFirstRun => string.IsNullOrEmpty(PasswordHash);
}
