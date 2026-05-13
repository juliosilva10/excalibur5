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
    private const string ContractTypeCall = "VANILLALONGCALL";
    private const string ContractTypePut = "VANILLALONGPUT";

    private readonly IContractService _contractService;
    private readonly int _pipSize;
    private readonly decimal _barrierInnerBase;
    private readonly decimal _barrierOuterBase;
    private CancellationTokenSource? _proposalCts;
    private bool _active;
    private string _callProposalId = string.Empty;
    private string _putProposalId = string.Empty;

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
    [ObservableProperty] private string _selectedContractType = ContractTypeCall;
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

    public bool UseEndTime => !UseDuration;
    public string StrikePriceDisplay => StrikePrice.ToString("F2", CultureInfo.InvariantCulture);

    public string AbsoluteStrikeDisplay
    {
        get
        {
            if (_spotForBarriers <= 0)
                return string.Empty;
            var format = $"F{_pipSize}";
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
        OpenPositions = new OpenPositionsViewModel(contractService);
        UpdateDurationRange();
    }

    public OpenPositionsViewModel OpenPositions { get; }

    partial void OnUseDurationChanged(bool value)
    {
        OnPropertyChanged(nameof(UseEndTime));
        OnPropertyChanged(nameof(StrikePriceDisplay));
        UpdateExpiryFromEndTime();
        if (!value)
        {
            RefreshBarriersForEndTime();
        }
        else
        {
            // Switching to Duration mode — regenerate relative offset barriers
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
            if (!UseDuration && IsEndTimeMoreThan24h())
            {
                // End Time > 24h: store absolute value directly
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
        if (UseDuration && ContractsLoaded)
            GenerateFallbackBarriers();
        RequestProposalDebounced();
    }

    partial void OnDurationTextChanged(string value)
    {
        if (int.TryParse(value, out var val))
        {
            var max = DurationUnit switch
            {
                DurationUnitType.Minutes => 1440,
                DurationUnitType.Hours => 24,
                DurationUnitType.Days => 365,
                _ => 1440
            };
            if (val > max) { DurationText = max.ToString(); return; }
            if (val < 1 && value.Length > 0 && value != "0") { DurationText = "1"; return; }
        }
        if (UseDuration && ContractsLoaded)
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
                var expiry = GetDateExpiryForPosition();
                _ = OpenPositions.AddPositionAsync(result, Symbol, DisplayName, contractType, expiry);
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
            string? barrier = GetBarrierForApi();

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
            "Hours" => DurationUnitType.Hours,
            "Days" => DurationUnitType.Days,
            _ => DurationUnitType.Minutes
        };
    }

    private void UpdateDurationRange()
    {
        DurationRange = DurationUnit switch
        {
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

        var showAbsolute = !UseDuration && IsEndTimeMoreThan24h();

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

        var currentSelection = SelectedBarrier;

        _apiBarriers.Clear();
        foreach (var b in newBarriers)
            _apiBarriers.Add(b);

        _apiBarriers.Sort();
        _barriersAreRelative = true;
        _barriersFromApi = true;

        UpdateBarrierDisplays();

        // Try to restore previous selection
        if (AvailableBarrierDisplays.Count > 0)
        {
            var format = $"F{_pipSize}";
            string targetDisplay;
            if (!UseDuration && IsEndTimeMoreThan24h())
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
            // End Time mode: generate barriers as offsets from spot
            if (IsEndTimeMoreThan24h())
            {
                // > 24h: API requires absolute barriers — generate 8 barriers like the site
                // Site pattern: spot rounded down to nearest 10, then 4 below and 4 above (or similar)
                decimal step = 10m;
                var baseBarrier = Math.Floor(_spotForBarriers / step) * step;

                // Generate 8 barriers: from baseBarrier - 10 to baseBarrier + 60
                for (int i = -1; i <= 6; i++)
                {
                    var absBarrier = baseBarrier + (i * step);
                    if (absBarrier > 0)
                        _apiBarriers.Add(absBarrier - _spotForBarriers);
                }

                _apiBarriers.Sort();
                _barriersAreRelative = true;
            }
            else
            {
                // ≤ 24h: uses relative barriers like Duration mode
                decimal step = (decimal)Math.Pow(10, 1 - _pipSize);

                var dateExpiry = GetDateExpiryUnix();
                int minutesToExpiry = 1440;
                if (dateExpiry.HasValue)
                {
                    var seconds = dateExpiry.Value - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    minutesToExpiry = Math.Max(1, (int)(seconds / 60));
                }

                decimal scaleFactor = (decimal)Math.Pow(minutesToExpiry, 0.513);
                decimal innerStep = Math.Round(_barrierInnerBase * scaleFactor / step, MidpointRounding.AwayFromZero) * step;
                decimal outerStep = Math.Round(_barrierOuterBase * scaleFactor / step, MidpointRounding.AwayFromZero) * step;

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
            DurationUnitType.Minutes => 1440,
            DurationUnitType.Hours => 24,
            DurationUnitType.Days => 365,
            _ => 1440
        };
        return val > max ? -1 : val;
    }

    private int GetDurationInMinutes()
    {
        if (!int.TryParse(DurationText, out var val) || val < 1) return 1;
        return DurationUnit switch
        {
            DurationUnitType.Minutes => val,
            DurationUnitType.Hours => val * 60,
            DurationUnitType.Days => val * 1440,
            _ => val
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
        DurationUnitType.Minutes => "m",
        DurationUnitType.Hours => "h",
        DurationUnitType.Days => "d",
        _ => "m"
    };

    private string? GetBarrierForApi()
    {
        if (!UseDuration && IsEndTimeMoreThan24h())
        {
            // End Time > 24h: SelectedBarrier holds the absolute value directly
            if (SelectedBarrier <= 0) return null;
            return SelectedBarrier.ToString($"F{_pipSize}", CultureInfo.InvariantCulture);
        }

        // Duration mode and End Time ≤ 24h use relative offsets
        var offset = SelectedBarrier;
        var sign = offset >= 0 ? "+" : "";
        return $"{sign}{offset.ToString($"F{_pipSize}", CultureInfo.InvariantCulture)}";
    }

    private bool IsEndTimeMoreThan24h()
    {
        var dateExpiry = GetDateExpiryUnix();
        if (dateExpiry == null) return false;
        var secondsUntilExpiry = dateExpiry.Value - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return secondsUntilExpiry > 86400;
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrEmpty(Symbol) || GetStakeValue() <= 0) return false;
        if (UseDuration) return GetDurationValue() > 0;
        return GetDateExpiryUnix() != null;
    }

    private async void RequestProposalDebounced()
    {
        if (!_active || !ContractsLoaded) return;

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
        string? barrier = GetBarrierForApi();

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

    public void RestoreState(string? durationUnit, string? durationText, string? stakeText, bool? useDuration = null)
    {
        AppLogger.Info(Src, $"RestoreState: unit={durationUnit}, duration={durationText}, stake={stakeText}, useDuration={useDuration}");
        if (useDuration.HasValue)
            UseDuration = useDuration.Value;
        if (!string.IsNullOrEmpty(durationUnit) && Enum.TryParse<DurationUnitType>(durationUnit, out var unit))
            DurationUnit = unit;
        if (!string.IsNullOrEmpty(durationText))
            DurationText = durationText;
        if (!string.IsNullOrEmpty(stakeText))
            StakeText = stakeText;
    }

    public void Dispose()
    {
        _proposalCts?.Cancel();
        _proposalCts?.Dispose();
        _barrierRefreshCts?.Cancel();
        _barrierRefreshCts?.Dispose();
        _contractService.ProposalUpdated -= OnProposalUpdated;
        OpenPositions.Dispose();
    }
}
