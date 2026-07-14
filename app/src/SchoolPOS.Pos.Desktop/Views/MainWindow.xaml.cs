using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SchoolPOS.Pos.Desktop.Infrastructure;
using SchoolPOS.Pos.Desktop.ViewModels;

namespace SchoolPOS.Pos.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly PosSession _session;

    public MainWindow(MainViewModel viewModel, PosSession session)
    {
        InitializeComponent();
        _session = session;
        DataContext = viewModel;
        viewModel.SignOutRequested += OnSignOutRequested;
    }

    private void OnSignOutRequested()
    {
        _session.SignOut();
        var login = App.Services.GetRequiredService<LoginWindow>();
        login.Show();
        Close();
    }
}
