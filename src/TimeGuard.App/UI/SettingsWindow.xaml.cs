using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TimeGuard.Helpers;
using TimeGuard.Models;
using TimeGuard.Services;
using WpfMessageBox = System.Windows.MessageBox;

namespace TimeGuard.UI;

public partial class SettingsWindow : Window
{
    private readonly DatabaseService _db;
    private ObservableCollection<AppRule> _rules = [];

    private record UsageRow(string ProcessName, string UsageMinutesDisplay, bool Blocked);

    public SettingsWindow(DatabaseService db)
    {
        InitializeComponent();
        _db = db;
        LoadRules();
        LoadUsage();
        LoadGlobalCap();
        StartupCheckBox.IsChecked = StartupHelper.IsRegistered();
    }

    // ── Rules Tab ─────────────────────────────────────────────────────────────

    private void LoadRules()
    {
        _rules = new ObservableCollection<AppRule>(_db.GetRules());
        RulesGrid.ItemsSource = _rules;

        BuildRecentPanel(_db.GetRecentlySeenProcesses(7));
    }

    private void BuildRecentPanel(IReadOnlyList<string> recent)
    {
        RecentPanel.Children.Clear();

        // Always show at least a test entry so the UI can be verified
        var items = recent.Count > 0 ? recent : new[] { "[test] no-recent-sessions" };

        foreach (var proc in items)
        {
            var row = new System.Windows.Controls.Grid();
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
                { Width = new GridLength(100) });

            var label = new System.Windows.Controls.TextBlock
            {
                Text = proc,
                Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 6, 4, 6)
            };
            System.Windows.Controls.Grid.SetColumn(label, 0);

            var captured = proc; // capture for lambda
            var btn = new System.Windows.Controls.Button
            {
                Content = "Add Rule →",
                Style   = (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(8, 4, 8, 4),
                Tag     = proc
            };
            btn.Click += (_, _) =>
            {
                if (captured.StartsWith("[test]")) return;
                PromoteProcess(captured);
            };
            System.Windows.Controls.Grid.SetColumn(btn, 1);

            row.Children.Add(label);
            row.Children.Add(btn);
            RecentPanel.Children.Add(row);
        }

        RecentPanel.Visibility = Visibility.Visible;
    }

    private void PromoteProcess(string processName)
    {
        var rule = new AppRule
        {
            ProcessName = processName,
            DisplayName = processName,
            Enabled     = true
        };
        var dialog = new RuleEditWindow(rule) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _db.SaveRule(dialog.Result);
            LoadRules();
        }
    }

    private void OnAddRule(object sender, RoutedEventArgs e)
    {
        var dialog = new RuleEditWindow(new AppRule());
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _db.SaveRule(dialog.Result);
            LoadRules();
        }
    }

    private void OnEditRule(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not AppRule selected) return;
        var dialog = new RuleEditWindow(selected);
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            dialog.Result.Id = selected.Id;
            _db.SaveRule(dialog.Result);
            LoadRules();
        }
    }

    private void OnDeleteRule(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not AppRule selected) return;
        if (WpfMessageBox.Show($"Remove rule for \'{selected.DisplayName}\'?", "Confirm",
                MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            _db.DeleteRule(selected.Id);
            LoadRules();
        }
    }

    private void OnPickProcess(object sender, RoutedEventArgs e)
    {
        var running = Process.GetProcesses()
            .Select(p => p.ProcessName.ToLowerInvariant())
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var picker = new ProcessPickerWindow(running);
        if (picker.ShowDialog() == true && picker.SelectedProcess is not null)
        {
            var rule = new AppRule
            {
                ProcessName = picker.SelectedProcess,
                DisplayName = picker.SelectedProcess,
                Enabled = true
            };
            var dialog = new RuleEditWindow(rule);
            if (dialog.ShowDialog() == true && dialog.Result is not null)
            {
                _db.SaveRule(dialog.Result);
                LoadRules();
            }
        }
    }

    private void OnViewDashboard(object sender, RoutedEventArgs e)
    {
        new DashboardWindow(_db).ShowDialog();
    }

    // ── Usage Tab ─────────────────────────────────────────────────────────────

    private void LoadUsage()
    {
        var log = _db.LoadTodayLog();
        UsageGrid.ItemsSource = log.Entries
            .Select(e => new UsageRow(e.ProcessName, $"{e.UsageMinutes:F1}", e.Blocked))
            .ToList();
    }

    // ── Global Cap Tab ────────────────────────────────────────────────────────

    private void LoadGlobalCap()
    {
        OverallCapBox.Text = _db.GetSetting("OverallDailyLimitMinutes") ?? "0";
    }

    // ── Security Tab ─────────────────────────────────────────────────────────

    private void OnChangePassword(object sender, RoutedEventArgs e)
    {
        if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
        {
            PasswordStatusText.Text = "Passwords do not match.";
            PasswordStatusText.Visibility = Visibility.Visible;
            return;
        }
        if (NewPasswordBox.Password.Length < 6)
        {
            PasswordStatusText.Text = "Password must be at least 6 characters.";
            PasswordStatusText.Visibility = Visibility.Visible;
            return;
        }

        var (hash, salt) = PasswordHelper.Hash(NewPasswordBox.Password);
        _db.SetSetting("PasswordHash", hash);
        _db.SetSetting("PasswordSalt", salt);

        NewPasswordBox.Clear();
        ConfirmPasswordBox.Clear();
        PasswordStatusText.Text = "✅ Password changed successfully.";
        PasswordStatusText.Visibility = Visibility.Visible;
    }

    private void OnStartupToggle(object sender, RoutedEventArgs e)
    {
        if (StartupCheckBox.IsChecked == true) StartupHelper.Register();
        else StartupHelper.Unregister();
    }

    // ── Footer ────────────────────────────────────────────────────────────────

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(OverallCapBox.Text, out var cap))
            _db.SetSetting("OverallDailyLimitMinutes", cap.ToString());

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
