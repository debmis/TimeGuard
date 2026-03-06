using FlaUI.Core.Input;
using TimeGuard.UITests.Helpers;
using Xunit;

namespace TimeGuard.UITests;

/// <summary>
/// Tests the SettingsWindow (rules management, daily cap, save/cancel).
/// Uses <see cref="SeededAppFixture"/> so the app starts with a known password
/// and the first-run window is skipped.
/// </summary>
public class SettingsWindowTests : IClassFixture<SeededAppFixture>
{
    private readonly SeededAppFixture _fx;

    public SettingsWindowTests(SeededAppFixture fx) => _fx = fx;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Triggers the settings hotkey (Ctrl+Alt+Shift+G), types the password in the
    /// PasswordPromptWindow, clicks Unlock, then waits for SettingsWindow.
    /// Returns the SettingsWindow.
    /// </summary>
    private FlaUI.Core.AutomationElements.Window OpenSettingsWindow()
    {
        // Fire the global hotkey
        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL);
        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ALT);
        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.SHIFT);
        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_G);
        Thread.Sleep(100);
        Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_G);
        Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.SHIFT);
        Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.ALT);
        Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL);

        // Wait for PasswordPromptWindow
        var prompt = _fx.App.WaitForWindow(_fx.Automation, "Parent Access");

        var passwordBoxes = prompt.FindAllDescendants(cf =>
            cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));
        if (passwordBoxes.Length > 0)
        {
            passwordBoxes[0].Click();
            Keyboard.Type(AppFixture.TestPassword);
        }

        prompt.FindButton("Unlock").Click();

        return _fx.App.WaitForWindow(_fx.Automation, "TimeGuard Settings");
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Settings_OpensAfterCorrectPassword()
    {
        var settings = OpenSettingsWindow();
        Assert.Contains("Settings", settings.Title);
        settings.Close();
    }

    [Fact]
    public void Settings_AddRuleButton_IsPresent()
    {
        var settings = OpenSettingsWindow();
        var addBtn = settings.FindButton("➕ Add Rule");
        Assert.NotNull(addBtn);
        settings.Close();
    }

    [Fact]
    public void Settings_SaveButton_IsPresent()
    {
        var settings = OpenSettingsWindow();
        var saveBtn = settings.FindButton("Save");
        Assert.NotNull(saveBtn);
        settings.Close();
    }

    [Fact]
    public void Settings_CancelButton_ClosesWindow()
    {
        var settings = OpenSettingsWindow();
        settings.FindButton("Cancel").Click();
        Thread.Sleep(500);

        var windows = _fx.App.GetAllTopLevelWindows(_fx.Automation);
        Assert.False(windows.Any(w => w.Title?.Contains("Settings") == true),
            "SettingsWindow should close on Cancel.");
    }

    [Fact]
    public void Settings_DashboardButton_OpensDashboard()
    {
        var settings = OpenSettingsWindow();
        settings.FindButton("📊 Dashboard").Click();

        var dashboard = _fx.App.WaitForWindow(_fx.Automation, "Usage Dashboard");
        Assert.Contains("Dashboard", dashboard.Title);

        dashboard.Close();
        settings.Close();
    }
}
