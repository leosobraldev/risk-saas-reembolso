# Desenvolvimento

## 1. Requisitos

- **.NET 8 SDK** (`global.json` fixa a versão).
- **Docker** (PostgreSQL, Redis e os testes de integração com Testcontainers).
- **Node 24** para o painel (`volta run --node 24 ...` ou `volta pin node@24`).
- **Python 3** apenas para o gerador de carga.

## 2. Rodar localmente

```bash
docker compose up -d postgres redis          # só a infraestrutura
dotnet run --project src/RiskGuard.Api       # http://localhost:5080 (Development: migra ao subir)
cd frontend && npm install && npm run dev    # http://localhost:5173
```

A API em Development lê o `appsettings.Development.json` (conexões locais, chave de criptografia **só de desenvolvimento**, CORS do painel) e os `user-secrets`. Os dados do Auth0 entram por `user-secrets`:

```bash
cd src/RiskGuard.Api
dotnet user-secrets set "Identity:Auth0:Domain" "meu-tenant.us.auth0.com"
dotnet user-secrets set "Identity:Audience" "https://api.riskguard.app"
dotnet user-secrets set "Identity:Auth0:ClientId" "<client id da M2M>"
dotnet user-secrets set "Identity:Auth0:ClientSecret" "<client secret da M2M>"
```

Alternativas: `docker compose up -d --build` sobe tudo em containers (ver [operação](configuracao-e-operacao.md)); o painel tem o modo demonstração `npm run dev:demo`, que usa **dados sintéticos** sem Auth0 nem API.

## 3. Testes

| O quê | Comando | Observações |
| --- | --- | --- |
| Backend (unitários e integração) | `dotnet test RiskGuard.sln` | A integração usa **Testcontainers** (PostgreSQL 16 e Redis 7): o Docker precisa estar rodando. Plataformas e Auth0 são simulados (`FakePlatformsHandler`, `TestIdentity`, `FakeIdentityProvider`). |
| Gate de cobertura | `pwsh ./scripts/Test-RiskCoverage.ps1 -Threshold 90` | Mescla unitários e integração e exige **90%** de linhas no módulo de risco. |
| Frontend | `cd frontend && npm test` | Vitest e Testing Library. |
| Lint e tipos do frontend | `npm run lint` e `npm run typecheck` | |
| Arquitetura | `ModuleBoundariesTests` | Falha se um módulo referenciar outro. |

Os testes de integração cobrem o isolamento entre lojistas (RLS), os papéis de banco, a auditoria imutável, a assinatura dos webhooks, o fluxo completo pedido → risco → ação e a autenticação JWT.

**Estilo e qualidade:** analisadores `8.0-All` com `TreatWarningsAsErrors`, `dotnet format --verify-no-changes`, NuGetAudit (vulnerabilidade moderada ou pior quebra o build). O hook de pré-commit (Husky.Net) roda o `dotnet format` e os testes unitários.

## 4. CI (GitHub Actions)

Em pull requests para `main` (`.github/workflows/ci.yml`):

1. **Backend:** restore, checagem de pacotes vulneráveis, `dotnet format`, build Release, testes com cobertura, gate de 90%.
2. **Frontend:** `npm ci`, `npm audit` (produção, nível alto), lint, typecheck, testes e build.
3. **Docker:** validação do build da imagem da API.

Há também CodeQL e o Dependabot (NuGet, Actions, Docker, npm; atualizações menores do painel agrupadas).

## 5. Teste de carga e simulação

`scripts/load-webhooks.py` envia webhooks **sintéticos e assinados** (HMAC) para uma loja de teste.

```bash
# 1. no painel: conecte a Shopify com um domínio fictício (ex.: loja-teste.myshopify.com), um segredo à sua escolha
#    SEM token de acesso e com o bloqueio automático DESLIGADO (as decisões ficam Skipped: nenhuma loja é chamada)
# 2. rode a carga
export WEBHOOK_SECRET="<o segredo da conexão>"
python scripts/load-webhooks.py --tenant <tenantId> --count 2000 --concurrency 20
python scripts/load-webhooks.py --tenant <tenantId> --source dummyjson --count 2000   # valores e e-mails da API pública DummyJSON
python scripts/load-webhooks.py --tenant x --source dummyjson --count 500 --dry-run   # mistura de risco prevista, sem enviar
```

O script mostra vazão, latência (p50, p95, p99) e a contagem por status. Com o limite padrão de **600 por minuto por lojista**, uma carga rápida recebe `429` no excesso: é o comportamento esperado. Para volumes maiores suba `RATE_LIMIT_WEBHOOK_TENANT` e `RATE_LIMIT_WEBHOOK_IP` no `.env`, ou use `--rps`.

Para receber webhooks de **lojas reais** na sua máquina existe o `scripts/dev-tunnel.ps1` (API em Production local + Cloudflare Tunnel, URL de teste que muda a cada execução).

## 6. Como estender

### Adicionar uma plataforma

O RiskGuard não é restrito a Shopify, Nuvemshop e Wake: uma plataforma nova é um conjunto de adaptadores. A Wake (`WakeWebhookParser`, `WakeTokenVerifier`, `WakeOrderFetcher`) é um bom modelo de plataforma com **webhook só com o id e sem assinatura**; a Shopify, de uma com HMAC e pedido completo.

1. **Core:** acrescente o id em `PlatformIds` (constante, `All` e a regra de validação do identificador da loja: o valor entra em URLs de saída, então valide antes de usar) e uma entrada em `PlatformCatalog` (nome, rótulo e exemplo do identificador, `RequiresAccessToken`, `SupportsActions`, ajuda do segredo). **O painel lê o catálogo** (`GET /api/v1/platforms`): não é preciso mexer nele.
2. **Ingestion:**
   - `IWebhookParser` (herde de `WebhookParserBase`; sobrescreva os `Read*`). Se o webhook traz só o id, devolva `webhook.needs_order_fetch` em `DetectIncompletePayload` e, se o id não estiver em `id`, sobrescreva `ReadNotificationOrderId`. Notificações que não são pedido novo devolvem `webhook.ignored`.
   - `IWebhookSignatureVerifier`: herde de `HmacWebhookSignatureVerifier` (plataforma que assina o corpo) ou de `SharedTokenWebhookVerifier` (token em cabeçalho).
   - `IPlatformOrderFetcher`, se o webhook não traz os dados do pedido (valide o id antes de montar a rota).
   - Registre tudo em `IngestionModule`. O `HttpClient` da plataforma é criado sozinho a partir de `PlatformIds.All`.
3. **Action:** se a plataforma permite reter ou sinalizar, crie um `IOrderBlocker` e registre em `ActionModule`; prefira uma ação `Flag` (nota interna) a cancelar o pedido. **Sem blocker**, o RiskGuard só avalia (a decisão vira `Skipped` com a explicação) e `SupportsActions` fica `false` no catálogo. Um teste garante que `SupportsActions` e os blockers registrados não divergem.
4. **Testes:** unitários do parser, do verificador (válido, inválido, ausente, vazio) e do buscador; estenda a plataforma simulada (`FakePlatformsHandler`, que é roteada pelo host de `PlatformApis:Platforms:{id}:BaseUrl`) e cubra o fluxo completo, as notificações ignoradas e as falhas.
5. **Documentação:** guia do usuário (como conectar) e esta referência.

### Adicionar uma regra de risco

1. Implemente `IRiskRule` em `RiskGuard.Modules.Risk/Application/Rules` e devolva uma `RiskContribution` (nome, motivo em português, pontos) ou `null`.
2. Registre a regra no pipeline em `RiskModule` (a ordem é a de registro; o score é limitado a 100).
3. Escreva os testes unitários de cada faixa. O gate exige 90% de cobertura neste módulo.
4. O motivo aparece no painel (*Por que este score*); escreva-o para o lojista, não para o engenheiro.

### Adicionar um evento de auditoria

1. Acrescente a constante em `AuditActions` (contrato com quem consulta; **não renomeie** depois).
2. No módulo Tenant, grave com `AuditTrail.Record(...)` **antes** do `SaveChanges` (mesma transação). Em outro módulo, envie `RecordAuditEventCommand` **antes** de efetivar o efeito.
3. Nunca coloque segredos nem dados pessoais de compradores em `Details`.
4. Inclua o rótulo em português em `frontend/src/lib/labels.ts`.

### Adicionar uma migration

```bash
cd src/Modules/RiskGuard.Modules.Tenant   # ou o módulo desejado
dotnet ef migrations add NomeDaMigration -c TenantDbContext -o Persistence/Migrations
```

Tabelas novas com `TenantId` precisam de **RLS** (policy `tenant_isolation`) e de `GRANT` para `riskguard_app` no SQL da migration (veja `ExternalIdentity` e `AuditTrail` como modelo). Atualize o SDD.

## 7. Convenções

- **Resultados, não exceções**, para erros de negócio (`Result<T>` com `ErrorType`); exceções só para o inesperado.
- **Idempotência** em tudo que roda em job ou evento (a entrega é *at-least-once*).
- **Fail-closed** em segurança (sem tenant, sem segredo, sem token: nada passa); **fail-open** só onde indisponibilidade de um auxiliar não pode derrubar o serviço (limitador de taxa).
- **Mensagens ao usuário** em português do Brasil, com acentos; códigos de erro estáveis em inglês com ponto (`modulo.erro`).
- **Commits** por fase ou assunto, mensagem em português; nunca inclua segredos.
