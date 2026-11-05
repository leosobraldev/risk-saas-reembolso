# Referência da API

Base: `http://localhost:5080` em desenvolvimento. Todas as rotas têm o prefixo `/api/v1`, exceto as de saúde. JSON em **camelCase**.

## 1. Autenticação e autorização

| Tipo | Rotas | Como |
| --- | --- | --- |
| **Token do Auth0** | tudo, exceto webhooks e saúde | `Authorization: Bearer <access token>` (JWT RS256 emitido para a *audience* da API, com o claim do lojista) |
| **Assinatura HMAC** | `POST /webhooks/...` | cabeçalho de assinatura da plataforma (ver seção 3) |
| **Pública** | `/health/*` | sem autenticação |

O lojista vem **sempre do token**, nunca de parâmetro. Token sem o claim do lojista, ou de lojista inativo, recebe `403`. Corpo de webhook: no máximo **1 MB**. Não existe rota de login nem de cadastro: o login é no Auth0 e o cadastro é pela CLI (`--provision-tenant`).

## 2. Convenções

**Erros** seguem o ProblemDetails (RFC 9457) com um campo estável `code`:

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Acao nao encontrada.", "status": 404, "code": "action.not_found" }
```

Use o `code` (contrato estável), não o `title`. A tabela completa está na seção 6.

**Paginação:** `page` (a partir de 1) e `pageSize` (padrão 20, **máximo 100**; valores maiores são reduzidos a 100). Resposta:

```json
{ "items": [ ... ], "page": 1, "pageSize": 20, "total": 137 }
```

**Limite de taxa:** `429` com o cabeçalho `Retry-After` (segundos). Limites padrão: 100 requisições por minuto por IP nas rotas `/api`; 3000 por minuto por IP nas rotas de webhook; 600 por minuto por lojista na ingestão. Configuráveis (ver [configuração](configuracao-e-operacao.md)).

## 3. Webhooks (entrada)

`POST /api/v1/webhooks/{platform}/{tenantId}`, anônimo, protegido por HMAC.

- `platform`: `shopify`, `nuvemshop` ou `wake` (a lista vem de `GET /api/v1/platforms`).
- `tenantId`: GUID do lojista (o RiskGuard mostra na CLI de cadastro; faz parte da URL cadastrada na plataforma).
- Corpo: o JSON do pedido, **exatamente como a plataforma envia** (a assinatura cobre os bytes do corpo).

**Autenticidade:** o **segredo do webhook** cadastrado na conexão do lojista prova que o aviso veio da loja. Cada plataforma tem o seu mecanismo:

| Plataforma | Como | Cabeçalho | Formato |
| --- | --- | --- | --- |
| Shopify | HMAC-SHA256 do corpo cru | `X-Shopify-Hmac-SHA256` | base64 |
| Nuvemshop | HMAC-SHA256 do corpo cru | `x-linkedstore-hmac-sha256` | hex (base64 também é aceito) |
| Wake | **token compartilhado** (a Wake não assina o corpo) | `X-RiskGuard-Token` | o próprio segredo, comparado em tempo constante |

Exemplo (Shopify), no Bash:

```bash
BODY='{"id": 1001, "email": "ana@exemplo.com", "total_price": "259.90", "created_at": "2026-11-05T12:00:00Z"}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$WEBHOOK_SECRET" -binary | base64)
curl -i -X POST "http://localhost:5080/api/v1/webhooks/shopify/$TENANT_ID" \
  -H "Content-Type: application/json" -H "X-Shopify-Hmac-SHA256: $SIG" -d "$BODY"
```

**Campos lidos do pedido**

| Plataforma | Id | E-mail | Valor | Data |
| --- | --- | --- | --- | --- |
| Shopify (`orders/create`) | `id` | `email`, `contact_email` ou `customer.email` | `total_price` | `created_at` |
| Nuvemshop | `id` | `contact_email` (ou o pedido é buscado na API da loja) | `total` | `created_at` |
| Wake (`pedido.novo`) | `evento.pedido` | **buscado** em `GET /pedidos/{id}` (`usuario.email`) | **buscado** (`valorTotalPedido`) | **buscada** (`data`) |

**Wake.** O webhook traz só `{ topico, evento: { loja, pedido, ... } }`: o RiskGuard busca o pedido em `https://api.fbits.net/pedidos/{id}` com o **token de acesso** do lojista (por isso a conexão da Wake exige o token). Só o tópico `pedido.novo` é avaliado; os demais (`pedido.situacao.alterado`, rastreamento, indexação etc.) recebem `202` e são dados como processados, sem avaliar. Como o corpo é só um número e o pedido é lido da API da Wake, uma notificação forjada não consegue injetar e-mail nem valor.

**Resposta** `202 Accepted`: `{ "webhookId": "<guid>" }`, assim que o payload bruto e o job estão gravados. O cálculo de risco roda em segundo plano.

Reenvio do mesmo pedido é seguro: não duplica pedido nem avaliação.

## 4. Endpoints autenticados

### Lojista e configurações

| Método e rota | Corpo | Resposta |
| --- | --- | --- |
| `GET /tenants/me` | | `{ id, name, email, settings: { highRiskThreshold, autoBlockEnabled } }` |
| `PUT /tenants/me/settings` | `{ "highRiskThreshold": 0..100, "autoBlockEnabled": true\|false }` (ambos obrigatórios) | as configurações salvas; vale para os **próximos** pedidos |

### Conexões com plataformas

| Método e rota | Corpo | Resposta |
| --- | --- | --- |
| `GET /tenants/me/connections` | | `[ { platform, accountId, hasAccessToken, updatedAt } ]` |
| `PUT /tenants/me/connections/{platform}` | `{ "accountId": "...", "webhookSecret": "...", "accessToken": "..." }` (`accessToken` opcional) | a conexão (sem segredos) |
| `DELETE /tenants/me/connections/{platform}` | | `204` |

`accountId`: Shopify = domínio `minha-loja.myshopify.com`; Nuvemshop = id numérico da loja; Wake = nome da loja (letras, números, `-`, `_` e `.`). Segredos até 1000 caracteres. **Nenhuma resposta devolve segredos**: só `hasAccessToken`. Sem `webhookSecret` os webhooks são recusados.

### Plataformas

`GET /platforms` lista as plataformas suportadas e o que o painel precisa saber de cada uma (dados estáticos do produto):

```json
[ { "id": "wake", "name": "Wake", "accountIdLabel": "Nome da loja", "accountIdHint": "...",
    "requiresAccessToken": true, "supportsActions": false, "webhookSecretHelp": "..." } ]
```

`requiresAccessToken`: o webhook traz só o id do pedido, então a conexão **exige** o token (`400 connection.access_token_required` sem ele). `supportsActions`: falso = o RiskGuard só avalia e recomenda (a ação fica `Skipped`).

### Avaliações de risco

`GET /risk/evaluations?page=1&pageSize=20&level=High` (`level`: `Low`, `Medium` ou `High`), do mais recente ao mais antigo.

```json
{
  "id": "…", "orderId": "…", "platform": "shopify", "externalOrderId": "1001",
  "totalAmount": 259.9, "score": 85, "level": "High", "calculatedAt": "2026-11-05T12:00:03Z",
  "factors": [ { "reason": "E-mail suspeito: o endereco contem o termo 'fraude'.", "pointsAdded": 50 } ]
}
```

### Ações

| Método e rota | Descrição |
| --- | --- |
| `GET /actions?page&pageSize&status=Succeeded` | Ações do RiskGuard. `status`: `Pending`, `Succeeded`, `Failed`, `Skipped`, `Releasing`, `Released`, `ReleaseFailed`. |
| `POST /actions/{id}/release` | Libera um pedido bloqueado ou sinalizado (falso positivo). Responde **`202`** com a ação em `Releasing`; o desfazer na plataforma é um job com retry. Só ações `Succeeded` podem ser liberadas (`409 action.not_releasable` nos demais casos). Pedido repetido não enfileira outro job. |

Campos da ação: `id, evaluationId, orderId, platform, externalOrderId, score, type (Block|Flag), status, attempts, lastError, createdAt, completedAt`.

### Privacidade (LGPD)

`POST /privacy/customers/anonymize`, corpo `{ "email": "comprador@exemplo.com" }`, responde `204`. Elimina os dados pessoais do comprador (pedidos, payloads e histórico de risco) do lojista autenticado. Idempotente. O e-mail não vai para logs nem para a auditoria.

### Auditoria

`GET /audit-events?page&pageSize&action=settings.updated`, do mais recente ao mais antigo. Somente leitura.

| `action` | Quando |
| --- | --- |
| `tenant.registered`, `tenant.deactivated` | cadastro e desativação do lojista |
| `settings.updated` | mudança de limite ou bloqueio automático (de → para) |
| `connection.upserted`, `connection.removed` | conexão criada, substituída ou removida |
| `privacy.anonymization_requested` | pedido de eliminação de dados |
| `action.release_requested` | liberação de pedido |

Evento: `{ id, occurredAt, actorId, action, target, details }`. `actorId` é o `sub` do Auth0, ou `system` (CLI, jobs, operações administrativas).

### Saúde

- `GET /health/live`: processo no ar (não consulta dependências).
- `GET /health/ready`: inclui PostgreSQL e Redis.

## 5. Exemplo com token

```bash
curl -H "Authorization: Bearer $TOKEN" "http://localhost:5080/api/v1/risk/evaluations?level=High&pageSize=5"
```

Para obter um token de teste, veja a seção 8 de [`docs/auth0-setup.md`](../auth0-setup.md). Tokens de aplicação *Machine to Machine* **não** têm o claim do lojista e recebem `403`.

## 6. Códigos de erro

| Status | `code` | Quando |
| --- | --- | --- |
| 400 | `settings.invalid_request`, `settings.invalid_threshold` | corpo ou limite inválido (limite deve ser 0 a 100) |
| 400 | `connection.invalid_account`, `connection.invalid_secret`, `connection.access_token_required` | dados da conexão inválidos (ou token ausente em plataforma que o exige) |
| 400 | `risk.invalid_paging`, `actions.invalid_paging`, `audit.invalid_paging` | `page` ou `pageSize` menor que 1 |
| 400 | `privacy.invalid_email` | e-mail inválido |
| 400 | `webhook.invalid_json`, `webhook.invalid_payload` | corpo do webhook malformado ou sem os campos necessários |
| 401 | `webhook.invalid_signature` | assinatura ausente ou incorreta |
| 401 | `auth.no_tenant` | requisição sem lojista no contexto |
| 401 | (sem `code`) | token ausente ou inválido |
| 403 | `tenant.inactive` | lojista inativo ou inexistente (inclui usuário órfão do Auth0) |
| 403 | (sem `code`) | token válido, mas sem o claim do lojista |
| 404 | `action.not_found`, `connection.not_found`, `tenant.not_found`, `webhook.tenant_not_found`, `webhook.unsupported_platform` | recurso inexistente (o de outro lojista também aparece como inexistente) ou plataforma desconhecida |
| 409 | `action.not_releasable`, `connection.conflict` | estado não permite; alteração simultânea |
| 413 | `webhook.payload_too_large` | corpo do webhook acima de 1 MB |
| 429 | `webhook.rate_limited` (webhooks) ou sem `code` | limite de taxa; veja `Retry-After` |
| 503 | `identity.unavailable` | Auth0 indisponível (cadastro de lojista) |
