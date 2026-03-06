using System.Diagnostics;
using TimeGuard.UITests.Helpers;
using TimeGuard.Services;
using Xunit;

namespace TimeGuard.UITests;

/// <summary>
/// Tests the BlockedPopup window.
/// The fixture seeds a "notepad" rule that is already over-limit, then starts
/// notepad so MonitorService detects it running and fires BlockRequested.
/// </summary>
public class PopupTests : IClassFixture<PopupTestFixture>
{
    private readonly PopupTestFixture _fx;

    public PopupTests(PopupTestFixture fx) => _fx = fx;

    [Fact]
    public void BlockedPopup_ShowsCorrectTitle()
    {
        _fx.EnsureNotepadRunning();
        var popup = _fx.App.WaitForWindow(_fx.Automation, "Time's Up",
            timeout: TimeSpan.FromSeconds(20));
        Assert.Contains("Time", popup.Title);
        popup.FindButton("OK").Click(); // close so the next test starts clean
        Thread.Sleep(300);
    }

    [Fact]
    public void BlockedPopup_OkButton_ClosesPopup()
    {
        _fx.EnsureNotepadRunning();
        var popup = _fx.App.WaitForWindow(_fx.Automation, "Time's Up",
            timeout: TimeSpan.FromSeconds(20));
        popup.FindButton("OK").Click();
        Thread.Sleep(500);

        var windows = _fx.App.GetAllTopLevelWindows(_fx.Automation);
        Assert.False(windows.Any(w => w.Title?.Contains("Time's Up") == true),
            "BlockedPopup should close after clicking OK.");
    }
}

/// <summary>
/// Fixture that seeds a "notepad" rule already over-limit.
/// Call <see cref="EnsureNotepadRunning"/> in each test so the monitor detects the process.
/// </summary>
public class PopupTestFixture : AppFixture
{
    private Process? _notepad;

    protected override void SeedDatabase()
    {
        var (hash, salt) = HashPassword(TestPassword);
        SaveConfigToDb(hash, salt);

        var db = OpenDb();
        db.SaveRule(new TimeGuard.Models.AppRule
        {
            ProcessName       = "notepad",
            DisplayName       = "Notepad",
            DailyLimitMinutes = 1,
            Enabled           = true
        });

        // Already 2 minutes over the 1-minute limit
        db.UpsertUsageEntry(DateOnly.FromDateTime(DateTime.Today),
            new TimeGuard.Models.UsageEntry
            {
                ProcessName  = "notepad",
                UsageMinutes = 2.0,
                Blocked      = false,
                WarningSent  = false
            });
    }

    /// <summary>Start notepad if it isn't running so the monitor can detect it.</summary>
    public void EnsureNotepadRunning()
    {
        if (Process.GetProcessesByName("notepad").Length == 0)
            _notepad = Process.Start("notepad.exe");
    }

    public new void Dispose()
    {
        try { _notepad?.Kill(); } catch { }
        base.Dispose();
    }
}
