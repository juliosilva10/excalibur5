---
name: deriv-api
description: "Referência da Deriv API (plataforma Options nova) como usada no Excalibur5: fluxo de auth REST-OTP, payloads WebSocket reais e onde cada um vive no código. Consultar antes de mexer em conexão/trading."
---

Referência prática da Deriv API conforme usada no Excalibur5. Fonte oficial: https://developers.deriv.com/docs/ (cópia local em `DERIV_API_Documentation/deriv_api_docs.md`). Plataforma **Options nova** (REST para conta + WebSocket para trading); a antiga (`authorize` por WS) é legada — **não usar**.

## Autenticação (REST OTP) — `DerivRestClient` + `DerivApiService.ConnectAndAuthorizeAsync`
Headers REST: `Deriv-App-ID: 33we3QV2jfoLet2b2EFxB` + `Authorization: Bearer {PAT}`.
1. `GET https://api.derivws.com/trading/v1/options/accounts` → `data[0].account_id` (ex: `DOT…`), `account_type` (`demo`/`real`).
2. `POST .../options/accounts/{id}/otp` → `data.url` (WSS já autenticada por OTP).
3. Conectar o WebSocket nessa URL — **sem** enviar `authorize`.
4. `balance` com `subscribe:1` para saldo em tempo real.

WS público (fallback/config): `wss://api.derivws.com/trading/v1/options/ws/public` (`AppConfig.WebSocketUrl`).

## Padrão WebSocket
Toda request leva `req_id` (int crescente); respostas correlacionadas por `req_id` via `TaskCompletionSource` (ver `SendAndWaitAsync` em cada serviço). Streams trazem `subscription.id` — guardar para dar `forget`. Erros vêm em `error.message`.

## Payloads usados (chave → onde)
**Ticks** (`TickStreamService`):
- `{ ticks: <symbol>, subscribe: 1, req_id }`
- `{ ticks_history: <symbol>, count, end:"latest", style:"ticks", req_id }`
- candles: `{ ticks_history, count, end:"latest", style:"candles", granularity, req_id }`
- `{ forget: <subId> }` / `{ forget_all: "ticks" }`

**Contratos/Trading** (`ContractService`):
- `{ contracts_for: <symbol>, req_id }` → tipos suportados filtrados: `CALL`,`PUT`,`CALLE`,`PUTE`,`VANILLALONGCALL`,`VANILLALONGPUT`.
- proposta: `{ proposal:1, subscribe:1, amount, basis:"stake", contract_type, currency, underlying_symbol, req_id }` + um de: `duration`+`duration_unit` **ou** `date_expiry`; `barrier` se a estratégia exigir.
- `{ buy: <proposalId>, price, req_id }`
- `{ sell: <contractId>, price:0, req_id }`
- `{ proposal_open_contract:1, contract_id, subscribe:1, req_id }` (monitorar contrato aberto)
- `{ profit_table:1, description:1, limit, offset, sort:"DESC", req_id }`

**Sistema** (`DerivApiService`): `{ ping:1 }`, `{ time:1 }`, `{ balance:1, subscribe:1 }`.

## Notas importantes
- Plataforma Options usa `underlying_symbol` (não `symbol`) nas propostas.
- Valores monetários: serializar `amount`/`price` como string `InvariantCulture`; parsear com `ParseDecimal` (aceita número ou string).
- `is_virtual`/flags podem vir como bool, número (1) ou string ("1") — usar os parsers existentes (`ParseFlag`).
- Ao trocar/cancelar subscrição, sempre `forget` o `subId` e descartar o `CancellationTokenSource`.
- Antes de adotar campo/endpoint novo, confirme em https://developers.deriv.com/docs/ — não inventar parâmetros.
