using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Pos.Desktop.ViewModels;

namespace SchoolPOS.Pos.Desktop.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.LoginSucceeded += OnLoginSucceeded;
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        // El PasswordBox no admite binding directo por seguridad; se pasa al VM manualmente.
        _viewModel.Password = ((PasswordBox)sender).Password;
    }

    private void OnLoginSucceeded()
    {
        var main = App.Services.GetRequiredService<MainWindow>();
        Application.Current.MainWindow = main;
        main.Show();
        Close();
    }
}
