using System.Globalization;

namespace Excalibur5.Services.Strategy;

/// <summary>
/// Pure helpers for computing barrier offsets for Vanilla/Turbo contracts. Extracted from
/// ContractPanelViewModel so the math is unit-testable in isolation — no UI state, no I/O.
/// The ViewModel keeps ownership of how these offsets are displayed and selected.
/// </summary>
public static class BarrierCalculator
{
    private const string ErrorPrefix = "Barriers available are ";

    /// <summary>
    /// Extracts the barrier list from a Deriv error message of the form
    /// "Barriers available are +29.320, +15.430, +0.050, -15.280, -29.040".
    /// Returns an empty list if the message isn't in that shape.
    /// </summary>
    public static List<string> ParseBarriersFromError(string message)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(message)) return result;

        var idx = message.IndexOf(ErrorPrefix, StringComparison.Ordinal);
        if (idx < 0) return result;

        var csv = message[(idx + ErrorPrefix.Length)..];
        foreach (var part in csv.Split(','))
        {
            var trimmed = part.Trim();
            // Stop at the first token that isn't a barrier value (handles trailing prose).
            if (trimmed.Length == 0) continue;
            if (decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                result.Add(trimmed);
        }
        return result;
    }

    /// <summary>
    /// Generates the fallback relative barrier offsets (used when the API hasn't returned
    /// barrier choices). Mirrors the Deriv website behaviour. Returns a sorted list of
    /// offsets relative to spot; empty when spot is non-positive.
    /// </summary>
    public static List<decimal> GenerateFallbackOffsets(
        decimal spot, int pipSize, bool useDuration, double durationMinutes,
        decimal innerBase, decimal outerBase)
    {
        var barriers = new List<decimal>();
        if (spot <= 0) return barriers;

        if (useDuration)
        {
            // Barriers scale with dur^0.513; round to 10-pip granularity.
            if (durationMinutes <= 0) durationMinutes = 1;

            decimal scaleFactor = (decimal)Math.Pow(durationMinutes, 0.513);
            decimal step = (decimal)Math.Pow(10, 1 - pipSize);

            decimal innerStep = Math.Round(innerBase * scaleFactor / step, MidpointRounding.AwayFromZero) * step;
            decimal outerStep = Math.Round(outerBase * scaleFactor / step, MidpointRounding.AwayFromZero) * step;

            if (innerStep < step) innerStep = step;
            if (outerStep <= innerStep) outerStep = innerStep + step;

            barriers.Add(-outerStep);
            barriers.Add(-innerStep);
            barriers.Add(0m);
            barriers.Add(innerStep);
            barriers.Add(outerStep);
        }
        else
        {
            // End Time mode: absolute round numbers around spot, expressed as offsets.
            decimal step = 10m;
            var baseBarrier = Math.Floor(spot / step) * step;

            for (int i = -3; i <= 6; i++)
            {
                var absBarrier = baseBarrier + (i * step);
                if (absBarrier > 0)
                    barriers.Add(absBarrier - spot);
            }
        }

        barriers.Sort();
        return barriers;
    }
}
