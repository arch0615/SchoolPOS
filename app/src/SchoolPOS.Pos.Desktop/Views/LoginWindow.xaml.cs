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

    private void PlainPasswordBox_OnTextChanged(object sender, TextChangedEventArgs e) =>
        _viewModel.Password = ((TextBox)sender).Text;

    // Alternar "Mostrar contraseña" cambia cuál de los dos controles está enlazado a la
    // contraseña real; sin copiar el texto al cambiar, lo ya tecleado se perdería.
    private void ShowPassword_OnChecked(object sender, RoutedEventArgs e) => PlainPasswordBox.Text = PasswordBox.Password;

    private void ShowPassword_OnUnchecked(object sender, RoutedEventArgs e) => PasswordBox.Password = PlainPasswordBox.Text;

    private void OnLoginSucceeded()
    {
        var main = App.Services.GetRequiredService<MainWindow>();
        Application.Current.MainWindow = main;
        main.Show();
        Close();
    }
}
