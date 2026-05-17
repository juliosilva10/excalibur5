using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public interface IStrategyEngine
{
    event EventHandler<TradeSignal>? SignalGenerated;
    void Start(StrategyConfig config);
    void Stop();
    void BeginBulkFeed();
    void EndBulkFeed();
    void FeedCandle(CandleData candle);
    void RecordTradeResult(IReadOnlyList<IndicatorType> contributors, bool won);
    void EmitExternalSignal(TradeSignal signal);
    void ReEvaluate();
    void ResetCooldown();
    bool IsRunning { get; }
}
