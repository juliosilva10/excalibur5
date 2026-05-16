# Dynamic TP/SL Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make TP and SL dynamic during recovery — TP based on expected payout, SL proportional to stake — so recovery is mathematically viable.

**Architecture:** Extend `IRecoverStrategy` with `GetDynamicTakeProfit` and `GetDynamicStopLoss` methods. `RecoverContext` gains `BaseTakeProfit`, `BaseStopLoss`, and `CurrentStake` fields. The `StrategyExecutor` queries the strategy for effective TP/SL instead of using config values directly.

**Tech Stack:** C# / .NET 9 / WPF / CommunityToolkit.Mvvm

---

### Task 1: Expand `RecoverContext` record

**Files:**
- Modify: `Services/Strategy/Recovery/RecoverContext.cs`

- [ ] **Step 1: Update the record to include new fields**

```csharp
namespace Excalibur5.Services.Strategy.Recovery;

public sealed record RecoverContext(
    decimal BaseStake,
    decimal BaseTakeProfit,
    decimal BaseStopLoss,
    decimal CurrentStake);
```

- [ ] **Step 2: Build to verify no compilation errors**

Run: `dotnet build`
Expected: Build succeeded. Errors in `StrategyExecutor.cs` and `GetCurrentStake` call sites are expected (they pass only `BaseStake` currently) — we fix those in Task 5.

- [ ] **Step 3: Fix existing `RecoverContext` usages to compile**

In `Services/Strategy/StrategyExecutor.cs`, update `GetCurrentStake`:

```csharp
private decimal GetCurrentStake()
{
    if (_recoverStrategy == null)
        return _config.Stake;

    var context = new RecoverContext(_config.Stake, _config.TakeProfitUsd, _config.StopLossUsd, _config.Stake);
    return _recoverStrategy.GetNextStake(context);
}
```

- [ ] **Step 4: Build to verify compilation passes**

Run: `dotnet build`
Expected: Build succeeded (0 errors).

- [ ] **Step 5: Commit**

```bash
git add Services/Strategy/Recovery/RecoverContext.cs Services/Strategy/StrategyExecutor.cs
git commit -m "refactor: expand RecoverContext with BaseTakeProfit, BaseStopLoss, CurrentStake"
```

---

### Task 2: Add new methods to `IRecoverStrategy`

**Files:**
- Modify: `Services/Strategy/Recovery/IRecoverStrategy.cs`

- [ ] **Step 1: Add the two new methods with default implementations**

```csharp
namespace Excalibur5.Services.Strategy.Recovery;

public interface IRecoverStrategy
{
    decimal GetNextStake(RecoverContext context);
    decimal GetDynamicTakeProfit(RecoverContext context);
    decimal GetDynamicStopLoss(RecoverContext context);
    void RecordResult(decimal profit, decimal stakeUsed);
    void Reset();
}
```

- [ ] **Step 2: Add stub implementations to `DeficitRecoverStrategy`**

In `Services/Strategy/Recovery/DeficitRecoverStrategy.cs`, add:

```csharp
public decimal GetDynamicTakeProfit(RecoverContext context)
{
    return context.BaseTakeProfit;
}

public decimal GetDynamicStopLoss(RecoverContext context)
{
    return context.BaseStopLoss;
}
```

- [ ] **Step 3: Add stub implementations to `MartingaleRecoverStrategy`**

In `Services/Strategy/Recovery/MartingaleRecoverStrategy.cs`, add:

```csharp
public decimal GetDynamicTakeProfit(RecoverContext context)
{
    return context.BaseTakeProfit;
}

public decimal GetDynamicStopLoss(RecoverContext context)
{
    return context.BaseStopLoss;
}
```

- [ ] **Step 4: Build to verify compilation passes**

Run: `dotnet build`
Expected: Build succeeded (0 errors).

- [ ] **Step 5: Commit**

```bash
git add Services/Strategy/Recovery/IRecoverStrategy.cs Services/Strategy/Recovery/DeficitRecoverStrategy.cs Services/Strategy/Recovery/MartingaleRecoverStrategy.cs
git commit -m "feat: add GetDynamicTakeProfit and GetDynamicStopLoss to IRecoverStrategy"
```

---

### Task 3: Implement dynamic TP/SL in `DeficitRecoverStrategy`

**Files:**
- Modify: `Services/Strategy/Recovery/DeficitRecoverStrategy.cs`

- [ ] **Step 1: Implement `GetDynamicTakeProfit` with deficit logic**

Replace the stub:

```csharp
public decimal GetDynamicTakeProfit(RecoverContext context)
{
    if (_deficit <= 0)
        return context.BaseTakeProfit;

    var avgRatio = GetAveragePayoutRatio();
    return Math.Round(context.CurrentStake * avgRatio, 2);
}
```

- [ ] **Step 2: Implement `GetDynamicStopLoss` with proportional scaling**

Replace the stub:

```csharp
public decimal GetDynamicStopLoss(RecoverContext context)
{
    if (_deficit <= 0)
        return context.BaseStopLoss;

    if (context.BaseStake <= 0)
        return context.BaseStopLoss;

    var ratio = context.CurrentStake / context.BaseStake;
    return Math.Round(context.BaseStopLoss * ratio, 2);
}
```

- [ ] **Step 3: Build to verify compilation passes**

Run: `dotnet build`
Expected: Build succeeded (0 errors).

- [ ] **Step 4: Commit**

```bash
git add Services/Strategy/Recovery/DeficitRecoverStrategy.cs
git commit -m "feat: implement dynamic TP/SL in DeficitRecoverStrategy"
```

---

### Task 4: Implement dynamic TP/SL in `MartingaleRecoverStrategy`

**Files:**
- Modify: `Services/Strategy/Recovery/MartingaleRecoverStrategy.cs`

- [ ] **Step 1: Add payout ratio tracking (circular buffer)**

Add fields and helper methods to `MartingaleRecoverStrategy`:

```csharp
private const int PayoutHistorySize = 5;
private const decimal DefaultPayoutRatio = 0.5m;
private readonly decimal[] _payoutRatios = new decimal[PayoutHistorySize];
private int _payoutIndex;
private int _payoutCount;
```

Add private helper:

```csharp
private void RecordPayoutRatio(decimal ratio)
{
    _payoutRatios[_payoutIndex] = ratio;
    _payoutIndex = (_payoutIndex + 1) % PayoutHistorySize;
    if (_payoutCount < PayoutHistorySize)
        _payoutCount++;
}

private decimal GetAveragePayoutRatio()
{
    if (_payoutCount == 0)
        return DefaultPayoutRatio;

    decimal sum = 0;
    for (int i = 0; i < _payoutCount; i++)
        sum += _payoutRatios[i];
    return sum / _payoutCount;
}
```

- [ ] **Step 2: Update `RecordResult` to track payout ratios**

```csharp
public void RecordResult(decimal profit, decimal stakeUsed)
{
    if (profit >= 0)
    {
        if (stakeUsed > 0)
            RecordPayoutRatio(profit / stakeUsed);
        _level = 0;
    }
    else if (_level < _maxLevel)
        _level++;
    else
        _level = 0;
}
```

- [ ] **Step 3: Implement `GetDynamicTakeProfit` with level logic**

Replace the stub:

```csharp
public decimal GetDynamicTakeProfit(RecoverContext context)
{
    if (_level <= 0)
        return context.BaseTakeProfit;

    var avgRatio = GetAveragePayoutRatio();
    return Math.Round(context.CurrentStake * avgRatio, 2);
}
```

- [ ] **Step 4: Implement `GetDynamicStopLoss` with proportional scaling**

Replace the stub:

```csharp
public decimal GetDynamicStopLoss(RecoverContext context)
{
    if (_level <= 0)
        return context.BaseStopLoss;

    if (context.BaseStake <= 0)
        return context.BaseStopLoss;

    var ratio = context.CurrentStake / context.BaseStake;
    return Math.Round(context.BaseStopLoss * ratio, 2);
}
```

- [ ] **Step 5: Update `Reset` to clear payout state**

```csharp
public void Reset()
{
    _level = 0;
    _payoutCount = 0;
    _payoutIndex = 0;
}
```

- [ ] **Step 6: Build to verify compilation passes**

Run: `dotnet build`
Expected: Build succeeded (0 errors).

- [ ] **Step 7: Commit**

```bash
git add Services/Strategy/Recovery/MartingaleRecoverStrategy.cs
git commit -m "feat: implement dynamic TP/SL in MartingaleRecoverStrategy with payout tracking"
```

---

### Task 5: Update `StrategyExecutor` to use dynamic TP/SL

**Files:**
- Modify: `Services/Strategy/StrategyExecutor.cs`

- [ ] **Step 1: Add `GetEffectiveTakeProfit` helper method**

Add after the existing `GetCurrentStake` method (around line 484):

```csharp
private decimal GetEffectiveTakeProfit()
{
    if (_recoverStrategy == null)
        return _config.TakeProfitUsd;

    var stake = GetCurrentStake();
    var context = new RecoverContext(_config.Stake, _config.TakeProfitUsd, _config.StopLossUsd, stake);
    return _recoverStrategy.GetDynamicTakeProfit(context);
}
```

- [ ] **Step 2: Add `GetEffectiveStopLoss` helper method**

Add after `GetEffectiveTakeProfit`:

```csharp
private decimal GetEffectiveStopLoss()
{
    if (_recoverStrategy == null)
        return _config.StopLossUsd;

    var stake = GetCurrentStake();
    var context = new RecoverContext(_config.Stake, _config.TakeProfitUsd, _config.StopLossUsd, stake);
    return _recoverStrategy.GetDynamicStopLoss(context);
}
```

- [ ] **Step 3: Update SL initialization when opening a position**

In `OnSignalGenerated`, change the `TrackedPosition` initialization (around line 317):

From:
```csharp
DynamicStopLoss = -_config.StopLossUsd,
```

To:
```csharp
DynamicStopLoss = -GetEffectiveStopLoss(),
```

- [ ] **Step 4: Update trailing stop logic to use dynamic TP**

In `OnOpenContractUpdated`, replace the trailing stop section (around lines 382-401):

From:
```csharp
if (_config.EnableTrailingStop && update.Profit > 0)
{
    decimal tp = _config.TakeProfitUsd;
```

To:
```csharp
if (_config.EnableTrailingStop && update.Profit > 0)
{
    decimal tp = GetEffectiveTakeProfit();
```

- [ ] **Step 5: Update TP check to use dynamic value**

In `OnOpenContractUpdated` (around line 405):

From:
```csharp
if (update.Profit >= _config.TakeProfitUsd)
{
    if (!update.IsValidToSell) return;
    tracked.IsSelling = true;
    AppLogger.Info(Src, $"TP hit for {update.ContractId}: profit={update.Profit:F2} >= {_config.TakeProfitUsd}");
```

To:
```csharp
var effectiveTp = GetEffectiveTakeProfit();
if (update.Profit >= effectiveTp)
{
    if (!update.IsValidToSell) return;
    tracked.IsSelling = true;
    AppLogger.Info(Src, $"TP hit for {update.ContractId}: profit={update.Profit:F2} >= {effectiveTp:F2}");
```

- [ ] **Step 6: Update time-based SL to use dynamic value**

In `OnOpenContractUpdated` (around line 419):

From:
```csharp
var timeSl = -(_config.StopLossUsd * timeRatio);
```

To:
```csharp
var timeSl = -(GetEffectiveStopLoss() * timeRatio);
```

- [ ] **Step 7: Build to verify compilation passes**

Run: `dotnet build`
Expected: Build succeeded (0 errors).

- [ ] **Step 8: Commit**

```bash
git add Services/Strategy/StrategyExecutor.cs
git commit -m "feat: use dynamic TP/SL from recovery strategy in StrategyExecutor"
```

---

### Task 6: Final integration build and verification

**Files:**
- All modified files from Tasks 1-5

- [ ] **Step 1: Clean build**

Run: `dotnet build --no-incremental`
Expected: Build succeeded (0 errors, 0 warnings related to recovery).

- [ ] **Step 2: Verify the full flow logic**

Check that:
1. `RecoverContext` has 4 fields: `BaseStake`, `BaseTakeProfit`, `BaseStopLoss`, `CurrentStake`
2. `IRecoverStrategy` has 5 methods: `GetNextStake`, `GetDynamicTakeProfit`, `GetDynamicStopLoss`, `RecordResult`, `Reset`
3. `DeficitRecoverStrategy.GetDynamicTakeProfit` returns `context.BaseTakeProfit` when `_deficit <= 0`
4. `DeficitRecoverStrategy.GetDynamicTakeProfit` returns `context.CurrentStake * avgPayoutRatio` when `_deficit > 0`
5. `MartingaleRecoverStrategy.GetDynamicTakeProfit` returns `context.BaseTakeProfit` when `_level == 0`
6. `StrategyExecutor` never references `_config.TakeProfitUsd` or `_config.StopLossUsd` directly for trade decisions (only in `GetEffective*` fallback and log at Start)

- [ ] **Step 3: Verify no direct config references remain in trade logic**

Run: `grep -n "TakeProfitUsd\|StopLossUsd" Services/Strategy/StrategyExecutor.cs`

Expected: Only matches in `GetEffectiveTakeProfit`, `GetEffectiveStopLoss`, and the `Start` log line. No matches in `OnOpenContractUpdated` or `OnSignalGenerated` directly.
