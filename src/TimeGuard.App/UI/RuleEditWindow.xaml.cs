using System.Windows;
using TimeGuard.Models;

namespace TimeGuard.UI;

public partial class RuleEditWindow : Window
{
    public AppRule? Result { get; private set; }

    public RuleEditWindow(AppRule existing)
    {
        InitializeComponent();
        DisplayNameBox.Text   = existing.DisplayName;
        ProcessNameBox.Text   = existing.ProcessName;
        LimitBox.Text         = existing.DailyLimitMinutes.ToString();
        WindowStartBox.Text   = existing.AllowedWindowStart ?? string.Empty;
        WindowEndBox.Text     = existing.AllowedWindowEnd   ?? string.Empty;
        BreakEveryBox.Text    = existing.BreakEveryMinutes.ToString();
        BreakDurationBox.Text = existing.BreakDurationMinutes.ToString();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text) ||
            string.IsNullOrWhiteSpace(ProcessNameBox.Text))
        {
            ErrorText.Text = "App name and process name are required.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (!int.TryParse(LimitBox.Text, out var limit) || limit < 0)
        {
            ErrorText.Text = "Daily limit must be a non-negative number.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (!int.TryParse(BreakEveryBox.Text, out var breakEvery) || breakEvery < 0)
        {
            ErrorText.Text = "Break interval must be a non-negative number.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (!int.TryParse(BreakDurationBox.Text, out var breakDur) || breakDur < 0)
        {
            ErrorText.Text = "Break duration must be a non-negative number.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        // Break every N minutes must be less than the daily limit (when both are set)
        if (breakEvery > 0 && limit > 0 && breakEvery >= limit)
        {
            ErrorText.Text = "Break interval must be less than the daily limit.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        // Break duration must not exceed the break interval
        if (breakEvery > 0 && breakDur > breakEvery)
        {
            ErrorText.Text = "Break duration must be less than or equal to the break interval.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        var start = WindowStartBox.Text.Trim();
        var end   = WindowEndBox.Text.Trim();

        if ((start.Length > 0) != (end.Length > 0))
        {
            ErrorText.Text = "Provide both From and Until times, or leave both empty.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (start.Length > 0 &&
            (!TimeOnly.TryParse(start, out _) || !TimeOnly.TryParse(end, out _)))
        {
            ErrorText.Text = "Time format must be HH:mm (e.g. 15:00).";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Result = new AppRule
        {
            DisplayName          = DisplayNameBox.Text.Trim(),
            ProcessName          = ProcessNameBox.Text.Trim().ToLowerInvariant(),
            DailyLimitMinutes    = limit,
            AllowedWindowStart   = start.Length > 0 ? start : null,
            AllowedWindowEnd     = end.Length   > 0 ? end   : null,
            BreakEveryMinutes    = breakEvery,
            BreakDurationMinutes = breakDur,
            Enabled              = true
        };
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
