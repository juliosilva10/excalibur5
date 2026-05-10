using System.Windows;
using Excalibur5.Services;
using Excalibur5.ViewModels;
using Excalibur5.Views;

namespace Excalibur5;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLogger.Info("App", $"=== Excalibur5 starting — {Environment.OSVersion} ===");

        DispatcherUnhandledException += (_, ex) =>
        {
            AppLogger.Error("App", "Unhandled UI exception", ex.Exception);
            ex.Handled = true;
        };

        var wsService  = new DerivWebSocketService();
        var apiService = new DerivApiService(wsService);
        var viewModel  = new MainViewModel(apiService, wsService);
        new MainWindow { DataContext = viewModel }.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("App", "=== Excalibur5 exiting ===");
        base.OnExit(e);
    }
}
