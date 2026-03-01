using System.Collections.ObjectModel;
using System.Windows;
using TimeGuard.Helpers;
using TimeGuard.Models;
using TimeGuard.Services;

namespace TimeGuard.UI;

public partial class SettingsWindow : Window
{
    private readonly StorageService _storage;
    private AppConfig _config;
    private ObservableCollection<AppRule> _rules = [];

    // Projection for the usage tab
    private record UsageRow(string ProcessName, string UsageMinutesDisplay, bool Blocked);

    public SettingsWindow(StorageService storage)
    {
        InitializeComponent();
        _storage = storage;
        _config  = storage.LoadConfig();

        LoadRules();
        LoadUsage();
        LoadGlobalCap();
        StartupCheckBox.IsChecked = StartupHelper.IsRegistered();
    }

    // ── Rules Tab ─────────────────────────────────────────────────────────────

    private void LoadRules()
    {
        _rules = new ObservableCollection<AppRule>(_config.Rules);
        RulesGrid.ItemsSource = _rules;
    }

    private void OnAddRule(object sender, RoutedEventArgs e)
    {
        var dialog = new RuleEditWindow(new AppRule());
        if (dialog.ShowDialog() == true)
            _rules.Add(dialog.Result!);
    }

    private void OnEditRule(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not AppRule selected) return;
        var dialog = new RuleEditWindow(selected);
        if (dialog.ShowDialog() == true)
        {
            var idx = _rules.IndexOf(selected);
            _rules[idx] = dialog.Result!;
        }
    }

    private void OnDeleteRule(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is AppRule selected &&
            MessageBox.Show($"Remove rule for '{selected.DisplayName}'?", "Confirm",
                MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            _rules.Remove(selected);
    }

    // ── Usage Tab ─────────────────────────────────────────────────────────────

    private void LoadUsage()
    {
        var log = _storage.LoadTodayLog();
        UsageGrid.ItemsSource = log.Entries.Select(e =>
            new UsageRow(e.ProcessName, $"{e.UsageMinutes:F1}", e.Blocked)).ToList();
    }

    // ── Global Cap Tab ────────────────────────────────────────────────────────

    private void LoadGlobalCap()
    {
        OverallCapBox.Text = _config.OverallDailyLimitMinutes.ToString();
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
        _config.PasswordHash = hash;
        _config.PasswordSalt = salt;
        _storage.SaveConfig(_config);

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
        _config.Rules = [.. _rules];

        if (int.TryParse(OverallCapBox.Text, out var cap))
            _config.OverallDailyLimitMinutes = cap;

        _storage.SaveConfig(_config);
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
