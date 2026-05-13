using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public interface IIndicator
{
    IndicatorType Type { get; }
    IndicatorSignal Evaluate(IReadOnlyList<CandleData> candles);
    void Reset();
}
