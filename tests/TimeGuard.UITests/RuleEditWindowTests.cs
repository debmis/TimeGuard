using FlaUI.Core.Input;
using TimeGuard.UITests.Helpers;
using Xunit;

namespace TimeGuard.UITests;

/// <summary>
/// Tests for the RuleEditWindow validation rules (Bugs 2 and 3).
/// Opened via Settings → Add Rule.
/// </summary>
public class RuleEditWindowTests : IClassFixture<SeededAppFixture>
{
    private readonly SeededAppFixture _fx;

    public RuleEditWindowTests(SeededAppFixture fx) => _fx = fx;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private FlaUI.Core.AutomationElements.Window OpenSettingsWindow()
    {
        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL);
        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ALT);
        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.SHIFT);
        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_G);
        Thread.Sleep(100);
        Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_G);
        Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.SHIFT);
        Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.ALT);
        Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL);

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

    private FlaUI.Core.AutomationElements.Window OpenRuleEditWindow(
        FlaUI.Core.AutomationElements.Window settings)
    {
        settings.FindButton("➕ Add Rule").Click();
        return _fx.App.WaitForWindow(_fx.Automation, "Edit App Rule");
    }

    private static void FillField(FlaUI.Core.AutomationElements.Window window,
        string automationId, string value)
    {
        var box = window.FindTextBox(automationId);
        box.Click();
        Keyboard.TypeSimultaneously(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL,
            FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_A);
        Keyboard.Type(value);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bug 2: Break every N minutes must be less than the daily limit.
    /// Entering breakEvery = 60 with limit = 60 should show a validation error.
    /// </summary>
    [Fact]
    public void Save_ShowsError_WhenBreakEveryEqualsLimit()
    {
        var settings   = OpenSettingsWindow();
        var ruleWindow = OpenRuleEditWindow(settings);

        FillField(ruleWindow, "DisplayNameBox",   "TestApp");
        FillField(ruleWindow, "ProcessNameBox",   "testapp");
        FillField(ruleWindow, "LimitBox",         "60");
        FillField(ruleWindow, "BreakEveryBox",    "60");   // equals limit — invalid
        FillField(ruleWindow, "BreakDurationBox", "5");

        ruleWindow.FindButton("Save").Click();
        Thread.Sleep(300);

        // Window must still be open (save was blocked)
        var windows = _fx.App.GetAllTopLevelWindows(_fx.Automation);
        Assert.True(windows.Any(w => w.Title?.Contains("Edit App Rule") == true),
            "RuleEditWindow should remain open when validation fails.");

        // Error text must mention the break interval / daily limit
        var error = ruleWindow.FindTextContaining("Break interval");
        Assert.NotNull(error);

        ruleWindow.Close();
        settings.Close();
    }

    /// <summary>
    /// Bug 2: Break every N minutes greater than daily limit also invalid.
    /// </summary>
    [Fact]
    public void Save_ShowsError_WhenBreakEveryExceedsLimit()
    {
        var settings   = OpenSettingsWindow();
        var ruleWindow = OpenRuleEditWindow(settings);

        FillField(ruleWindow, "DisplayNameBox",   "TestApp");
        FillField(ruleWindow, "ProcessNameBox",   "testapp");
        FillField(ruleWindow, "LimitBox",         "60");
        FillField(ruleWindow, "BreakEveryBox",    "90");   // exceeds limit — invalid
        FillField(ruleWindow, "BreakDurationBox", "5");

        ruleWindow.FindButton("Save").Click();
        Thread.Sleep(300);

        var error = ruleWindow.FindTextContaining("Break interval");
        Assert.NotNull(error);

        ruleWindow.Close();
        settings.Close();
    }

    /// <summary>
    /// Bug 2: When daily limit = 0 (no limit) and breakEvery > 0, no error should appear.
    /// </summary>
    [Fact]
    public void Save_NoError_WhenNoLimitSet_AndBreakEveryHasValue()
    {
        var settings   = OpenSettingsWindow();
        var ruleWindow = OpenRuleEditWindow(settings);

        FillField(ruleWindow, "DisplayNameBox",   "TestApp");
        FillField(ruleWindow, "ProcessNameBox",   "testapp");
        FillField(ruleWindow, "LimitBox",         "0");    // no limit
        FillField(ruleWindow, "BreakEveryBox",    "60");
        FillField(ruleWindow, "BreakDurationBox", "10");

        ruleWindow.FindButton("Save").Click();
        Thread.Sleep(300);

        // Window should have closed — save succeeded
        var windows = _fx.App.GetAllTopLevelWindows(_fx.Automation);
        Assert.False(windows.Any(w => w.Title?.Contains("Edit App Rule") == true),
            "RuleEditWindow should close when no daily limit is set.");

        settings.Close();
    }

    /// <summary>
    /// Bug 3: Break duration greater than break interval must show a validation error.
    /// </summary>
    [Fact]
    public void Save_ShowsError_WhenBreakDurationExceedsBreakEvery()
    {
        var settings   = OpenSettingsWindow();
        var ruleWindow = OpenRuleEditWindow(settings);

        FillField(ruleWindow, "DisplayNameBox",   "TestApp");
        FillField(ruleWindow, "ProcessNameBox",   "testapp");
        FillField(ruleWindow, "LimitBox",         "120");
        FillField(ruleWindow, "BreakEveryBox",    "30");
        FillField(ruleWindow, "BreakDurationBox", "45");  // > breakEvery — invalid

        ruleWindow.FindButton("Save").Click();
        Thread.Sleep(300);

        var windows = _fx.App.GetAllTopLevelWindows(_fx.Automation);
        Assert.True(windows.Any(w => w.Title?.Contains("Edit App Rule") == true),
            "RuleEditWindow should remain open when break duration exceeds break interval.");

        var error = ruleWindow.FindTextContaining("Break duration");
        Assert.NotNull(error);

        ruleWindow.Close();
        settings.Close();
    }

    /// <summary>
    /// Bug 3: Break duration equal to break interval is valid.
    /// </summary>
    [Fact]
    public void Save_NoError_WhenBreakDurationEqualsBreakEvery()
    {
        var settings   = OpenSettingsWindow();
        var ruleWindow = OpenRuleEditWindow(settings);

        FillField(ruleWindow, "DisplayNameBox",   "TestApp");
        FillField(ruleWindow, "ProcessNameBox",   "testapp");
        FillField(ruleWindow, "LimitBox",         "120");
        FillField(ruleWindow, "BreakEveryBox",    "30");
        FillField(ruleWindow, "BreakDurationBox", "30");  // equals breakEvery — valid

        ruleWindow.FindButton("Save").Click();
        Thread.Sleep(300);

        var windows = _fx.App.GetAllTopLevelWindows(_fx.Automation);
        Assert.False(windows.Any(w => w.Title?.Contains("Edit App Rule") == true),
            "RuleEditWindow should close when break duration equals break interval.");

        settings.Close();
    }
}
