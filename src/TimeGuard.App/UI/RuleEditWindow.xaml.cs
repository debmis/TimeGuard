using System.Globalization;
using System.Windows;
using TimeGuard.Models;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace TimeGuard.UI;

public partial class RuleEditWindow : Window
{
    private readonly bool _enabled;

    private sealed record WeekdayEditor(
        DayOfWeek DayOfWeek,
        string Name,
        WpfTextBox LimitBox,
        WpfTextBox StartBox,
        WpfTextBox EndBox);

    public AppRule? Result { get; private set; }

    public RuleEditWindow(AppRule existing)
    {
        InitializeComponent();

        _enabled              = existing.Enabled;
        DisplayNameBox.Text   = existing.DisplayName;
        ProcessNameBox.Text   = existing.ProcessName;
        BreakEveryBox.Text    = existing.BreakEveryMinutes.ToString();
        BreakDurationBox.Text = existing.BreakDurationMinutes.ToString();

        foreach (var schedule in existing.GetWeekSchedule())
        {
            var editor = GetWeekdayEditors().First(x => x.DayOfWeek == schedule.DayOfWeek);
            editor.LimitBox.Text = schedule.DailyLimitMinutes.ToString();
            editor.StartBox.Text = schedule.AllowedWindowStart ?? string.Empty;
            editor.EndBox.Text   = schedule.AllowedWindowEnd ?? string.Empty;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text) ||
            string.IsNullOrWhiteSpace(ProcessNameBox.Text))
        {
            ShowError("App name and process name are required.");
            return;
        }

        if (!int.TryParse(BreakEveryBox.Text, out var breakEvery) || breakEvery < 0)
        {
            ShowError("Break interval must be a non-negative number.");
            return;
        }

        if (!int.TryParse(BreakDurationBox.Text, out var breakDur) || breakDur < 0)
        {
            ShowError("Break duration must be a non-negative number.");
            return;
        }

        var schedules = new List<AppRuleDaySchedule>();
        foreach (var editor in GetWeekdayEditors())
        {
            if (!int.TryParse(editor.LimitBox.Text, out var limit) || limit < 0)
            {
                ShowError($"{editor.Name} limit must be a non-negative number.");
                return;
            }

            var start = editor.StartBox.Text.Trim();
            var end   = editor.EndBox.Text.Trim();

            if ((start.Length > 0) != (end.Length > 0))
            {
                ShowError($"{editor.Name}: provide both From and Until times, or leave both empty.");
                return;
            }

            if (!TryNormalizeTime(start, out var normalizedStart) ||
                !TryNormalizeTime(end, out var normalizedEnd))
            {
                ShowError($"{editor.Name}: time format must be HH:mm (e.g. 15:00).");
                return;
            }

            schedules.Add(new AppRuleDaySchedule
            {
                DayOfWeek          = editor.DayOfWeek,
                DailyLimitMinutes  = limit,
                AllowedWindowStart = normalizedStart,
                AllowedWindowEnd   = normalizedEnd
            });
        }

        var invalidBreakDays = schedules
            .Where(schedule => schedule.DailyLimitMinutes > 0 && breakEvery > 0 && breakEvery >= schedule.DailyLimitMinutes)
            .Select(schedule => schedule.DayLabel)
            .ToList();

        if (invalidBreakDays.Count > 0)
        {
            ShowError(invalidBreakDays.Count == 1
                ? $"Break interval must be less than the daily limit for {invalidBreakDays[0]}."
                : "Break interval must be less than the daily limit for each limited day.");
            return;
        }

        if (breakEvery > 0 && breakDur > breakEvery)
        {
            ShowError("Break duration must be less than or equal to the break interval.");
            return;
        }

        var rule = new AppRule
        {
            DisplayName          = DisplayNameBox.Text.Trim(),
            ProcessName          = ProcessNameBox.Text.Trim().ToLowerInvariant(),
            BreakEveryMinutes    = breakEvery,
            BreakDurationMinutes = breakDur,
            Enabled              = _enabled
        };
        rule.SetWeekSchedule(schedules);

        Result       = rule;
        DialogResult = true;
        Close();
    }

    private IEnumerable<WeekdayEditor> GetWeekdayEditors()
    {
        yield return new WeekdayEditor(DayOfWeek.Monday, "Monday", MondayLimitBox, MondayWindowStartBox, MondayWindowEndBox);
        yield return new WeekdayEditor(DayOfWeek.Tuesday, "Tuesday", TuesdayLimitBox, TuesdayWindowStartBox, TuesdayWindowEndBox);
        yield return new WeekdayEditor(DayOfWeek.Wednesday, "Wednesday", WednesdayLimitBox, WednesdayWindowStartBox, WednesdayWindowEndBox);
        yield return new WeekdayEditor(DayOfWeek.Thursday, "Thursday", ThursdayLimitBox, ThursdayWindowStartBox, ThursdayWindowEndBox);
        yield return new WeekdayEditor(DayOfWeek.Friday, "Friday", FridayLimitBox, FridayWindowStartBox, FridayWindowEndBox);
        yield return new WeekdayEditor(DayOfWeek.Saturday, "Saturday", SaturdayLimitBox, SaturdayWindowStartBox, SaturdayWindowEndBox);
        yield return new WeekdayEditor(DayOfWeek.Sunday, "Sunday", SundayLimitBox, SundayWindowStartBox, SundayWindowEndBox);
    }

    private static bool TryNormalizeTime(string input, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(input))
            return true;

        if (!TimeOnly.TryParseExact(input, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;

        normalized = parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
        return true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text       = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
