# Design: TP/SL Dinâmicos na Recuperação

## Problema

Na estratégia Multi-Indicadores, o usuário define SL e TP fixos em USD. Quando a recuperação está ativa (déficit > 0), o stake aumenta para recuperar perdas, mas o TP fixo limita o ganho por trade — tornando a recuperação matematicamente inviável em certos cenários. O SL fixo também não acompanha o risco proporcional do stake elevado.

## Decisões

1. **TP dinâmico durante recuperação:** TP = `stake * avgPayoutRatio` (lucro esperado real baseado no histórico de payout)
2. **SL proporcional ao stake:** SL = `baseStopLoss * (currentStake / baseStake)` — mantém o mesmo perfil de risco percentual
3. **Sem déficit:** Volta aos valores fixos de SL/TP definidos pelo usuário
4. **Abordagem:** Métodos na interface `IRecoverStrategy` (Abordagem 1 — responsabilidade coesa na strategy)

## Arquitetura

### Interface `IRecoverStrategy` — novos métodos

```csharp
decimal GetDynamicTakeProfit(RecoverContext context);
decimal GetDynamicStopLoss(RecoverContext context);
```

Ambos com semântica: retornam o valor efetivo de TP/SL. Quando não há recuperação ativa, retornam os valores base do contexto.

### `RecoverContext` — campos adicionais

```csharp
public sealed record RecoverContext(
    decimal BaseStake,
    decimal BaseTakeProfit,
    decimal BaseStopLoss,
    decimal CurrentStake);
```

`CurrentStake` é o stake efetivamente usado no trade (resultado de `GetNextStake`). O executor passa esse valor para que `GetDynamicTakeProfit` e `GetDynamicStopLoss` usem o stake real, sem recalcular.

### `DeficitRecoverStrategy`

- `GetDynamicTakeProfit`:
  - Se `_deficit <= 0`: retorna `context.BaseTakeProfit`
  - Se `_deficit > 0`: retorna `context.CurrentStake * avgPayoutRatio`
    - `avgPayoutRatio` = média móvel dos últimos 5 payouts (já implementado)

- `GetDynamicStopLoss`:
  - Se `_deficit <= 0`: retorna `context.BaseStopLoss`
  - Se `_deficit > 0`: retorna `context.BaseStopLoss * (context.CurrentStake / context.BaseStake)`

### `MartingaleRecoverStrategy`

- `GetDynamicTakeProfit`:
  - Se `_level == 0`: retorna `context.BaseTakeProfit`
  - Se `_level > 0`: retorna `context.CurrentStake * avgPayoutRatio`
    - Requer adicionar tracking de payout ratio (mesmo mecanismo circular buffer do Deficit)

- `GetDynamicStopLoss`:
  - Se `_level == 0`: retorna `context.BaseStopLoss`
  - Se `_level > 0`: retorna `context.BaseStopLoss * (context.CurrentStake / context.BaseStake)`

### `StrategyExecutor` — mudanças

Substituir referências diretas a `_config.TakeProfitUsd` e `_config.StopLossUsd` por consulta à strategy:

```csharp
private decimal GetEffectiveTakeProfit()
{
    if (_recoverStrategy == null)
        return _config.TakeProfitUsd;

    var stake = GetCurrentStake();
    var context = new RecoverContext(_config.Stake, _config.TakeProfitUsd, _config.StopLossUsd, stake);
    return _recoverStrategy.GetDynamicTakeProfit(context);
}

private decimal GetEffectiveStopLoss()
{
    if (_recoverStrategy == null)
        return _config.StopLossUsd;

    var stake = GetCurrentStake();
    var context = new RecoverContext(_config.Stake, _config.TakeProfitUsd, _config.StopLossUsd, stake);
    return _recoverStrategy.GetDynamicStopLoss(context);
}
```

Pontos de uso no executor:
- **TP check** (linha ~405): `update.Profit >= GetEffectiveTakeProfit()`
- **SL inicial** (linha ~317): `DynamicStopLoss = -GetEffectiveStopLoss()`
- **Time-based SL** (linha ~419): `timeSl = -(GetEffectiveStopLoss() * timeRatio)`
- **Trailing stop** (linhas ~385-399): referencia `GetEffectiveTakeProfit()` em vez de `_config.TakeProfitUsd`

### Trailing Stop com TP dinâmico

O trailing stop continua funcionando normalmente, mas usa o TP dinâmico como referência:
- Move SL para breakeven quando profit >= 70% do TP dinâmico
- Move SL para 50% do TP dinâmico quando profit >= 90% do TP dinâmico

Isso garante que durante recuperação, o trailing dá mais espaço ao trade (TP maior = thresholds maiores).

## Fluxo de dados

```
Usuário define: Stake=10, TP=5, SL=3
                    ↓
Trade perde → déficit = 10 (ex: 2 perdas de $5)
                    ↓
DeficitRecoverStrategy.GetNextStake() → stake = 20
DeficitRecoverStrategy.GetDynamicTakeProfit() → 20 * 0.5 = 10 (avgPayout=50%)
DeficitRecoverStrategy.GetDynamicStopLoss() → 3 * (20/10) = 6
                    ↓
Trade ganha $10 → déficit = 0
                    ↓
Volta a: stake=10, TP=5, SL=3
```

## Arquivos afetados

1. `Services/Strategy/Recovery/IRecoverStrategy.cs` — novos métodos
2. `Services/Strategy/Recovery/RecoverContext.cs` — novos campos
3. `Services/Strategy/Recovery/DeficitRecoverStrategy.cs` — implementação TP/SL dinâmicos
4. `Services/Strategy/Recovery/MartingaleRecoverStrategy.cs` — implementação TP/SL dinâmicos + payout tracking
5. `Services/Strategy/StrategyExecutor.cs` — usar métodos dinâmicos em vez de config direta

## Riscos e mitigações

- **TP muito alto impossibilita venda:** O TP dinâmico é baseado no payout real — se o mercado paga 50%, o TP será o lucro esperado, não um valor inalcançável.
- **SL muito alto durante recuperação:** É proporcional, então o risco percentual é o mesmo. O `DeficitMaxStake` já limita o stake máximo.
- **Payout ratio sem histórico:** Default de 0.5 (50%) já existe no `DeficitRecoverStrategy`, usado até ter dados reais.
