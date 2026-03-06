using FlaUI.Core;
using FlaUI.Core.AutomationElements;

namespace TimeGuard.UITests.Helpers;

/// <summary>
/// Extension helpers for finding UI elements in FlaUI automation trees.
/// </summary>
public static class WindowHelpers
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Waits until a top-level window with the given title substring appears.
    /// </summary>
    public static Window WaitForWindow(this Application app, AutomationBase automation,
        string titleContains, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow.Add(timeout ?? DefaultTimeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var windows = app.GetAllTopLevelWindows(automation);
                var match = windows.FirstOrDefault(w =>
                    w.Title?.Contains(titleContains, StringComparison.OrdinalIgnoreCase) == true);
                if (match is not null)
                    return match;
            }
            catch { /* window not ready yet */ }
            Thread.Sleep(100);
        }
        throw new TimeoutException($"Window containing '{titleContains}' did not appear within {(timeout ?? DefaultTimeout).TotalSeconds}s.");
    }

    /// <summary>
    /// Finds a Button whose name or AutomationId matches the given text.
    /// </summary>
    public static Button FindButton(this AutomationElement parent, string name)
    {
        var btn = parent.FindFirstDescendant(cf => cf.ByName(name))?.AsButton()
               ?? parent.FindFirstDescendant(cf => cf.ByAutomationId(name))?.AsButton();
        if (btn is null)
            throw new InvalidOperationException($"Button '{name}' not found.");
        return btn;
    }

    /// <summary>
    /// Finds a TextBox (or PasswordBox) by AutomationId.
    /// </summary>
    public static AutomationElement FindTextBox(this AutomationElement parent, string automationId)
    {
        var el = parent.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
        if (el is null)
            throw new InvalidOperationException($"TextBox with AutomationId '{automationId}' not found.");
        return el;
    }

    /// <summary>
    /// Finds a TextBlock (text element) whose text contains the given string.
    /// </summary>
    public static AutomationElement? FindTextContaining(this AutomationElement parent, string text)
    {
        return parent.FindFirstDescendant(cf =>
            cf.ByName(text)) ??
            parent.FindAllDescendants()
                  .FirstOrDefault(e => e.Name?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);
    }
}
