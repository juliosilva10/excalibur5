# Excalibur5 — Guia para Agentes

A documentação canônica do projeto está em **`CLAUDE.md`** (stack, estrutura, fluxo principal,
mapa de código e convenções). Este arquivo apenas aponta para lá para evitar duplicação.

**Antes de escrever código:** ler e seguir `skills/code/SKILL.md`.

Pontos não negociáveis (resumo):
- SOLID, funções curtas (≤20 linhas), sem código morto.
- Compilar sem erros **e sem warnings** após cada mudança.
- Nunca hardcodar tokens/secrets; validar entradas externas.
- Não quebrar funcionalidade existente — rodar build/testes antes de entregar.
