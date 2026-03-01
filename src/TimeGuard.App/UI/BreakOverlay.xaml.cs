using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
// Explicitly use WPF KeyEventArgs
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace TimeGuard.UI;

public partial class BreakOverlay : Window
{
    private int _remainingSeconds;
    private readonly DispatcherTimer _timer;

    public BreakOverlay(string displayName, int breakDurationMinutes)
    {
        InitializeComponent();

        _remainingSeconds = breakDurationMinutes * 60;
        AppNameText.Text  = $"{displayName} — break in progress";
        UpdateCountdown();

        // Cover all screens
        var allScreens = System.Windows.Forms.Screen.AllScreens;
        var left   = allScreens.Min(s => s.Bounds.Left);
        var top    = allScreens.Min(s => s.Bounds.Top);
        var right  = allScreens.Max(s => s.Bounds.Right);
        var bottom = allScreens.Max(s => s.Bounds.Bottom);

        Left   = left;
        Top    = top;
        Width  = right  - left;
        Height = bottom - top;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();

        // Prevent alt-tab / task-switching during break
        Loaded += (_, _) => Focus();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;
        UpdateCountdown();
        if (_remainingSeconds <= 0)
        {
            _timer.Stop();
            DialogResult = true;
            Close();
        }
    }

    private void UpdateCountdown()
    {
        var ts = TimeSpan.FromSeconds(_remainingSeconds);
        CountdownText.Text = ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }

    // Eat all key events — child cannot keyboard-shortcut out
    private void OnKeyDown(object sender, KeyEventArgs e) => e.Handled = true;

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Focus();
    }
}
