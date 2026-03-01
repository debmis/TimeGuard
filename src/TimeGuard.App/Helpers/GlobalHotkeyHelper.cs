using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TimeGuard.Helpers;

/// <summary>
/// Registers a system-wide hotkey via Win32 RegisterHotKey.
/// The window that receives WM_HOTKEY must be kept alive for the duration.
/// Usage: create a hidden helper window and pass its HwndSource here.
/// </summary>
public sealed class GlobalHotkeyHelper : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_ALT     = 0x0001;
    private const uint MOD_CTRL    = 0x0002;
    private const uint MOD_SHIFT   = 0x0004;
    private const uint MOD_WIN     = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly IntPtr _hwnd;
    private readonly int    _id;
    private readonly Action _callback;
    private HwndSource?     _source;
    private bool            _disposed;

    public GlobalHotkeyHelper(IntPtr hwnd, int id, string hotkeyString, Action callback)
    {
        _hwnd     = hwnd;
        _id       = id;
        _callback = callback;

        (var modifiers, var vk) = Parse(hotkeyString);

        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);

        if (!RegisterHotKey(hwnd, id, modifiers | MOD_NOREPEAT, vk))
            throw new InvalidOperationException($"Failed to register hotkey '{hotkeyString}'. It may already be in use.");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _id)
        {
            _callback();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static (uint modifiers, uint vk) Parse(string hotkey)
    {
        uint mods = 0;
        uint vk   = 0;

        foreach (var part in hotkey.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":  case "CONTROL": mods |= MOD_CTRL;  break;
                case "ALT":                   mods |= MOD_ALT;   break;
                case "SHIFT":                 mods |= MOD_SHIFT; break;
                case "WIN":                   mods |= MOD_WIN;   break;
                default:
                    if (Enum.TryParse<System.Windows.Input.Key>(part, true, out var key))
                        vk = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return (mods, vk);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterHotKey(_hwnd, _id);
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
