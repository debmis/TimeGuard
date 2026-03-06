using FlaUI.Core.Input;
using TimeGuard.UITests.Helpers;
using Xunit;

namespace TimeGuard.UITests;

/// <summary>
/// Tests the FirstRunWindow that appears when the app has no password configured.
/// Each test creates its own AppFixture (fresh blank DB) so ordering never matters.
/// </summary>
public class FirstRunWindowTests : IDisposable
{
    private readonly AppFixture _fx = new();
    public void Dispose() => _fx.Dispose();

    [Fact]
    public void App_ShowsFirstRunWindow_OnFreshDb()
    {
        var win = _fx.App.WaitForWindow(_fx.Automation, "Welcome to TimeGuard");
        Assert.Contains("Welcome", win.Title);
    }

    [Fact]
    public void FirstRun_MismatchedPasswords_ShowsError()
    {
        var win = _fx.App.WaitForWindow(_fx.Automation, "Welcome to TimeGuard");

        // Type mismatched passwords using keyboard — PasswordBoxes don't expose Text via UIA
        var boxes = win.FindAllDescendants(cf =>
            cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));

        if (boxes.Length >= 2)
        {
            boxes[0].Click();
            Keyboard.Type("abc123");
            boxes[1].Click();
            Keyboard.Type("different");
        }

        win.FindButton("Get Started →").Click();
        Thread.Sleep(300);

        // ErrorText becomes visible on mismatch
        var errorEl = win.FindAllDescendants()
            .FirstOrDefault(e => e.Name?.Contains("match", StringComparison.OrdinalIgnoreCase) == true
                              || e.Name?.Contains("do not", StringComparison.OrdinalIgnoreCase) == true);
        Assert.NotNull(errorEl);
    }

    [Fact]
    public void FirstRun_ValidPassword_ClosesFirstRunWindow()
    {
        var win = _fx.App.WaitForWindow(_fx.Automation, "Welcome to TimeGuard");

        var boxes = win.FindAllDescendants(cf =>
            cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));

        Assert.True(boxes.Length >= 2, $"Expected 2+ PasswordBox controls, found {boxes.Length}.");

        boxes[0].Click();
        Thread.Sleep(100);
        Keyboard.Type(AppFixture.TestPassword);
        Thread.Sleep(100);

        boxes[1].Click();
        Thread.Sleep(100);
        Keyboard.Type(AppFixture.TestPassword);
        Thread.Sleep(100);

        win.FindButton("Get Started →").Click();

        // Poll until the FirstRunWindow disappears (up to 5s)
        var deadline = DateTime.UtcNow.AddSeconds(5);
        bool closed = false;
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(200);
            try
            {
                var windows = _fx.App.GetAllTopLevelWindows(_fx.Automation);
                closed = !windows.Any(w =>
                    w.Title?.Contains("Welcome to TimeGuard", StringComparison.OrdinalIgnoreCase) == true);
                if (closed) break;
            }
            catch { /* process may be transitioning */ }
        }

        Assert.True(closed, "FirstRunWindow should have closed after valid password setup.");
    }
}
