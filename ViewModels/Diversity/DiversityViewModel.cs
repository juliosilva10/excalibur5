using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Excalibur5.Models.Diversity;
using Excalibur5.Services;
using Excalibur5.Services.Strategy.Diversity;
using Excalibur5.Services.Strategy.Virtual;

namespace Excalibur5.ViewModels.Diversity;

/// <summary>
/// ViewModel for the Diversity panel: edits the group's contracts, surfaces coverage / dead-digit
/// guidance, and fires the group through the <see cref="DiversityCoordinator"/>. When Virtual is
/// configured, the manual buy simulates the group repeatedly until the target W/L sequence occurs,
/// then buys for real. The recover integration is owned by the session wiring.
/// </summary>
public partial class DiversityViewModel : ObservableObject
{
    private readonly DiversityCoordinator? _coordinator;
    // Diversity runs its own virtual state machine for the pre-buy simulation, separate from the
    // single-contract bot's shared controller, so simulating a group never corrupts the bot's
    // virtual sequence. Only the target-sequence *settings* are shared (read from the Virtual panel).
    private readonly IVirtualEntryModeController _virtualController = new VirtualEntryModeController();
    private readonly IVirtualEntrySettingsProvider? _virtualSettings;
    private Services.Strategy.Diversity.DiversityRecoveryBridge? _recoveryBridge;
    private decimal _currentTotalStake;
    private readonly Random _sim = new();
    // Atomic re-entrancy gate (0 = idle, 1 = a buy is in flight). Guards against two near-
    // simultaneous fires opening two groups regardless of which thread the signal arrives on.
    private int _firing;

    [ObservableProperty] private bool _isDiversityVisible;
    [ObservableProperty] private bool _useSignalMode;
    [ObservableProperty] private string _confidenceThresholdText = "0.60";
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _coverageText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Minimum signal confidence (0-1) required to fire a group in signal mode.</summary>
    public double ConfidenceThreshold =>
        double.TryParse(ConfidenceThresholdText, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v is > 0 and <= 1 ? v : 0.6;

    public ObservableCollection<DiversityContractRow> Contracts { get; } = new();

    /// <summary>Parameterless ctor for design-time / pre-connection binding.</summary>
    public DiversityViewModel() : this(null, null) { }

    public DiversityViewModel(
        DiversityCoordinator? coordinator,
        IVirtualEntrySettingsProvider? virtualSettings = null)
    {
        _coordinator = coordinator;
        _virtualSettings = virtualSettings;
        // Seed with a full-coverage over/under example so the panel isn't empty.
        AddRow(new DiversityContractRow { ContractType = "DIGITOVER", BarrierDigit = 2, StakeText = "1.00" });
        AddRow(new DiversityContractRow { ContractType = "DIGITUNDER", BarrierDigit = 7, StakeText = "1.00" });
        Contracts.CollectionChanged += (_, _) => RecomputeCoverage();
        RecomputeCoverage();
    }

    private void AddRow(DiversityContractRow row)
    {
        row.PropertyChanged += (_, _) => RecomputeCoverage();
        Contracts.Add(row);
    }

    [RelayCommand]
    private void ToggleDiversity() => IsDiversityVisible = !IsDiversityVisible;

    /// <summary>
    /// Supplies the recover bridge used in signal mode so each auto-fired group draws its total
    /// stake from recover (distributed across legs) and escalates after a net group loss.
    /// </summary>
    public void SetRecoveryBridge(Services.Strategy.Diversity.DiversityRecoveryBridge? bridge)
    {
        _recoveryBridge = bridge;
        _currentTotalStake = BuildConfig(DiversityTriggerMode.Signal).TotalStake;
    }

    /// <summary>
    /// Called on each strategy signal. Fires the whole group when the signal gate allows it
    /// (signal mode on, not busy, no pending group, confidence above threshold). Returns the
    /// group config that was sent, or null if nothing fired. Applies recover sizing when a bridge
    /// is set. Safe to call from the live signal path.
    /// </summary>
    public async Task<DiversityGroupConfig?> TryFireFromSignalAsync(double signalConfidence)
    {
        if (_coordinator == null) return null;
        if (!DiversitySignalGate.ShouldFire(
                UseSignalMode, signalConfidence, ConfidenceThreshold,
                _coordinator.PendingGroupCount, IsBusy))
            return null;
        if (Contracts.Count < 2) return null;

        // Atomically claim the firing slot; if another fire is already in flight, bail.
        if (Interlocked.CompareExchange(ref _firing, 1, 0) != 0) return null;

        var template = BuildConfig(DiversityTriggerMode.Signal);
        var config = _recoveryBridge != null
            ? _recoveryBridge.BuildNextGroup(template, _currentTotalStake)
            : template;
        _currentTotalStake = config.TotalStake;

        IsBusy = true;
        try
        {
            var groupId = await _coordinator.ExecuteGroupAsync(config);
            return groupId != null ? config : null;
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref _firing, 0);
        }
    }

    [RelayCommand]
    private void AddContract()
    {
        AddRow(new DiversityContractRow());
        RecomputeCoverage();
    }

    [RelayCommand]
    private void RemoveContract(DiversityContractRow? row)
    {
        if (row != null && Contracts.Count > 1)
        {
            Contracts.Remove(row);
            RecomputeCoverage();
        }
    }

    /// <summary>Builds the immutable group config from the current contract rows.</summary>
    public DiversityGroupConfig BuildConfig(DiversityTriggerMode mode)
    {
        var legs = new List<DiversityLeg>();
        foreach (var row in Contracts)
            legs.Add(row.ToLeg());

        // Hedge when all contracts share one market; diversification otherwise.
        bool sameMarket = legs.Count > 0 && legs.TrueForAll(l => l.Symbol == legs[0].Symbol);
        return new DiversityGroupConfig
        {
            Legs = legs,
            TriggerMode = mode,
            Kind = sameMarket ? DiversityGroupKind.Hedge : DiversityGroupKind.Diversification
        };
    }

    [RelayCommand]
    private async Task ExecuteGroupAsync()
    {
        if (_coordinator == null)
        {
            StatusText = "Indisponível (conecte-se primeiro).";
            return;
        }
        if (Contracts.Count < 2)
        {
            StatusText = "Um grupo Diversity precisa de pelo menos 2 contratos.";
            return;
        }
        foreach (var row in Contracts)
        {
            if (row.Stake <= 0)
            {
                StatusText = $"Stake inválido num contrato ({ContractLabel(row)}).";
                return;
            }
        }
        // Don't open a second concurrent group while one is still live (keeps "group = 1 contract").
        if (_coordinator.PendingGroupCount > 0)
        {
            StatusText = "Já há um grupo em andamento. Aguarde a liquidação.";
            return;
        }
        // Share the same atomic firing slot as signal mode so manual + signal can't double-fire.
        if (Interlocked.CompareExchange(ref _firing, 1, 0) != 0)
        {
            StatusText = "Outro disparo em andamento.";
            return;
        }

        IsBusy = true;
        try
        {
            // If Virtual is configured, simulate the group repeatedly until the target W/L
            // sequence occurs; only then buy for real (group treated as one contract).
            if (!await RunVirtualUntilSequenceAsync())
            {
                StatusText = "Simulação virtual cancelada.";
                return;
            }

            StatusText = "Comprando grupo...";
            var config = BuildConfig(DiversityTriggerMode.Manual);
            var groupId = await _coordinator.ExecuteGroupAsync(config);
            StatusText = groupId != null
                ? $"Grupo aberto com {config.Legs.Count} contratos (total {config.TotalStake:F2})."
                : "Grupo abortado (falha num contrato; contratos desfeitos).";
        }
        catch (Exception ex)
        {
            StatusText = $"Erro ao executar grupo: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref _firing, 0);
        }
    }

    private static string ContractLabel(DiversityContractRow row)
        => Models.ContractTypeFormatter.ToDisplayLabel(row.ContractType);

    /// <summary>
    /// When Virtual entry mode is active, simulates the whole group (net of all contracts → one
    /// W/L) repeatedly, feeding each outcome to the controller, until the configured target
    /// sequence is reached. Returns true when it's time to buy for real (or when Virtual is off).
    /// Each simulated group uses the digit win-probabilities — no money is spent.
    /// </summary>
    private async Task<bool> RunVirtualUntilSequenceAsync()
    {
        if (_virtualSettings == null) return true;
        // Refresh our private controller with the latest target sequence/tolerance from the panel.
        _virtualController.Start(_virtualSettings.GetSettings());
        if (!_virtualController.IsVirtualMode) return true; // Virtual not configured → buy for real

        int guard = 0;
        const int maxIterations = 10_000; // safety net against an unreachable target sequence
        while (_virtualController.IsVirtualMode && guard++ < maxIterations)
        {
            if (!_virtualController.TryReserveVirtualEntry())
                break;

            bool groupWon = SimulateGroupOutcome();
            StatusText = $"Simulando grupo (virtual): {(groupWon ? 'W' : 'L')}...";
            _virtualController.RecordVirtualResult(groupWon);
            await Task.Yield(); // keep the UI responsive during the simulation loop
        }
        return !_virtualController.IsVirtualMode;
    }

    /// <summary>
    /// Simulates one group: draws each contract's outcome from its digit win-probability and sums
    /// the net profit; the group wins if the net is positive. Non-digit contracts are treated as a
    /// coin-flip on direction (no spot model here). Pure simulation, no orders.
    /// </summary>
    private bool SimulateGroupOutcome()
    {
        var legProfits = new List<decimal>();
        foreach (var row in Contracts)
        {
            double winProb;
            var pred = row.ToDigitPrediction();
            if (pred != null)
                winProb = DigitOddsCalculator.WinProbability(pred.Value);
            else
                winProb = 0.5; // no spot model for non-digit legs in simulation

            bool win = _sim.NextDouble() < winProb;
            // Approximate payout from the implied fair odds (no broker margin in sim).
            decimal profit = win
                ? row.Stake * (decimal)((1.0 / Math.Max(winProb, 0.01)) - 1.0)
                : -row.Stake;
            legProfits.Add(profit);
        }
        return GroupVirtualEvaluator.GroupWon(legProfits);
    }

    /// <summary>
    /// Recomputes the coverage / dead-digit / win-zone guidance for same-tick digit groups. Purely
    /// informational — the live system always prices from the broker proposal.
    /// </summary>
    public void RecomputeCoverage()
    {
        var predictions = new List<DigitPrediction>();
        foreach (var row in Contracts)
        {
            var p = row.ToDigitPrediction();
            if (p != null) predictions.Add(p.Value);
        }

        if (predictions.Count < 2)
        {
            CoverageText = string.Empty;
            return;
        }

        var dead = DigitOddsCalculator.DeadDigits(predictions);
        var full = DigitOddsCalculator.FullWinDigits(predictions);

        if (dead.Count == 0)
            CoverageText = full.Count > 0
                ? $"✓ Cobertura total. Ganho duplo nos dígitos: {string.Join(",", full)}."
                : "✓ Cobertura total (todo dígito ganha ao menos um contrato).";
        else
            CoverageText = $"⚠ Dígito(s) morto(s) (perde tudo): {string.Join(",", dead)}.";
    }
}
