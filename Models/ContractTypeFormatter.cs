namespace Excalibur5.Models;

public static class ContractTypeFormatter
{
    public static string ToDisplayLabel(string contractType) => contractType switch
    {
        "CALL" or "CALLE" => "Rise",
        "PUT" or "PUTE" => "Fall",
        "VANILLALONGCALL" => "Call",
        "VANILLALONGPUT" => "Put",
        "MULTUP" => "Multiplier Up",
        "MULTDOWN" => "Multiplier Down",
        _ when contractType.Contains("CALL", StringComparison.OrdinalIgnoreCase) => "Call",
        _ when contractType.Contains("PUT", StringComparison.OrdinalIgnoreCase) => "Put",
        _ => contractType
    };
}
