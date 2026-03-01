using Microsoft.Win32;

namespace TimeGuard.Helpers;

/// <summary>
/// Manages the Windows startup registry entry so TimeGuard launches with Windows.
/// Uses HKCU so no admin rights are needed.
/// </summary>
public static class StartupHelper
{
    private const string RegistryKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName    = "TimeGuard";

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: false);
        return key?.GetValue(ValueName) is not null;
    }

    public static void Register()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true)
            ?? throw new InvalidOperationException("Cannot open Run registry key.");
        key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
    }

    public static void Unregister()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
