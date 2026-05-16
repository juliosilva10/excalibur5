# Deficit Recovery — Design Spec

## Resumo

Novo modo de recuperação ("Deficit Recovery") para o bot de estratégia automatizada, que calcula a próxima stake com base no déficit acumulado e no payout ratio médio dos últimos trades vencedores, em vez de multiplicar cegamente a stake como o Martingale.

## Motivação

Contratos VANILLALONGCALL/VANILLALONGPUT têm payout variável (depende de quanto o preço se move além do strike). O Martingale clássico (stake × factor^level) não garante recuperação porque o próximo trade pode retornar apenas 30% da stake. A recuperação por déficit adapta a stake ao comportamento real do mercado.

## Arquitetura

### Strategy Pattern — `IRecoverStrategy`

```
Services/Strategy/Recovery/
├── IRecoverStrategy.cs        — interface comum
├── RecoverContext.cs           — record com dados para cálculo
├── MartingaleRecoverStrategy.cs — encapsula lógica existente
├── DeficitRecoverStrategy.cs   — nova lógica por déficit
└── RecoverStrategyFactory.cs   — factory estática
```

### Interface

```csharp
public interface IRecoverStrategy
{
    decimal GetNextStake(RecoverContext context);
    void RecordResult(decimal profit, decimal stakeUsed);
    void Reset();
}
```

### RecoverContext

```csharp
public sealed record RecoverContext(decimal BaseStake);
```

O contexto passa a stake base. Cada implementação mantém seu próprio estado interno (déficit, nível, histórico).

### MartingaleRecoverStrategy

Encapsula a lógica atual sem mudança de comportamento:
- Estado: `_level` (int)
- Parâmetros: `Factor` (decimal), `MaxLevel` (int)
- `RecordResult`: se profit >= 0, reset level; senão, incrementa até MaxLevel
- `GetNextStake`: `baseStake * factor^level`

### DeficitRecoverStrategy

- Estado: `_deficit` (decimal), `_payoutRatios` (circular buffer de 5 posições)
- Parâmetros: `MaxStake` (decimal), `RecoveryTrades` (int, 1=agressivo, N=gradual)
- `RecordResult`:
  - Se perda: `_deficit += |profit|` (profit é negativo, soma o absoluto)
  - Se ganho: `_deficit = max(0, _deficit - profit)`, registra `profit/stakeUsed` no buffer
- `GetNextStake`:
  - Se `_deficit <= 0`: retorna `baseStake`
  - `avgRatio = média do buffer` (default 0.5 se vazio)
  - `needed = _deficit / (avgRatio * recoveryTrades)`
  - Retorna `min(max(needed, baseStake), maxStake)`

### RecoverStrategyFactory

```csharp
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

## Mudanças no StrategyExecutor

1. Remover campo `_martingaleLevel` (int)
2. Remover método inline `GetCurrentStake()` com lógica de martingale
3. Adicionar campo `private IRecoverStrategy? _recoverStrategy`
4. No `Start()`: `_recoverStrategy = RecoverStrategyFactory.Create(config)`
5. No `RecordResult()`: `_recoverStrategy?.RecordResult(profit, tracked.BuyPrice)`
6. Novo `GetCurrentStake()`:
   ```csharp
   private decimal GetCurrentStake()
   {
       if (_recoverStrategy == null)
           return _config.Stake;
       var context = new RecoverContext(_config.Stake);
       return _recoverStrategy.GetNextStake(context);
   }
   ```

## Mudanças no StrategyConfig

Novos campos:
```csharp
public decimal DeficitMaxStake { get; set; } = 50m;
public int DeficitRecoveryTrades { get; set; } = 1;
```

## Mudanças na UI

### StrategyViewModel
- `RecoverModes`: `["", "Martingale", "Deficit Recovery"]`
- Novas propriedades: `DeficitMaxStakeText`, `DeficitRecoveryTradesText`
- Propriedade computada `IsDeficitMode => RecoverMode == "Deficit Recovery"`
- Persistência via BotStateStore (novos campos)

### BotStateStore
- Novos campos: `DeficitMaxStake`, `DeficitRecoveryTrades`

### StrategyPanelView.xaml
- Seção condicional (Visibility bound a `IsDeficitMode`):
  - TextBox "Stake Máxima (USD)"
  - TextBox "Trades p/ Recuperar" (1 = total, N = gradual)

## Comportamento esperado

1. Usuário seleciona "Deficit Recovery" na ComboBox de RecoverMode
2. Configura MaxStake e RecoveryTrades
3. Bot opera normalmente com stake base
4. Ao perder, déficit acumula
5. Próxima stake = déficit / (payoutRatio médio × recoveryTrades), limitada por maxStake
6. Ao ganhar, déficit diminui pelo lucro obtido
7. Quando déficit chega a 0, volta à stake base

## Não-escopo

- Não altera o RecoverViewModel do painel manual (ContractPanel) — apenas o bot automatizado
- Não persiste déficit entre sessões (reseta ao parar/iniciar o bot)
- Não altera a lógica de sinais ou indicadores
