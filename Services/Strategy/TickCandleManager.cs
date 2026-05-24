using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

/// <summary>
/// Centralized candle lifecycle manager.
/// Dispatches CandleBorn (when a new candle starts) and CandleDied (when a candle completes at its final tick).
/// All tick-based strategies consume these events instead of managing tick accumulation themselves.
/// </summary>
public sealed class TickCandleManager
{
    private const string Src = "TickCandleManager";

    private int _ticksPerCandle;
    private int _currentTickCount;
    private bool _hasFirstTick;
    private decimal _open;
    private decimal _high;
    private decimal _low;
    private long _birthEpoch;
    private int _totalCandlesFormed;

    /// <summary>Fired when a brand-new candle starts (the very first tick of the candle).</summary>
    public event EventHandler<CandleBornEventArgs>? CandleBorn;

    /// <summary>Fired when a candle completes its final tick.</summary>
    public event EventHandler<CandleDiedEventArgs>? CandleDied;

    public int TicksPerCandle => _ticksPerCandle;
    public int TotalCandlesFormed => _totalCandlesFormed;

    public TickCandleManager(int ticksPerCandle)
    {
        if (ticksPerCandle < 1)
            throw new ArgumentOutOfRangeException(nameof(ticksPerCandle), "Ticks per candle must be >= 1");
        _ticksPerCandle = ticksPerCandle;
        Reset();
    }

    public void SetTicksPerCandle(int value)
    {
        if (value < 1) return;
        if (value == _ticksPerCandle) return;
        _ticksPerCandle = value;
        Reset();
        AppLogger.Info(Src, $"TicksPerCandle changed to {value}");
    }

    public void Reset()
    {
        _currentTickCount = 0;
        _hasFirstTick = false;
        _open = 0;
        _high = 0;
        _low = 0;
        _birthEpoch = 0;
    }

    /// <summary>
    /// Feed one tick. Returns true if a candle died on this tick (meaning we are at a candle boundary).
    /// The engine should evaluate signals ONLY when a candle has just died.
    /// </summary>
    public bool FeedTick(decimal price, long epoch = 0)
    {
        // First tick ever — this is a candle birth
        if (!_hasFirstTick)
        {
            _open = price;
            _high = price;
            _low = price;
            _birthEpoch = epoch;
            _currentTickCount = 1;
            _hasFirstTick = true;

            CandleBorn?.Invoke(this, new CandleBornEventArgs
            {
                Open = price,
                Epoch = epoch,
                CandleIndex = _totalCandlesFormed
            });

            // For 1-tick candles, the candle is born and dies on the same tick
            if (_ticksPerCandle == 1)
            {
                _totalCandlesFormed++;
                var died = new CandleDiedEventArgs
                {
                    Open = price,
                    High = price,
                    Low = price,
                    Close = price,
                    BirthEpoch = epoch,
                    DeathEpoch = epoch,
                    TickCount = 1,
                    CandleIndex = _totalCandlesFormed - 1
                };
                CandleDied?.Invoke(this, died);
                Reset();
                return true;
            }

            return false;
        }

        // Accumulate tick into current candle
        _currentTickCount++;
        if (price > _high) _high = price;
        if (price < _low) _low = price;

        // Check if candle dies on this tick
        if (_currentTickCount >= _ticksPerCandle)
        {
            _totalCandlesFormed++;
            var died = new CandleDiedEventArgs
            {
                Open = _open,
                High = _high,
                Low = _low,
                Close = price,
                BirthEpoch = _birthEpoch,
                DeathEpoch = epoch,
                TickCount = _ticksPerCandle,
                CandleIndex = _totalCandlesFormed - 1
            };
            CandleDied?.Invoke(this, died);

            // Reset for next candle
            Reset();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Feeds multiple ticks from history to build candles without firing events.
    /// Returns the built list of completed CandleData.
    /// </summary>
    public List<CandleData> FeedHistory(IReadOnlyList<decimal> prices, IReadOnlyList<long>? epochs = null)
    {
        Reset();
        var candles = new List<CandleData>();

        for (int i = 0; i < prices.Count; i++)
        {
            var price = prices[i];
            var epoch = epochs != null && i < epochs.Count ? epochs[i] : 0L;

            if (!_hasFirstTick)
            {
                _open = price;
                _high = price;
                _low = price;
                _birthEpoch = epoch;
                _currentTickCount = 1;
                _hasFirstTick = true;

                if (_ticksPerCandle == 1)
                {
                    candles.Add(new CandleData
                    {
                        Open = price,
                        High = price,
                        Low = price,
                        Close = price,
                        Epoch = epoch
                    });
                    Reset();
                }
                continue;
            }

            _currentTickCount++;
            if (price > _high) _high = price;
            if (price < _low) _low = price;

            if (_currentTickCount >= _ticksPerCandle)
            {
                candles.Add(new CandleData
                {
                    Open = _open,
                    High = _high,
                    Low = _low,
                    Close = price,
                    Epoch = _birthEpoch
                });
                Reset();
            }
        }

        _totalCandlesFormed = candles.Count;
        return candles;
    }
}

public sealed class CandleBornEventArgs : EventArgs
{
    public decimal Open { get; init; }
    public long Epoch { get; init; }
    public int CandleIndex { get; init; }
}

public sealed class CandleDiedEventArgs : EventArgs
{
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public long BirthEpoch { get; init; }
    public long DeathEpoch { get; init; }
    public int TickCount { get; init; }
    public int CandleIndex { get; init; }

    public bool IsBullish => Close > Open;
    public bool IsBearish => Close < Open;
    public decimal Body => Math.Abs(Close - Open);
    public decimal Range => High - Low;
}