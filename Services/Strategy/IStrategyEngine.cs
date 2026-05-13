using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public interface IStrategyEngine
{
    event EventHandler<TradeSignal>? SignalGenerated;
    void Start(StrategyConfig config);
    void Stop();
    void FeedCandle(CandleData candle);
    bool IsRunning { get; }
}
