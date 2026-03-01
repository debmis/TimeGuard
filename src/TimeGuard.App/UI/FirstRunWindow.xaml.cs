using System.Windows;
using TimeGuard.Helpers;
using TimeGuard.Services;

namespace TimeGuard.UI;

public partial class FirstRunWindow : Window
{
    private readonly DatabaseService _db;

    public FirstRunWindow(DatabaseService db)
    {
        InitializeComponent();
        _db = db;
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        if (PasswordBox.Password != ConfirmBox.Password)
        {
            ErrorText.Text = "Passwords do not match.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        if (PasswordBox.Password.Length < 6)
        {
            ErrorText.Text = "Password must be at least 6 characters.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        var (hash, salt) = PasswordHelper.Hash(PasswordBox.Password);
        _db.SetSetting("PasswordHash", hash);
        _db.SetSetting("PasswordSalt", salt);

        DialogResult = true;
        Close();
    }
}
