using System.Windows;
using Excalibur5.Services;
using Excalibur5.ViewModels;
using Excalibur5.Views;

namespace Excalibur5;

public partial class App : Application
{
    private DerivWebSocketService? _wsService;
    private DerivRestClient? _restClient;
    private DerivApiService? _apiService;
    private TickStreamService? _tickService;
    private ContractService? _contractService;
    private MainViewModel? _viewModel;

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

        _wsService       = new DerivWebSocketService();
        _restClient      = new DerivRestClient();
        _apiService      = new DerivApiService(_wsService, _restClient);
        _tickService     = new TickStreamService(_wsService);
        _contractService = new ContractService(_wsService);
        _viewModel       = new MainViewModel(_apiService, _wsService, _tickService, _contractService);
        new MainWindow { DataContext = _viewModel }.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("App", "=== Excalibur5 exiting ===");

        // Dispose services (which unhook their WebSocket event handlers) before the
        // WebSocket itself, then dispose the socket last.
        TryDispose(_viewModel);
        TryDispose(_contractService);
        TryDispose(_tickService);
        TryDispose(_apiService);
        TryDispose(_restClient);

        if (_wsService != null)
        {
            try { await _wsService.DisposeAsync(); }
            catch { /* ignore */ }
        }
        base.OnExit(e);
    }

    private static void TryDispose(IDisposable? disposable)
    {
        try { disposable?.Dispose(); }
        catch (Exception ex) { AppLogger.Warn("App", $"Dispose error: {ex.Message}"); }
    }
}
