using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly TrendEngine _trendEngine = new();
    private StrategyExecutor? _executor;
    private string _activeSymbol = string.Empty;
    private EventHandler? _candleHandler;
    private MarketTabViewModel? _activeMarketTab;
    private bool _restoringState;
    private RecoverViewModel? _recoverVm;
    private long _lastTrendCandleEpoch;

    // Config
    [ObservableProperty] private bool _useDuration = true;
    [ObservableProperty] private string _durationUnit = "Minutes";
    [ObservableProperty] private string _durationText = "5";
    [ObservableProperty] private string _durationRange = "Intervalo: 1 - 1440 minutos";
    [ObservableProperty] private DateTime _selectedEndDate = DateTime.UtcNow.Date.AddDays(1);
    [ObservableProperty] private string _expiryDisplay = string.Empty;
    [ObservableProperty] private string _selectedBarrierDisplay = string.Empty;
    [ObservableProperty] private string _payoutPerPointDisplay = "0.000000";
    [ObservableProperty] private string _directionMode = "Ambos"; // "Call", "Put", "Ambos"
    [ObservableProperty] private string _stakeText = "10";
    [ObservableProperty] private string _takeProfitText = "5.00";
    [ObservableProperty] private string _stopLossText = "3.00";
    [ObservableProperty] private string _maxContractsText = "3";
    [ObservableProperty] private double _confidenceThreshold = 0.70;
    [ObservableProperty] private string _recoverMode = string.Empty;
    [ObservableProperty] private string _strategyMode = string.Empty;
    [ObservableProperty] private string _sampleSizeText = "5";

    public ObservableCollection<string> AvailableBarrierDisplays { get; } = new();
    public bool UseEndTime => !UseDuration;
    public List<string> RecoverModes { get; } = ["", "Martingale"];
    public List<string> StrategyModes { get; } = ["", "Tendência"];
    public bool IsTrendMode => StrategyMode == "Tendência";

    // Indicators
    [ObservableProperty] private bool _enableEma = true;
    [ObservableProperty] private bool _enableRsi = true;
    [ObservableProperty] private bool _enableSupportResistance = true;
    [ObservableProperty] private bool _enableMacd = true;
    [ObservableProperty] private bool _enableBollinger = true;
    [ObservableProperty] private bool _enableCandlePattern = true;
    [ObservableProperty] private bool _enableMomentum = true;
    [ObservableProperty] private bool _enableTrailingStop = true;

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

    public void SetRecoverViewModel(RecoverViewModel recoverVm)
    {
        _recoverVm = recoverVm;
    }

    private void RestoreState()
    {
        _restoringState = true;
        var s = BotStateStore.Load();
        UseDuration = s.UseDuration;
        DurationUnit = s.DurationUnit;
        DurationText = s.DurationText;
        DirectionMode = s.DirectionMode;
        StakeText = s.StakeText;
        TakeProfitText = s.TakeProfitText;
        StopLossText = s.StopLossText;
        MaxContractsText = s.MaxContractsText;
        ConfidenceThreshold = s.ConfidenceThreshold;
        RecoverMode = s.RecoverMode;
        StrategyMode = s.StrategyMode;
        SampleSizeText = s.SampleSizeText;
        EnableEma = s.EnableEma;
        EnableRsi = s.EnableRsi;
        EnableSupportResistance = s.EnableSupportResistance;
        EnableMacd = s.EnableMacd;
        EnableBollinger = s.EnableBollinger;
        EnableCandlePattern = s.EnableCandlePattern;
        EnableMomentum = s.EnableMomentum;
        EnableTrailingStop = s.EnableTrailingStop;
        _restoringState = false;
    }

    private void SaveState()
    {
        if (_restoringState) return;
        BotStateStore.Save(new BotState
        {
            UseDuration = UseDuration,
            DurationUnit = DurationUnit,
            DurationText = DurationText,
            DirectionMode = DirectionMode,
            StakeText = StakeText,
            TakeProfitText = TakeProfitText,
            StopLossText = StopLossText,
            MaxContractsText = MaxContractsText,
            ConfidenceThreshold = ConfidenceThreshold,
            RecoverMode = RecoverMode,
            StrategyMode = StrategyMode,
            SampleSizeText = SampleSizeText,
            EnableEma = EnableEma,
            EnableRsi = EnableRsi,
            EnableSupportResistance = EnableSupportResistance,
            EnableMacd = EnableMacd,
            EnableBollinger = EnableBollinger,
            EnableCandlePattern = EnableCandlePattern,
            EnableMomentum = EnableMomentum,
            EnableTrailingStop = EnableTrailingStop
        });
    }

    partial void OnUseDurationChanged(bool value)
    {
        OnPropertyChanged(nameof(UseEndTime));
        SaveState();
    }
    partial void OnDurationUnitChanged(string value)
    {
        DurationRange = value switch
        {
            "Hours" => "Intervalo: 1 - 24 horas",
            "Days" => "Intervalo: 1 - 365 dias",
            _ => "Intervalo: 1 - 1440 minutos"
        };
        SaveState();
        UpdateChartGranularity();
    }
    partial void OnDurationTextChanged(string value)
    {
        SaveState();
        UpdateChartGranularity();
    }
    partial void OnSelectedEndDateChanged(DateTime value) => SaveState();
    partial void OnDirectionModeChanged(string value) => SaveState();
    partial void OnStakeTextChanged(string value) => SaveState();
    partial void OnTakeProfitTextChanged(string value) => SaveState();
    partial void OnStopLossTextChanged(string value) => SaveState();
    partial void OnMaxContractsTextChanged(string value) => SaveState();
    partial void OnConfidenceThresholdChanged(double value) => SaveState();
    partial void OnEnableEmaChanged(bool value) => SaveState();
    partial void OnEnableRsiChanged(bool value) => SaveState();
    partial void OnEnableSupportResistanceChanged(bool value) => SaveState();
    partial void OnEnableMacdChanged(bool value) => SaveState();
    partial void OnEnableBollingerChanged(bool value) => SaveState();
    partial void OnEnableCandlePatternChanged(bool value) => SaveState();
    partial void OnEnableMomentumChanged(bool value) => SaveState();
    partial void OnEnableTrailingStopChanged(bool value) => SaveState();
    partial void OnRecoverModeChanged(string value) => SaveState();
    partial void OnStrategyModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsTrendMode));
        SaveState();
        UpdateChartGranularity();
    }
    partial void OnSampleSizeTextChanged(string value) => SaveState();

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
        _executor.PositionOpened += OnBotPositionOpened;

        if (!IsTrendMode)
        {
            _engine.Start(config);
            _executor.Start(config, _activeSymbol);

            _engine.BeginBulkFeed();
            foreach (var candle in _activeMarketTab.CandleValues)
                _engine.FeedCandle(candle);
            _engine.EndBulkFeed();
        }
        else
        {
            _executor.Start(config, _activeSymbol);
            _lastTrendCandleEpoch = _activeMarketTab.CandleValues.Count > 0
                ? _activeMarketTab.CandleValues[^1].Epoch : 0;
        }

        _candleHandler = (_, _) => OnCandleUpdated();
        _activeMarketTab.CandleUpdated += _candleHandler;

        _activeMarketTab.ContractPanel.LockBarrier();

        IsRunning = true;
        IsPaused = false;
        CurrentSignalText = "Analisando...";
        AppLogger.Info(Src, $"Bot started on {_activeSymbol} (mode={StrategyMode})");
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

        _activeMarketTab?.ContractPanel.UnlockBarrier();

        IsRunning = false;
        IsPaused = false;
        CurrentSignalText = "Parado";
        AppLogger.Info(Src, "Bot stopped");
    }

    public void SetActiveMarketTab(MarketTabViewModel? tab)
    {
        if (_activeMarketTab == tab) return;

        if (IsRunning && tab?.Symbol != _activeSymbol)
            Stop();

        if (_activeMarketTab != null)
            _activeMarketTab.ContractPanel.PropertyChanged -= OnContractPanelSync;

        _activeMarketTab = tab;

        if (_activeMarketTab != null)
        {
            _activeMarketTab.ContractPanel.PropertyChanged += OnContractPanelSync;
            SyncFromContractPanel();
            UpdateChartGranularity();
        }
    }

    private void OnContractPanelSync(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ContractPanelViewModel.CallPayoutPerPoint)
            or nameof(ContractPanelViewModel.PutPayoutPerPoint)
            or nameof(ContractPanelViewModel.ExpiryDisplay)
            or nameof(ContractPanelViewModel.ContractsLoaded))
        {
            Application.Current?.Dispatcher?.InvokeAsync(SyncFromContractPanel);
        }
    }

    private void SyncFromContractPanel()
    {
        if (_activeMarketTab == null) return;
        var cp = _activeMarketTab.ContractPanel;

        PayoutPerPointDisplay = cp.CallPayoutPerPoint.ToString("F6");
        ExpiryDisplay = cp.ExpiryDisplay;

        var currentSelection = SelectedBarrierDisplay;
        AvailableBarrierDisplays.Clear();
        foreach (var b in cp.AvailableBarrierDisplays)
            AvailableBarrierDisplays.Add(b);

        if (!string.IsNullOrEmpty(currentSelection) && AvailableBarrierDisplays.Contains(currentSelection))
            SelectedBarrierDisplay = currentSelection;
        else if (AvailableBarrierDisplays.Count > 0 && string.IsNullOrEmpty(currentSelection))
            SelectedBarrierDisplay = AvailableBarrierDisplays[0];
    }

    private void OnCandleUpdated()
    {
        if (!IsRunning || IsPaused || _activeMarketTab == null) return;

        var candles = _activeMarketTab.CandleValues;
        if (candles.Count == 0) return;

        if (IsTrendMode)
        {
            var lastCandle = candles[^1];
            if (lastCandle.Epoch <= _lastTrendCandleEpoch) return;
            _lastTrendCandleEpoch = lastCandle.Epoch;

            var sampleSize = int.TryParse(SampleSizeText, out var ss) && ss >= 1 ? ss : 5;
            var direction = _trendEngine.Evaluate(candles, sampleSize);

            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                if (direction == null)
                {
                    CurrentSignalText = "Empate — aguardando tendência";
                    return;
                }

                var dir = direction == SignalDirection.Call ? "CALL" : "PUT";
                CurrentSignalText = $"Tendência: {dir} ({sampleSize} candles)";
            });

            if (direction != null)
            {
                var signal = new TradeSignal
                {
                    Direction = direction.Value,
                    Confidence = 1.0,
                    Reason = $"Tendência {sampleSize} candles",
                    Timestamp = DateTimeOffset.UtcNow,
                    ContributingIndicators = new List<IndicatorType>()
                };
                _engine.EmitExternalSignal(signal);
            }
        }
        else
        {
            _engine.FeedCandle(candles[^1]);
        }
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

    private void OnBotPositionOpened(object? sender, BotPositionOpened e)
    {
        if (_activeMarketTab == null) return;
        var openPos = _activeMarketTab.ContractPanel.OpenPositions;
        var expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + CalculateDurationSeconds();
        _ = openPos.AddPositionAsync(e.BuyResult, _activeSymbol, _activeMarketTab.DisplayName, e.ContractType, expiry);
    }

    private StrategyConfig BuildConfig()
    {
        var indicators = new List<IndicatorType>();
        if (EnableEma) indicators.Add(IndicatorType.EmaCrossover);
        if (EnableRsi) indicators.Add(IndicatorType.Rsi);
        if (EnableSupportResistance) indicators.Add(IndicatorType.SupportResistance);
        if (EnableMacd) indicators.Add(IndicatorType.Macd);
        if (EnableBollinger) indicators.Add(IndicatorType.BollingerBands);
        if (EnableCandlePattern) indicators.Add(IndicatorType.CandlePattern);
        if (EnableMomentum) indicators.Add(IndicatorType.Momentum);

        var direction = DirectionMode switch
        {
            "Call" => SignalDirection.Call,
            "Put" => SignalDirection.Put,
            _ => SignalDirection.None // Both
        };

        return new StrategyConfig
        {
            AllowedDirection = direction,
            Stake = decimal.TryParse(StakeText, NumberStyles.Any, CultureInfo.InvariantCulture, out var s) ? s : 10m,
            TakeProfitUsd = decimal.TryParse(TakeProfitText, NumberStyles.Any, CultureInfo.InvariantCulture, out var tp) ? tp : 5m,
            StopLossUsd = decimal.TryParse(StopLossText, NumberStyles.Any, CultureInfo.InvariantCulture, out var sl) ? sl : 3m,
            MaxConcurrentContracts = int.TryParse(MaxContractsText, out var mc) ? mc : 3,
            DurationSeconds = CalculateDurationSeconds(),
            ConfidenceThreshold = ConfidenceThreshold,
            EnableTrailingStop = IsTrendMode ? false : EnableTrailingStop,
            RecoverMode = RecoverMode,
            MartingaleFactor = _recoverVm?.Factor ?? 2.0m,
            MartingaleMaxLevel = _recoverVm?.MaxLevel ?? 3,
            EnabledIndicators = indicators,
            Barrier = GetSelectedBarrier(),
            StrategyMode = StrategyMode,
            SampleSize = int.TryParse(SampleSizeText, out var ss) && ss >= 1 ? ss : 5
        };
    }

    private int CalculateDurationSeconds()
    {
        if (!int.TryParse(DurationText, out var val)) val = 5;
        return DurationUnit switch
        {
            "Hours" => val * 3600,
            "Days" => val * 86400,
            _ => val * 60
        };
    }

    private void UpdateChartGranularity()
    {
        if (_restoringState || _activeMarketTab == null) return;
        var seconds = CalculateDurationSeconds();
        _ = _activeMarketTab.SetCandleGranularityAsync(seconds);
    }

    private string GetSelectedBarrier()
    {
        if (_activeMarketTab != null)
        {
            var cpBarrier = _activeMarketTab.ContractPanel.SelectedBarrierDisplay;
            if (!string.IsNullOrEmpty(cpBarrier))
                return cpBarrier;
        }

        if (!string.IsNullOrEmpty(SelectedBarrierDisplay))
            return SelectedBarrierDisplay;

        return "+0.000";
    }

    public void Dispose()
    {
        SaveState();
        Stop();
        if (_activeMarketTab != null)
            _activeMarketTab.ContractPanel.PropertyChanged -= OnContractPanelSync;
        _engine.SignalGenerated -= OnSignalGenerated;
        _executor?.Dispose();
    }
}
