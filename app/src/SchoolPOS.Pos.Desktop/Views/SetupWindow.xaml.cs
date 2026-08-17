using System.Windows;
using System.Windows.Controls;
using SchoolPOS.Pos.Desktop.ViewModels;

namespace SchoolPOS.Pos.Desktop.Views;

/// <summary>
/// Asistente de primer arranque. Se construye sin contenedor de servicios a propósito: corre
/// <b>antes</b> de que exista configuración, así que no puede depender de un DbContext ya
/// registrado con una cadena de conexión que todavía no se conoce.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly SetupViewModel _viewModel;

    public SetupWindow()
    {
        InitializeComponent();
        _viewModel = new SetupViewModel();
        DataContext = _viewModel;
        _viewModel.SetupCompleted += OnSetupCompleted;
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.AdminPassword = ((PasswordBox)sender).Password;

    private void ConfirmBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.AdminPasswordConfirm = ((PasswordBox)sender).Password;

    private void OnSetupCompleted()
    {
        DialogResult = true;
        Close();
    }
}
