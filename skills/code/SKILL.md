---
name: code
description: "Regras de construção de código para o Excalibur5: SOLID, código limpo, performance, segurança e revisão pós-implementação. Ler antes de implementar."
---

Você está escrevendo código no Excalibur5 (.NET 9 / WPF / MVVM). Siga estas regras.

## 1. Antes de implementar
- Entenda o requisito; se houver ambiguidade, pergunte.
- Decomponha tarefas complexas em subtarefas com critério de conclusão claro.
- Consulte `skills/arquitetura/SKILL.md` (mapa do projeto) e `skills/deriv-api/SKILL.md` (API) — evita reexplorar o código.

## 2. SOLID (obrigatório)
- **S** — uma responsabilidade por classe/função.
- **O** — estender via abstração/composição, não modificar.
- **L** — subtipos substituíveis sem quebrar comportamento.
- **I** — interfaces pequenas e específicas.
- **D** — depender de abstrações; injetar dependências via construtor.

## 3. Código limpo
- Nomes descritivos; funções curtas (≤20 linhas); aninhamento ≤2-3 níveis (early return).
- Sem código morto, imports/variáveis não usados, comentários obsoletos.
- Sem valores mágicos — use constantes (ex: `AppConfig`).
- Tratamento de erro específico; nunca engula exceção com `catch {}` genérico.
- Prefira imutabilidade; DRY sem abstração prematura.

## 4. Performance e recursos
- Libere recursos: `Dispose`/`DisposeAsync`, feche streams/sockets, descarte `CancellationTokenSource` ao substituir.
- Concorrência: locks de escopo curto e ordem consistente; use `Interlocked`/`volatile` para flags lidas em múltiplas threads; timeouts em I/O.
- Sem loops infinitos sem condição de saída; não aloque em loops quentes.
- WPF: toda alteração de estado de UI no Dispatcher; nunca bloqueie a UI thread.

## 5. Segurança
- Valide toda entrada externa (usuário, API, JSON).
- **Nunca hardcode tokens/secrets.** Tokens via `TokenStore` (DPAPI). O App ID público (`33we3QV2jfoLet2b2EFxB`, em `DerivRestClient`) não é segredo e pode ficar no código.
- Não logue valores de segredos — apenas presença/ausência.
- O `.gitignore` já cobre `.env`, `*.local.json`, segredos e artefatos de build. Ao adicionar stack nova, complemente sem sobrescrever.

## 6. Processo (cada subtarefa)
1. Implemente seguindo as regras acima.
2. Compile (`dotnet build`) — ver `skills/arquitetura/SKILL.md` para o comando exato.
3. Zero erros **e zero warnings**.
4. Não quebre o existente — rode os testes (`dotnet run --project Excalibur5.Tests`).

## 7. Revisão final (checklist)
- [ ] Compila sem erros/warnings.
- [ ] SOLID respeitado; sem código não usado.
- [ ] Sem memory leak, deadlock ou loop infinito (recursos descartados).
- [ ] Erro tratado de forma específica.
- [ ] Funcionalidade existente intacta (testes passam).
- [ ] Nenhum segredo exposto.
- [ ] Nomes claros, legível sem comentários excessivos.

## 8. Regras de ouro
- Resolva o problema atual, não problemas hipotéticos.
- Não prejudique o que funciona; legibilidade > esperteza; menos é mais.
