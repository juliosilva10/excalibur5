namespace Excalibur5.Services.Strategy;

/// <summary>
/// Owns the running recovery state (martingale level, accumulated deficit, observed payout
/// ratios) that was previously scattered across ContractPanelViewModel. Stake math is
/// delegated to <see cref="RecoveryStakeCalculator"/>. This type holds state but no UI/I/O,
/// so its transitions are unit-testable; the ViewModel applies the returned stake.
/// </summary>
public sealed class RecoveryController
{
    private const int PayoutWindow = 5;

    private int _martingaleLevel;
    private decimal _baseStake;
    private decimal _deficit;
    private readonly decimal[] _payoutRatios = new decimal[PayoutWindow];
    private int _payoutIndex;
    private int _payoutCount;

    public decimal BaseStake => _baseStake;

    /// <summary>True when a martingale ladder or deficit is currently in progress.</summary>
    public bool HasActiveProgress => _martingaleLevel > 0 || _deficit > 0;

    /// <summary>Begins a fresh recovery cycle from the given base stake.</summary>
    public void Start(decimal baseStake)
    {
        _baseStake = baseStake;
        _martingaleLevel = 0;
        _deficit = 0;
        _payoutIndex = 0;
        _payoutCount = 0;
    }

    /// <summary>Clears in-progress ladder/deficit (e.g. when recovery is turned off).</summary>
    public void ResetProgress()
    {
        _martingaleLevel = 0;
        _deficit = 0;
    }

    /// <summary>
    /// Records a settled trade in Martingale mode and returns the next stake.
    /// On a loss within the ladder, advances a level and uses <paramref name="calculateStake"/>;
    /// otherwise resets to the base stake.
    /// </summary>
    public decimal RegisterMartingaleResult(bool isLoss, int maxLevel, Func<int, decimal> calculateStake)
    {
        if (isLoss && _martingaleLevel < maxLevel)
        {
            _martingaleLevel++;
            return calculateStake(_martingaleLevel);
        }

        _martingaleLevel = 0;
        return _baseStake;
    }

    /// <summary>
    /// Records a settled trade in Deficit Recovery mode and returns the next stake.
    /// Tracks the running deficit and a rolling window of payout ratios, then sizes the
    /// next stake via <see cref="RecoveryStakeCalculator"/>.
    /// </summary>
    public decimal RegisterDeficitResult(bool isLoss, decimal profit, int recoveryTrades, decimal maxStake)
    {
        if (isLoss)
        {
            _deficit += Math.Abs(profit);
        }
        else
        {
            _deficit = Math.Max(0, _deficit - profit);
            if (_baseStake > 0)
            {
                _payoutRatios[_payoutIndex] = profit / _baseStake;
                _payoutIndex = (_payoutIndex + 1) % PayoutWindow;
                if (_payoutCount < PayoutWindow) _payoutCount++;
            }
        }

        if (_deficit <= 0) return _baseStake;

        var avgRatio = RecoveryStakeCalculator.AveragePayoutRatio(_payoutRatios, _payoutCount);
        return RecoveryStakeCalculator.NextStake(_deficit, avgRatio, recoveryTrades, _baseStake, maxStake);
    }
}
