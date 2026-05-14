using Excalibur5.Models;
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy;

public sealed class TrendEngine
{
    private const string Src = "TrendEngine";

    public SignalDirection? Evaluate(IReadOnlyList<CandleData> candles, int sampleSize)
    {
        if (candles.Count < sampleSize)
            return null;

        int calls = 0;
        int puts = 0;
        int startIndex = candles.Count - sampleSize;

        for (int i = startIndex; i < candles.Count; i++)
        {
            var c = candles[i];
            if (c.Close > c.Open)
                calls++;
            else if (c.Close < c.Open)
                puts++;
        }

        if (calls > puts)
            return SignalDirection.Call;
        if (puts > calls)
            return SignalDirection.Put;

        return null;
    }
}
