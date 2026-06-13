# Plano de Implementação — Modalidade Diversity

## Visão geral

Diversity é uma nova modalidade onde o usuário compra **2 ou mais contratos ("pernas"/legs)
simultaneamente** — do mesmo mercado de ticks ou de mercados diferentes — e o sistema os trata
como **um único contrato consolidado** para fins de recover e virtual: soma o lucro/prejuízo de
cada perna e considera o **líquido do grupo** como o resultado a recuperar.

Acessível por um botão "Diversity" abaixo do botão "Virtual" no sidebar.

## Decisões de produto (travadas)

| Tema | Decisão |
|------|---------|
| Disparo das pernas | Manual **e** por sinal (ambos os modos) |
| Tipos/mercados | Qualquer tipo, multi-mercado |
| Stake | Configurável por perna |
| Virtual | Líquido do grupo > 0 → 1 'W' na sequência; senão 1 'L' |

## Fatos confirmados da Deriv

- 6 tipos de dígito: `DIGITOVER`, `DIGITUNDER`, `DIGITMATCH`, `DIGITDIFF`, `DIGITEVEN`, `DIGITODD`
  (confirmado no schema oficial `proposal/send.json`).
- Para dígitos, `barrier` = **previsão do último dígito** (0–9), não preço.
- Dígitos rodam em **duração de ticks** (1–10), já suportado pelo app.
- **Payout é dinâmico**: vem na resposta do `proposal`. Nada de tabela chumbada — a execução
  real sempre usa o payout que a API devolve (o ContractService já captura `payout`).

## Modelagem (resultado do estudo)

- **EV = −margem da casa**, para qualquer perna, independente de barreira. Logo
  `EV_grupo = −margem · Σ stakes`. **O hedge não cria lucro** — redistribui o formato do risco.
- **Combinações de cobertura total** (sem "dígito morto" que perde todas as pernas) dão passos de
  perda limitados → muito mais amigáveis ao Deficit Recovery. Ex.: Over 2 + Under 7 (dígitos 3–6
  ganham as duas; nenhum dígito perde as duas).
- **Mesmo tick** (Over+Under no mesmo mercado) = hedge real (pernas anticorrelacionadas).
  **Mercados diferentes** = diversificação (pernas independentes), não hedge. A UI deve distinguir.
- O motor de lucro real, se houver, vem de: (a) edge preditivo de dígito via sinal, ou
  (b) gestão de variância + recuperação disciplinada sobre o líquido.

## Ponto de costura arquitetural

O `StrategyExecutor` é single-symbol e está no caminho de dinheiro real. Em vez de torná-lo
multi-símbolo (alto risco), criamos um **`DiversityCoordinator`** paralelo que reusa peças já
agnósticas de símbolo: `ContractService.BuyDirectAsync(symbol, ...)`, `SubscribeOpenContractAsync`
(por contrato), `IRecoverStrategy` e o simulador virtual (ambos operam sobre escalares).

O conceito-chave novo é um **GroupId** ligando os ContractIds de um grupo, e um
**GroupResultAggregator** que segura os resultados até todas as pernas liquidarem e emite um
**resultado consolidado** — que alimenta recover e virtual exatamente como um contrato único faria.

## Fases

- **Fase 0** — Modelo de dados (`DiversityLeg`, `DiversityGroupConfig`, `GroupResult`) + tipos de
  dígito (`DigitOver/DigitUnderContractStrategy`) + `DigitOddsCalculator` puro (p, dígito-morto,
  cobertura, EV estimado — só para orientação de UI). Testes.
- **Fase 1** — `GroupResultAggregator` puro: liquidações por ContractId → `GroupResult` quando a
  última perna fecha. Soma P/L e stakes. Trata ordem trocada e perna travada (timeout). Testes. ⭐
- **Fase 2** — `DiversityCoordinator`: compra multi-perna (multi-mercado), registra no agregador,
  assina updates. Política de falha parcial (abortar + vender pernas já compradas). Modo manual.
- **Fase 3+4** — Recover: `RecordResult(totalProfit, totalStake)` uma vez por grupo; próximo stake
  distribuído entre pernas. Virtual: simular pernas, somar, líquido>0 → 1 W/L.
- **Fase 5+6** — UI: botão+painel, editor de pernas, indicador de cobertura/EV, posições agrupadas
  por GroupId. Modo por sinal reusando o engine de indicadores.

## Princípios de implementação

- Núcleo (agregador, odds, distribuição de stake) é **lógica pura testável** → baixo risco apesar
  de tocar o caminho de trade real.
- Build + testes a cada fase. Nada de churn arriscado sem cobertura.
- Não alterar o `StrategyExecutor` de contrato único além do mínimo necessário.
