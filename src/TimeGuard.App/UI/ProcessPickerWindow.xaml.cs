using System.Windows;
using System.Windows.Controls;

namespace TimeGuard.UI;

public partial class ProcessPickerWindow : Window
{
    public string? SelectedProcess { get; private set; }

    private readonly List<string> _allProcesses;

    public ProcessPickerWindow(IEnumerable<string> processes)
    {
        InitializeComponent();
        _allProcesses = processes.ToList();
        ProcessList.ItemsSource = _allProcesses;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        var filter = SearchBox.Text.Trim().ToLowerInvariant();
        ProcessList.ItemsSource = string.IsNullOrEmpty(filter)
            ? _allProcesses
            : _allProcesses.Where(p => p.Contains(filter)).ToList();
    }

    private void OnDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Pick();
    private void OnSelect(object sender, RoutedEventArgs e) => Pick();

    private void Pick()
    {
        if (ProcessList.SelectedItem is string selected)
        {
            SelectedProcess = selected;
            DialogResult = true;
            Close();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
