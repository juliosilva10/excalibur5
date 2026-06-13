using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Excalibur5.Models.Diversity;
using Excalibur5.Services;
using Excalibur5.Services.Strategy.Diversity;

namespace Excalibur5.ViewModels.Diversity;

/// <summary>
/// ViewModel for the Diversity panel: edits the group's legs, surfaces coverage / dead-digit / EV
/// guidance, and fires the group (manual mode) through the <see cref="DiversityCoordinator"/>.
/// The recover/virtual integration is owned by the session wiring; this VM focuses on the panel.
/// </summary>
public partial class DiversityViewModel : ObservableObject
{
    private readonly DiversityCoordinator? _coordinator;
    private Services.Strategy.Diversity.DiversityRecoveryBridge? _recoveryBridge;
    private decimal _currentTotalStake;
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

    public ObservableCollection<DiversityLegRow> Legs { get; } = new();

    /// <summary>Parameterless ctor for design-time / pre-connection binding.</summary>
    public DiversityViewModel() : this(null) { }

    public DiversityViewModel(DiversityCoordinator? coordinator)
    {
        _coordinator = coordinator;
        // Seed with a full-coverage over/under example so the panel isn't empty.
        AddRow(new DiversityLegRow { ContractType = "DIGITOVER", Barrier = "2", StakeText = "1.00" });
        AddRow(new DiversityLegRow { ContractType = "DIGITUNDER", Barrier = "7", StakeText = "1.00" });
        Legs.CollectionChanged += (_, _) => RecomputeCoverage();
        RecomputeCoverage();
    }

    private void AddRow(DiversityLegRow row)
    {
        row.PropertyChanged += (_, _) => RecomputeCoverage();
        Legs.Add(row);
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
        if (Legs.Count < 2) return null;

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
    private void AddLeg()
    {
        AddRow(new DiversityLegRow());
        RecomputeCoverage();
    }

    [RelayCommand]
    private void RemoveLeg(DiversityLegRow? row)
    {
        if (row != null && Legs.Count > 1)
        {
            Legs.Remove(row);
            RecomputeCoverage();
        }
    }

    /// <summary>Builds the immutable group config from the current rows.</summary>
    public DiversityGroupConfig BuildConfig(DiversityTriggerMode mode)
    {
        var legs = new List<DiversityLeg>();
        foreach (var row in Legs)
            legs.Add(row.ToLeg());

        // Hedge when all legs share one market; diversification otherwise.
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
            StatusText = "Coordinator indisponível (conecte-se primeiro).";
            return;
        }
        if (Legs.Count < 2)
        {
            StatusText = "Um grupo Diversity precisa de pelo menos 2 pernas.";
            return;
        }
        foreach (var row in Legs)
        {
            if (row.Stake <= 0)
            {
                StatusText = $"Stake inválido numa perna ({row.ContractType}).";
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
        StatusText = "Comprando grupo...";
        try
        {
            var config = BuildConfig(DiversityTriggerMode.Manual);
            var groupId = await _coordinator.ExecuteGroupAsync(config);
            StatusText = groupId != null
                ? $"Grupo aberto com {config.Legs.Count} pernas (total {config.TotalStake:F2})."
                : "Grupo abortado (falha numa perna; pernas desfeitas).";
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

    /// <summary>
    /// Recomputes the coverage / dead-digit / win-zone guidance for same-tick digit groups. Purely
    /// informational — the live system always prices from the broker proposal.
    /// </summary>
    public void RecomputeCoverage()
    {
        var predictions = new List<DigitPrediction>();
        foreach (var row in Legs)
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
                : "✓ Cobertura total (todo dígito ganha ao menos uma perna).";
        else
            CoverageText = $"⚠ Dígito(s) morto(s) (perde tudo): {string.Join(",", dead)}.";
    }
}
