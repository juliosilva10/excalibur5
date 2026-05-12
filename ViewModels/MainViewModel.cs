using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Excalibur5.Config;
using Excalibur5.Models;
using Excalibur5.Services;

namespace Excalibur5.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private const string Src = "ViewModel";

    private readonly IDerivApiService        _api;
    private readonly IDerivWebSocketService  _ws;
    private readonly ITickStreamService      _tickStream;
    private readonly IContractService        _contractService;
    private readonly DispatcherTimer         _timer;
    private readonly DispatcherTimer         _uptimeTimer;
    private readonly System.Diagnostics.Stopwatch _uptimeWatch = new();
    private volatile string _token = string.Empty;
    private int _timerBusy; // 0 = idle, 1 = busy — use Interlocked for atomic check-and-set
    private TimeSpan _serverOffset; // difference between server UTC and local UTC

    [ObservableProperty] private bool    _isConnected;
    [ObservableProperty] private bool    _isConnecting;
    [ObservableProperty] private bool    _hasToken;
    [ObservableProperty] private string  _loginId      = string.Empty;
    [ObservableProperty] private string  _accountType  = string.Empty;
    [ObservableProperty] private decimal _balance;
    [ObservableProperty] private string  _currency     = string.Empty;
    [ObservableProperty] private long    _pingMs;
    [ObservableProperty] private string  _serverUtc    = string.Empty;
    [ObservableProperty] private string  _statusMessage = string.Empty;
    [ObservableProperty] private string  _uptime        = "00:00:00";

    public bool   IsVirtual        => AccountType.Equals("virtual", StringComparison.OrdinalIgnoreCase);
    public string AccountTypeLabel => IsVirtual ? "Virtual" : "Real";
    public bool   ShowVirtualGlow  => IsConnected && IsVirtual;
    public bool   ShowRealGlow     => IsConnected && !IsVirtual && !string.IsNullOrEmpty(AccountType);

    public MarketsViewModel Markets { get; }
    public LogViewModel Log { get; } = new();

    partial void OnAccountTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsVirtual));
        OnPropertyChanged(nameof(AccountTypeLabel));
        OnPropertyChanged(nameof(ShowVirtualGlow));
        OnPropertyChanged(nameof(ShowRealGlow));
    }

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowVirtualGlow));
        OnPropertyChanged(nameof(ShowRealGlow));
    }

    public MainViewModel(IDerivApiService api, IDerivWebSocketService ws, ITickStreamService tickStream, IContractService contractService)
    {
        _api        = api;
        _ws         = ws;
        _tickStream = tickStream;
        _contractService = contractService;

        Markets = new MarketsViewModel(tickStream, contractService);

        Markets.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Markets.IsMarketsVisible) && Markets.IsMarketsVisible)
                Log.IsLogVisible = false;
            if (e.PropertyName == nameof(Markets.IsMarketsVisible) || e.PropertyName == nameof(Markets.SelectedTab))
            {
                SaveUiState();
                WatchContractPanelChanges();
            }
        };
        Log.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Log.IsLogVisible) && Log.IsLogVisible)
                Markets.IsMarketsVisible = false;
            if (e.PropertyName == nameof(Log.IsLogVisible))
                SaveUiState();
        };

        _api.Authorized     += OnAuthorized;
        _api.BalanceUpdated += OnBalanceUpdated;
        _ws.Disconnected    += OnDisconnected;
        _ws.Connected       += OnConnected;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += OnTimerTick;

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) =>
        {
            var e = _uptimeWatch.Elapsed;
            Uptime = $"{(int)e.TotalHours:D2}:{e.Minutes:D2}:{e.Seconds:D2}";
            if (IsConnected)
            {
                var serverNow = DateTimeOffset.UtcNow.Add(_serverOffset);
                ServerUtc = serverNow.ToString("HH:mm:ss") + " UTC";
            }
        };

        AppLogger.Info(Src, $"MainViewModel created — log: {AppLogger.GetLogPath()}");
    }

    public void SetToken(string token)
    {
        _token   = token;
        HasToken = !string.IsNullOrEmpty(token);
    }

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleConnectionAsync()
    {
        if (IsConnected)
            await DisconnectAsync();
        else
            await ConnectAsync();
    }

    private bool CanToggle() => !IsConnecting;

    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(_token))
        {
            StatusMessage = "Digite o Token antes de conectar.";
            AppLogger.Warn(Src, "ConnectAsync called without token");
            return;
        }

        IsConnecting  = true;
        StatusMessage = "Conectando...";
        ToggleConnectionCommand.NotifyCanExecuteChanged();
        AppLogger.Info(Src, "ConnectAsync started");

        try
        {
            await _ws.ConnectAsync();
            await _api.AuthorizeAsync(_token);
            await _api.SubscribeBalanceAsync();
            TokenStore.Save(_token);

            // Busca ping e hora antes de mostrar a UI — garante que tudo aparece junto
            var pingTask = _api.PingAsync();
            var timeTask = _api.GetServerTimeAsync();
            await Task.WhenAll(pingTask, timeTask);
            PingMs    = pingTask.Result;
            ServerUtc = timeTask.Result.ToString("HH:mm:ss") + " UTC";
            _serverOffset = timeTask.Result - DateTimeOffset.UtcNow;

            IsConnected   = true;
            StatusMessage = string.Empty;
            _timer.Start();
            _uptimeWatch.Restart();
            _uptimeTimer.Start();
            AppLogger.Info(Src, "Connection fully established — timer started");

            await RestoreUiStateAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            IsConnected   = false;
            AppLogger.Error(Src, "ConnectAsync failed", ex);
            try { await _ws.DisconnectAsync(); } catch { /* ignore */ }
        }
        finally
        {
            IsConnecting = false;
            ToggleConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task DisconnectAsync()
    {
        _timer.Stop();
        ToggleConnectionCommand.NotifyCanExecuteChanged();
        AppLogger.Info(Src, "DisconnectAsync called");
        try
        {
            await Markets.UnsubscribeAllAsync();
            await _ws.DisconnectAsync();
        }
        finally
        {
            IsConnected   = false;
            IsConnecting  = false;
            LoginId       = string.Empty;
            AccountType   = string.Empty;
            Balance       = 0;
            Currency      = string.Empty;
            PingMs        = 0;
            ServerUtc     = string.Empty;
            StatusMessage = string.Empty;
            Uptime        = "00:00:00";
            _uptimeTimer.Stop();
            _uptimeWatch.Reset();
            ToggleConnectionCommand.NotifyCanExecuteChanged();
            AppLogger.Info(Src, "Disconnected — UI state cleared");
        }
    }

    private void OnAuthorized(object? sender, AuthorizeResponse e)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            LoginId     = e.LoginId;
            AccountType = e.IsVirtual ? "virtual" : "real";
            Balance     = e.Balance;
            Currency    = e.Currency;
            AppLogger.Info(Src, $"UI updated: {e.LoginId} {e.Balance} {e.Currency}");
        }).Task.ContinueWith(t =>
            AppLogger.Error(Src, "OnAuthorized dispatcher error", t.Exception?.InnerException),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void OnBalanceUpdated(object? sender, BalanceResponse e)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Balance  = e.Balance;
            Currency = e.Currency;
        }).Task.ContinueWith(t =>
            AppLogger.Error(Src, "OnBalanceUpdated dispatcher error", t.Exception?.InnerException),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private async void OnConnected(object? sender, EventArgs e)
    {
        var isManual = await Application.Current.Dispatcher.InvokeAsync(() => IsConnecting);
        if (isManual) return;

        AppLogger.Info(Src, "OnConnected (reconexão automática) — re-autorizando…");
        try
        {
            await _api.AuthorizeAsync(_token);
            await _api.SubscribeBalanceAsync();

            var pingTask = _api.PingAsync();
            var timeTask = _api.GetServerTimeAsync();
            await Task.WhenAll(pingTask, timeTask);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PingMs        = pingTask.Result;
                ServerUtc     = timeTask.Result.ToString("HH:mm:ss") + " UTC";
                _serverOffset = timeTask.Result - DateTimeOffset.UtcNow;
                IsConnected   = true;
                StatusMessage = string.Empty;
                if (!_timer.IsEnabled) _timer.Start();
                if (!_uptimeTimer.IsEnabled) { _uptimeWatch.Restart(); _uptimeTimer.Start(); }
            });

            await Markets.ResubscribeActiveAsync();
            AppLogger.Info(Src, "Reconexão automática concluída");
        }
        catch (Exception ex)
        {
            AppLogger.Error(Src, "Re-autorização falhou", ex);
            await Application.Current.Dispatcher.InvokeAsync(() =>
                StatusMessage = "Reconectando...");
        }
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (!IsConnecting)
            {
                IsConnected   = false;
                StatusMessage = "Reconectando...";
                AppLogger.Warn(Src, "OnDisconnected (unexpected) — showing reconnect status");
            }
        }).Task.ContinueWith(t =>
            AppLogger.Error(Src, "OnDisconnected dispatcher error", t.Exception?.InnerException),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (!IsConnected || Interlocked.CompareExchange(ref _timerBusy, 1, 0) != 0) return;
        try
        {
            var pingTask = _api.PingAsync();
            var timeTask = _api.GetServerTimeAsync();
            await Task.WhenAll(pingTask, timeTask);

            PingMs    = pingTask.Result;
            ServerUtc = timeTask.Result.ToString("HH:mm:ss") + " UTC";
            _serverOffset = timeTask.Result - DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Timer tick error (ignored): {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _timerBusy, 0);
        }
    }

    private ContractPanelViewModel? _watchedPanel;
    private bool _restoringState;

    private void WatchContractPanelChanges()
    {
        if (_watchedPanel != null)
            _watchedPanel.PropertyChanged -= OnContractPanelChanged;

        _watchedPanel = Markets.SelectedTab?.ContractPanel;

        if (_watchedPanel != null)
            _watchedPanel.PropertyChanged += OnContractPanelChanged;
    }

    private void OnContractPanelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_restoringState) return;
        if (e.PropertyName is nameof(ContractPanelViewModel.DurationText)
            or nameof(ContractPanelViewModel.DurationUnit)
            or nameof(ContractPanelViewModel.StakeText)
            or nameof(ContractPanelViewModel.UseDuration))
        {
            SaveUiState();
        }
    }

    private void SaveUiState()
    {
        var panel = Log.IsLogVisible ? "log" : Markets.IsMarketsVisible ? "markets" : "";
        var market = Markets.SelectedTab?.Symbol;
        var cp = Markets.SelectedTab?.ContractPanel;
        UiStateStore.Save(panel, market,
            cp?.DurationUnit.ToString(),
            cp?.DurationText,
            cp?.StakeText,
            cp?.UseDuration);
    }

    private async Task RestoreUiStateAsync()
    {
        var state = UiStateStore.Load();
        if (state.ActivePanel == "log")
        {
            Log.IsLogVisible = true;
        }
        else if (state.ActivePanel == "markets")
        {
            Markets.IsMarketsVisible = true;
            if (!string.IsNullOrEmpty(state.SelectedMarket))
            {
                var tab = Markets.Tabs.FirstOrDefault(t => t.Symbol == state.SelectedMarket);
                if (tab is not null)
                {
                    _restoringState = true;
                    tab.ContractPanel.RestoreState(state.DurationUnit, state.DurationText, state.StakeText, state.UseDuration);
                    _restoringState = false;
                    await Markets.SelectTabAsync(tab);
                }
            }
        }
    }

    public void Dispose()
    {
        SaveUiState();
        if (_watchedPanel != null)
            _watchedPanel.PropertyChanged -= OnContractPanelChanged;
        _timer.Stop();
        _uptimeTimer.Stop();
        _uptimeWatch.Stop();
        _api.Authorized     -= OnAuthorized;
        _api.BalanceUpdated -= OnBalanceUpdated;
        _ws.Disconnected    -= OnDisconnected;
        _ws.Connected       -= OnConnected;
        Markets.Dispose();
        Log.Dispose();
        (_tickStream as IDisposable)?.Dispose();
        (_api as IDisposable)?.Dispose();
        AppLogger.Info(Src, "MainViewModel disposed");
    }
}
