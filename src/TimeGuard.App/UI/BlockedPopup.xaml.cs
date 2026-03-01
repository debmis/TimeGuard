using System.Windows;

namespace TimeGuard.UI;

public partial class BlockedPopup : Window
{
    public BlockedPopup(string displayName)
    {
        InitializeComponent();
        MessageText.Text = $"{displayName} time is up for today. Limits reset at midnight. 🌙";
        PositionCenter();
    }

    private void PositionCenter()
    {
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width  - Width)  / 2;
        Top  = (screen.Height - Height) / 2;
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Close();
}
