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

    public event EventHandler<BotPositionOpened>? BotTradeOpened;
    public event EventHandler<TradeCompleted>? BotTradeCompleted;

    private readonly IContractService _contractService;
    private readonly StrategyEngine _engine = new();
    private readonly TrendEngine _trendEngine = new();
    private readonly TickScalperEngine _tickScalperEngine = new();
    private readonly CandleDynamicsEngine _candleDynamicsEngine = new();
    private StrategyExecutor? _executor;
    private string _activeSymbol = string.Empty;
    private EventHandler? _candleHandler;
    private EventHandler<TickData>? _tickHandler;
    private EventHandler? _tickCandleHandler;
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
    [ObservableProperty] private string _strategyMode = "Multi-Indicador";
    [ObservableProperty] private string _sampleSizeText = "5";
    [ObservableProperty] private string _deficitMaxStakeText = "50";
    [ObservableProperty] private string _deficitRecoveryTradesText = "1";

    // Tick Scalper config
    [ObservableProperty] private int _tickScalperCooldown = 5;
    [ObservableProperty] private double _tickScalperThreshold = 0.70;
    [ObservableProperty] private int _tickScalperMinAgreement = 2;
    [ObservableProperty] private bool _tickScalperFlatFilter = true;

    // Candle Dynamics config
    [ObservableProperty] private int _candleDynamicsCooldown = 10;
    [ObservableProperty] private double _candleDynamicsThreshold = 0.55;
    [ObservableProperty] private int _candleDynamicsMinStreak = 3;

    public ObservableCollection<string> AvailableBarrierDisplays { get; } = new();
    public bool UseEndTime => !UseDuration;
    public List<string> RecoverModes { get; } = ["", "Martingale", "Deficit Recovery"];
    public List<string> StrategyModes { get; } = ["Multi-Indicador", "Tendência", "Tick Scalper", "Candle Dynamics"];
    public bool IsTrendMode => StrategyMode == "Tendência";
    public bool IsTickScalperMode => StrategyMode == "Tick Scalper";
    public bool IsCandleDynamicsMode => StrategyMode == "Candle Dynamics";
    public bool IsDeficitMode => RecoverMode == "Deficit Recovery";

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
        _tickScalperEngine.SignalGenerated += OnTickScalperSignal;
        _candleDynamicsEngine.SignalGenerated += OnCandleDynamicsSignal;
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
        DeficitMaxStakeText = s.DeficitMaxStakeText;
        DeficitRecoveryTradesText = s.DeficitRecoveryTradesText;
        EnableEma = s.EnableEma;
        EnableRsi = s.EnableRsi;
        EnableSupportResistance = s.EnableSupportResistance;
        EnableMacd = s.EnableMacd;
        EnableBollinger = s.EnableBollinger;
        EnableCandlePattern = s.EnableCandlePattern;
        EnableMomentum = s.EnableMomentum;
        EnableTrailingStop = s.EnableTrailingStop;
        TickScalperCooldown = s.TickScalperCooldown;
        TickScalperThreshold = s.TickScalperThreshold;
        TickScalperMinAgreement = s.TickScalperMinAgreement;
        TickScalperFlatFilter = s.TickScalperFlatFilter;
        CandleDynamicsCooldown = s.CandleDynamicsCooldown;
        CandleDynamicsThreshold = s.CandleDynamicsThreshold;
        CandleDynamicsMinStreak = s.CandleDynamicsMinStreak;
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
            DeficitMaxStakeText = DeficitMaxStakeText,
            DeficitRecoveryTradesText = DeficitRecoveryTradesText,
            EnableEma = EnableEma,
            EnableRsi = EnableRsi,
            EnableSupportResistance = EnableSupportResistance,
            EnableMacd = EnableMacd,
            EnableBollinger = EnableBollinger,
            EnableCandlePattern = EnableCandlePattern,
            EnableMomentum = EnableMomentum,
            EnableTrailingStop = EnableTrailingStop,
            TickScalperCooldown = TickScalperCooldown,
            TickScalperThreshold = TickScalperThreshold,
            TickScalperMinAgreement = TickScalperMinAgreement,
            TickScalperFlatFilter = TickScalperFlatFilter,
            CandleDynamicsCooldown = CandleDynamicsCooldown,
            CandleDynamicsThreshold = CandleDynamicsThreshold,
            CandleDynamicsMinStreak = CandleDynamicsMinStreak
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
            "Ticks" => "Intervalo: 1 - 10 ticks",
            "Seconds" => "Intervalo: 15 - 86400 segundos",
            "Hours" => "Intervalo: 1 - 24 horas",
            "Days" => "Intervalo: 1 - 365 dias",
            _ => "Intervalo: 1 - 1440 minutos"
        };
        SaveState();
        UpdateChartGranularity();
        SyncDurationToMarketTab();
    }
    partial void OnDurationTextChanged(string value)
    {
        if (int.TryParse(value, out var val))
        {
            var max = DurationUnit switch
            {
                "Ticks" => 10,
                "Seconds" => 86400,
                "Hours" => 24,
                "Days" => 365,
                _ => 1440
            };
            var min = DurationUnit == "Seconds" ? 15 : 1;
            if (val > max) { DurationText = max.ToString(); return; }
            if (val < min && value.Length > 0 && value != "0") { DurationText = min.ToString(); return; }
        }
        SaveState();
        UpdateChartGranularity();
        SyncDurationToMarketTab();
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
    partial void OnRecoverModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsDeficitMode));
        SaveState();
    }
    partial void OnStrategyModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsTrendMode));
        OnPropertyChanged(nameof(IsTickScalperMode));
        OnPropertyChanged(nameof(IsCandleDynamicsMode));
        SaveState();
        UpdateChartGranularity();
        SyncDurationToMarketTab();
    }
    partial void OnSampleSizeTextChanged(string value) => SaveState();
    partial void OnDeficitMaxStakeTextChanged(string value) => SaveState();
    partial void OnDeficitRecoveryTradesTextChanged(string value) => SaveState();
    partial void OnTickScalperCooldownChanged(int value) => SaveState();
    partial void OnTickScalperThresholdChanged(double value) => SaveState();
    partial void OnTickScalperMinAgreementChanged(int value) => SaveState();
    partial void OnTickScalperFlatFilterChanged(bool value) => SaveState();
    partial void OnCandleDynamicsCooldownChanged(int value) => SaveState();
    partial void OnCandleDynamicsThresholdChanged(double value) => SaveState();
    partial void OnCandleDynamicsMinStreakChanged(int value) => SaveState();

    [RelayCommand]
    private void ToggleBot()
    {
        IsBotVisible = !IsBotVisible;
    }

    [RelayCommand]
    private async Task Start()
    {
        if (IsRunning) return;
        if (_activeMarketTab == null)
        {
            AppLogger.Warn(Src, "Cannot start — no active market tab");
            return;
        }

        var config = BuildConfig();
        _activeSymbol = _activeMarketTab.Symbol;

        _activeMarketTab.ContractPanel.LockBarrier();
        _activeMarketTab.ContractPanel.SuspendProposals();
        await Task.Delay(200);

        _executor?.Dispose();
        _executor = new StrategyExecutor(_contractService, _engine);
        _executor.StatsUpdated += OnStatsUpdated;
        _executor.TradeExecuted += OnTradeExecuted;
        _executor.PositionOpened += OnBotPositionOpened;
        _executor.TradeCompleted += OnBotTradeCompleted;

        if (IsTickScalperMode)
        {
            _tickScalperEngine.Start(
                TickScalperCooldown,
                TickScalperThreshold,
                TickScalperMinAgreement,
                TickScalperFlatFilter);
            _executor.Start(config, _activeSymbol);

            // Feed tick history
            var history = _activeMarketTab.ChartValues;
            if (history.Count > 0)
                _tickScalperEngine.FeedHistory(history);

            // Feed tick candles
            if (_activeMarketTab.TickCandleValues.Count > 0)
                _tickScalperEngine.FeedTickCandles(_activeMarketTab.TickCandleValues);

            // Subscribe to live ticks
            _tickHandler = (_, tick) => OnTickReceived(tick);
            _activeMarketTab.TickReceived += _tickHandler;

            // Subscribe to tick candle updates
            _tickCandleHandler = (_, _) => OnTickCandleUpdated();
            _activeMarketTab.TickCandleUpdated += _tickCandleHandler;
        }
        else if (IsCandleDynamicsMode)
        {
            _candleDynamicsEngine.Start(
                CandleDynamicsCooldown,
                CandleDynamicsThreshold,
                CandleDynamicsMinStreak);
            _executor.Start(config, _activeSymbol);

            var history = _activeMarketTab.ChartValues;
            if (history.Count > 0)
                _candleDynamicsEngine.FeedHistory(history);

            _tickHandler = (_, tick) => OnTickReceived(tick);
            _activeMarketTab.TickReceived += _tickHandler;

            _tickCandleHandler = (_, _) => OnTickCandleUpdated();
            _activeMarketTab.TickCandleUpdated += _tickCandleHandler;
        }
        else if (!IsTrendMode)
        {
            _engine.Start(config);
            _executor.Start(config, _activeSymbol);

            if (!_activeMarketTab.CandleValues.Any())
                await _activeMarketTab.LoadCandlesAsync();

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

        if (!IsTickScalperMode && !IsCandleDynamicsMode)
        {
            _candleHandler = (_, _) => OnCandleUpdated();
            _activeMarketTab.CandleUpdated += _candleHandler;
        }

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
        _tickScalperEngine.Stop();
        _candleDynamicsEngine.Stop();
        _executor?.Stop();

        if (_activeMarketTab != null && _candleHandler != null)
        {
            _activeMarketTab.CandleUpdated -= _candleHandler;
            _candleHandler = null;
        }

        if (_activeMarketTab != null && _tickHandler != null)
        {
            _activeMarketTab.TickReceived -= _tickHandler;
            _tickHandler = null;
        }

        if (_activeMarketTab != null && _tickCandleHandler != null)
        {
            _activeMarketTab.TickCandleUpdated -= _tickCandleHandler;
            _tickCandleHandler = null;
        }

        _activeMarketTab?.ContractPanel.UnlockBarrier();
        _activeMarketTab?.ContractPanel.ResumeProposals();

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
            SyncDurationToMarketTab();
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

        var lastCandle = candles[^1];
        _executor?.UpdateCurrentSpot(lastCandle.Close);

        if (IsTrendMode)
        {
            if (lastCandle.Epoch <= _lastTrendCandleEpoch) return;
            _lastTrendCandleEpoch = lastCandle.Epoch;

            if (candles.Count >= 2)
            {
                var previousClose = candles[^2].Close;
                _executor?.ResolveExpiredPositionsLocally(previousClose);
            }

            var sampleSize = int.TryParse(SampleSizeText, out var ss) && ss >= 1 ? ss : 5;
            var direction = _trendEngine.Evaluate(candles, sampleSize);

            AppLogger.Info(Src, $"New candle detected epoch={lastCandle.Epoch}, trend={direction?.ToString() ?? "Empate"}, sample={sampleSize}");

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
        var durationSec = CalculateDurationSeconds();
        var expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + durationSec;
        _ = openPos.AddPositionAsync(e.BuyResult, _activeSymbol, _activeMarketTab.DisplayName, e.ContractType, expiry, durationSec);
        BotTradeOpened?.Invoke(this, e);
    }

    private void OnBotTradeCompleted(object? sender, TradeCompleted e)
    {
        if (IsTickScalperMode)
        {
            _tickScalperEngine.ReportTradeResult(e.Won);
            if (e.Won)
                _tickScalperEngine.SetCooldown();
        }
        else if (IsCandleDynamicsMode)
        {
            _candleDynamicsEngine.ReportTradeResult(e.Won);
            if (e.Won)
                _candleDynamicsEngine.SetCooldown();
        }
        BotTradeCompleted?.Invoke(this, e);
    }

    private void OnTickReceived(TickData tick)
    {
        if (!IsRunning || IsPaused) return;
        _executor?.UpdateCurrentSpot(tick.Quote);

        if (IsTickScalperMode)
            _tickScalperEngine.FeedTick(tick.Quote);
        else if (IsCandleDynamicsMode)
            _candleDynamicsEngine.FeedTick(tick.Quote);
    }

    private void OnTickCandleUpdated()
    {
        if (!IsRunning || IsPaused || _activeMarketTab == null) return;
        _tickScalperEngine.FeedTickCandles(_activeMarketTab.TickCandleValues);
    }

    private void OnTickScalperSignal(object? sender, TradeSignal signal)
    {
        _engine.EmitExternalSignal(signal);

        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            var dir = signal.Direction == SignalDirection.Call ? "CALL" : "PUT";
            CurrentSignalText = $"{dir} (conf: {signal.Confidence:P0})";
        });
    }

    private void OnCandleDynamicsSignal(object? sender, TradeSignal signal)
    {
        _engine.EmitExternalSignal(signal);

        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            var dir = signal.Direction == SignalDirection.Call ? "CALL" : "PUT";
            CurrentSignalText = $"{dir} (conf: {signal.Confidence:P0})";
        });
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

        var (apiValue, apiUnit) = GetDurationApi();

        return new StrategyConfig
        {
            AllowedDirection = direction,
            Stake = decimal.TryParse(StakeText, NumberStyles.Any, CultureInfo.InvariantCulture, out var s) ? s : 10m,
            TakeProfitUsd = decimal.TryParse(TakeProfitText, NumberStyles.Any, CultureInfo.InvariantCulture, out var tp) ? tp : 5m,
            StopLossUsd = decimal.TryParse(StopLossText, NumberStyles.Any, CultureInfo.InvariantCulture, out var sl) ? sl : 3m,
            MaxConcurrentContracts = int.TryParse(MaxContractsText, out var mc) ? mc : 3,
            DurationSeconds = CalculateDurationSeconds(),
            DurationApiValue = apiValue,
            DurationApiUnit = apiUnit,
            ConfidenceThreshold = ConfidenceThreshold,
            EnableTrailingStop = IsTrendMode ? false : EnableTrailingStop,
            RecoverMode = RecoverMode,
            MartingaleFactor = _recoverVm?.Factor ?? 2.0m,
            MartingaleMaxLevel = _recoverVm?.MaxLevel ?? 3,
            EnabledIndicators = indicators,
            Barrier = GetSelectedBarrier(),
            CallContractType = _activeMarketTab?.ContractPanel.EffectiveCallContractType ?? "VANILLALONGCALL",
            PutContractType = _activeMarketTab?.ContractPanel.EffectivePutContractType ?? "VANILLALONGPUT",
            StrategyMode = StrategyMode,
            SampleSize = int.TryParse(SampleSizeText, out var ss) && ss >= 1 ? ss : 5,
            DeficitMaxStake = decimal.TryParse(DeficitMaxStakeText, NumberStyles.Any, CultureInfo.InvariantCulture, out var dms) ? dms : 50m,
            DeficitRecoveryTrades = int.TryParse(DeficitRecoveryTradesText, out var drt) ? Math.Max(1, drt) : 1,
            TickScalperCooldown = TickScalperCooldown,
            TickScalperThreshold = TickScalperThreshold,
            TickScalperMinAgreement = TickScalperMinAgreement,
            TickScalperFlatFilter = TickScalperFlatFilter,
            CandleDynamicsCooldown = CandleDynamicsCooldown,
            CandleDynamicsThreshold = CandleDynamicsThreshold,
            CandleDynamicsMinStreak = CandleDynamicsMinStreak
        };
    }

    private int CalculateDurationSeconds()
    {
        if (!int.TryParse(DurationText, out var val)) val = 5;
        return DurationUnit switch
        {
            "Ticks" => Math.Max(1, val * 2),
            "Seconds" => val,
            "Hours" => val * 3600,
            "Days" => val * 86400,
            _ => val * 60
        };
    }

    private (int value, string unit) GetDurationApi()
    {
        if (!int.TryParse(DurationText, out var val)) val = 5;
        return DurationUnit switch
        {
            "Ticks" => (val, "t"),
            "Seconds" => (val, "s"),
            "Hours" => (val, "h"),
            "Days" => (val, "d"),
            _ => (val, "m")
        };
    }

    private void UpdateChartGranularity()
    {
        if (_restoringState || _activeMarketTab == null) return;
        var seconds = CalculateDurationSeconds();
        _ = _activeMarketTab.SetCandleGranularityAsync(seconds);
    }

    private void SyncDurationToMarketTab()
    {
        if (_restoringState || _activeMarketTab == null) return;

        if (DurationUnit == "Ticks")
        {
            if (int.TryParse(DurationText, out var n) && n >= 1)
                _activeMarketTab.EnableTickCandles(n);
        }
        else if (IsCandleDynamicsMode)
        {
            _activeMarketTab.EnableTickCandles(10);
        }
        else
        {
            _activeMarketTab.DisableTickCandles();
        }
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

    public void RefreshProposals()
    {
        _executor?.RefreshProposals();
    }

    public void Dispose()
    {
        SaveState();
        Stop();
        if (_activeMarketTab != null)
            _activeMarketTab.ContractPanel.PropertyChanged -= OnContractPanelSync;
        _engine.SignalGenerated -= OnSignalGenerated;
        _tickScalperEngine.SignalGenerated -= OnTickScalperSignal;
        _executor?.Dispose();
    }
}
