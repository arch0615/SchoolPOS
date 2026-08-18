using System.Windows;
using System.Windows.Controls;
using SchoolPOS.Pos.Desktop.ViewModels;

namespace SchoolPOS.Pos.Desktop.Views;

public partial class OperatorsView : UserControl
{
    public OperatorsView() => InitializeComponent();

    // El PasswordBox no admite binding por seguridad; se pasan al view-model a mano.
    private void NewPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is OperatorsViewModel vm)
            vm.NewPassword = ((PasswordBox)sender).Password;
    }

    private void ResetPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is OperatorsViewModel vm)
            vm.ResetPassword = ((PasswordBox)sender).Password;
    }
}
