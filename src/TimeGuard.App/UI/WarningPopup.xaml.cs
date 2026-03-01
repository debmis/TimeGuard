using System.Windows;

namespace TimeGuard.UI;

public partial class WarningPopup : Window
{
    public WarningPopup(string displayName)
    {
        InitializeComponent();
        MessageText.Text = $"{displayName} has about 5 minutes left for today. Save your work!";
        PositionBottomRight();

        // Auto-dismiss after 30 seconds
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
    }

    private void PositionBottomRight()
    {
        var screen = SystemParameters.WorkArea;
        Left = screen.Right  - Width  - 20;
        Top  = screen.Bottom - Height - 20;
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Close();
}
