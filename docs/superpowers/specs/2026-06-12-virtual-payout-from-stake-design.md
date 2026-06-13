# Payout virtual em função do Stake

## Problema

O lucro exibido nas entradas virtuais não reflete o payout real da API aplicado
ao valor do campo Stake. O cálculo está espalhado por três lugares com
comportamentos divergentes:

- **Entrada manual** (`ContractPanelViewModel.GetVirtualWinProfit`): usa
  `payout - stake` — correto.
- **Entrada automática** (`StrategyExecutor.GetVirtualWinProfit`): usa
  `stake * (payout / proposalStake - 1)` com fallback `stake * 0.5`.
- **Atualização ao vivo na lista** (`OpenPositionsViewModel.UpdateVirtualPosition`):
  ignora payout e Stake, usando lucro fixo de 50%:
  `item.Profit = won ? item.BuyPrice * 0.5m : -item.BuyPrice`.

O `VirtualPositionOpened` já carrega um `WinProfit` calculado corretamente, mas
`AddVirtualPositionAsync` descarta esse valor, então a lista cai no 50% fixo.

## Objetivo

O lucro das entradas virtuais deve refletir o payout real da API aplicado ao
valor do campo Stake, de forma consistente em todos os pontos.

A tabela de Posições Abertas deve mostrar esse retorno na coluna **Lucro/Perda**
(e no **Valor** que a acompanha), em vez do lucro fixo de 50%. Nenhuma coluna
nova é adicionada à interface — o campo `WinProfit` introduzido no
`OpenPositionItem` é apenas interno, carregando o valor do payout (já presente no
evento `VirtualPositionOpened`) até a coluna Lucro/Perda existente.

## Fórmula única

```
winProfit = payout - stake
```

O `payout` da API é o retorno bruto total do contrato; subtraindo o `stake`
obtém-se o lucro líquido. Em caso de vitória, o lucro exibido é `winProfit`; em
caso de derrota, a perda é `-stake` (o `BuyPrice` do item).

## Fallback (payout indisponível)

Quando o payout da API ainda não estiver pronto (proposta não carregada),
`winProfit = 0` — lucro indefinido. Isso substitui o atual fallback `stake * 0.5m`
em ambos os pontos de cálculo (`StrategyExecutor` e `ContractPanelViewModel`).

## Mudanças

1. **`OpenPositionItem`** — adicionar propriedade interna `WinProfit` (lucro alvo
   definido na abertura). Não é uma coluna nova na UI; serve apenas para a coluna
   Lucro/Perda existente usar esse valor em vez do 50% fixo.

2. **`OpenPositionsViewModel.AddVirtualPositionAsync`** — receber `winProfit` como
   parâmetro e armazená-lo no item.

3. **`OpenPositionsViewModel.UpdateVirtualPosition`** — substituir
   `item.BuyPrice * 0.5m` por `item.WinProfit`:
   ```csharp
   item.Profit = won ? item.WinProfit : -item.BuyPrice;
   item.CurrentValue = Math.Max(0m, item.BuyPrice + item.Profit);
   ```

4. **Chamadores** — `StrategyViewModel.OnVirtualPositionOpened` e
   `ContractPanelViewModel` repassam o `WinProfit` (já presente no evento/request)
   para `AddVirtualPositionAsync`.

5. **Fallback** — `GetVirtualWinProfit` em `StrategyExecutor` e
   `ContractPanelViewModel` retorna `0` quando o payout não está disponível, em vez
   de `stake * 0.5m`.

## Testes

Arquivos existentes: `Excalibur5.Tests/SimulatorTests.cs`,
`Excalibur5.Tests/EntryModeTests.cs`.

Casos a cobrir:
- Cálculo de lucro com payout válido (`payout - stake`).
- Fallback zero quando não há payout disponível.
- Propagação do `WinProfit` da abertura até o `OpenPositionItem` e seu uso em
  `UpdateVirtualPosition`.

## Fora de escopo

- Alteração do payout real de contratos reais.
- Mudança na lógica de sequência/tolerância das entradas virtuais.
- Refatoração não relacionada ao cálculo de lucro virtual.
