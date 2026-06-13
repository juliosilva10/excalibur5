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

    [ObservableProperty] private bool _isDiversityVisible;
    [ObservableProperty] private bool _useSignalMode;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _coverageText = string.Empty;
    [ObservableProperty] private bool _isBusy;

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
