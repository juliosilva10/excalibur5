using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Excalibur5.Models;
using Excalibur5.Services;

namespace Excalibur5.ViewModels;

public partial class ContractPanelViewModel : ObservableObject, IDisposable
{
    private const string Src = "ContractPanel";

    private string ContractTypeCall => AllowEquals && SelectedStrategy.Category == ContractCategory.RiseFall
        ? "CALLE" : SelectedStrategy.CallContractType;
    private string ContractTypePut => AllowEquals && SelectedStrategy.Category == ContractCategory.RiseFall
        ? "PUTE" : SelectedStrategy.PutContractType;

    public string EffectiveCallContractType => ContractTypeCall;
    public string EffectivePutContractType => ContractTypePut;

    private readonly IContractService _contractService;
    private readonly int _pipSize;
    private readonly decimal _barrierInnerBase;
    private readonly decimal _barrierOuterBase;
    private CancellationTokenSource? _proposalCts;
    private bool _active;
    private bool _restoringState;
    private string _callProposalId = string.Empty;
    private string _putProposalId = string.Empty;

    // Recovery state
    private RecoverViewModel? _recoverVm;
    private int _martingaleLevel;
    private decimal _baseStake;
    private long _lastBoughtContractId;
    private decimal _deficit;
    private readonly decimal[] _payoutRatios = new decimal[5];
    private int _payoutIndex;
    private int _payoutCount;

    [ObservableProperty] private string _recoverMode = string.Empty;
    public List<string> RecoverModes { get; } = ["", "Martingale", "Deficit Recovery"];

    // Contract type strategy
    public List<IContractTypeStrategy> AvailableStrategies { get; } =
        [new VanillaContractStrategy(), new RiseFallContractStrategy()];

    [ObservableProperty] private IContractTypeStrategy _selectedStrategy = new VanillaContractStrategy();
    [ObservableProperty] private bool _allowEquals = true;

    public string CallButtonLabel => SelectedStrategy.CallButtonLabel;
    public string PutButtonLabel => SelectedStrategy.PutButtonLabel;
    public bool ShowBarrier => SelectedStrategy.RequiresBarrier;
    public bool ShowPayoutPerPoint => SelectedStrategy.Category == ContractCategory.Vanillas;
    public bool ShowExpiry => !UseDuration || SelectedStrategy.Category == ContractCategory.Vanillas;
    public bool ShowEquals => SelectedStrategy.Category == ContractCategory.RiseFall;

    [ObservableProperty] private string _symbol = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private DurationUnitType _durationUnit = DurationUnitType.Minutes;
    [ObservableProperty] private string _durationText = "5";
    [ObservableProperty] private string _durationRange = "1 - 1440 minutos";
    [ObservableProperty] private string _expiryDisplay = string.Empty;
    [ObservableProperty] private decimal _strikePrice;
    [ObservableProperty] private string _stakeText = "10";
    [ObservableProperty] private decimal _minStake;
    [ObservableProperty] private decimal _maxStake;
    [ObservableProperty] private decimal _payoutPerPoint;
    [ObservableProperty] private decimal _askPrice;
    [ObservableProperty] private decimal _payout;
    [ObservableProperty] private string _selectedContractType = "VANILLALONGCALL";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _contractsLoaded;
    [ObservableProperty] private string _stakeRange = string.Empty;
    [ObservableProperty] private bool _isBuying;
    [ObservableProperty] private string _lastBuyResult = string.Empty;

    // CALL/PUT separate payout data
    [ObservableProperty] private decimal _callAskPrice;
    [ObservableProperty] private decimal _putAskPrice;
    [ObservableProperty] private decimal _callPayout;
    [ObservableProperty] private decimal _putPayout;
    [ObservableProperty] private decimal _callPayoutPerPoint;
    [ObservableProperty] private decimal _putPayoutPerPoint;

    // End Time mode
    [ObservableProperty] private bool _useDuration = true;
    [ObservableProperty] private DateTime _selectedEndDate = DateTime.UtcNow.Date.AddDays(1);
    [ObservableProperty] private string _endTimeText = "12:00";
    [ObservableProperty] private decimal _selectedBarrier;
    [ObservableProperty] private string _selectedBarrierDisplay = string.Empty;

    public ObservableCollection<decimal> AvailableBarriers { get; } = new();

    public ObservableCollection<string> AvailableBarrierDisplays { get; } = new();
    private readonly List<decimal> _barrierOffsets = new();
    private readonly List<decimal> _apiBarriers = new();
    private bool _barriersAreRelative; // true when _apiBarriers contains relative offsets (duration mode)
    private decimal _spotForBarriers;
    private bool _barrierLocked;
    private bool _proposalsSuspended;

    public void LockBarrier() => _barrierLocked = true;
    public void UnlockBarrier() => _barrierLocked = false;

    public async void SuspendProposals()
    {
        _proposalsSuspended = true;
        _proposalCts?.Cancel();
        await _contractService.UnsubscribeAllProposalsAsync();
    }

    public void ResumeProposals()
    {
        _proposalsSuspended = false;
        RequestProposalDebounced();
    }

    public bool UseEndTime => !UseDuration;
    public string StrikePriceDisplay => StrikePrice.ToString("F2", CultureInfo.InvariantCulture);

    public string AbsoluteStrikeDisplay
    {
        get
        {
            if (_spotForBarriers <= 0)
                return string.Empty;
            var format = $"F{_pipSize}";
            if (!UseDuration)
                return (SelectedBarrier > 0 ? SelectedBarrier : _spotForBarriers).ToString(format, CultureInfo.InvariantCulture);
            if (SelectedBarrier != 0)
                return (_spotForBarriers + SelectedBarrier).ToString(format, CultureInfo.InvariantCulture);
            return _spotForBarriers.ToString(format, CultureInfo.InvariantCulture);
        }
    }

    public bool IsCallSelected => SelectedContractType == ContractTypeCall;
    public bool IsPutSelected => SelectedContractType == ContractTypePut;

    public DateTime MinEndDate => new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
    public DateTime MaxEndDate => DateTime.UtcNow.Date.AddYears(1);

    public ContractPanelViewModel(IContractService contractService, int pipSize = 2, decimal barrierInnerBase = 0.45m, decimal barrierOuterBase = 0.86m)
    {
        _contractService = contractService;
        _pipSize = pipSize;
        _barrierInnerBase = barrierInnerBase;
        _barrierOuterBase = barrierOuterBase;
        _contractService.ProposalUpdated += OnProposalUpdated;
        _contractService.OpenContractUpdated += OnMartingaleContractUpdated;
        OpenPositions = new OpenPositionsViewModel(contractService);
        UpdateDurationRange();
    }

    public void SetRecoverViewModel(RecoverViewModel recoverVm)
    {
        _recoverVm = recoverVm;
    }

    partial void OnRecoverModeChanged(string value)
    {
        if (value == "Martingale" || value == "Deficit Recovery")
        {
            _baseStake = GetStakeValue();
            if (_baseStake <= 0)
                _baseStake = decimal.TryParse(StakeText, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 10m;
            _martingaleLevel = 0;
            _deficit = 0;
            _payoutIndex = 0;
            _payoutCount = 0;
        }
        else
        {
            if (_martingaleLevel > 0 && _baseStake > 0)
                StakeText = _baseStake.ToString("F2", CultureInfo.InvariantCulture);
            if (_deficit > 0 && _baseStake > 0)
                StakeText = _baseStake.ToString("F2", CultureInfo.InvariantCulture);
            _martingaleLevel = 0;
            _deficit = 0;
        }
    }

    private void OnMartingaleContractUpdated(object? sender, OpenContractUpdate update)
    {
        if (_recoverVm == null) return;
        if (RecoverMode != "Martingale" && RecoverMode != "Deficit Recovery") return;
        if (update.ContractId != _lastBoughtContractId) return;
        if (!update.IsExpired && !update.IsSold && update.Status is not ("sold" or "won" or "lost")) return;

        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            bool isLoss = update.Profit < 0;

            if (RecoverMode == "Martingale")
            {
                if (isLoss && _martingaleLevel < _recoverVm.MaxLevel)
                {
                    _martingaleLevel++;
                    var newStake = _recoverVm.CalculateStake(_martingaleLevel);
                    StakeText = newStake.ToString("F2", CultureInfo.InvariantCulture);
                }
                else
                {
                    _martingaleLevel = 0;
                    StakeText = _baseStake.ToString("F2", CultureInfo.InvariantCulture);
                }
            }
            else if (RecoverMode == "Deficit Recovery")
            {
                if (isLoss)
                {
                    _deficit += Math.Abs(update.Profit);
                }
                else
                {
                    _deficit = Math.Max(0, _deficit - update.Profit);
                    if (_baseStake > 0)
                    {
                        var ratio = update.Profit / _baseStake;
                        _payoutRatios[_payoutIndex] = ratio;
                        _payoutIndex = (_payoutIndex + 1) % 5;
                        if (_payoutCount < 5) _payoutCount++;
                    }
                }

                if (_deficit <= 0)
                {
                    StakeText = _baseStake.ToString("F2", CultureInfo.InvariantCulture);
                }
                else
                {
                    var avgRatio = GetAveragePayoutRatio();
                    var recoveryTrades = _recoverVm.DeficitRecoveryTrades;
                    var needed = _deficit / (avgRatio * recoveryTrades);
                    var stake = Math.Max(needed, _baseStake);
                    stake = Math.Min(stake, _recoverVm.DeficitMaxStake);
                    StakeText = Math.Round(stake, 2).ToString("F2", CultureInfo.InvariantCulture);
                }
            }

            _lastBoughtContractId = 0;
        });
    }

    private decimal GetAveragePayoutRatio()
    {
        if (_payoutCount == 0) return 0.5m;
        decimal sum = 0;
        for (int i = 0; i < _payoutCount; i++)
            sum += _payoutRatios[i];
        return sum / _payoutCount;
    }

    public event EventHandler<ManualTradeOpened>? ManualTradeOpened;

    public OpenPositionsViewModel OpenPositions { get; }

    partial void OnUseDurationChanged(bool value)
    {
        OnPropertyChanged(nameof(UseEndTime));
        OnPropertyChanged(nameof(ShowExpiry));
        OnPropertyChanged(nameof(StrikePriceDisplay));
        UpdateExpiryFromEndTime();
        if (_restoringState) return;
        if (!value)
        {
            if (SelectedStrategy.RequiresBarrier)
                RefreshBarriersForEndTime();
        }
        else
        {
            if (SelectedStrategy.RequiresBarrier)
                GenerateFallbackBarriers();
        }
        RequestProposalDebounced();
    }

    partial void OnSelectedEndDateChanged(DateTime value)
    {
        UpdateExpiryFromEndTime();
        if (!UseDuration)
        {
            RefreshBarriersForEndTime();
            RequestProposalDebounced();
        }
    }

    partial void OnEndTimeTextChanged(string value)
    {
        UpdateExpiryFromEndTime();
        if (!UseDuration)
        {
            RefreshBarriersForEndTime();
            RequestProposalDebounced();
        }
    }

    partial void OnSelectedBarrierChanged(decimal value)
    {
        OnPropertyChanged(nameof(StrikePriceDisplay));
        OnPropertyChanged(nameof(AbsoluteStrikeDisplay));
        RequestProposalDebounced();
    }

    partial void OnSelectedBarrierDisplayChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;

        if (_pendingBarrierRestore != null)
        {
            if (value == _pendingBarrierRestore && _barriersFromApi)
                _pendingBarrierRestore = null;
        }

        var idx = -1;
        for (int i = 0; i < AvailableBarrierDisplays.Count; i++)
        {
            if (AvailableBarrierDisplays[i] == value)
            {
                idx = i;
                break;
            }
        }

        if (idx >= 0)
        {
            if (!UseDuration)
            {
                // End Time mode: store absolute value directly
                if (idx < AvailableBarriers.Count)
                    SelectedBarrier = AvailableBarriers[idx];
            }
            else
            {
                // Duration / End Time ≤ 24h: store relative offset
                if (idx < _barrierOffsets.Count)
                    SelectedBarrier = _barrierOffsets[idx];
            }
        }
    }

    partial void OnStrikePriceChanged(decimal value)
    {
        OnPropertyChanged(nameof(StrikePriceDisplay));
    }

    private void UpdateExpiryFromEndTime()
    {
        if (UseDuration) return;
        var unix = GetDateExpiryUnix();
        if (unix.HasValue)
        {
            var expiry = DateTimeOffset.FromUnixTimeSeconds(unix.Value);
            ExpiryDisplay = expiry.ToString("dd MMM yyyy, HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " GMT +0";
        }
        else
        {
            ExpiryDisplay = string.Empty;
        }
    }

    private void OnProposalUpdated(object? sender, ProposalResponse proposal)
    {
        if (!_active) return;

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (proposal.ContractType == ContractTypeCall)
            {
                _callProposalId = proposal.ProposalId;
                CallAskPrice = proposal.AskPrice;
                CallPayout = proposal.Payout;
                CallPayoutPerPoint = proposal.PayoutPerPoint;
            }
            else if (proposal.ContractType == ContractTypePut)
            {
                _putProposalId = proposal.ProposalId;
                PutAskPrice = proposal.AskPrice;
                PutPayout = proposal.Payout;
                PutPayoutPerPoint = proposal.PayoutPerPoint;
            }

            if (proposal.Spot > 0)
            {
                _spotForBarriers = proposal.Spot;
                StrikePrice = proposal.Spot;
                OnPropertyChanged(nameof(AbsoluteStrikeDisplay));
            }
            if (proposal.DateExpiry > 0)
            {
                var expiry = DateTimeOffset.FromUnixTimeSeconds(proposal.DateExpiry);
                ExpiryDisplay = expiry.ToString("dd MMM yyyy, HH:mm:ss", CultureInfo.InvariantCulture) + " GMT +0";
            }

            if (proposal.BarrierChoices.Count > 0 && !_barriersFromApi)
                UpdateBarriersFromApi(proposal.BarrierChoices);
        });
    }

    partial void OnDurationUnitChanged(DurationUnitType value)
    {
        UpdateDurationRange();
        if (!_restoringState && UseDuration && ContractsLoaded && SelectedStrategy.RequiresBarrier)
            GenerateFallbackBarriers();
        RequestProposalDebounced();
    }

    partial void OnDurationTextChanged(string value)
    {
        if (int.TryParse(value, out var val))
        {
            var max = DurationUnit switch
            {
                DurationUnitType.Ticks => 10,
                DurationUnitType.Seconds => 86400,
                DurationUnitType.Minutes => 1440,
                DurationUnitType.Hours => 24,
                DurationUnitType.Days => 365,
                _ => 1440
            };
            var min = DurationUnit == DurationUnitType.Seconds ? 15 : 1;
            if (val > max) { DurationText = max.ToString(); return; }
            if (val < min && value.Length > 0 && value != "0") { DurationText = min.ToString(); return; }
        }
        if (!_restoringState && UseDuration && ContractsLoaded && SelectedStrategy.RequiresBarrier)
            GenerateFallbackBarriers();
        RequestProposalDebounced();
    }

    partial void OnStakeTextChanged(string value)
    {
        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
        {
            if (MaxStake > 0 && val > MaxStake) { StakeText = MaxStake.ToString("F2", CultureInfo.InvariantCulture); return; }
        }
        RequestProposalDebounced();
    }

    partial void OnSelectedContractTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsCallSelected));
        OnPropertyChanged(nameof(IsPutSelected));
    }

    partial void OnAllowEqualsChanged(bool value)
    {
        RequestProposalDebounced();
    }

    partial void OnSelectedStrategyChanged(IContractTypeStrategy value)
    {
        OnPropertyChanged(nameof(CallButtonLabel));
        OnPropertyChanged(nameof(PutButtonLabel));
        OnPropertyChanged(nameof(ShowBarrier));
        OnPropertyChanged(nameof(ShowPayoutPerPoint));
        OnPropertyChanged(nameof(ShowExpiry));
        OnPropertyChanged(nameof(ShowEquals));

        SelectedContractType = ContractTypeCall;

        var units = value.AvailableDurationUnits;
        if (!units.Contains(DurationUnit))
            DurationUnit = units[0];

        UpdateDurationRange();
        RequestProposalDebounced();
    }

    [RelayCommand]
    private async Task SelectCallAsync()
    {
        if (IsBuying) return;
        SelectedContractType = ContractTypeCall;
        await BuyAsync(ContractTypeCall);
    }

    [RelayCommand]
    private async Task SelectPutAsync()
    {
        if (IsBuying) return;
        SelectedContractType = ContractTypePut;
        await BuyAsync(ContractTypePut);
    }

    private async Task BuyAsync(string contractType)
    {
        var proposalId = contractType == ContractTypeCall ? _callProposalId : _putProposalId;
        var price = contractType == ContractTypeCall ? CallAskPrice : PutAskPrice;

        AppLogger.Info(Src, $"BuyAsync {contractType}: proposalId={proposalId}, price={price}");

        if (string.IsNullOrEmpty(proposalId) || price <= 0)
        {
            AppLogger.Warn(Src, $"BuyAsync {contractType} aborted: proposalId empty={string.IsNullOrEmpty(proposalId)}, price={price}");
            return;
        }

        if (contractType == ContractTypeCall)
            _callProposalId = string.Empty;
        else
            _putProposalId = string.Empty;

        IsBuying = true;
        LastBuyResult = string.Empty;

        try
        {
            var result = await _contractService.BuyContractAsync(proposalId, price);
            if (result.Success)
            {
                LastBuyResult = $"Comprado! ID: {result.ContractId}";
                _lastBoughtContractId = result.ContractId;
                var expiry = GetDateExpiryForPosition();
                var durationSec = GetDurationInSeconds();
                _ = OpenPositions.AddPositionAsync(result, Symbol, DisplayName, contractType, expiry, durationSec);
                ManualTradeOpened?.Invoke(this, new ManualTradeOpened(result, contractType));
            }
            else
                LastBuyResult = $"Erro: {result.Error}";

            await ResubscribeAfterBuyAsync(contractType);
        }
        catch (Exception ex)
        {
            LastBuyResult = $"Erro: {ex.Message}";
            AppLogger.Error(Src, "Buy failed", ex);
        }
        finally
        {
            IsBuying = false;
        }
    }

    private async Task ResubscribeAfterBuyAsync(string contractType)
    {
        if (!_active || !ContractsLoaded || !ValidateInputs()) return;

        try
        {
            var stake = GetStakeValue();
            int? duration = null;
            string? unitStr = null;
            long? dateExpiry = null;
            string? barrier = SelectedStrategy.RequiresBarrier ? GetBarrierForApi() : null;

            if (UseDuration)
            {
                duration = GetDurationValue();
                unitStr = GetDurationUnitString();
            }
            else
            {
                dateExpiry = GetDateExpiryUnix();
                if (dateExpiry == null) return;
            }

            _contractService.ClearSubscription(contractType);

            var response = await _contractService.SubscribeProposalAsync(
                Symbol, contractType, stake, duration, unitStr, dateExpiry, barrier);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (contractType == ContractTypeCall)
                {
                    _callProposalId = response.ProposalId;
                    CallAskPrice = response.AskPrice;
                    CallPayout = response.Payout;
                    CallPayoutPerPoint = response.PayoutPerPoint;
                }
                else
                {
                    _putProposalId = response.ProposalId;
                    PutAskPrice = response.AskPrice;
                    PutPayout = response.Payout;
                    PutPayoutPerPoint = response.PayoutPerPoint;
                }
            });
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Resubscribe after buy failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SetDurationUnit(string unit)
    {
        DurationUnit = unit switch
        {
            "Ticks" => DurationUnitType.Ticks,
            "Seconds" => DurationUnitType.Seconds,
            "Hours" => DurationUnitType.Hours,
            "Days" => DurationUnitType.Days,
            _ => DurationUnitType.Minutes
        };
    }

    private void UpdateDurationRange()
    {
        DurationRange = DurationUnit switch
        {
            DurationUnitType.Ticks => "Intervalo: 1 - 10 ticks",
            DurationUnitType.Seconds => "Intervalo: 15 - 86400 segundos",
            DurationUnitType.Minutes => "Intervalo: 1 - 1440 minutos",
            DurationUnitType.Hours => "Intervalo: 1 - 24 horas",
            DurationUnitType.Days => "Intervalo: 1 - 365 dias",
            _ => ""
        };
    }

    private void UpdateBarrierDisplays()
    {
        AvailableBarriers.Clear();
        AvailableBarrierDisplays.Clear();
        _barrierOffsets.Clear();

        if (_spotForBarriers <= 0) return;

        if (_apiBarriers.Count == 0)
        {
            // No barriers from API — show only spot as default
            AvailableBarriers.Add(_spotForBarriers);
            _barrierOffsets.Add(0m);
            AvailableBarrierDisplays.Add($"+{0m.ToString($"F{_pipSize}", CultureInfo.InvariantCulture)}");
            SelectedBarrierDisplay = AvailableBarrierDisplays[0];
            return;
        }

        var showAbsolute = !UseDuration;

        foreach (var b in _apiBarriers)
        {
            decimal offset;
            if (_barriersAreRelative)
            {
                offset = b;
            }
            else
            {
                offset = b - _spotForBarriers;
            }

            if (showAbsolute)
            {
                // End Time > 24h: compute fixed absolute value and store it directly
                var absVal = Math.Round((_spotForBarriers + offset) / 10m) * 10m;
                AvailableBarriers.Add(absVal);
                _barrierOffsets.Add(offset);
                AvailableBarrierDisplays.Add(absVal.ToString($"F{_pipSize}", CultureInfo.InvariantCulture));
            }
            else
            {
                AvailableBarriers.Add(_spotForBarriers + offset);
                _barrierOffsets.Add(offset);
                var sign = offset >= 0 ? "+" : "";
                var fmt = offset.ToString($"F{_pipSize}", CultureInfo.InvariantCulture);
                AvailableBarrierDisplays.Add($"{sign}{fmt}");
            }
        }

        // Restore persisted barrier if available
        if (_pendingBarrierRestore != null)
        {
            if (AvailableBarrierDisplays.Contains(_pendingBarrierRestore))
            {
                SelectedBarrierDisplay = _pendingBarrierRestore;
                if (_barriersFromApi) _pendingBarrierRestore = null;
                return;
            }

            // Try closest match by parsing the numeric value
            if (decimal.TryParse(_pendingBarrierRestore.TrimStart('+'), NumberStyles.Any, CultureInfo.InvariantCulture, out var targetVal))
            {
                string? closest = null;
                decimal closestDiff = decimal.MaxValue;
                foreach (var display in AvailableBarrierDisplays)
                {
                    if (decimal.TryParse(display.TrimStart('+'), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                    {
                        var diff = Math.Abs(val - targetVal);
                        if (diff < closestDiff)
                        {
                            closestDiff = diff;
                            closest = display;
                        }
                    }
                }
                if (closest != null)
                {
                    SelectedBarrierDisplay = closest;
                    if (_barriersFromApi) _pendingBarrierRestore = null;
                    return;
                }
            }
        }

        // Default select the entry closest to spot
        if (showAbsolute)
        {
            var closest = AvailableBarriers
                .Select((v, i) => (Diff: Math.Abs(v - _spotForBarriers), Index: i))
                .OrderBy(x => x.Diff)
                .First().Index;
            SelectedBarrierDisplay = AvailableBarrierDisplays[closest];
        }
        else
        {
            var zeroFormat = $"+{0m.ToString($"F{_pipSize}", CultureInfo.InvariantCulture)}";
            var spotEntry = AvailableBarrierDisplays.FirstOrDefault(d => d == zeroFormat);
            if (spotEntry == null)
            {
                var minIdx = _barrierOffsets
                    .Select((o, i) => (Offset: Math.Abs(o), Index: i))
                    .OrderBy(x => x.Offset)
                    .First().Index;
                spotEntry = AvailableBarrierDisplays[minIdx];
            }
            SelectedBarrierDisplay = spotEntry;
        }
    }

    private CancellationTokenSource? _barrierRefreshCts;

    private void RefreshBarriersForEndTime()
    {
        if (string.IsNullOrEmpty(Symbol) || _spotForBarriers <= 0) return;

        var dateExpiry = GetDateExpiryUnix();
        if (dateExpiry == null)
        {
            GenerateFallbackBarriers();
            return;
        }

        // End Time mode uses relative barriers from the proposal response.
        // Generate fallback barriers initially; they will be replaced by
        // barrier_choices from the first proposal response.
        GenerateFallbackBarriers();
    }

    private bool _barriersFromApi;

    private void UpdateBarriersFromApi(List<string> barrierChoices)
    {
        if (barrierChoices.Count == 0) return;

        var newBarriers = new List<decimal>();
        bool areRelative = false;
        foreach (var b in barrierChoices)
        {
            if (b.StartsWith("+") || b.StartsWith("-"))
                areRelative = true;
            if (decimal.TryParse(b.TrimStart('+'), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                newBarriers.Add(val);
        }

        if (newBarriers.Count == 0) return;

        // If barriers are absolute, convert to offsets from spot
        if (!areRelative && _spotForBarriers > 0)
        {
            for (int i = 0; i < newBarriers.Count; i++)
                newBarriers[i] = newBarriers[i] - _spotForBarriers;
            areRelative = true;
        }

        // Check if barriers are the same as current
        if (_barriersFromApi && _apiBarriers.Count == newBarriers.Count)
        {
            bool same = true;
            for (int i = 0; i < newBarriers.Count; i++)
            {
                if (_apiBarriers[i] != newBarriers[i]) { same = false; break; }
            }
            if (same) return;
        }

        // When barrier is locked (bot running), update internal list but preserve selection
        if (_barrierLocked)
        {
            _apiBarriers.Clear();
            foreach (var b in newBarriers)
                _apiBarriers.Add(b);
            _apiBarriers.Sort();
            _barriersAreRelative = true;
            _barriersFromApi = true;
            return;
        }

        var currentSelection = SelectedBarrier;
        var hadPendingRestore = _pendingBarrierRestore != null;

        _apiBarriers.Clear();
        foreach (var b in newBarriers)
            _apiBarriers.Add(b);

        _apiBarriers.Sort();
        _barriersAreRelative = true;
        _barriersFromApi = true;

        UpdateBarrierDisplays();

        // Try to restore previous selection (skip if pending barrier was just applied)
        if (!hadPendingRestore && _pendingBarrierRestore == null && AvailableBarrierDisplays.Count > 0)
        {
            var format = $"F{_pipSize}";
            string targetDisplay;
            if (!UseDuration)
            {
                var absVal = _spotForBarriers + currentSelection;
                targetDisplay = absVal.ToString(format, CultureInfo.InvariantCulture);
            }
            else
            {
                targetDisplay = (currentSelection >= 0 ? "+" : "") + currentSelection.ToString(format, CultureInfo.InvariantCulture);
            }
            if (AvailableBarrierDisplays.Contains(targetDisplay))
                SelectedBarrierDisplay = targetDisplay;
        }
    }

    private void GenerateFallbackBarriers()
    {
        _apiBarriers.Clear();

        if (_spotForBarriers <= 0) return;

        if (UseDuration)
        {
            // Duration mode: barriers scale with dur^0.513 — matches Deriv website behavior.
            // Formula: inner = round(inner_base × dur^0.513, step)
            //          outer = round(outer_base × dur^0.513, step)
            // step = 10^(1 - pipSize)  (rounds to 10-pip granularity)
            // Barriers: [-outer, -inner, 0, +inner, +outer]

            var durationMinutes = GetDurationInMinutes();
            if (durationMinutes <= 0) durationMinutes = 1;

            decimal scaleFactor = (decimal)Math.Pow(durationMinutes, 0.513);
            decimal step = (decimal)Math.Pow(10, 1 - _pipSize);

            decimal innerStep = Math.Round(_barrierInnerBase * scaleFactor / step, MidpointRounding.AwayFromZero) * step;
            decimal outerStep = Math.Round(_barrierOuterBase * scaleFactor / step, MidpointRounding.AwayFromZero) * step;

            // Ensure minimum of 1 step
            if (innerStep < step) innerStep = step;
            if (outerStep <= innerStep) outerStep = innerStep + step;

            _apiBarriers.Add(-outerStep);
            _apiBarriers.Add(-innerStep);
            _apiBarriers.Add(0m);
            _apiBarriers.Add(innerStep);
            _apiBarriers.Add(outerStep);

            _apiBarriers.Sort();
            _barriersAreRelative = true;
        }
        else
        {
            // End Time mode: generate barriers as absolute round numbers
            decimal step = 10m;
            var baseBarrier = Math.Floor(_spotForBarriers / step) * step;

            // Generate 10 barriers around spot (matches Deriv site pattern)
            for (int i = -3; i <= 6; i++)
            {
                var absBarrier = baseBarrier + (i * step);
                if (absBarrier > 0)
                    _apiBarriers.Add(absBarrier - _spotForBarriers);
            }

            _apiBarriers.Sort();
            _barriersAreRelative = true;
        }

        UpdateBarrierDisplays();
    }

    public async Task LoadContractsAsync(string symbol, string? displayName = null)
    {
        Symbol = symbol;
        DisplayName = displayName ?? symbol;
        _active = true;
        IsLoading = true;

        try
        {
            var response = await _contractService.GetContractsForAsync(symbol);
            if (response.Available.Count == 0) return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StrikePrice = response.Spot;

                var callContract = response.Available.FirstOrDefault(c => c.ContractType == ContractTypeCall);
                if (callContract != null)
                {
                    MinStake = callContract.MinStake;
                    MaxStake = callContract.MaxStake;
                    StakeRange = $"Min: {MinStake:F2} | Max: {MaxStake:F2}";
                }

                AvailableBarriers.Clear();
                AvailableBarrierDisplays.Clear();
                _barrierOffsets.Clear();
                _apiBarriers.Clear();
                _spotForBarriers = response.Spot;

                // Store API barriers
                foreach (var b in response.AvailableBarriers)
                    _apiBarriers.Add(b);

                if (_apiBarriers.Count > 0)
                {
                    var maxAbs = _apiBarriers.Max(b => Math.Abs(b));
                    _barriersAreRelative = maxAbs < _spotForBarriers * 0.1m;
                    UpdateBarrierDisplays();
                }
                else
                {
                    GenerateFallbackBarriers();
                }

                ContractsLoaded = true;
            });

            RequestProposalDebounced();
        }
        catch (Exception ex)
        {
            AppLogger.Error(Src, $"LoadContracts failed for {symbol}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task DeactivateAsync()
    {
        _active = false;
        _proposalCts?.Cancel();
        _proposalCts = null;
        _barrierRefreshCts?.Cancel();
        _barrierRefreshCts = null;
        _barriersFromApi = false;

        try
        {
            await _contractService.UnsubscribeAllProposalsAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Deactivate unsubscribe error: {ex.Message}");
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _callProposalId = string.Empty;
            _putProposalId = string.Empty;
            CallAskPrice = 0;
            PutAskPrice = 0;
            CallPayout = 0;
            PutPayout = 0;
            CallPayoutPerPoint = 0;
            PutPayoutPerPoint = 0;
            AskPrice = 0;
            Payout = 0;
            PayoutPerPoint = 0;
            ExpiryDisplay = string.Empty;
            ContractsLoaded = false;
            AvailableBarriers.Clear();
            AvailableBarrierDisplays.Clear();
            _barrierOffsets.Clear();
            _apiBarriers.Clear();
            _spotForBarriers = 0;
            SelectedBarrierDisplay = string.Empty;
            SelectedBarrier = 0;
        });
    }

    private int GetDurationValue()
    {
        if (!int.TryParse(DurationText, out var val) || val < 1) return -1;
        var max = DurationUnit switch
        {
            DurationUnitType.Ticks => 10,
            DurationUnitType.Seconds => 86400,
            DurationUnitType.Minutes => 1440,
            DurationUnitType.Hours => 24,
            DurationUnitType.Days => 365,
            _ => 1440
        };
        var min = DurationUnit == DurationUnitType.Seconds ? 15 : 1;
        return val > max || val < min ? -1 : val;
    }

    private int GetDurationInMinutes()
    {
        if (!int.TryParse(DurationText, out var val) || val < 1) return 1;
        return DurationUnit switch
        {
            DurationUnitType.Ticks => Math.Max(1, val * 2 / 60),
            DurationUnitType.Seconds => Math.Max(1, val / 60),
            DurationUnitType.Minutes => val,
            DurationUnitType.Hours => val * 60,
            DurationUnitType.Days => val * 1440,
            _ => val
        };
    }

    private int GetDurationInSeconds()
    {
        if (!int.TryParse(DurationText, out var val) || val < 1) return 60;
        return DurationUnit switch
        {
            DurationUnitType.Ticks => Math.Max(2, val * 2),
            DurationUnitType.Seconds => val,
            DurationUnitType.Minutes => val * 60,
            DurationUnitType.Hours => val * 3600,
            DurationUnitType.Days => val * 86400,
            _ => val * 60
        };
    }

    private decimal GetStakeValue()
    {
        if (!decimal.TryParse(StakeText, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            return -1;
        if (MinStake > 0 && val < MinStake) return -1;
        if (MaxStake > 0 && val > MaxStake) return -1;
        return val;
    }

    private string GetDurationUnitString() => DurationUnit switch
    {
        DurationUnitType.Ticks => "t",
        DurationUnitType.Seconds => "s",
        DurationUnitType.Minutes => "m",
        DurationUnitType.Hours => "h",
        DurationUnitType.Days => "d",
        _ => "m"
    };

    private string? GetBarrierForApi()
    {
        if (!UseDuration)
        {
            // End Time mode: SelectedBarrier holds the absolute value directly
            if (SelectedBarrier <= 0) return null;
            return SelectedBarrier.ToString($"F{_pipSize}", CultureInfo.InvariantCulture);
        }

        // Duration mode uses relative offsets
        var offset = SelectedBarrier;
        var sign = offset >= 0 ? "+" : "";
        return $"{sign}{offset.ToString($"F{_pipSize}", CultureInfo.InvariantCulture)}";
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrEmpty(Symbol) || GetStakeValue() <= 0) return false;
        if (UseDuration) return GetDurationValue() > 0;
        return GetDateExpiryUnix() != null;
    }

    private async void RequestProposalDebounced()
    {
        if (!_active || !ContractsLoaded || _proposalsSuspended) return;

        _proposalCts?.Cancel();
        _proposalCts = new CancellationTokenSource();
        var ct = _proposalCts.Token;

        try
        {
            await Task.Delay(300, ct);
            if (!ValidateInputs()) return;

            await SubscribeAndUpdateProposalsAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Barriers available are"))
        {
            var barriers = ParseBarriersFromError(ex.Message);
            if (barriers.Count > 0)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    UpdateBarriersFromApi(barriers);
                });

                try
                {
                    await Task.Delay(100, ct);
                    await SubscribeAndUpdateProposalsAsync(ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception retryEx)
                {
                    AppLogger.Warn(Src, $"Proposal retry error: {retryEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Proposal request error: {ex.Message}");
        }
    }

    private async Task SubscribeAndUpdateProposalsAsync(CancellationToken ct)
    {
        var stake = GetStakeValue();
        int? duration = null;
        string? unitStr = null;
        long? dateExpiry = null;
        string? barrier = SelectedStrategy.RequiresBarrier ? GetBarrierForApi() : null;

        if (UseDuration)
        {
            duration = GetDurationValue();
            unitStr = GetDurationUnitString();
        }
        else
        {
            dateExpiry = GetDateExpiryUnix();
            if (dateExpiry == null) return;
        }

        var callResponse = await _contractService.SubscribeProposalAsync(
            Symbol, ContractTypeCall, stake, duration, unitStr, dateExpiry, barrier, ct: ct);

        var putResponse = await _contractService.SubscribeProposalAsync(
            Symbol, ContractTypePut, stake, duration, unitStr, dateExpiry, barrier, ct: ct);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _callProposalId = callResponse.ProposalId;
            CallAskPrice = callResponse.AskPrice;
            CallPayout = callResponse.Payout;
            CallPayoutPerPoint = callResponse.PayoutPerPoint;

            _putProposalId = putResponse.ProposalId;
            PutAskPrice = putResponse.AskPrice;
            PutPayout = putResponse.Payout;
            PutPayoutPerPoint = putResponse.PayoutPerPoint;

            if (callResponse.DateExpiry > 0)
            {
                var expiry = DateTimeOffset.FromUnixTimeSeconds(callResponse.DateExpiry);
                ExpiryDisplay = expiry.ToString("dd MMM yyyy, HH:mm:ss", CultureInfo.InvariantCulture) + " GMT +0";
            }

            if (callResponse.Spot > 0)
            {
                _spotForBarriers = callResponse.Spot;
                StrikePrice = callResponse.Spot;
                OnPropertyChanged(nameof(AbsoluteStrikeDisplay));
            }

            if (callResponse.BarrierChoices.Count > 0 && !_barriersFromApi)
                UpdateBarriersFromApi(callResponse.BarrierChoices);
            else
                _barriersFromApi = true;

            AppLogger.Info(Src, $"Proposals ready: CALL id={_callProposalId} ask={CallAskPrice} ppp={CallPayoutPerPoint}, PUT id={_putProposalId} ask={PutAskPrice} ppp={PutPayoutPerPoint}");
        });
    }

    private static List<string> ParseBarriersFromError(string message)
    {
        // Format: "Barriers available are +29.320, +15.430, +0.050, -15.280, -29.040"
        var result = new List<string>();
        var prefix = "Barriers available are ";
        if (!message.StartsWith(prefix)) return result;

        var csv = message.Substring(prefix.Length);
        foreach (var part in csv.Split(','))
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                result.Add(trimmed);
        }
        return result;
    }

    private long? GetDateExpiryUnix()
    {
        var dt = SelectedEndDate.Date.AddHours(23).AddMinutes(59).AddSeconds(59);
        var utcDt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        if (utcDt <= DateTime.UtcNow) return null;

        return new DateTimeOffset(utcDt).ToUnixTimeSeconds();
    }

    private long GetDateExpiryForPosition()
    {
        if (!UseDuration)
        {
            var unix = GetDateExpiryUnix();
            return unix ?? DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        }

        var minutes = GetDurationInMinutes();
        return DateTimeOffset.UtcNow.AddMinutes(minutes).ToUnixTimeSeconds();
    }

    private string? _pendingBarrierRestore;

    public void RestoreState(string? durationUnit, string? durationText, string? stakeText, bool? useDuration = null, string? selectedBarrier = null, string? selectedStrategy = null, bool? allowEquals = null, string? recoverMode = null)
    {
        AppLogger.Info(Src, $"RestoreState: unit={durationUnit}, duration={durationText}, stake={stakeText}, useDuration={useDuration}, barrier={selectedBarrier}, strategy={selectedStrategy}, allowEquals={allowEquals}, recoverMode={recoverMode}");
        _restoringState = true;
        if (!string.IsNullOrEmpty(selectedStrategy))
        {
            var match = AvailableStrategies.FirstOrDefault(s => s.DisplayName == selectedStrategy);
            if (match != null)
                SelectedStrategy = match;
        }
        if (allowEquals.HasValue)
            AllowEquals = allowEquals.Value;
        if (!string.IsNullOrEmpty(recoverMode))
            RecoverMode = recoverMode;
        if (!string.IsNullOrEmpty(selectedBarrier))
            _pendingBarrierRestore = selectedBarrier;
        if (useDuration.HasValue)
            UseDuration = useDuration.Value;
        if (!string.IsNullOrEmpty(durationUnit) && Enum.TryParse<DurationUnitType>(durationUnit, out var unit))
            DurationUnit = unit;
        if (!string.IsNullOrEmpty(durationText))
            DurationText = durationText;
        if (!string.IsNullOrEmpty(stakeText))
            StakeText = stakeText;
        _restoringState = false;
    }

    public void Dispose()
    {
        _proposalCts?.Cancel();
        _proposalCts?.Dispose();
        _barrierRefreshCts?.Cancel();
        _barrierRefreshCts?.Dispose();
        _contractService.ProposalUpdated -= OnProposalUpdated;
        _contractService.OpenContractUpdated -= OnMartingaleContractUpdated;
        OpenPositions.Dispose();
    }
}

public sealed record ManualTradeOpened(BuyResponse BuyResult, string ContractType);
