using System.Windows;
using System.Windows.Input;
using TimeGuard.Helpers;
using TimeGuard.Services;

namespace TimeGuard.UI;

public partial class PasswordPromptWindow : Window
{
    private readonly StorageService _storage;

    public PasswordPromptWindow()
    {
        InitializeComponent();
        _storage = new StorageService();
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void OnUnlock(object sender, RoutedEventArgs e) => Verify();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Verify();
    }

    private void Verify()
    {
        var config = _storage.LoadConfig();
        if (PasswordHelper.Verify(PasswordBox.Password, config.PasswordHash, config.PasswordSalt))
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
