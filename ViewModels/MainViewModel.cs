using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Excalibur5.Config;
using Excalibur5.Models;
using Excalibur5.Services;
using Excalibur5.Services.Strategy;
using Excalibur5.Services.Strategy.Virtual;

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
    private readonly HashSet<long> _currentSessionContracts = new();
    private readonly IVirtualEntryModeController _virtualEntryModeController;
    private volatile string _token = string.Empty;
    private int _timerBusy; // 0 = idle, 1 = busy — use Interlocked for atomic check-and-set
    private TimeSpan _serverOffset; // difference between server UTC and local UTC
    private volatile bool _isBotSessionActive;
    private volatile bool _initialBalanceSet;

    [ObservableProperty] private bool    _isConnected;
    [ObservableProperty] private bool    _isConnecting;
    [ObservableProperty] private bool    _hasToken;
    [ObservableProperty] private string  _loginId      = string.Empty;
    [ObservableProperty] private string  _accountType  = string.Empty;
    [ObservableProperty] private decimal _balance;
    [ObservableProperty] private decimal _initialBalance;
    [ObservableProperty] private bool    _isRefreshingBalance;
    [ObservableProperty] private string  _currency     = string.Empty;
    [ObservableProperty] private long    _pingMs;
    [ObservableProperty] private string  _serverUtc    = string.Empty;
    [ObservableProperty] private string  _statusMessage = string.Empty;
    [ObservableProperty] private string  _uptime        = "00h 00m 00s";
    public VirtualViewModel Virtual { get; } = new();

    public bool   IsVirtual        => AccountType.Equals("virtual", StringComparison.OrdinalIgnoreCase);
    public string AccountTypeLabel => IsVirtual ? "Virtual" : "Real";
    public bool   ShowVirtualGlow  => IsConnected && IsVirtual;
    public bool   ShowRealGlow     => IsConnected && !IsVirtual && !string.IsNullOrEmpty(AccountType);

    public MarketsViewModel Markets { get; }
    public LogViewModel Log { get; } = new();
    public StrategyViewModel Strategy { get; }
    public RecoverViewModel Recover { get; } = new();
    public HistoryViewModel History { get; }
    public PerformanceViewModel Performance { get; } = new();

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

        _virtualEntryModeController = new VirtualEntryModeController();
        _virtualEntryModeController.Start(Virtual.GetSettings());
        Virtual.SettingsChanged += OnVirtualSettingsChanged;

        Markets = new MarketsViewModel(
            tickStream,
            contractService,
            _virtualEntryModeController);
        Markets.SetRecoverViewModel(Recover);
        Strategy = new StrategyViewModel(
            contractService,
            _virtualEntryModeController);
        Strategy.SetRecoverViewModel(Recover);
        History = new HistoryViewModel(contractService);
        History.TradeSettled += OnTradeSettledForPerformance;

        Markets.MarketsRequested += () =>
        {
            Log.IsLogVisible = false;
            Strategy.IsBotVisible = false;
            Recover.IsRecoverVisible = false;
            History.IsHistoryVisible = false;
            Performance.IsPerformanceVisible = false;
            Virtual.IsVirtualVisible = false;
        };
        Markets.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Markets.IsMarketsVisible) && Markets.IsMarketsVisible)
            {
                Log.IsLogVisible = false;
                Strategy.IsBotVisible = false;
                Recover.IsRecoverVisible = false;
                Virtual.IsVirtualVisible = false;
            }
            if (e.PropertyName == nameof(Markets.IsMarketsVisible) || e.PropertyName == nameof(Markets.SelectedTab))
            {
                if (!_restoringState && !_disconnecting)
                    SaveUiState();
                WatchContractPanelChanges();
                Strategy.SetActiveMarketTab(Markets.SelectedTab);
            }
        };
        Log.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Log.IsLogVisible) && Log.IsLogVisible)
            {
                Markets.IsMarketsVisible = false;
                Strategy.IsBotVisible = false;
                Recover.IsRecoverVisible = false;
                History.IsHistoryVisible = false;
                Performance.IsPerformanceVisible = false;
                Virtual.IsVirtualVisible = false;
            }
            if (e.PropertyName == nameof(Log.IsLogVisible))
            {
                if (!_disconnecting)
                    SaveUiState();
            }
        };
        Strategy.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Strategy.IsRunning))
            {
                if (Strategy.IsRunning)
                    StartBotSession();
                else
                    _isBotSessionActive = false;
            }

            if (e.PropertyName == nameof(Strategy.IsBotVisible) && Strategy.IsBotVisible)
            {
                Markets.IsMarketsVisible = false;
                Log.IsLogVisible = false;
                Recover.IsRecoverVisible = false;
                History.IsHistoryVisible = false;
                Performance.IsPerformanceVisible = false;
                Virtual.IsVirtualVisible = false;
            }
        };
        Recover.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Recover.IsRecoverVisible) && Recover.IsRecoverVisible)
            {
                Markets.IsMarketsVisible = false;
                Log.IsLogVisible = false;
                Strategy.IsBotVisible = false;
                History.IsHistoryVisible = false;
                Performance.IsPerformanceVisible = false;
                Virtual.IsVirtualVisible = false;
            }
        };
        History.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(History.IsHistoryVisible) && History.IsHistoryVisible)
            {
                Markets.IsMarketsVisible = false;
                Log.IsLogVisible = false;
                Strategy.IsBotVisible = false;
                Recover.IsRecoverVisible = false;
                Performance.IsPerformanceVisible = false;
                Virtual.IsVirtualVisible = false;
            }
        };
        Performance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Performance.IsPerformanceVisible) && Performance.IsPerformanceVisible)
            {
                Markets.IsMarketsVisible = false;
                Log.IsLogVisible = false;
                Strategy.IsBotVisible = false;
                Recover.IsRecoverVisible = false;
                History.IsHistoryVisible = false;
                Virtual.IsVirtualVisible = false;
            }
        };
        Virtual.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Virtual.IsVirtualVisible) && Virtual.IsVirtualVisible)
            {
                Markets.IsMarketsVisible = false;
                Log.IsLogVisible = false;
                Strategy.IsBotVisible = false;
                Recover.IsRecoverVisible = false;
                History.IsHistoryVisible = false;
                Performance.IsPerformanceVisible = false;
            }
        };

        _api.Authorized     += OnAuthorized;
        _api.BalanceUpdated += OnBalanceUpdated;
        _ws.Disconnected    += OnDisconnected;
        _ws.Connected       += OnConnected;

        Strategy.BotTradeOpened += (_, e) =>
        {
            var tab = Markets.SelectedTab;
            var market = tab?.DisplayName ?? "";
            _currentSessionContracts.Add(e.BuyResult.ContractId);
            History.AddBotTrade(e.BuyResult, e.ContractType, Strategy.StrategyMode, market);
            if (tab != null)
            {
                Performance.OnTradeOpened(e.BuyResult.ContractId, market, e.BuyResult.StartTime,
                    e.EntryCandleIndex, e.EntryCandleType,
                    tab.ChartValues, tab.ChartEpochs, tab.ChartDirections,
                    tab.ChartType, tab.TickCandleValues, tab.CandleValues);
            }
        };

        Strategy.BotTradeCompleted += (_, e) =>
        {
            if (!_currentSessionContracts.Contains(e.ContractId)) return;

            var tab = Markets.SelectedTab;
            if (tab != null)
                Performance.OnTradeClosed(e.ContractId, tab.ChartType, tab.TickCandleValues, tab.CandleValues);

            History.UpdateTradeResult(e.ContractId, e.Profit, e.SellTime);
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += OnTimerTick;

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) =>
        {
            var e = _uptimeWatch.Elapsed;
            Uptime = $"{(int)e.TotalHours:D2}h {e.Minutes:D2}m {e.Seconds:D2}s";
            if (IsConnected)
            {
                var serverNow = DateTimeOffset.UtcNow.Add(_serverOffset);
                ServerUtc = serverNow.ToString("HH'h' mm'm' ss's'") + " UTC";
            }
        };

        AppLogger.Info(Src, $"MainViewModel created — log: {AppLogger.GetLogPath()}");
    }

    private void OnVirtualSettingsChanged(object? sender, EventArgs e)
    {
        _virtualEntryModeController.Start(Virtual.GetSettings());
    }

    private void OnTradeSettledForPerformance(object? sender, TradeHistoryItem trade)
    {
        if (!_currentSessionContracts.Contains(trade.ContractId)) return;

        var tab = Markets.SelectedTab;
        if (tab != null && IsTradeFromSelectedMarket(trade, tab))
            Performance.OnTradeClosed(trade.ContractId, tab.ChartType, tab.TickCandleValues, tab.CandleValues);

        Performance.OnTradeCompleted(trade);
    }

    private static bool IsTradeFromSelectedMarket(TradeHistoryItem trade, MarketTabViewModel tab)
    {
        return string.IsNullOrWhiteSpace(trade.Market)
            || trade.Market.Equals(tab.DisplayName, StringComparison.OrdinalIgnoreCase)
            || trade.Market.Equals(tab.Symbol, StringComparison.OrdinalIgnoreCase);
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
            await _api.ConnectAndAuthorizeAsync(_token);
            TokenStore.Save(_token);

            // Busca ping e hora antes de mostrar a UI — garante que tudo aparece junto
            var pingTask = _api.PingAsync();
            var timeTask = _api.GetServerTimeAsync();
            await Task.WhenAll(pingTask, timeTask);
            PingMs    = pingTask.Result;
            ServerUtc = timeTask.Result.ToString("HH'h' mm'm' ss's'") + " UTC";
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
        _disconnecting = true;
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
            InitialBalance = 0;
            _initialBalanceSet = false;
            Currency      = string.Empty;
            PingMs        = 0;
            ServerUtc     = string.Empty;
            StatusMessage = string.Empty;
            Uptime        = "00h 00m 00s";
            _uptimeTimer.Stop();
            _uptimeWatch.Reset();
            _disconnecting = false;
            ToggleConnectionCommand.NotifyCanExecuteChanged();
            AppLogger.Info(Src, "Disconnected — UI state cleared");
        }
    }

    private void OnAuthorized(object? sender, AuthorizeResponse e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        dispatcher.InvokeAsync(() =>
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
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        dispatcher.InvokeAsync(() =>
        {
            Balance  = e.Balance;
            Currency = e.Currency;
            if (!_initialBalanceSet)
            {
                _initialBalanceSet = true;
                InitialBalance = e.Balance;
            }
            if (!_isBotSessionActive) return;

            var tab = Markets.SelectedTab;
            if (tab != null)
                Performance.CaptureTickSnapshot(tab.ChartValues, tab.ChartEpochs, tab.ChartDirections,
                    tab.ChartType, tab.TickCandleValues, tab.CandleValues);
            Performance.OnBalanceUpdated(e.Balance);
        }).Task.ContinueWith(t =>
            AppLogger.Error(Src, "OnBalanceUpdated dispatcher error", t.Exception?.InnerException),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    [RelayCommand]
    private async Task RefreshBalanceAsync()
    {
        IsRefreshingBalance = true;
        InitialBalance = Balance;
        await Task.Delay(600);
        IsRefreshingBalance = false;
    }

    [RelayCommand]
    private void ToggleVirtualPanel()
    {
        Virtual.ToggleVirtualCommand.Execute(null);
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        _ = HandleConnectedAsync();
    }

    private async Task HandleConnectedAsync()
    {
        var app = Application.Current;
        if (app == null) return;

        var isManual = app.Dispatcher.Invoke(() => IsConnecting);
        if (isManual) return;

        AppLogger.Info(Src, "OnConnected (reconexão automática) — re-autorizando…");
        try
        {
            await _api.ConnectAndAuthorizeAsync(_token);

            var pingTask = _api.PingAsync();
            var timeTask = _api.GetServerTimeAsync();
            await Task.WhenAll(pingTask, timeTask);

            await app.Dispatcher.InvokeAsync(() =>
            {
                PingMs        = pingTask.Result;
                ServerUtc     = timeTask.Result.ToString("HH'h' mm'm' ss's'") + " UTC";
                _serverOffset = timeTask.Result - DateTimeOffset.UtcNow;
                IsConnected   = true;
                StatusMessage = string.Empty;
                if (!_timer.IsEnabled) _timer.Start();
                if (!_uptimeTimer.IsEnabled) { _uptimeWatch.Restart(); _uptimeTimer.Start(); }
            });

            await Markets.ResubscribeActiveAsync();
            if (Strategy.IsRunning)
                Strategy.RefreshProposals();
            AppLogger.Info(Src, "Reconexão automática concluída");
        }
        catch (Exception ex)
        {
            AppLogger.Error(Src, "Re-autorização falhou", ex);
            _ = app.Dispatcher.InvokeAsync(() =>
                StatusMessage = "Reconectando...");
        }
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            if (!IsConnecting)
            {
                StatusMessage = "Reconectando...";
                AppLogger.Warn(Src, "OnDisconnected (unexpected) — showing reconnect status");
            }
        });
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _ = HandleTimerTickAsync();
    }

    private async Task HandleTimerTickAsync()
    {
        if (!IsConnected || Interlocked.CompareExchange(ref _timerBusy, 1, 0) != 0) return;
        try
        {
            var pingTask = _api.PingAsync();
            var timeTask = _api.GetServerTimeAsync();
            await Task.WhenAll(pingTask, timeTask);

            PingMs    = pingTask.Result;
            ServerUtc = timeTask.Result.ToString("HH'h' mm'm' ss's'") + " UTC";
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
    private volatile bool _restoringState;
    private volatile bool _disconnecting;

    private void WatchContractPanelChanges()
    {
        if (_watchedPanel != null)
        {
            _watchedPanel.PropertyChanged -= OnContractPanelChanged;
            _watchedPanel.ManualTradeOpened -= OnManualTradeOpened;
        }

        _watchedPanel = Markets.SelectedTab?.ContractPanel;

        if (_watchedPanel != null)
        {
            _watchedPanel.PropertyChanged += OnContractPanelChanged;
            _watchedPanel.ManualTradeOpened += OnManualTradeOpened;
        }
    }

    private void OnManualTradeOpened(object? sender, ManualTradeOpened e)
    {
        if (!_isBotSessionActive) return;

        var tab = Markets.SelectedTab;
        var market = tab?.DisplayName ?? "";
        _currentSessionContracts.Add(e.BuyResult.ContractId);
        History.AddManualTrade(e.BuyResult, e.ContractType, market);
        if (tab != null)
        {
            Performance.OnTradeOpened(e.BuyResult.ContractId, market, e.BuyResult.StartTime, null, null,
                tab.ChartValues, tab.ChartEpochs, tab.ChartDirections,
                tab.ChartType, tab.TickCandleValues, tab.CandleValues);
        }
    }

    private void StartBotSession()
    {
        _isBotSessionActive = true;
        _currentSessionContracts.Clear();
        History.ResetSession();
        Performance.ResetSession(Balance);
        AppLogger.Info(Src, "Bot session metrics reset");
    }

    private void OnContractPanelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_restoringState || _disconnecting) return;
        if (e.PropertyName is nameof(ContractPanelViewModel.DurationText)
            or nameof(ContractPanelViewModel.DurationUnit)
            or nameof(ContractPanelViewModel.StakeText)
            or nameof(ContractPanelViewModel.UseDuration)
            or nameof(ContractPanelViewModel.SelectedBarrierDisplay)
            or nameof(ContractPanelViewModel.SelectedStrategy)
            or nameof(ContractPanelViewModel.AllowEquals)
            or nameof(ContractPanelViewModel.RecoverMode))
        {
            SaveUiState();
        }
    }

    private void SaveUiState()
    {
        var panel = Log.IsLogVisible ? "log" : Markets.IsMarketsVisible ? "markets" : "";
        var market = Markets.SelectedTab?.Symbol;
        var cp = Markets.SelectedTab?.ContractPanel;
        var chartType = Markets.SelectedTab?.ChartType.ToString();
        UiStateStore.Save(panel, market,
            cp?.DurationUnit.ToString(),
            cp?.DurationText,
            cp?.StakeText,
            cp?.UseDuration,
            cp?.SelectedBarrierDisplay,
            chartType,
            cp?.SelectedStrategy.DisplayName,
            cp?.AllowEquals,
            cp?.RecoverMode);
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
                    tab.ContractPanel.RestoreState(state.DurationUnit, state.DurationText, state.StakeText, state.UseDuration, state.SelectedBarrierDisplay, state.SelectedStrategy, state.AllowEquals, state.RecoverMode);
                    if (!string.IsNullOrEmpty(state.ChartType) && Enum.TryParse<ChartType>(state.ChartType, out var ct))
                    {
                        if (ct == ChartType.TickCandles && int.TryParse(state.DurationText, out var n) && n >= 1)
                            tab.EnableTickCandles(n);
                        else
                            tab.ChartType = ct;
                    }
                    await Markets.SelectTabAsync(tab);
                    _restoringState = false;
                }
            }
        }
    }

    public void Dispose()
    {
        SaveUiState();
        if (_watchedPanel != null)
        {
            _watchedPanel.PropertyChanged -= OnContractPanelChanged;
            _watchedPanel.ManualTradeOpened -= OnManualTradeOpened;
        }
        _timer.Stop();
        _uptimeTimer.Stop();
        _uptimeWatch.Stop();
        _api.Authorized     -= OnAuthorized;
        _api.BalanceUpdated -= OnBalanceUpdated;
        _ws.Disconnected    -= OnDisconnected;
        _ws.Connected       -= OnConnected;
        Virtual.SettingsChanged -= OnVirtualSettingsChanged;
        Markets.Dispose();
        Strategy.Dispose();
        Recover.Dispose();
        History.Dispose();
        Log.Dispose();
        (_tickStream as IDisposable)?.Dispose();
        (_contractService as IDisposable)?.Dispose();
        (_api as IDisposable)?.Dispose();
        AppLogger.Info(Src, "MainViewModel disposed");
    }
}
