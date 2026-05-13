namespace Excalibur5.Models;

public sealed class CandleData
{
    public long    Epoch { get; set; }
    public decimal Open  { get; set; }
    public decimal High  { get; set; }
    public decimal Low   { get; set; }
    public decimal Close { get; set; }
}
