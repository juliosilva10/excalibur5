using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy.TickIndicators;

public interface ITickIndicator
{
    IndicatorType Type { get; }
    IndicatorSignal Evaluate(IReadOnlyList<decimal> ticks);
    void Reset();
}
