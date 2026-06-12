using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Excalibur5.Services;

/// <summary>
/// REST client for the new Deriv Options platform
/// (https://api.derivws.com/trading/v1).
///
/// Flow:
///   1. GET /trading/v1/options/accounts → discover account ID (DOT…)
///   2. POST /trading/v1/options/accounts/{id}/otp → get WebSocket URL
/// </summary>
public sealed class DerivRestClient : IDerivRestClient, IDisposable
{
    private const string Src = "RestClient";
    private const string BaseUrl = "https://api.derivws.com/trading/v1/options";
    private const string AppId = "33we3QV2jfoLet2b2EFxB";

    private readonly HttpClient _http;

    public DerivRestClient()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <inheritdoc />
    public async Task<(string AccountId, string AccountType)> GetAccountIdAsync(string patToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/accounts");
        AddAuthHeaders(request, patToken);

        AppLogger.Info(Src, "GET /accounts — discovering account ID");
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var msg = ExtractErrorMessage(body);
            AppLogger.Error(Src, $"GET /accounts failed ({response.StatusCode}): {msg}");
            throw new InvalidOperationException($"Failed to retrieve accounts: {msg}");
        }

        // Response shape: { "data": [ { "account_id": "DOT90004580", "account_type": "demo", ... } ], "meta": {...} }
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");

        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            AppLogger.Error(Src, "GET /accounts returned empty data array");
            throw new InvalidOperationException("No Options trading accounts found for this token.");
        }

        var first = data[0];
        var accountId = first.GetProperty("account_id").GetString();
        var accountType = first.TryGetProperty("account_type", out var at)
            ? at.GetString() ?? "real"
            : "real";
        AppLogger.Info(Src, $"Account ID: {accountId} | Type: {accountType}");
        return (accountId!, accountType);
    }

    /// <inheritdoc />
    public async Task<string> GetWebSocketUrlAsync(string accountId, string patToken,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/accounts/{accountId}/otp");
        AddAuthHeaders(request, patToken);

        AppLogger.Info(Src, $"POST /accounts/{accountId}/otp — requesting WebSocket URL");
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var msg = ExtractErrorMessage(body);
            AppLogger.Error(Src, $"OTP request failed ({response.StatusCode}): {msg}");
            throw new InvalidOperationException($"OTP generation failed: {msg}");
        }

        // Response shape: { "data": { "url": "wss://api.derivws.com/...?otp=..." } }
        using var doc = JsonDocument.Parse(body);
        var url = doc.RootElement.GetProperty("data").GetProperty("url").GetString();
        AppLogger.Info(Src, $"WebSocket URL obtained (otp length={url?.Length})");
        return url!;
    }

    private static void AddAuthHeaders(HttpRequestMessage request, string patToken)
    {
        request.Headers.Add("Deriv-App-ID", AppId);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", patToken);
    }

    private static string ExtractErrorMessage(string jsonBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonBody);
            if (doc.RootElement.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array &&
                errors.GetArrayLength() > 0)
            {
                return errors[0].TryGetProperty("message", out var m)
                    ? m.GetString() ?? "unknown"
                    : "unknown";
            }
        }
        catch { /* ignore parse failures */ }
        return jsonBody.Length > 200 ? jsonBody[..200] + "…" : jsonBody;
    }

    public void Dispose()
    {
        _http.Dispose();
        AppLogger.Info(Src, "DerivRestClient disposed");
    }
}
