using System.Windows;
using System.Windows.Input;
using TimeGuard.Helpers;
using TimeGuard.Services;
// Explicitly use WPF KeyEventArgs
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace TimeGuard.UI;

public partial class PasswordPromptWindow : Window
{
    private readonly DatabaseService _db;

    public PasswordPromptWindow()
    {
        InitializeComponent();
        _db = new DatabaseService();
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void OnUnlock(object sender, RoutedEventArgs e) => Verify();
    private void OnKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Verify(); }

    private void Verify()
    {
        var hash = _db.GetSetting("PasswordHash") ?? string.Empty;
        var salt = _db.GetSetting("PasswordSalt") ?? string.Empty;
        if (PasswordHelper.Verify(PasswordBox.Password, hash, salt))
        {
            DialogResult = true;
            Close();
        }
        else
        {
            ErrorText.Text = "Incorrect password.";
            ErrorText.Visibility = Visibility.Visible;
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
    }
}
