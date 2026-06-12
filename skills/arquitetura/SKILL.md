---
name: arquitetura
description: "Mapa de arquitetura do Excalibur5 — camadas, serviços, ViewModels, fluxo de dados e comandos de build/teste. Consultar antes de localizar ou alterar código, para evitar reexploração."
---

Mapa do Excalibur5 para localizar código rápido, sem reler arquivos grandes.

## Stack e build
- .NET 9 (`net9.0-windows`), WPF, MVVM com CommunityToolkit.Mvvm 8.4.0.
- Namespace raiz `Excalibur5`. Análise estática: Roslynator. Baseline: **0 warnings / 0 erros**.
- **Build:** `export DOTNET_CLI_HOME="$(pwd)/.dotnet-cli-home" DOTNET_CLI_TELEMETRY_OPTOUT=1 && dotnet build Excalibur5.csproj` (o dotnet global é .NET 10; usar o home local).
- **Testes:** `dotnet run --project Excalibur5.Tests/Excalibur5.Tests.csproj` — runner **console custom** (`Program.cs` + `TestAssert.cs`), não xUnit. Tests excluídos do csproj principal.

## Camadas (DI manual em `App.xaml.cs` → `OnStartup`)
Composição: `DerivWebSocketService` → `DerivRestClient`/`DerivApiService` + `TickStreamService` + `ContractService` → `MainViewModel` → `MainWindow`.

## Serviços (`Services/`) — todos atrás de interface `I*`
- `DerivWebSocketService` (`IDerivWebSocketService`) — socket bruto: connect/reconnect com backoff, receive loop, `SendAsync`, eventos `Connected`/`Disconnected`/`MessageReceived`.
- `DerivRestClient` (`IDerivRestClient`) — REST OTP: `GetAccountIdAsync`, `GetWebSocketUrlAsync`.
- `DerivApiService` (`IDerivApiService`) — orquestra auth (`ConnectAndAuthorizeAsync`), `balance`, `ping`, `time`; correlaciona respostas por `req_id`.
- `TickStreamService` (`ITickStreamService`) — `ticks`/`ticks_history`, subscrições, `forget`/`forget_all`.
- `ContractService` (`IContractService`) — `contracts_for`, `proposal`, `buy`, `sell`, `proposal_open_contract`, `profit_table`.
- `Services/Strategy/` — motor do bot: `StrategyEngine`/`TrendEngine`/`TickScalperEngine` (`IStrategyEngine`), `StrategyExecutor` (executa sinais → ordens), 16 indicadores (`IIndicator`/`ITickIndicator`), `WeightedSignalAggregator`, `SignalFilter`.
- `Services/Strategy/Recovery/` — `IRecoverStrategy`: `Martingale`/`Deficit`, montadas por `RecoverStrategyFactory` com `RecoverContext`.
- `Services/Strategy/Virtual/` — modo de entrada virtual (`VirtualTradeSimulator`, `VirtualEntryModeController`).
- `AppLogger` — logging estático central.

## ViewModels (`ViewModels/`)
- `MainViewModel` — raiz: conexão, saldo, timers, agrega os demais VMs e eventos de serviço.
- `ContractPanelViewModel` — trade manual: proposta (debounce), compra/venda, barreiras, recovery manual.
- `StrategyViewModel` — bot: start/stop, configura `StrategyExecutor`.
- `MarketTabViewModel` / `MarketsViewModel` — abas de mercado, ticks/candles, watchdog de reconexão.
- `PerformanceViewModel`, `HistoryViewModel`, `OpenPositionsViewModel`, `RecoverViewModel`, `VirtualViewModel`, `LogViewModel`.

## Views (`Views/`)
- `MainWindow.xaml` + controls em `Views/Controls/` (ContractPanel, MarketTab, Strategy, Performance, History, OpenPositions, Sidebar).
- **Estilos globais em `App.xaml`**: paleta (`*Brush`/`*Color`), botões (`DerivCallButtonStyle`, `DerivPutButtonStyle`, `DerivSellProfit/LossButtonStyle`, `DerivToggleButtonStyle`), inputs (`DerivTextBox`, `DerivComboBox`, `DerivDatePicker`). Reutilizar esses recursos; não duplicar inline.

## Config (`Config/`)
- `AppConfig` — constantes (URLs, timeouts, delays). `TokenStore` — token via DPAPI.
- Stores de estado de UI/bot: `UiStateStore`, `BotStateStore`, `RecoverStateStore`, `VirtualEntryStateStore`.

## Fluxo principal
Auth REST-OTP (sem `authorize` WS) → subscrição de ticks → `contracts_for` → `proposal` → `buy` → `proposal_open_contract`. Detalhe da API em `skills/deriv-api/SKILL.md`.
