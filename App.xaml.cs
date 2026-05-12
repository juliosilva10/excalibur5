using System.Windows;
using Excalibur5.Services;
using Excalibur5.ViewModels;
using Excalibur5.Views;

namespace Excalibur5;

public partial class App : Application
{
    private DerivWebSocketService? _wsService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLogger.Info("App", $"=== Excalibur5 starting — {Environment.OSVersion} ===");

        DispatcherUnhandledException += (_, ex) =>
        {
            AppLogger.Error("App", "Unhandled UI exception", ex.Exception);
            ex.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            AppLogger.Error("App", "Unhandled domain exception",
                ex.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            AppLogger.Error("App", "Unobserved task exception", ex.Exception);
            ex.SetObserved();
        };

        _wsService = new DerivWebSocketService();
        var apiService      = new DerivApiService(_wsService);
        var tickService     = new TickStreamService(_wsService);
        var contractService = new ContractService(_wsService);
        var viewModel       = new MainViewModel(apiService, _wsService, tickService, contractService);
        new MainWindow { DataContext = viewModel }.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("App", "=== Excalibur5 exiting ===");
        if (_wsService != null)
        {
            try { await _wsService.DisposeAsync(); }
            catch { /* ignore */ }
        }
        base.OnExit(e);
    }
}
