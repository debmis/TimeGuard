using System.Diagnostics;
using System.Windows;
using TimeGuard.Helpers;
using TimeGuard.Services;

namespace TimeGuard;

public partial class App : Application
{
    private StorageService? _storage;
    private MonitorService? _monitor;
    private GlobalHotkeyHelper? _hotkey;

    // Hidden message-only window that hosts the hotkey WndProc
    private System.Windows.Window? _helperWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _storage = new StorageService();
        var config = _storage.LoadConfig();

        if (config.IsFirstRun)
        {
            var setup = new UI.FirstRunWindow(_storage);
            if (setup.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
            config = _storage.LoadConfig();
        }

        // Ensure registered at startup
        if (!StartupHelper.IsRegistered())
            StartupHelper.Register();

        // Start background monitor
        var rules = new RulesEngine();
        _monitor = new MonitorService(_storage, rules, config);
        _monitor.BlockRequested += OnBlockRequested;
        _monitor.WarnRequested  += OnWarnRequested;
        _monitor.Start();

        // Create a hidden message-only helper window to receive WM_HOTKEY
        _helperWindow = new System.Windows.Window
        {
            Width = 0, Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false
        };
        _helperWindow.Show();   // creates the HWND
        _helperWindow.Hide();   // immediately invisible

        var hwnd = new System.Windows.Interop.WindowInteropHelper(_helperWindow).Handle;
        try
        {
            _hotkey = new GlobalHotkeyHelper(hwnd, 1, config.SettingsHotkey, OpenSettings);
        }
        catch
        {
            // Hotkey already taken — silently continue; settings still accessible other ways
        }
    }

    private void OnBlockRequested(string processName, string displayName)
    {
        Dispatcher.Invoke(() =>
        {
            KillProcess(processName);
            var popup = new UI.BlockedPopup(displayName);
            popup.Show();
        });
    }

    private void OnWarnRequested(string processName, string displayName)
    {
        Dispatcher.Invoke(() =>
        {
            var popup = new UI.WarningPopup(displayName);
            popup.Show();
        });
    }

    private static void KillProcess(string processName)
    {
        foreach (var proc in Process.GetProcessesByName(processName))
        {
            try { proc.Kill(); } catch { /* process may have exited */ }
        }
    }

    private void OpenSettings()
    {
        var prompt = new UI.PasswordPromptWindow();
        if (prompt.ShowDialog() != true) return;

        var settings = new UI.SettingsWindow(_storage!);
        settings.ShowDialog();

        // Reload config after settings may have changed
        var updated = _storage!.LoadConfig();
        _monitor?.ReloadConfig(updated);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _monitor?.Dispose();
        base.OnExit(e);
    }
}
