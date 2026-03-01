using System.Windows;
using TimeGuard.Helpers;
using TimeGuard.Services;

namespace TimeGuard.UI;

public partial class FirstRunWindow : Window
{
    private readonly StorageService _storage;

    public FirstRunWindow(StorageService storage)
    {
        InitializeComponent();
        _storage = storage;
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

        var config = _storage.LoadConfig();
        var (hash, salt) = PasswordHelper.Hash(PasswordBox.Password);
        config.PasswordHash = hash;
        config.PasswordSalt = salt;
        _storage.SaveConfig(config);

        DialogResult = true;
        Close();
    }
}
