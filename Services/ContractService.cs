using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Excalibur5.Models;

namespace Excalibur5.Services;

public sealed class ContractService : IContractService, IDisposable
{
    private const string Src = "ContractSvc";

    private readonly IDerivWebSocketService _ws;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, string> _activeSubIds = new();
    private readonly ConcurrentDictionary<string, string> _subIdToContractType = new();
    private readonly ConcurrentDictionary<long, string> _openContractSubIds = new();
    private int _reqId = 20000;

    public event EventHandler<ProposalResponse>? ProposalUpdated;
    public event EventHandler<OpenContractUpdate>? OpenContractUpdated;

    public ContractService(IDerivWebSocketService ws)
    {
        _ws = ws;
        _ws.MessageReceived += OnMessage;
        AppLogger.Info(Src, "ContractService created");
    }

    private void OnMessage(object? sender, string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch { return; }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.TryGetProperty("req_id", out var reqIdEl) &&
                _pending.TryRemove(reqIdEl.GetInt32(), out var tcs))
            {
                tcs.TrySetResult(root.Clone());
            }

            if (root.TryGetProperty("msg_type", out var mt) && mt.GetString() == "proposal" &&
                root.TryGetProperty("proposal", out var propEl))
            {
                var proposal = ParseProposal(root, propEl);
                if (proposal != null)
                {
                    var withType = proposal;
                    if (string.IsNullOrEmpty(proposal.ContractType) &&
                        !string.IsNullOrEmpty(proposal.SubscriptionId) &&
                        _subIdToContractType.TryGetValue(proposal.SubscriptionId, out var ct))
                    {
                        withType = proposal with { ContractType = ct };
                    }
                    ProposalUpdated?.Invoke(this, withType);
                }
            }

            if (root.TryGetProperty("msg_type", out var mt2) && mt2.GetString() == "proposal_open_contract" &&
                root.TryGetProperty("proposal_open_contract", out var pocEl))
            {
                var update = ParseOpenContractUpdate(root, pocEl);
                if (update != null)
                    OpenContractUpdated?.Invoke(this, update);
            }
        }
    }

    private static ProposalResponse? ParseProposal(JsonElement root, JsonElement propEl)
    {
        var subId = "";
        if (root.TryGetProperty("subscription", out var sub) &&
            sub.TryGetProperty("id", out var idEl))
            subId = idEl.GetString() ?? "";

        var askPrice = propEl.TryGetProperty("ask_price", out var ap) ? ParseDecimal(ap) : 0m;
        var payout = propEl.TryGetProperty("payout", out var pay) ? ParseDecimal(pay) : 0m;
        var ppp = propEl.TryGetProperty("payout_per_point", out var pppEl) ? ParseDecimal(pppEl) : 0m;

        // Vanilla options: payout_per_point comes from display_number_of_contracts
        if (ppp == 0m && propEl.TryGetProperty("display_number_of_contracts", out var dnc))
            ppp = ParseDecimal(dnc);

        // Parse barrier_choices from proposal response
        var barrierChoices = new List<string>();
        if (propEl.TryGetProperty("barrier_choices", out var bc) && bc.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in bc.EnumerateArray())
            {
                var val = item.GetString();
                if (!string.IsNullOrEmpty(val))
                    barrierChoices.Add(val);
            }
        }

        return new ProposalResponse
        {
            ProposalId = propEl.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            AskPrice = askPrice,
            Payout = payout,
            Spot = propEl.TryGetProperty("spot", out var sp) ? ParseDecimal(sp) : 0m,
            SpotTime = propEl.TryGetProperty("spot_time", out var st) ? st.GetInt64() : 0,
            DateExpiry = propEl.TryGetProperty("date_expiry", out var de) ? de.GetInt64() : 0,
            PayoutPerPoint = ppp,
            SubscriptionId = subId,
            BarrierChoices = barrierChoices
        };
    }

    private static string ParseSpotField(JsonElement el, params string[] fieldNames)
    {
        foreach (var name in fieldNames)
        {
            if (el.TryGetProperty(name, out var val))
            {
                var raw = val.ValueKind == JsonValueKind.Number
                    ? val.GetDecimal().ToString(CultureInfo.InvariantCulture)
                    : val.GetString() ?? "";
                if (!string.IsNullOrEmpty(raw))
                    return raw;
            }
        }
        return "";
    }

    private static decimal ParseDecimal(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number)
            return el.GetDecimal();
        var raw = el.GetString();
        return string.IsNullOrEmpty(raw) ? 0m : decimal.Parse(raw, CultureInfo.InvariantCulture);
    }

    public async Task<ContractsForResponse> GetContractsForAsync(string symbol, string currency = "USD", CancellationToken ct = default)
    {
        var reqId = Interlocked.Increment(ref _reqId);

        var dict = new Dictionary<string, object>
        {
            ["contracts_for"] = symbol,
            ["currency"] = currency,
            ["product_type"] = "basic",
            ["req_id"] = reqId
        };

        var payload = JsonSerializer.Serialize(dict);

        var root = await SendAndWaitAsync(reqId, payload, ct);

        if (root.TryGetProperty("error", out var err))
        {
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "unknown";
            AppLogger.Error(Src, $"contracts_for error for {symbol}: {msg}");
            return new ContractsForResponse();
        }

        var result = new ContractsForResponse
        {
            Spot = root.TryGetProperty("contracts_for", out var cf) &&
                   cf.TryGetProperty("spot", out var spot) ? ParseDecimal(spot) : 0m,
            Currency = currency,
            Available = ParseAvailableContracts(root),
            AvailableBarriers = ParseAvailableBarriers(root)
        };

        AppLogger.Info(Src, $"contracts_for {symbol}: {result.Available.Count} contracts, spot={result.Spot}, barriers={result.AvailableBarriers.Count}");
        return result;
    }

    private static List<ContractInfo> ParseAvailableContracts(JsonElement root)
    {
        var list = new List<ContractInfo>();
        if (!root.TryGetProperty("contracts_for", out var cf) ||
            !cf.TryGetProperty("available", out var available) ||
            available.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in available.EnumerateArray())
        {
            var contractType = item.TryGetProperty("contract_type", out var ct) ? ct.GetString() ?? "" : "";
            if (contractType != "VANILLALONGCALL" && contractType != "VANILLALONGPUT"
                && contractType != "CALL" && contractType != "PUT")
                continue;

            list.Add(new ContractInfo
            {
                ContractType = contractType,
                MinDuration = item.TryGetProperty("min_contract_duration", out var minD) ? ParseDurationValue(minD.GetString()) : 1,
                MinDurationUnit = item.TryGetProperty("min_contract_duration", out var minDu) ? ParseDurationUnit(minDu.GetString()) : "m",
                MaxDuration = item.TryGetProperty("max_contract_duration", out var maxD) ? ParseDurationValue(maxD.GetString()) : 1440,
                MaxDurationUnit = item.TryGetProperty("max_contract_duration", out var maxDu) ? ParseDurationUnit(maxDu.GetString()) : "m",
                MinStake = item.TryGetProperty("min_stake", out var minS) ? ParseDecimal(minS) : 0.35m,
                MaxStake = item.TryGetProperty("max_stake", out var maxS) ? ParseDecimal(maxS) : 50000m,
                Barrier = item.TryGetProperty("barrier", out var bar) ? ParseDecimal(bar) : null,
                BarrierCategory = item.TryGetProperty("barrier_category", out var bc) ? bc.GetString() ?? "" : ""
            });
        }

        return list;
    }

    private static int ParseDurationValue(string? duration)
    {
        if (string.IsNullOrEmpty(duration)) return 1;
        var numPart = new string(duration.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(numPart, out var val) ? val : 1;
    }

    private static string ParseDurationUnit(string? duration)
    {
        if (string.IsNullOrEmpty(duration)) return "m";
        var unitPart = new string(duration.SkipWhile(char.IsDigit).ToArray());
        return unitPart.Length > 0 ? unitPart : "m";
    }

    private static List<decimal> ParseAvailableBarriers(JsonElement root)
    {
        var seen = new HashSet<decimal>();
        if (!root.TryGetProperty("contracts_for", out var cf))
            return new List<decimal>();

        // Try root-level available_barriers first
        if (cf.TryGetProperty("available_barriers", out var rootBarriers) &&
            rootBarriers.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in rootBarriers.EnumerateArray())
                seen.Add(ParseDecimal(b));
        }

        // Also try per-contract available_barriers
        if (seen.Count == 0 &&
            cf.TryGetProperty("available", out var available) &&
            available.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in available.EnumerateArray())
            {
                if (!item.TryGetProperty("available_barriers", out var barriersEl))
                    continue;

                if (barriersEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var b in barriersEl.EnumerateArray())
                        seen.Add(ParseDecimal(b));
                }
                else if (barriersEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in barriersEl.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var b in prop.Value.EnumerateArray())
                                seen.Add(ParseDecimal(b));
                        }
                        else
                        {
                            var val = ParseDecimal(prop.Value);
                            if (val != 0)
                                seen.Add(val);
                        }
                    }
                }

                if (seen.Count > 0) break;
            }
        }

        var barriers = seen.ToList();
        barriers.Sort();
        AppLogger.Info(Src, $"ParseAvailableBarriers: found {barriers.Count} barriers");
        return barriers;
    }

    public async Task<ProposalResponse> SubscribeProposalAsync(string symbol, string contractType, decimal amount, int? duration = null, string? durationUnit = null, long? dateExpiry = null, string? barrier = null, string currency = "USD", string? subscriptionKey = null, CancellationToken ct = default)
    {
        var key = subscriptionKey ?? contractType;
        await UnsubscribeProposalAsync(key, ct);

        var reqId = Interlocked.Increment(ref _reqId);

        var dict = new Dictionary<string, object>
        {
            ["proposal"] = 1,
            ["subscribe"] = 1,
            ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
            ["basis"] = "stake",
            ["contract_type"] = contractType,
            ["currency"] = currency,
            ["symbol"] = symbol,
            ["req_id"] = reqId
        };

        if (dateExpiry.HasValue)
        {
            dict["date_expiry"] = dateExpiry.Value;
        }
        else if (duration.HasValue && durationUnit != null)
        {
            dict["duration"] = duration.Value;
            dict["duration_unit"] = durationUnit;
        }

        if (!string.IsNullOrEmpty(barrier))
            dict["barrier"] = barrier;

        var payload = JsonSerializer.Serialize(dict);

        var root = await SendAndWaitAsync(reqId, payload, ct);

        if (root.TryGetProperty("error", out var err))
        {
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "unknown";
            AppLogger.Error(Src, $"proposal error: {msg}");
            throw new InvalidOperationException(msg);
        }

        if (root.TryGetProperty("subscription", out var sub) &&
            sub.TryGetProperty("id", out var idEl))
        {
            var subId = idEl.GetString() ?? "";
            _activeSubIds[key] = subId;
            _subIdToContractType[subId] = contractType;
        }

        var propEl = root.GetProperty("proposal");
        var response = ParseProposal(root, propEl) ?? new ProposalResponse();
        response = response with { ContractType = contractType };
        AppLogger.Info(Src, $"Proposal subscribed: {key} {symbol} dateExpiry={dateExpiry} dur={duration}{durationUnit} barrier={barrier} ask={response.AskPrice}");
        return response;
    }

    public async Task UnsubscribeProposalAsync(string? contractType = null, CancellationToken ct = default)
    {
        if (contractType == null)
        {
            await UnsubscribeAllProposalsAsync(ct);
            return;
        }

        if (!_activeSubIds.TryRemove(contractType, out var subId)) return;
        _subIdToContractType.TryRemove(subId, out _);

        var reqId = Interlocked.Increment(ref _reqId);
        var payload = JsonSerializer.Serialize(new { forget = subId, req_id = reqId });

        try
        {
            await SendAndWaitAsync(reqId, payload, ct);
            AppLogger.Info(Src, $"Proposal unsubscribed: {contractType} sub={subId}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Unsubscribe proposal error: {ex.Message}");
        }
    }

    public async Task UnsubscribeAllProposalsAsync(CancellationToken ct = default)
    {
        var types = _activeSubIds.Keys.ToArray();
        foreach (var type in types)
        {
            await UnsubscribeProposalAsync(type, ct);
        }
    }

    public void ClearSubscription(string contractType)
    {
        if (_activeSubIds.TryRemove(contractType, out var subId))
            _subIdToContractType.TryRemove(subId, out _);
    }

    private async Task<JsonElement> SendAndWaitAsync(int reqId, string json, CancellationToken ct, int timeoutMs = 15000)
    {
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[reqId] = tcs;

        using var timeout = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using var reg = linked.Token.Register(() =>
        {
            if (_pending.TryRemove(reqId, out var t))
                t.TrySetCanceled();
        });

        await _ws.SendAsync(json, ct);
        return await tcs.Task;
    }

    public async Task<BuyResponse> BuyContractAsync(string proposalId, decimal price, CancellationToken ct = default)
    {
        var reqId = Interlocked.Increment(ref _reqId);
        var payload = JsonSerializer.Serialize(new
        {
            buy = proposalId,
            price = price.ToString(CultureInfo.InvariantCulture),
            req_id = reqId
        });

        var root = await SendAndWaitAsync(reqId, payload, ct);

        if (root.TryGetProperty("error", out var err))
        {
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() ?? "unknown" : "unknown";
            AppLogger.Error(Src, $"buy error: {msg}");
            return new BuyResponse { Error = msg };
        }

        if (root.TryGetProperty("buy", out var buyEl))
        {
            var response = new BuyResponse
            {
                ContractId = buyEl.TryGetProperty("contract_id", out var cid) ? cid.GetInt64() : 0,
                BuyPrice = buyEl.TryGetProperty("buy_price", out var bp) ? ParseDecimal(bp) : 0m,
                Payout = buyEl.TryGetProperty("payout", out var pay) ? ParseDecimal(pay) : 0m,
                StartTime = buyEl.TryGetProperty("start_time", out var st) ? st.GetInt64() : 0,
                LongCode = buyEl.TryGetProperty("longcode", out var lc) ? lc.GetString() ?? "" : ""
            };
            AppLogger.Info(Src, $"Buy success: contract_id={response.ContractId} price={response.BuyPrice}");
            return response;
        }

        return new BuyResponse { Error = "Unexpected response" };
    }

    public async Task<BuyResponse> BuyDirectAsync(string symbol, string contractType, decimal stake, int duration, string durationUnit, string? barrier = null, CancellationToken ct = default)
    {
        var reqId = Interlocked.Increment(ref _reqId);
        var dict = new Dictionary<string, object>
        {
            ["proposal"] = 1,
            ["amount"] = stake.ToString(CultureInfo.InvariantCulture),
            ["basis"] = "stake",
            ["contract_type"] = contractType,
            ["currency"] = "USD",
            ["symbol"] = symbol,
            ["duration"] = duration,
            ["duration_unit"] = durationUnit,
            ["req_id"] = reqId
        };

        if (!string.IsNullOrEmpty(barrier))
            dict["barrier"] = barrier;

        var payload = JsonSerializer.Serialize(dict);
        var root = await SendAndWaitAsync(reqId, payload, ct);

        if (root.TryGetProperty("error", out var err))
        {
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() ?? "unknown" : "unknown";
            AppLogger.Error(Src, $"BuyDirect proposal error: {msg}");
            return new BuyResponse { Error = msg };
        }

        var proposalId = root.TryGetProperty("proposal", out var propEl) &&
                         propEl.TryGetProperty("id", out var idEl)
            ? idEl.GetString() ?? ""
            : "";

        if (string.IsNullOrEmpty(proposalId))
            return new BuyResponse { Error = "No proposal ID returned" };

        var askPrice = propEl.TryGetProperty("ask_price", out var ap) ? ParseDecimal(ap) : stake;
        return await BuyContractAsync(proposalId, askPrice, ct);
    }

    public async Task SubscribeOpenContractAsync(long contractId, CancellationToken ct = default)
    {
        if (_openContractSubIds.ContainsKey(contractId))
        {
            AppLogger.Info(Src, $"Already subscribed to open contract: {contractId}");
            return;
        }

        var reqId = Interlocked.Increment(ref _reqId);
        var payload = JsonSerializer.Serialize(new
        {
            proposal_open_contract = 1,
            contract_id = contractId,
            subscribe = 1,
            req_id = reqId
        });

        var root = await SendAndWaitAsync(reqId, payload, ct);

        if (root.TryGetProperty("error", out var err))
        {
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() ?? "unknown" : "unknown";
            AppLogger.Error(Src, $"proposal_open_contract error: {msg}");
            return;
        }

        if (root.TryGetProperty("subscription", out var sub) &&
            sub.TryGetProperty("id", out var idEl))
        {
            var subId = idEl.GetString() ?? "";
            _openContractSubIds[contractId] = subId;
        }

        if (root.TryGetProperty("proposal_open_contract", out var pocEl))
        {
            var update = ParseOpenContractUpdate(root, pocEl);
            if (update != null)
                OpenContractUpdated?.Invoke(this, update);
        }

        AppLogger.Info(Src, $"Subscribed to open contract: {contractId}");
    }

    public async Task UnsubscribeOpenContractAsync(long contractId, CancellationToken ct = default)
    {
        if (!_openContractSubIds.TryRemove(contractId, out var subId)) return;

        var reqId = Interlocked.Increment(ref _reqId);
        var payload = JsonSerializer.Serialize(new { forget = subId, req_id = reqId });

        try
        {
            await SendAndWaitAsync(reqId, payload, ct);
            AppLogger.Info(Src, $"Unsubscribed open contract: {contractId}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"Unsubscribe open contract error: {ex.Message}");
        }
    }

    public async Task<SellResponse> SellContractAsync(long contractId, CancellationToken ct = default)
    {
        var reqId = Interlocked.Increment(ref _reqId);
        var payload = JsonSerializer.Serialize(new
        {
            sell = contractId,
            price = 0,
            req_id = reqId
        });

        var root = await SendAndWaitAsync(reqId, payload, ct);

        if (root.TryGetProperty("error", out var err))
        {
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() ?? "unknown" : "unknown";
            AppLogger.Error(Src, $"sell error: {msg}");
            return new SellResponse { Error = msg };
        }

        if (root.TryGetProperty("sell", out var sellEl))
        {
            var response = new SellResponse
            {
                ContractId = contractId,
                SoldFor = sellEl.TryGetProperty("sold_for", out var sf) ? ParseDecimal(sf) : 0m
            };
            AppLogger.Info(Src, $"Sell success: contract_id={contractId} sold_for={response.SoldFor}");
            return response;
        }

        return new SellResponse { ContractId = contractId, Error = "Unexpected response" };
    }

    private static OpenContractUpdate? ParseOpenContractUpdate(JsonElement root, JsonElement pocEl)
    {
        var subId = "";
        if (root.TryGetProperty("subscription", out var sub) &&
            sub.TryGetProperty("id", out var idEl))
            subId = idEl.GetString() ?? "";

        var exitSpotRaw = pocEl.TryGetProperty("exit_tick_display_value", out var etd) ? etd.GetString() ?? ""
                        : pocEl.TryGetProperty("exit_tick", out var et) ? et.GetRawText().Trim('"') : "";

        return new OpenContractUpdate
        {
            ContractId = pocEl.TryGetProperty("contract_id", out var cid) ? cid.GetInt64() : 0,
            Symbol = pocEl.TryGetProperty("underlying", out var sym) ? sym.GetString() ?? "" : "",
            ContractType = pocEl.TryGetProperty("contract_type", out var ct) ? ct.GetString() ?? "" : "",
            BuyPrice = pocEl.TryGetProperty("buy_price", out var bp) ? ParseDecimal(bp) : 0m,
            BidPrice = pocEl.TryGetProperty("bid_price", out var bid) ? ParseDecimal(bid) : 0m,
            CurrentSpot = pocEl.TryGetProperty("current_spot", out var cs) ? ParseDecimal(cs) : 0m,
            EntrySpot = pocEl.TryGetProperty("entry_spot", out var es) ? ParseDecimal(es) : 0m,
            EntrySpotRaw = pocEl.TryGetProperty("entry_spot_display_value", out var esd) ? esd.GetString() ?? ""
                         : pocEl.TryGetProperty("entry_spot", out var esRaw) ? esRaw.GetRawText().Trim('"') : "",
            ExitSpotRaw = exitSpotRaw,
            Profit = pocEl.TryGetProperty("profit", out var pf) ? ParseDecimal(pf) : 0m,
            DateStart = pocEl.TryGetProperty("date_start", out var ds) ? ds.GetInt64() : 0,
            DateExpiry = pocEl.TryGetProperty("date_expiry", out var de) ? de.GetInt64() : 0,
            EntryTickTime = pocEl.TryGetProperty("entry_tick_time", out var ett) ? ett.GetInt64() : 0,
            IsExpired = pocEl.TryGetProperty("is_expired", out var ie) && (ie.ValueKind == JsonValueKind.True || (ie.ValueKind == JsonValueKind.Number && ie.GetInt32() == 1)),
            IsSold = pocEl.TryGetProperty("is_sold", out var isl) && (isl.ValueKind == JsonValueKind.True || (isl.ValueKind == JsonValueKind.Number && isl.GetInt32() == 1)),
            IsValidToSell = pocEl.TryGetProperty("is_valid_to_sell", out var ivs) && (ivs.ValueKind == JsonValueKind.True || (ivs.ValueKind == JsonValueKind.Number && ivs.GetInt32() == 1)),
            Status = pocEl.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
            SubscriptionId = subId
        };
    }

    public async Task<(string EntrySpot, string ExitSpot)> GetContractSpotsAsync(long contractId, CancellationToken ct = default)
    {
        var reqId = Interlocked.Increment(ref _reqId);
        var payload = JsonSerializer.Serialize(new
        {
            proposal_open_contract = 1,
            contract_id = contractId,
            req_id = reqId
        });

        try
        {
            var root = await SendAndWaitAsync(reqId, payload, ct);
            if (root.TryGetProperty("proposal_open_contract", out var poc))
            {
                var entry = poc.TryGetProperty("entry_spot_display_value", out var esd) ? esd.GetString() ?? ""
                          : poc.TryGetProperty("entry_spot", out var es2) ? es2.GetRawText().Trim('"') : "";
                var exit = poc.TryGetProperty("exit_tick_display_value", out var etd) ? etd.GetString() ?? ""
                         : poc.TryGetProperty("exit_tick", out var et2) ? et2.GetRawText().Trim('"')
                         : poc.TryGetProperty("current_spot_display_value", out var csd) ? csd.GetString() ?? "" : "";
                return (entry, exit);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"GetContractSpots error for {contractId}: {ex.Message}");
        }
        return ("", "");
    }

    public async Task<OpenContractUpdate?> GetContractStatusAsync(long contractId, CancellationToken ct = default)
    {
        var reqId = Interlocked.Increment(ref _reqId);
        var payload = JsonSerializer.Serialize(new
        {
            proposal_open_contract = 1,
            contract_id = contractId,
            req_id = reqId
        });

        try
        {
            var root = await SendAndWaitAsync(reqId, payload, ct);
            if (root.TryGetProperty("proposal_open_contract", out var pocEl))
                return ParseOpenContractUpdate(root, pocEl);
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Src, $"GetContractStatus error for {contractId}: {ex.Message}");
        }
        return null;
    }

    public async Task<List<ProfitTableEntry>> GetProfitTableAsync(int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        var reqId = Interlocked.Increment(ref _reqId);
        var payload = JsonSerializer.Serialize(new
        {
            profit_table = 1,
            description = 1,
            limit,
            offset,
            sort = "DESC",
            req_id = reqId
        });

        var root = await SendAndWaitAsync(reqId, payload, ct);

        if (root.TryGetProperty("error", out var err))
        {
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() ?? "unknown" : "unknown";
            AppLogger.Error(Src, $"profit_table error: {msg}");
            return [];
        }

        var results = new List<ProfitTableEntry>();
        if (root.TryGetProperty("profit_table", out var pt) &&
            pt.TryGetProperty("transactions", out var txns) &&
            txns.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in txns.EnumerateArray())
            {
                results.Add(new ProfitTableEntry
                {
                    TransactionId = t.TryGetProperty("transaction_id", out var tid) ? tid.GetInt64() : 0,
                    ContractId = t.TryGetProperty("contract_id", out var cid) ? cid.GetInt64() : 0,
                    ContractType = t.TryGetProperty("contract_type", out var ctype) ? ctype.GetString() ?? "" : "",
                    Shortcode = t.TryGetProperty("shortcode", out var sc) ? sc.GetString() ?? "" : "",
                    Longcode = t.TryGetProperty("longcode", out var lc) ? lc.GetString() ?? "" : "",
                    BuyPrice = t.TryGetProperty("buy_price", out var bp) ? ParseDecimal(bp) : 0m,
                    SellPrice = t.TryGetProperty("sell_price", out var sp) ? ParseDecimal(sp) : 0m,
                    PurchaseTime = t.TryGetProperty("purchase_time", out var ptm) ? ptm.GetInt64() : 0,
                    SellTime = t.TryGetProperty("sell_time", out var stm) ? stm.GetInt64() : 0,
                    ProfitLoss = t.TryGetProperty("sell_price", out var sp2) && t.TryGetProperty("buy_price", out var bp2)
                        ? ParseDecimal(sp2) - ParseDecimal(bp2) : 0m,
                    EntrySpot = ParseSpotField(t, "entry_spot", "entry_tick", "entry_tick_display_value"),
                    ExitSpot = ParseSpotField(t, "sell_spot", "exit_tick", "sell_spot_display_value", "exit_tick_display_value")
                });
            }
        }

        AppLogger.Info(Src, $"profit_table: {results.Count} entries loaded");
        return results;
    }

    public void Dispose()
    {
        _ws.MessageReceived -= OnMessage;
        _activeSubIds.Clear();
        _subIdToContractType.Clear();
        _openContractSubIds.Clear();
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var tcs))
                tcs.TrySetCanceled();
        }
        AppLogger.Info(Src, "ContractService disposed");
    }
}
