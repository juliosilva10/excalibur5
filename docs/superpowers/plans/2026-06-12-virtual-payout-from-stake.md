# Payout virtual em função do Stake — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fazer a coluna Lucro/Perda (e Valor) das entradas virtuais na tabela de Posições Abertas refletir o payout real da API aplicado ao Stake (`payout − stake`), em vez do lucro fixo de 50%; mostrar `0` quando o payout não estiver disponível.

**Architecture:** O evento `VirtualPositionOpened` já carrega um `WinProfit` calculado. O trabalho restante conecta esse valor até a tabela: adiciona `WinProfit` ao `OpenPositionItem`, propaga-o por `AddVirtualPositionAsync`, e troca o cálculo fixo de 50% em `UpdateVirtualPosition`. Por fim, ajusta os fallbacks de `GetVirtualWinProfit` para `0`.

**Tech Stack:** C# / .NET 10 / WPF / MVVM (CommunityToolkit.Mvvm). Testes: console runner em `Excalibur5.Tests` (sem framework, asserts via `TestAssert`).

---

## Contexto do estado atual

O repositório já contém trabalho **não commitado** que fez parte da tubulação do `WinProfit`:

- `VirtualTradeSimulator.cs`: `VirtualTradeRequest` e `VirtualTradeCompleted` já têm `WinProfit`/`ExitSpot`; `CompleteTrade` já os propaga.
- `StrategyExecutor.cs`: campos `_callPayout`/`_putPayout`, método `GetVirtualWinProfit` (fallback `stake * 0.5m`), `VirtualPositionOpened` com `WinProfit`.
- `ContractPanelViewModel.cs`: `GetVirtualWinProfit` (fallback `stake * 0.5m`), eventos `ManualVirtualTradeOpened/Settled`.
- `HistoryViewModel.cs` / `MainViewModel.cs`: tubulação de histórico virtual.

**O que falta** (escopo deste plano):
1. `OpenPositionItem` não tem `WinProfit`.
2. `OpenPositionsViewModel.AddVirtualPositionAsync` não recebe nem armazena `WinProfit`.
3. `OpenPositionsViewModel.UpdateVirtualPosition` usa `BuyPrice * 0.5m` (50% fixo) em vez de `WinProfit`.
4. Os chamadores (`StrategyViewModel.OnVirtualPositionOpened`, `ContractPanelViewModel.ExecuteVirtualBuyAsync`) não repassam o `WinProfit`.
5. Fallbacks de `GetVirtualWinProfit` retornam `stake * 0.5m` em vez de `0` (decisão: opção 3).
6. `Excalibur5.Tests/SimulatorTests.cs` **não compila** (`CreateRequest` passa 5 args, record exige 6).

> Nota sobre histórico: o trabalho parcial em `HistoryViewModel.UpdateVirtualTradeResult` ainda usa `item.Stake * 0.5m`. Está **fora do escopo** deste plano (o spec trata da tabela de Posições Abertas, não do histórico). A Task 6 apenas observa isso; não o altera.

---

## Estrutura de arquivos

- **Modify:** `ViewModels/OpenPositionItem.cs` — adicionar propriedade `WinProfit`.
- **Modify:** `ViewModels/OpenPositionsViewModel.cs` — `AddVirtualPositionAsync` recebe `winProfit`; `UpdateVirtualPosition` usa `WinProfit`.
- **Modify:** `ViewModels/StrategyViewModel.cs` — repassar `e.WinProfit` em `OnVirtualPositionOpened`.
- **Modify:** `ViewModels/ContractPanelViewModel.cs` — repassar `request.WinProfit` em `ExecuteVirtualBuyAsync`; fallback `0`.
- **Modify:** `Services/Strategy/StrategyExecutor.cs` — fallback `0`.
- **Modify:** `Excalibur5.Tests/SimulatorTests.cs` — corrigir `CreateRequest` (6º arg) + novos testes.

---

## Task 0: Estabelecer baseline verde (corrigir build dos testes)

O projeto de testes não compila por causa do `WinProfit` adicionado ao record. Antes de qualquer mudança, deixe o build/testes verdes corrigindo a assinatura de `CreateRequest`. Isso é pré-requisito para TDD nas tasks seguintes.

**Files:**
- Test: `Excalibur5.Tests/SimulatorTests.cs:65-77`

- [ ] **Step 1: Corrigir `CreateRequest` para passar `WinProfit`**

Em `Excalibur5.Tests/SimulatorTests.cs`, substituir o método `CreateRequest` (linhas 65-77) por uma versão que aceita e repassa `winProfit`:

```csharp
    private static VirtualTradeRequest CreateRequest(
        SignalDirection direction,
        decimal entrySpot,
        int seconds = 60,
        int ticks = 0,
        decimal winProfit = 0m)
    {
        return new VirtualTradeRequest(
            VirtualTradeIdGenerator.Next(),
            new TradeSignal { Direction = direction },
            entrySpot,
            seconds,
            ticks,
            winProfit);
    }
```

- [ ] **Step 2: Compilar os testes para verificar que voltou a compilar**

Run: `export DOTNET_CLI_HOME="$PWD/.dotnet-cli-home" && dotnet build Excalibur5.Tests/Excalibur5.Tests.csproj -v q --nologo`
Expected: `Compilação com êxito` / `0 Erro(s)`

- [ ] **Step 3: Rodar a suíte de testes para confirmar baseline verde**

Run: `export DOTNET_CLI_HOME="$PWD/.dotnet-cli-home" && dotnet run --project Excalibur5.Tests/Excalibur5.Tests.csproj -v q --nologo`
Expected: termina com `12/12 tests passed.` e exit code 0

- [ ] **Step 4: Commit**

```bash
git add Excalibur5.Tests/SimulatorTests.cs
git commit -m "test: corrige CreateRequest para o novo WinProfit do VirtualTradeRequest"
```

---

## Task 1: Adicionar `WinProfit` ao `OpenPositionItem`

**Files:**
- Modify: `ViewModels/OpenPositionItem.cs`

- [ ] **Step 1: Adicionar a propriedade `WinProfit`**

Em `ViewModels/OpenPositionItem.cs`, logo após a declaração de `BuyPrice` (linha 12: `public decimal BuyPrice { get; }`), adicionar uma propriedade settável com inicialização padrão `0`:

```csharp
    public decimal BuyPrice { get; }
    public decimal WinProfit { get; set; }
```

(É interna ao modelo — não há binding novo na UI. Default `0` cobre posições reais, que não a usam.)

- [ ] **Step 2: Compilar o projeto principal para verificar**

Run: `export DOTNET_CLI_HOME="$PWD/.dotnet-cli-home" && dotnet build Excalibur5.csproj -v q --nologo`
Expected: `Compilação com êxito` / `0 Erro(s)`

- [ ] **Step 3: Commit**

```bash
git add ViewModels/OpenPositionItem.cs
git commit -m "feat: adiciona WinProfit interno ao OpenPositionItem"
```

---

## Task 2: `AddVirtualPositionAsync` recebe e armazena `winProfit`

**Files:**
- Modify: `ViewModels/OpenPositionsViewModel.cs:120-147`

- [ ] **Step 1: Adicionar o parâmetro `winProfit` e gravá-lo no item**

Em `ViewModels/OpenPositionsViewModel.cs`, substituir a assinatura e o corpo de `AddVirtualPositionAsync` (linhas 120-147). Adicionar `decimal winProfit` como último parâmetro e setar `WinProfit` no inicializador do objeto:

```csharp
    public Task AddVirtualPositionAsync(
        long tradeId,
        string symbol,
        string displayName,
        string contractType,
        decimal stake,
        decimal entrySpot,
        int durationSeconds,
        decimal winProfit)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var item = new OpenPositionItem(
            tradeId,
            symbol,
            displayName,
            contractType,
            stake,
            now,
            now + durationSeconds,
            durationSeconds,
            isVirtual: true)
        {
            EntrySpot = entrySpot,
            EntrySpotDisplay = MarketPriceFormatter.Format(entrySpot, _pipSize),
            CurrentValue = stake,
            WinProfit = winProfit
        };

        return AddItemAsync(item);
    }
```

- [ ] **Step 2: Compilar — espera-se ERRO nos chamadores (esperado nesta etapa)**

Run: `export DOTNET_CLI_HOME="$PWD/.dotnet-cli-home" && dotnet build Excalibur5.csproj -v q --nologo`
Expected: FALHA com erros CS7036 em `StrategyViewModel.cs` e `ContractPanelViewModel.cs` (chamadas a `AddVirtualPositionAsync` sem o novo argumento). Isso confirma que os dois call sites precisam ser atualizados nas Tasks 3 e 4.

> Não commitar ainda — o build só ficará verde após as Tasks 3 e 4 atualizarem os chamadores.

---

## Task 3: Repassar `WinProfit` no chamador automático (`StrategyViewModel`)

**Files:**
- Modify: `ViewModels/StrategyViewModel.cs:665-672`

- [ ] **Step 1: Passar `e.WinProfit` para `AddVirtualPositionAsync`**

Em `ViewModels/StrategyViewModel.cs`, dentro de `OnVirtualPositionOpened`, a chamada a `AddVirtualPositionAsync` (linhas 665-672) deve passar `e.WinProfit` como último argumento:

```csharp
        _ = _activeMarketTab.ContractPanel.OpenPositions.AddVirtualPositionAsync(
            e.TradeId,
            e.Symbol,
            _activeMarketTab.DisplayName,
            e.ContractType,
            e.Stake,
            e.EntrySpot,
            e.DurationSeconds,
            e.WinProfit);
```

(`VirtualPositionOpened` já tem `WinProfit` — adicionado no trabalho parcial existente.)

- [ ] **Step 2: Não compilar ainda (ContractPanelViewModel ainda quebra). Seguir para Task 4.**

---

## Task 4: Repassar `WinProfit` no chamador manual + fallback `0` (`ContractPanelViewModel`)

**Files:**
- Modify: `ViewModels/ContractPanelViewModel.cs:602-609` (chamada a `AddVirtualPositionAsync`)
- Modify: `ViewModels/ContractPanelViewModel.cs:629-637` (`GetVirtualWinProfit`)

- [ ] **Step 1: Passar `request.WinProfit` para `AddVirtualPositionAsync`**

Em `ViewModels/ContractPanelViewModel.cs`, dentro de `ExecuteVirtualBuyAsync`, a chamada a `OpenPositions.AddVirtualPositionAsync` (linhas 602-609) deve passar `request.WinProfit` como último argumento:

```csharp
        await OpenPositions.AddVirtualPositionAsync(
            tradeId,
            Symbol,
            DisplayName,
            contractType,
            stake,
            _spotForBarriers,
            request.DurationSeconds,
            request.WinProfit);
```

- [ ] **Step 2: Ajustar fallback de `GetVirtualWinProfit` para `0`**

Em `ViewModels/ContractPanelViewModel.cs`, substituir o corpo de `GetVirtualWinProfit` (linhas 629-637) para retornar `0` quando o payout não estiver disponível:

```csharp
    private decimal GetVirtualWinProfit(SignalDirection direction)
    {
        var stake = GetStakeValue();
        var payout = direction == SignalDirection.Call ? CallPayout : PutPayout;
        if (payout > 0 && stake > 0)
            return payout - stake;

        return 0m;
    }
```

- [ ] **Step 3: Compilar o projeto principal — agora deve ficar verde**

Run: `export DOTNET_CLI_HOME="$PWD/.dotnet-cli-home" && dotnet build Excalibur5.csproj -v q --nologo`
Expected: `Compilação com êxito` / `0 Erro(s)` (Tasks 2, 3 e 4 juntas fecham a propagação)

- [ ] **Step 4: Commit (Tasks 2–4 juntas)**

```bash
git add ViewModels/OpenPositionsViewModel.cs ViewModels/StrategyViewModel.cs ViewModels/ContractPanelViewModel.cs
git commit -m "feat: propaga WinProfit ate a tabela e usa fallback zero (manual)"
```

---

## Task 5: `UpdateVirtualPosition` usa `WinProfit` em vez de 50% fixo

Esta é a mudança central do pedido. Como o cálculo vive em `OpenPositionsViewModel` (não no simulador testável headless), o teste é manual via execução do app; o passo de verificação por leitura confirma o valor.

**Files:**
- Modify: `ViewModels/OpenPositionsViewModel.cs:149-159`

- [ ] **Step 1: Substituir o lucro fixo de 50% pelo `WinProfit`**

Em `ViewModels/OpenPositionsViewModel.cs`, no método `UpdateVirtualPosition`, trocar a linha que calcula `item.Profit` (linha 157) de `item.BuyPrice * 0.5m` para `item.WinProfit`:

```csharp
    public void UpdateVirtualPosition(long tradeId, decimal currentSpot)
    {
        var item = FindPosition(tradeId);
        if (item == null || !item.IsVirtual) return;

        var won = item.IsCallDirection
            ? currentSpot > item.EntrySpot
            : currentSpot < item.EntrySpot;
        item.Profit = won ? item.WinProfit : -item.BuyPrice;
        item.CurrentValue = Math.Max(0m, item.BuyPrice + item.Profit);
    }
```

- [ ] **Step 2: Compilar o projeto principal**

Run: `export DOTNET_CLI_HOME="$PWD/.dotnet-cli-home" && dotnet build Excalibur5.csproj -v q --nologo`
Expected: `Compilação com êxito` / `0 Erro(s)`

- [ ] **Step 3: Commit**

```bash
git add ViewModels/OpenPositionsViewModel.cs
git commit -m "feat: tabela de posicoes usa WinProfit (payout - stake) no lucro virtual"
```

---

## Task 6: Fallback `0` no chamador automático (`StrategyExecutor`)

**Files:**
- Modify: `Services/Strategy/StrategyExecutor.cs` (método `GetVirtualWinProfit`)
- Test: `Excalibur5.Tests/SimulatorTests.cs`

- [ ] **Step 1: Escrever teste falho — o simulador propaga `WinProfit` no resultado**

Em `Excalibur5.Tests/SimulatorTests.cs`, adicionar um teste que confirma que o `WinProfit` informado na requisição chega ao `VirtualTradeCompleted` (garante que o valor calculado a montante não se perde no simulador):

```csharp
    public static Task CompletedTradeCarriesWinProfit()
    {
        using var simulator = new VirtualTradeSimulator();
        VirtualTradeCompleted? completed = null;
        simulator.TradeCompleted += (_, result) => completed = result;

        simulator.TryStart(CreateRequest(SignalDirection.Call, 100m, ticks: 1, winProfit: 9.5m));
        simulator.UpdateSpot(101m);

        TestAssert.NotNull(completed, "Trade did not complete");
        TestAssert.True(completed!.WinProfit == 9.5m, "WinProfit was not propagated to the result");
        return Task.CompletedTask;
    }
```

- [ ] **Step 2: Registrar o teste no runner**

Em `Excalibur5.Tests/Program.cs`, adicionar a entrada na lista `tests` (após a linha 11, junto das demais de `SimulatorTests`):

```csharp
    ("Completed trade carries win profit", SimulatorTests.CompletedTradeCarriesWinProfit),
```

- [ ] **Step 3: Rodar para ver o teste passar (o simulador já propaga `WinProfit`)**

Run: `export DOTNET_CLI_HOME="$PWD/.dotnet-cli-home" && dotnet run --project Excalibur5.Tests/Excalibur5.Tests.csproj -v q --nologo`
Expected: `13/13 tests passed.`

> Observação: este teste PASSA imediatamente porque a propagação no simulador já foi feita no trabalho parcial. Ele serve de teste de regressão para a cadeia de dados que sustenta a feature.

- [ ] **Step 4: Ajustar fallback de `GetVirtualWinProfit` para `0` no `StrategyExecutor`**

Em `Services/Strategy/StrategyExecutor.cs`, no método `GetVirtualWinProfit`, trocar o `return stake * 0.5m;` final por `return 0m;`. Além disso, a fórmula do caminho feliz deve usar `payout - stake` (consistente com o spec e com `ContractPanelViewModel`), em vez de `stake * (payout / _proposalStake - 1m)`:

```csharp
    private decimal GetVirtualWinProfit(SignalDirection direction, decimal stake)
    {
        var payout = direction == SignalDirection.Call ? _callPayout : _putPayout;
        if (_proposalsReady && payout > 0 && stake > 0)
            return payout - stake;

        return 0m;
    }
```

> Por que mudar a fórmula: `_callPayout`/`_putPayout` vêm de `resp.Payout`, que é o retorno bruto total para o stake proposto. O spec define a fórmula única `payout - stake`. Manter `payout - stake` alinha os dois call sites (automático e manual) numa fórmula só.

- [ ] **Step 5: Compilar o projeto principal**

Run: `export DOTNET_CLI_HOME="$PWD/.dotnet-cli-home" && dotnet build Excalibur5.csproj -v q --nologo`
Expected: `Compilação com êxito` / `0 Erro(s)`

- [ ] **Step 6: Rodar testes novamente para confirmar que nada quebrou**

Run: `export DOTNET_CLI_HOME="$PWD/.dotnet-cli-home" && dotnet run --project Excalibur5.Tests/Excalibur5.Tests.csproj -v q --nologo`
Expected: `13/13 tests passed.`

- [ ] **Step 7: Commit**

```bash
git add Services/Strategy/StrategyExecutor.cs Excalibur5.Tests/SimulatorTests.cs Excalibur5.Tests/Program.cs
git commit -m "feat: fallback zero e formula payout-stake no StrategyExecutor + teste de regressao"
```

---

## Task 7: Verificação final

**Files:** nenhum (apenas verificação)

- [ ] **Step 1: Build completo da solução (principal + testes)**

Run: `export DOTNET_CLI_HOME="$PWD/.dotnet-cli-home" && dotnet build Excalibur5.csproj -v q --nologo && dotnet build Excalibur5.Tests/Excalibur5.Tests.csproj -v q --nologo`
Expected: ambos `Compilação com êxito` / `0 Erro(s)`

- [ ] **Step 2: Suíte de testes verde**

Run: `export DOTNET_CLI_HOME="$PWD/.dotnet-cli-home" && dotnet run --project Excalibur5.Tests/Excalibur5.Tests.csproj -v q --nologo`
Expected: `13/13 tests passed.` e exit code 0

- [ ] **Step 3: Conferência manual da feature (grep de sanidade)**

Run: `grep -n "0.5m" ViewModels/OpenPositionsViewModel.cs ViewModels/ContractPanelViewModel.cs Services/Strategy/StrategyExecutor.cs`
Expected: nenhuma ocorrência de `0.5m` nesses três arquivos (o lucro fixo de 50% foi totalmente removido dos caminhos de Posições Abertas e dos dois `GetVirtualWinProfit`).

- [ ] **Step 4 (opcional): Validação visual no app**

Rodar o app, ativar Entrada Virtual com uma Sequência Alvo, abrir uma entrada virtual com Stake conhecido (ex: 10) e payout disponível, e confirmar na tabela de Posições Abertas que a coluna Lucro/Perda mostra `payout − stake` (ex: ~9,50 para payout 19,50) em vez de 5,00 (50%). Sem payout disponível, deve mostrar 0,00.

---

## Self-Review

- **Spec coverage:** fórmula `payout − stake` (Tasks 4, 6); coluna Lucro/Perda usa o valor (Task 5); sem coluna nova / campo interno (Task 1); fallback `0` (Tasks 4, 6); testes (Tasks 0, 6). ✓
- **Placeholders:** nenhum — todo passo de código tem o código. ✓
- **Type consistency:** `WinProfit` (decimal) consistente em `OpenPositionItem`, `AddVirtualPositionAsync`, `VirtualPositionOpened.WinProfit`, `VirtualTradeRequest.WinProfit`. Assinatura de `AddVirtualPositionAsync` com 8 parâmetros (último `decimal winProfit`) batida nos dois call sites (Tasks 3, 4). ✓
