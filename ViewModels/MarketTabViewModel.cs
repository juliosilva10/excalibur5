using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Excalibur5.Models;
using Excalibur5.Services;

namespace Excalibur5.ViewModels;

public partial class MarketTabViewModel : ObservableObject, IDisposable
{
    private const int MaxTicks = 1000;
    private const int MaxRecentDisplay = 50;

    private readonly ITickStreamService _tickService;
    private readonly MarketInfo _market;
    private bool _isActive;

    public ContractPanelViewModel ContractPanel { get; }

    [ObservableProperty] private string        _currentQuote = string.Empty;
    [ObservableProperty] private TickDirection  _currentDirection = TickDirection.Flat;
    [ObservableProperty] private bool           _isSubscribed;
    [ObservableProperty] private bool           _isSelected;
    [ObservableProperty] private bool           _isChartReady;
    [ObservableProperty] private string         _tickVariation = string.Empty;
    [ObservableProperty] private ChartType      _chartType = ChartType.Line;
    [ObservableProperty] private bool           _isCandlesEnabled;

    private decimal _previousQuote;

    [RelayCommand]
    private void SetChartType(ChartType type) => ChartType = type;

    partial void OnChartTypeChanged(ChartType value)
    {
        if (value == ChartType.Candles && !_candlesLoaded && _isActive)
            _ = LoadCandlesAsync();
    }

    public async Task LoadCandlesAsync()
    {
        try
        {
            var candles = await _tickService.GetCandleHistoryAsync(Symbol, 60, 500);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CandleValues.Clear();
                CandleValues.AddRange(candles);
                _candlesLoaded = true;
                OnPropertyChanged(nameof(CandleValues));
            });
        }
        catch (Exception ex)
        {
            AppLogger.Warn("MarketTab", $"Candle history load failed for {Symbol}: {ex.Message}");
        }
    }

    public string Symbol      => _market.Symbol;
    public string DisplayName => _market.DisplayName;
    public string FullName    => _market.FullName;

    public ObservableCollection<TickData> RecentTicks { get; } = new();
    public ObservableCollection<decimal>  ChartValues { get; } = new();
    public List<long> ChartEpochs { get; } = new();
    public List<TickDirection> ChartDirections { get; } = new();
    public List<CandleData> CandleValues { get; } = new();
    private bool _candlesLoaded;
    private int _candleGranularity = 60;

    public event EventHandler? CandleUpdated;

    public MarketTabViewModel(MarketInfo market, ITickStreamService tickService, IContractService contractService)
    {
        _market      = market;
        _tickService = tickService;
        ContractPanel = new ContractPanelViewModel(contractService, _market.PipSize, _market.BarrierInnerBase, _market.BarrierOuterBase);
        ContractPanel.PropertyChanged += OnContractPanelPropertyChanged;
    }

    private void OnContractPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ContractPanelViewModel.DurationUnit) or nameof(ContractPanelViewModel.DurationText))
            UpdateCandlesEnabled();
    }

    private void UpdateCandlesEnabled()
    {
        IsCandlesEnabled = ContractPanel.DurationUnit != DurationUnitType.Minutes
            || (int.TryParse(ContractPanel.DurationText, out var val) && val >= 1);
    }

    private void OnTickReceived(object? sender, TickData tick)
    {
        if (tick.Symbol != Symbol || !_isActive) return;

        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            if (_previousQuote != 0)
            {
                var diff = tick.Quote - _previousQuote;
                var decimals = GetDecimalPlaces(tick.QuoteRaw);
                var formatted = diff.ToString($"F{decimals}", CultureInfo.InvariantCulture);
                TickVariation = diff >= 0 ? $"+{formatted}" : formatted;
            }
            _previousQuote = tick.Quote;

            CurrentQuote     = tick.QuoteRaw;
            CurrentDirection = tick.Direction;

            RecentTicks.Insert(0, tick);
            if (RecentTicks.Count > MaxRecentDisplay)
                RecentTicks.RemoveAt(RecentTicks.Count - 1);

            ChartValues.Add(tick.Quote);
            ChartEpochs.Add(tick.Epoch);
            ChartDirections.Add(tick.Direction);
            if (ChartValues.Count > MaxTicks)
            {
                ChartValues.RemoveAt(0);
                ChartEpochs.RemoveAt(0);
                ChartDirections.RemoveAt(0);
            }

            if (_candlesLoaded && CandleValues.Count > 0)
                UpdateCandleWithTick(tick);
        });
    }

    private static int GetDecimalPlaces(string quoteRaw)
    {
        var dotIndex = quoteRaw.IndexOf('.');
        return dotIndex < 0 ? 0 : quoteRaw.Length - dotIndex - 1;
    }

    private void UpdateCandleWithTick(TickData tick)
    {
        var lastCandle = CandleValues[^1];
        long candleStart = lastCandle.Epoch;
        long tickEpoch = tick.Epoch;

        if (tickEpoch < candleStart + _candleGranularity)
        {
            lastCandle.Close = tick.Quote;
            if (tick.Quote > lastCandle.High) lastCandle.High = tick.Quote;
            if (tick.Quote < lastCandle.Low) lastCandle.Low = tick.Quote;
        }
        else
        {
            long newEpoch = candleStart + _candleGranularity;
            while (newEpoch + _candleGranularity <= tickEpoch)
                newEpoch += _candleGranularity;

            CandleValues.Add(new CandleData
            {
                Epoch = newEpoch,
                Open = tick.Quote,
                High = tick.Quote,
                Low = tick.Quote,
                Close = tick.Quote
            });
        }

        CandleUpdated?.Invoke(this, EventArgs.Empty);
    }

    public async Task ActivateAsync()
    {
        if (_isActive) return;
        _isActive = true;
        _tickService.TickReceived += OnTickReceived;
        IsChartReady = false;

        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                RecentTicks.Clear();
                ChartValues.Clear();
                ChartEpochs.Clear();
                ChartDirections.Clear();
                CandleValues.Clear();
                _candlesLoaded = false;
            });

            // Load history — non-fatal if it fails (chart will be empty but contracts still load)
            try
            {
                var history = await _tickService.GetHistoryAsync(Symbol);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var tick in history)
                    {
                        ChartValues.Add(tick.Quote);
                        ChartEpochs.Add(tick.Epoch);
                        ChartDirections.Add(tick.Direction);
                    }

                    for (int i = history.Count - 1; i >= 0; i--)
                        RecentTicks.Add(history[i]);

                    if (history.Count > 0)
                    {
                        var last = history[^1];
                        CurrentQuote     = last.QuoteRaw;
                        CurrentDirection = last.Direction;
                        _previousQuote = last.Quote;

                        if (history.Count > 1)
                        {
                            var diff = history[^1].Quote - history[^2].Quote;
                            var decimals = GetDecimalPlaces(last.QuoteRaw);
                            var formatted = diff.ToString($"F{decimals}", CultureInfo.InvariantCulture);
                            TickVariation = diff >= 0 ? $"+{formatted}" : formatted;
                        }
                    }

                    IsChartReady = true;
                });
            }
            catch (Exception ex)
            {
                AppLogger.Warn("MarketTab", $"History load failed for {Symbol}: {ex.Message}");
                await Application.Current.Dispatcher.InvokeAsync(() => IsChartReady = true);
            }

            // Subscribe to tick stream — non-fatal if it fails
            try
            {
                await _tickService.SubscribeAsync(Symbol);
                IsSubscribed = true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("MarketTab", $"Tick subscribe failed for {Symbol} (chart may use history only): {ex.Message}");
            }

            // Always load contracts even if tick/history failed
            await ContractPanel.LoadContractsAsync(Symbol, DisplayName);
            UpdateCandlesEnabled();
        }
        catch (Exception ex)
        {
            AppLogger.Error("MarketTab", $"Failed to activate {Symbol}", ex);
        }
    }

    public async Task DeactivateAsync()
    {
        if (!_isActive) return;
        _isActive = false;
        _tickService.TickReceived -= OnTickReceived;

        try
        {
            await ContractPanel.DeactivateAsync();
            await _tickService.UnsubscribeAsync(Symbol);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("MarketTab", $"Unsubscribe error {Symbol}: {ex.Message}");
        }

        IsSubscribed = false;
    }

    public async Task ForceDeactivateAsync()
    {
        _isActive = false;
        _tickService.TickReceived -= OnTickReceived;
        IsSubscribed = false;
        _tickService.ClearSubscription(Symbol);
        await ContractPanel.DeactivateAsync();
    }

    public void Dispose()
    {
        _tickService.TickReceived -= OnTickReceived;
        ContractPanel.PropertyChanged -= OnContractPanelPropertyChanged;
        ContractPanel.Dispose();
    }
}
