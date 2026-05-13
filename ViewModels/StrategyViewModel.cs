using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Excalibur5.Config;
using Excalibur5.Models;
using Excalibur5.Models.Strategy;
using Excalibur5.Services;
using Excalibur5.Services.Strategy;

namespace Excalibur5.ViewModels;

public partial class StrategyViewModel : ObservableObject, IDisposable
{
    private const string Src = "Strategy";

    private readonly IContractService _contractService;
    private readonly StrategyEngine _engine = new();
    private StrategyExecutor? _executor;
    private string _activeSymbol = string.Empty;
    private EventHandler? _candleHandler;
    private MarketTabViewModel? _activeMarketTab;
    private bool _restoringState;

    // Config
    [ObservableProperty] private int _timeframe = 60;
    [ObservableProperty] private string _directionMode = "Ambos"; // "Call", "Put", "Ambos"
    [ObservableProperty] private string _stakeText = "10";
    [ObservableProperty] private string _takeProfitText = "5.00";
    [ObservableProperty] private string _stopLossText = "3.00";
    [ObservableProperty] private string _maxContractsText = "3";
    [ObservableProperty] private int _durationMinutes = 5;
    [ObservableProperty] private double _confidenceThreshold = 0.70;

    // Indicators
    [ObservableProperty] private bool _enableEma = true;
    [ObservableProperty] private bool _enableRsi = true;
    [ObservableProperty] private bool _enableSupportResistance = true;

    // Status
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isBotVisible;
    [ObservableProperty] private string _currentSignalText = "Aguardando...";
    [ObservableProperty] private string _statsText = "0 ops (0W/0L) 0.00 USD";
    [ObservableProperty] private string _lastTradeText = string.Empty;

    public StrategyViewModel(IContractService contractService)
    {
        _contractService = contractService;
        _engine.SignalGenerated += OnSignalGenerated;
        RestoreState();
    }

    private void RestoreState()
    {
        _restoringState = true;
        var s = BotStateStore.Load();
        Timeframe = s.Timeframe;
        DirectionMode = s.DirectionMode;
        StakeText = s.StakeText;
        TakeProfitText = s.TakeProfitText;
        StopLossText = s.StopLossText;
        MaxContractsText = s.MaxContractsText;
        DurationMinutes = s.DurationMinutes;
        ConfidenceThreshold = s.ConfidenceThreshold;
        EnableEma = s.EnableEma;
        EnableRsi = s.EnableRsi;
        EnableSupportResistance = s.EnableSupportResistance;
        _restoringState = false;
    }

    private void SaveState()
    {
        if (_restoringState) return;
        BotStateStore.Save(new BotState
        {
            Timeframe = Timeframe,
            DirectionMode = DirectionMode,
            StakeText = StakeText,
            TakeProfitText = TakeProfitText,
            StopLossText = StopLossText,
            MaxContractsText = MaxContractsText,
            DurationMinutes = DurationMinutes,
            ConfidenceThreshold = ConfidenceThreshold,
            EnableEma = EnableEma,
            EnableRsi = EnableRsi,
            EnableSupportResistance = EnableSupportResistance
        });
    }

    partial void OnTimeframeChanged(int value) => SaveState();
    partial void OnDirectionModeChanged(string value) => SaveState();
    partial void OnStakeTextChanged(string value) => SaveState();
    partial void OnTakeProfitTextChanged(string value) => SaveState();
    partial void OnStopLossTextChanged(string value) => SaveState();
    partial void OnMaxContractsTextChanged(string value) => SaveState();
    partial void OnDurationMinutesChanged(int value) => SaveState();
    partial void OnConfidenceThresholdChanged(double value) => SaveState();
    partial void OnEnableEmaChanged(bool value) => SaveState();
    partial void OnEnableRsiChanged(bool value) => SaveState();
    partial void OnEnableSupportResistanceChanged(bool value) => SaveState();

    [RelayCommand]
    private void ToggleBot()
    {
        IsBotVisible = !IsBotVisible;
    }

    [RelayCommand]
    private void Start()
    {
        if (IsRunning) return;
        if (_activeMarketTab == null)
        {
            AppLogger.Warn(Src, "Cannot start — no active market tab");
            return;
        }

        var config = BuildConfig();
        _activeSymbol = _activeMarketTab.Symbol;

        _executor?.Dispose();
        _executor = new StrategyExecutor(_contractService, _engine);
        _executor.StatsUpdated += OnStatsUpdated;
        _executor.TradeExecuted += OnTradeExecuted;

        _engine.Start(config);
        _executor.Start(config, _activeSymbol);

        // Feed existing candles
        foreach (var candle in _activeMarketTab.CandleValues)
            _engine.FeedCandle(candle);

        // Subscribe to new candles
        _candleHandler = (_, _) => OnCandleUpdated();
        _activeMarketTab.CandleUpdated += _candleHandler;

        IsRunning = true;
        IsPaused = false;
        CurrentSignalText = "Analisando...";
        AppLogger.Info(Src, $"Bot started on {_activeSymbol}");
    }

    [RelayCommand]
    private void Pause()
    {
        if (!IsRunning) return;
        IsPaused = !IsPaused;
        if (IsPaused)
        {
            _executor?.Stop();
            CurrentSignalText = "Pausado";
        }
        else
        {
            var config = BuildConfig();
            _executor?.Start(config, _activeSymbol);
            CurrentSignalText = "Analisando...";
        }
    }

    [RelayCommand]
    private void Stop()
    {
        if (!IsRunning) return;

        _engine.Stop();
        _executor?.Stop();

        if (_activeMarketTab != null && _candleHandler != null)
        {
            _activeMarketTab.CandleUpdated -= _candleHandler;
            _candleHandler = null;
        }

        IsRunning = false;
        IsPaused = false;
        CurrentSignalText = "Parado";
        AppLogger.Info(Src, "Bot stopped");
    }

    public void SetActiveMarketTab(MarketTabViewModel? tab)
    {
        if (_activeMarketTab == tab) return;

        // If running and tab changes, stop the bot
        if (IsRunning && tab?.Symbol != _activeSymbol)
            Stop();

        _activeMarketTab = tab;
    }

    private void OnCandleUpdated()
    {
        if (!IsRunning || IsPaused || _activeMarketTab == null) return;

        var candles = _activeMarketTab.CandleValues;
        if (candles.Count == 0) return;

        _engine.FeedCandle(candles[^1]);
    }

    private void OnSignalGenerated(object? sender, TradeSignal signal)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            var dir = signal.Direction == SignalDirection.Call ? "CALL" : "PUT";
            CurrentSignalText = $"{dir} (conf: {signal.Confidence:P0})";
        });
    }

    private void OnStatsUpdated(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            if (_executor == null) return;
            var s = _executor.Stats;
            StatsText = $"{s.TotalTrades} ops ({s.Wins}W/{s.Losses}L) {s.AccumulatedProfit:+0.00;-0.00} USD";
        });
    }

    private void OnTradeExecuted(object? sender, string message)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            LastTradeText = message;
        });
    }

    private StrategyConfig BuildConfig()
    {
        var indicators = new List<IndicatorType>();
        if (EnableEma) indicators.Add(IndicatorType.EmaCrossover);
        if (EnableRsi) indicators.Add(IndicatorType.Rsi);
        if (EnableSupportResistance) indicators.Add(IndicatorType.SupportResistance);

        var direction = DirectionMode switch
        {
            "Call" => SignalDirection.Call,
            "Put" => SignalDirection.Put,
            _ => SignalDirection.None // Both
        };

        return new StrategyConfig
        {
            Timeframe = Timeframe,
            AllowedDirection = direction,
            Stake = decimal.TryParse(StakeText, out var s) ? s : 10m,
            TakeProfitUsd = decimal.TryParse(TakeProfitText, out var tp) ? tp : 5m,
            StopLossUsd = decimal.TryParse(StopLossText, out var sl) ? sl : 3m,
            MaxConcurrentContracts = int.TryParse(MaxContractsText, out var mc) ? mc : 3,
            DurationMinutes = DurationMinutes,
            ConfidenceThreshold = ConfidenceThreshold,
            EnabledIndicators = indicators
        };
    }

    public void Dispose()
    {
        SaveState();
        Stop();
        _engine.SignalGenerated -= OnSignalGenerated;
        _executor?.Dispose();
    }
}
