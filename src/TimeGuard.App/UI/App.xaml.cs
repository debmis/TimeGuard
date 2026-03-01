using System.Diagnostics;
using System.Windows;
using TimeGuard.Helpers;
using TimeGuard.Services;

// Disambiguate WPF vs WinForms Application
using WpfApplication = System.Windows.Application;

namespace TimeGuard;

public partial class App : WpfApplication
{
    private DatabaseService? _db;
    private MonitorService?  _monitor;
    private GlobalHotkeyHelper? _hotkey;
    private System.Windows.Window? _helperWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _db = new DatabaseService();
        var config = _db.LoadConfig();

        if (config.IsFirstRun)
        {
            var setup = new UI.FirstRunWindow(_db);
            if (setup.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
            config = _db.LoadConfig();
        }

        if (!StartupHelper.IsRegistered())
            StartupHelper.Register();

        var rules = new RulesEngine();
        _monitor = new MonitorService(_db, rules, config);
        _monitor.BlockRequested += OnBlockRequested;
        _monitor.WarnRequested  += OnWarnRequested;
        _monitor.BreakRequested += OnBreakRequested;
        _monitor.Start();

        _helperWindow = new System.Windows.Window
        {
            Width = 0, Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false
        };
        _helperWindow.Show();
        _helperWindow.Hide();

        var hwnd = new System.Windows.Interop.WindowInteropHelper(_helperWindow).Handle;
        try
        {
            _hotkey = new GlobalHotkeyHelper(hwnd, 1, config.SettingsHotkey, OpenSettings);
        }
        catch { /* hotkey already taken — silently continue */ }
    }

    private void OnBlockRequested(string processName, string displayName)
    {
        Dispatcher.Invoke(() =>
        {
            KillProcess(processName);
            new UI.BlockedPopup(displayName).Show();
        });
    }

    private void OnWarnRequested(string processName, string displayName)
    {
        Dispatcher.Invoke(() => new UI.WarningPopup(displayName).Show());
    }

    private void OnBreakRequested(string processName, string displayName, int sessionId)
    {
        Dispatcher.Invoke(() =>
        {
            var rule = _db!.GetRules()
                .FirstOrDefault(r => r.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
            if (rule is null) return;

            var overlay = new UI.BreakOverlay(displayName, rule.BreakDurationMinutes);
            overlay.ShowDialog();
            _monitor?.OnBreakCompleted(processName);
        });
    }

    private static void KillProcess(string processName)
    {
        foreach (var proc in Process.GetProcessesByName(processName))
            try { proc.Kill(); } catch { }
    }

    private void OpenSettings()
    {
        var prompt = new UI.PasswordPromptWindow();
        if (prompt.ShowDialog() != true) return;

        var settings = new UI.SettingsWindow(_db!);
        settings.ShowDialog();

        var updated = _db!.LoadConfig();
        _monitor?.ReloadConfig(updated);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _monitor?.Dispose();
        base.OnExit(e);
    }
}
