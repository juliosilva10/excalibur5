# Excalibur5

Aplicação desktop WPF (.NET 9) para trading de opções binárias via Deriv API (WebSocket).

## Stack

- **Framework:** .NET 9, WPF (Windows)
- **Linguagem:** C#
- **Padrão arquitetural:** MVVM (Model-View-ViewModel)
- **Toolkit MVVM:** CommunityToolkit.Mvvm 8.4.0
- **Comunicação:** WebSocket (Deriv API)
- **Segurança:** System.Security.Cryptography.ProtectedData (armazenamento de tokens)
- **Análise estática:** Roslynator.Analyzers

## Estrutura do Projeto

```
├── Config/           # Configurações, armazenamento de tokens e estado de UI
├── Converters/       # Value converters para WPF bindings
├── Models/           # Modelos de dados (responses da API, entidades)
├── Services/         # Serviços (WebSocket, API Deriv, contratos, logging)
├── ViewModels/       # ViewModels (MVVM)
├── Views/            # Views XAML e controls
├── DERIV_API_Documentation/  # Documentação completa da Deriv API
├── skills/code/      # Skill de código — SEMPRE consultar antes de implementar
└── referencia/       # Referência auxiliar (deriv_api_docs.md)
```

## Documentação de Referência

- **Deriv API completa:** `DERIV_API_Documentation/` — documentação técnica extraída de developers.deriv.com
- **Referência auxiliar:** `referencia/deriv_api_docs.md`
- **Geração dos docs:** Usar o script em `C:\Users\Júlio César\Downloads\deriv-docs\scrape_deriv_docs.py`

## Skill Obrigatória

**Antes de escrever qualquer código**, ler e seguir: `skills/code/SKILL.md`

A pasta `skills/` contém as skills do projeto. Consultar sempre antes de implementar.

Resumo dos pontos-chave:
1. Analisar e decompor o requisito antes de implementar
2. Aplicar princípios SOLID rigorosamente
3. Funções curtas (≤20 linhas), nomes descritivos, sem código morto
4. Performance: liberar recursos, evitar deadlocks, fechar streams
5. Segurança: validar inputs, nunca hardcodar tokens/secrets
6. Compilar sem erros e sem warnings após cada mudança
7. Revisão final obrigatória (checklist no SKILL.md)

## Convenções

- Namespace raiz: `Excalibur5`
- Interfaces prefixadas com `I` (ex: `IDerivApiService`, `IContractService`)
- ViewModels usam `ObservableObject` e `RelayCommand` do CommunityToolkit.Mvvm
- Serviços injetados via construtor (Dependency Inversion)
- Tokens armazenados com DPAPI via `TokenStore`
- Logs via `AppLogger`

## Fluxo Principal

1. Autenticação via token Deriv (WebSocket `authorize`)
2. Subscrição de ticks de mercado
3. Consulta de contratos disponíveis (`contracts_for`)
4. Proposta e compra de contratos (`proposal` → `buy`)
5. Monitoramento de contratos abertos (`proposal_open_contract`)
