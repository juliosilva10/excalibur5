# Deficit Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Deficit Recovery" recover mode to the automated strategy bot that calculates next stake based on accumulated deficit and average payout ratio of recent winning trades.

**Architecture:** Strategy Pattern with `IRecoverStrategy` interface, two implementations (Martingale + DeficitRecovery), and a static factory. The StrategyExecutor delegates stake calculation to the active strategy.

**Tech Stack:** C# / .NET 9 / WPF / CommunityToolkit.Mvvm

---

## File Structure

```
Services/Strategy/Recovery/
├── IRecoverStrategy.cs           — interface
├── RecoverContext.cs              — input record
├── MartingaleRecoverStrategy.cs  — existing logic extracted
├── DeficitRecoverStrategy.cs     — new deficit-based logic
└── RecoverStrategyFactory.cs     — factory
```

**Modified files:**
- `Models/Strategy/StrategyConfig.cs` — new fields
- `Config/BotStateStore.cs` — persistence fields
- `Services/Strategy/StrategyExecutor.cs` — delegate to IRecoverStrategy
- `ViewModels/StrategyViewModel.cs` — new properties + ComboBox item
- `Views/Controls/StrategyPanelView.xaml` — conditional UI fields

---

### Task 1: Create IRecoverStrategy and RecoverContext

**Files:**
- Create: `Services/Strategy/Recovery/IRecoverStrategy.cs`
- Create: `Services/Strategy/Recovery/RecoverContext.cs`

- [ ] **Step 1: Create the interface**

```csharp
namespace Excalibur5.Services.Strategy.Recovery;

public interface IRecoverStrategy
{
    decimal GetNextStake(RecoverContext context);
    void RecordResult(decimal profit, decimal stakeUsed);
    void Reset();
}
```

- [ ] **Step 2: Create the context record**

```csharp
namespace Excalibur5.Services.Strategy.Recovery;

public sealed record RecoverContext(decimal BaseStake);
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add Services/Strategy/Recovery/IRecoverStrategy.cs Services/Strategy/Recovery/RecoverContext.cs
git commit -m "feat: add IRecoverStrategy interface and RecoverContext"
```

---

### Task 2: Implement MartingaleRecoverStrategy

**Files:**
- Create: `Services/Strategy/Recovery/MartingaleRecoverStrategy.cs`

- [ ] **Step 1: Implement the class**

```csharp
namespace Excalibur5.Services.Strategy.Recovery;

public sealed class MartingaleRecoverStrategy : IRecoverStrategy
{
    private readonly decimal _factor;
    private readonly int _maxLevel;
    private int _level;

    public MartingaleRecoverStrategy(decimal factor, int maxLevel)
    {
        _factor = factor;
        _maxLevel = maxLevel;
    }

    public decimal GetNextStake(RecoverContext context)
    {
        if (_level <= 0)
            return context.BaseStake;

        var stake = context.BaseStake;
        for (int i = 0; i < _level; i++)
            stake *= _factor;
        return Math.Round(stake, 2);
    }

    public void RecordResult(decimal profit, decimal stakeUsed)
    {
        if (profit >= 0)
            _level = 0;
        else if (_level < _maxLevel)
            _level++;
        else
            _level = 0;
    }

    public void Reset() => _level = 0;
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add Services/Strategy/Recovery/MartingaleRecoverStrategy.cs
git commit -m "feat: extract MartingaleRecoverStrategy from StrategyExecutor"
```

---

### Task 3: Implement DeficitRecoverStrategy

**Files:**
- Create: `Services/Strategy/Recovery/DeficitRecoverStrategy.cs`

- [ ] **Step 1: Implement the class**

```csharp
namespace Excalibur5.Services.Strategy.Recovery;

public sealed class DeficitRecoverStrategy : IRecoverStrategy
{
    private const int PayoutHistorySize = 5;
    private const decimal DefaultPayoutRatio = 0.5m;

    private readonly decimal _maxStake;
    private readonly int _recoveryTrades;
    private readonly decimal[] _payoutRatios = new decimal[PayoutHistorySize];
    private int _payoutIndex;
    private int _payoutCount;
    private decimal _deficit;

    public DeficitRecoverStrategy(decimal maxStake, int recoveryTrades)
    {
        _maxStake = maxStake;
        _recoveryTrades = Math.Max(1, recoveryTrades);
    }

    public decimal GetNextStake(RecoverContext context)
    {
        if (_deficit <= 0)
            return context.BaseStake;

        var avgRatio = GetAveragePayoutRatio();
        var needed = _deficit / (avgRatio * _recoveryTrades);
        var stake = Math.Max(needed, context.BaseStake);
        return Math.Round(Math.Min(stake, _maxStake), 2);
    }

    public void RecordResult(decimal profit, decimal stakeUsed)
    {
        if (profit >= 0)
        {
            _deficit = Math.Max(0, _deficit - profit);
            if (stakeUsed > 0)
                RecordPayoutRatio(profit / stakeUsed);
        }
        else
        {
            _deficit += Math.Abs(profit);
        }
    }

    public void Reset()
    {
        _deficit = 0;
        _payoutCount = 0;
        _payoutIndex = 0;
    }

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
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add Services/Strategy/Recovery/DeficitRecoverStrategy.cs
git commit -m "feat: add DeficitRecoverStrategy with payout-ratio-based recovery"
```

---

### Task 4: Create RecoverStrategyFactory and add StrategyConfig fields

**Files:**
- Create: `Services/Strategy/Recovery/RecoverStrategyFactory.cs`
- Modify: `Models/Strategy/StrategyConfig.cs`

- [ ] **Step 1: Create the factory**

```csharp
using Excalibur5.Models.Strategy;

namespace Excalibur5.Services.Strategy.Recovery;

public static class RecoverStrategyFactory
{
    public static IRecoverStrategy? Create(StrategyConfig config)
    {
        return config.RecoverMode switch
        {
            "Martingale" => new MartingaleRecoverStrategy(config.MartingaleFactor, config.MartingaleMaxLevel),
            "Deficit Recovery" => new DeficitRecoverStrategy(config.DeficitMaxStake, config.DeficitRecoveryTrades),
            _ => null
        };
    }
}
```

- [ ] **Step 2: Add new fields to StrategyConfig**

Add after line 15 (`MartingaleMaxLevel`) in `Models/Strategy/StrategyConfig.cs`:

```csharp
public decimal DeficitMaxStake { get; set; } = 50m;
public int DeficitRecoveryTrades { get; set; } = 1;
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add Services/Strategy/Recovery/RecoverStrategyFactory.cs Models/Strategy/StrategyConfig.cs
git commit -m "feat: add RecoverStrategyFactory and deficit config fields"
```

---

### Task 5: Refactor StrategyExecutor to use IRecoverStrategy

**Files:**
- Modify: `Services/Strategy/StrategyExecutor.cs`

- [ ] **Step 1: Add using and field**

Add at top of file:
```csharp
using Excalibur5.Services.Strategy.Recovery;
```

Replace field `private int _martingaleLevel;` (line 21) with:
```csharp
private IRecoverStrategy? _recoverStrategy;
```

- [ ] **Step 2: Initialize in Start()**

In the `Start()` method, replace `_martingaleLevel = 0;` (line 56) with:
```csharp
_recoverStrategy = RecoverStrategyFactory.Create(config);
```

- [ ] **Step 3: Replace RecordResult logic**

Replace the `RecordResult` method (lines 459-481) with:

```csharp
private void RecordResult(TrackedPosition tracked, decimal profit)
{
    var previousStake = GetCurrentStake();

    if (profit >= 0)
        Stats.RecordWin(profit);
    else
        Stats.RecordLoss(profit);

    _recoverStrategy?.RecordResult(profit, tracked.BuyPrice);

    StatsUpdated?.Invoke(this, EventArgs.Empty);

    if (_active && GetCurrentStake() != previousStake)
        _ = SubscribeBotProposalsAsync();
}
```

- [ ] **Step 4: Replace GetCurrentStake**

Replace the `GetCurrentStake` method (lines 483-492) with:

```csharp
private decimal GetCurrentStake()
{
    if (_recoverStrategy == null)
        return _config.Stake;

    var context = new RecoverContext(_config.Stake);
    return _recoverStrategy.GetNextStake(context);
}
```

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add Services/Strategy/StrategyExecutor.cs
git commit -m "refactor: delegate stake recovery to IRecoverStrategy in StrategyExecutor"
```

---

### Task 6: Update StrategyViewModel and BotStateStore

**Files:**
- Modify: `ViewModels/StrategyViewModel.cs`
- Modify: `Config/BotStateStore.cs`

- [ ] **Step 1: Add fields to BotState**

Add after `SampleSizeText` (line 54) in `Config/BotStateStore.cs`:

```csharp
public string DeficitMaxStakeText { get; set; } = "50";
public string DeficitRecoveryTradesText { get; set; } = "1";
```

- [ ] **Step 2: Add properties to StrategyViewModel**

Add after `_sampleSizeText` field (line 48) in `ViewModels/StrategyViewModel.cs`:

```csharp
[ObservableProperty] private string _deficitMaxStakeText = "50";
[ObservableProperty] private string _deficitRecoveryTradesText = "1";
```

- [ ] **Step 3: Add computed property**

Add after `IsTrendMode` (line 54):

```csharp
public bool IsDeficitMode => RecoverMode == "Deficit Recovery";
```

- [ ] **Step 4: Update RecoverModes list**

Change line 52 from:
```csharp
public List<string> RecoverModes { get; } = ["", "Martingale"];
```
to:
```csharp
public List<string> RecoverModes { get; } = ["", "Martingale", "Deficit Recovery"];
```

- [ ] **Step 5: Update OnRecoverModeChanged**

Replace line 178:
```csharp
partial void OnRecoverModeChanged(string value) => SaveState();
```
with:
```csharp
partial void OnRecoverModeChanged(string value)
{
    OnPropertyChanged(nameof(IsDeficitMode));
    SaveState();
}
```

- [ ] **Step 6: Add change handlers for new fields**

Add after `OnSampleSizeTextChanged` (line 185):
```csharp
partial void OnDeficitMaxStakeTextChanged(string value) => SaveState();
partial void OnDeficitRecoveryTradesTextChanged(string value) => SaveState();
```

- [ ] **Step 7: Update RestoreState**

Add after `SampleSizeText = s.SampleSizeText;` in RestoreState:
```csharp
DeficitMaxStakeText = s.DeficitMaxStakeText;
DeficitRecoveryTradesText = s.DeficitRecoveryTradesText;
```

- [ ] **Step 8: Update SaveState**

Add inside the `new BotState { ... }` block in SaveState, after `SampleSizeText = SampleSizeText,`:
```csharp
DeficitMaxStakeText = DeficitMaxStakeText,
DeficitRecoveryTradesText = DeficitRecoveryTradesText,
```

- [ ] **Step 9: Update BuildConfig**

In `BuildConfig()`, add to the `new StrategyConfig { ... }` block after `SampleSize`:
```csharp
DeficitMaxStake = decimal.TryParse(DeficitMaxStakeText, NumberStyles.Any, CultureInfo.InvariantCulture, out var dms) ? dms : 50m,
DeficitRecoveryTrades = int.TryParse(DeficitRecoveryTradesText, out var drt) ? Math.Max(1, drt) : 1
```

- [ ] **Step 10: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 11: Commit**

```bash
git add ViewModels/StrategyViewModel.cs Config/BotStateStore.cs
git commit -m "feat: add Deficit Recovery properties to StrategyViewModel and BotStateStore"
```

---

### Task 7: Update StrategyPanelView.xaml

**Files:**
- Modify: `Views/Controls/StrategyPanelView.xaml`

- [ ] **Step 1: Add BooleanToVisibilityConverter if not present**

Check if `BooleanToVisibilityConverter` is already in resources. If not, add to the UserControl resources:
```xml
<BooleanToVisibilityConverter x:Key="BoolToVis"/>
```

- [ ] **Step 2: Add deficit recovery fields after the Recover ComboBox**

Insert after the closing `</Grid>` of the Recover/MaxContracts row (after line 151):

```xml
<!-- Deficit Recovery Config -->
<Grid Margin="0,8,0,0"
      Visibility="{Binding IsDeficitMode, Converter={StaticResource BoolToVis}}">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="8"/>
        <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>
    <StackPanel Grid.Column="0">
        <TextBlock Text="Stake Máxima (USD)" Foreground="#90b5c9" FontSize="10" Margin="0,0,0,4"/>
        <TextBox Style="{StaticResource DerivTextBox}"
                 Text="{Binding DeficitMaxStakeText, UpdateSourceTrigger=PropertyChanged}"
                 Height="28" MaxLength="8"/>
    </StackPanel>
    <StackPanel Grid.Column="2">
        <TextBlock Text="Trades p/ Recuperar" Foreground="#90b5c9" FontSize="10" Margin="0,0,0,4"/>
        <TextBox Style="{StaticResource DerivTextBox}"
                 Text="{Binding DeficitRecoveryTradesText, UpdateSourceTrigger=PropertyChanged}"
                 Height="28" MaxLength="2"/>
    </StackPanel>
</Grid>
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add Views/Controls/StrategyPanelView.xaml
git commit -m "feat: add Deficit Recovery config fields to StrategyPanelView"
```

---

### Task 8: Final build and verification

- [ ] **Step 1: Clean build**

Run: `dotnet build --no-incremental`
Expected: Build succeeded, 0 warnings related to new code

- [ ] **Step 2: Verify the app launches**

Run: `dotnet run`
Expected: App launches without crash. Navigate to Strategy panel, verify "Deficit Recovery" appears in the Recover ComboBox. Selecting it shows MaxStake and RecoveryTrades fields.

- [ ] **Step 3: Final commit (if any fixups needed)**

```bash
git add -A
git commit -m "fix: address any build issues from deficit recovery integration"
```
