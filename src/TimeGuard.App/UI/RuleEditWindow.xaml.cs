using System.Windows;
using TimeGuard.Models;

namespace TimeGuard.UI;

public partial class RuleEditWindow : Window
{
    public AppRule? Result { get; private set; }

    public RuleEditWindow(AppRule existing)
    {
        InitializeComponent();
        DisplayNameBox.Text  = existing.DisplayName;
        ProcessNameBox.Text  = existing.ProcessName;
        LimitBox.Text        = existing.DailyLimitMinutes.ToString();
        WindowStartBox.Text  = existing.AllowedWindowStart ?? string.Empty;
        WindowEndBox.Text    = existing.AllowedWindowEnd   ?? string.Empty;
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

        var start = WindowStartBox.Text.Trim();
        var end   = WindowEndBox.Text.Trim();

        if ((start.Length > 0) != (end.Length > 0))
        {
            ErrorText.Text = "Provide both 'From' and 'Until' times, or leave both empty.";
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
            Enabled              = true
        };
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
