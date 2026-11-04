# Documento de Especificação (SDD)

**Projeto:** RiskGuard - SaaS de Prevenção de Risco de Reembolso B2B
**Versão:** 1.6.0

> **Histórico:** 1.6.0 — **Fase 6 (frontend)** implementada em `/frontend` (Vite + React): painel do lojista com Pedidos, Ações, Configurações e Auditoria, login pelo Auth0 (ver 4.6 e `docs/auth0-setup.md`, seção 8.1). Marcado com **[1.6.0]**.
>
> 1.5.0 — **trilha de auditoria** de segurança e de negócio por lojista (seção 4.7): quem mudou configurações, conexões, pediu anonimização, liberou pedido ou desativou o lojista; imutável no banco, isolada por RLS, com consulta pela API e retenção. Marcado com **[1.5.0]**.
>
> 1.4.0 — **identidade delegada a um provedor OIDC (Auth0)**: o RiskGuard deixa de guardar senhas, emitir tokens e implementar login, MFA, renovação de sessão e trava de login (ver 4.6). Os trechos afetados estão marcados com **[1.4.0]**.
>
> 1.3.0 — endurecimento de segurança (seção 4.4 e 4.5): borda HTTP, privilégio mínimo no banco, trava de login por conta, dependências auditadas e privacidade de dados pessoais (retenção e anonimização). As decisões estão marcadas com **[1.3.0]**.
>
> 1.2.0 — consolida o que as Fases 0–5 introduziram além da 1.1.0 (colunas e tabelas extras no §7, endpoints no §8, requisitos de segurança RF06–RF09 e a seção 4.3) e registra o estado real das integrações com as plataformas. As decisões estão marcadas com **[1.2.0]**.

## 1. Contexto

O RiskGuard é uma plataforma Multi-Tenant que visa proteger e-commerces contra prejuízos causados por chargebacks, devoluções em massa e fraudes logísticas. A plataforma atua de forma invisível, recebendo notificações de novos pedidos (webhooks), processando os dados contra heurísticas de risco em segundo plano, e acionando bloqueios automáticos caso um pedido seja classificado como nocivo, tudo antes do envio logístico.

## 2. Atores

* **Lojista (Tenant):** Assinante do SaaS. Acessa o dashboard, configura as credenciais da sua plataforma (Shopify, Nuvemshop) e define a tolerância de risco.

* **Plataforma de E-commerce (Origem):** Sistema externo que envia o evento de "Novo Pedido" (Webhook) para o RiskGuard.

* **Sistema (Background Worker):** Motor assíncrono responsável por orquestrar a ingestão, normalizar os dados, calcular o risco e disparar ações preventivas.

## 3. Requisitos

### 3.1. Requisitos Funcionais (RF)

* **RF01:** O sistema deve permitir o cadastro de Lojistas e gerenciar o isolamento de seus dados.

* **RF02:** O sistema deve receber webhooks de diferentes plataformas de forma agnóstica.

* **RF03:** O sistema deve salvar o payload original do webhook para auditoria.

* **RF04:** O sistema deve calcular o risco (0 a 100) com base no histórico do cliente e do produto.

* **RF05:** O sistema deve realizar chamadas ativas para a API do E-commerce para pausar pedidos de risco "Alto".

* **RF06 [1.2.0]:** Todo webhook deve ter sua autenticidade verificada por HMAC-SHA256 do corpo cru com o segredo do lojista; sem segredo configurado, o webhook é recusado (fail-closed).

* **RF07 [1.2.0]:** O lojista deve conectar suas plataformas (conta, segredo de webhook e token de acesso). Segredos são cifrados em repouso e nunca retornam em respostas de API.

* **RF08 [1.2.0]:** O lojista deve poder alterar o limite de risco alto e o bloqueio automático, com efeito imediato, e liberar um pedido bloqueado (falso positivo).

* **RF09 [1.2.0]:** O lojista deve poder consultar as ações preventivas aplicadas (bloqueios, sinalizações, recomendações e falhas).

### 3.2. Requisitos Não-Funcionais (RNF)

* **RNF01 (Stack):** Backend em .NET 8 (C#) e Frontend em React (Next.js/Vite).

* **RNF02 (Banco de Dados):** PostgreSQL com esquemas (schemas) isolados por módulo.

* **RNF03 (Performance):** O endpoint de ingestão de webhooks deve responder em menos de 200ms, delegando o processamento pesado.

* **RNF04 (Resiliência):** Falhas no processamento de pedidos devem ser tentadas novamente automaticamente (Retry pattern).

* **RNF05 (Rate Limiting & Cache):** A API deve suportar proteção contra picos de requisições e armazenamento em cache usando o Redis.

## 4. Regras de Negócio e Arquitetura

### 4.1. Design de Arquitetura

* **Modular Monolith:** Sistema dividido lógicamente em `TenantModule`, `IngestionModule`, `RiskModule` e `ActionModule`.

* **CQRS e Event-Driven:** Comunicação inter-módulos feita estritamente via `MediatR` usando Commands e Domain Events. Módulos não acessam o banco uns dos outros.

* **Multi-Tenancy:** Single Database, Single Schema, isolado via Entity Framework Core Global Query Filter (`TenantId`).

### 4.2. Design Patterns Exigidos

* **Template Method + Strategy (IngestionModule):** Para lidar com as diferenças de payload entre plataformas de e-commerce na leitura do webhook (classe base orquestradora e implementações específicas).

* **Pipeline / Pipes and Filters (RiskModule):** O cálculo de risco deve ser uma cadeia de executores independentes (`IRiskRule`).

* **Background Processing:** Utilizar `Hangfire` com persistência no PostgreSQL para orquestrar as filas e workers.

* **Cache:** Utilizar `Redis` para armazenar as configurações do Tenant e fazer Rate Limiting.

### 4.3. Segurança e Integrações [1.2.0]

* **Autenticidade dos webhooks:** Shopify assina com `X-Shopify-Hmac-SHA256` (base64); Nuvemshop com `x-linkedstore-hmac-sha256` (hex; a documentação não declara a codificação, então o sistema aceita hex e base64). A comparação é em tempo constante e a resposta de falha é idêntica para "sem header", "assinatura errada" e "plataforma não conectada".

* **Segredos de terceiros:** AES-256-GCM com chave em `Security:EncryptionKey`; o contexto (tenant + plataforma + finalidade) é autenticado junto, então um valor cifrado não pode ser reaproveitado em outra linha. Os segredos decifrados circulam apenas em memória (contrato `GetPlatformCredentialsQuery`) e nunca vão ao Redis.

* **SSRF:** o host das chamadas externas nunca vem de dado livre do tenant: a Shopify só aceita domínios `*.myshopify.com` e a Nuvemshop apenas ids numéricos; as URLs-base são configuração do servidor (`PlatformApis`).

* **Capacidades reais das plataformas (verificadas na documentação oficial):**
  * **Shopify:** lista `fulfillment_orders` do pedido e aplica `hold` (motivo `high_risk_of_fraud`); a liberação usa `release_hold`, e só é feita se todos os holds forem do RiskGuard (nota com prefixo `RiskGuard:`). A API REST Admin é legada; migrar para a GraphQL Admin é trabalho futuro.
  * **Nuvemshop:** **não existe hold de pedido** (status: open/closed/cancelled) e cancelar é destrutivo. A ação é **Flag**: acrescenta uma nota interna `[RiskGuard]` ao pedido, preservando a nota do lojista. O webhook traz só `{store_id, event, id}`, então o pedido é buscado em `GET /v1/{store_id}/orders/{id}` (header `Authentication: bearer`, `User-Agent` obrigatório).

* **Cache de configuração do tenant:** o snapshot no Redis é invalidado quando as configurações mudam ou o tenant é desativado; o TTL (5 min; 1 min para tenant inexistente) é apenas a rede de segurança.

### 4.4. Endurecimento e Operação Segura [1.3.0]

* **Borda HTTP:** headers `X-Forwarded-*` são **ignorados** a menos que os proxies sejam declarados (`Proxy:KnownProxies` / `Proxy:KnownNetworks`); sem isso qualquer cliente forjaria o próprio IP e escaparia do rate limit, e atrás de um load balancer todos compartilhariam um único balde. Headers de resposta: `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Content-Security-Policy: default-src 'none'`, `Cache-Control: no-store`. HSTS (1 ano, subdomínios) fora de Development. CORS fechado por padrão; só as origens de `Cors:AllowedOrigins` (sem curingas, sem credenciais). Exceções viram `ProblemDetails` genérico, sem stack nem mensagem interna. Configuração inválida (IP, CIDR, origem) impede a subida.

* **Privilégio mínimo no banco:** três papéis. O **dono** (`AdminConnectionString`) é usado *somente* no passo de migrations (`dotnet RiskGuard.Api.dll --migrate`, ou `Postgres:MigrateOnStartup` em desenvolvimento) e **não deve existir no processo da API em produção**. A **aplicação** (`riskguard_app`) é sujeita à RLS. O **Hangfire** (`riskguard_jobs`) só enxerga o próprio schema, que o passo de migrations instala e concede. Nenhum dos dois consegue ler os dados dos módulos nem criar objetos.

* **Trava de login por conta [1.4.0]:** não é mais do RiskGuard. A proteção contra força bruta, IPs suspeitos e senhas vazadas é configurada no provedor de identidade (*Attack Protection* do Auth0; ver `docs/auth0-setup.md`). O limite por IP da API continua valendo.

* **Dependências:** `NuGetAudit` (inclui transitivos) faz vulnerabilidade moderada ou pior **quebrar o build**; versões vulneráveis de terceiros são fixadas (`CentralPackageTransitivePinning`); o CI repete a checagem, e há Dependabot e CodeQL.

### 4.6. Identidade e Acesso (Auth0) [1.4.0]

* **Divisão de responsabilidades:** o **provedor** cuida de senhas e política de senha, MFA (TOTP/WebAuthn), recuperação de conta, renovação de sessão, rotação das chaves de assinatura, detecção de força bruta e logs de autenticação. O **RiskGuard** valida o token, vincula o usuário ao lojista e autoriza. Não há rota de login na API: o frontend usa o fluxo *Authorization Code + PKCE* (Universal Login).

* **Validação do token:** JWT RS256 emitido pelo provedor; o emissor e o JWKS vêm da descoberta OIDC (`Identity:Authority`, sempre HTTPS), então as chaves giram sem deploy. São verificados emissor, audiência (`Identity:Audience`), assinatura, validade e algoritmo (`alg: none` e algoritmos simétricos são recusados).

* **Lojista no token:** o usuário carrega o id do lojista em `app_metadata.tenant_id`; uma Action de login o copia para um claim **com namespace** (`Identity:TenantClaim`). O RiskGuard o converte no claim interno `tenant_id`, **removendo antes qualquer `tenant_id` que já venha no token** e recusando valores inválidos ou conflitantes. Sem o claim, a resposta é 403.

* **Lojista ativo:** todo request autenticado confere o lojista no cache (Redis, com fallback ao banco); lojista inativo ou inexistente (usuário órfão) recebe 403 **imediatamente**, sem esperar o token expirar. A desativação também bloqueia o usuário no provedor.

* **Cadastro de lojista:** sem rota pública. Operação do dono da plataforma (`--provision-tenant`): cria o usuário no provedor (sem senha conhecida, com o tenant em `app_metadata`), grava lojista, configurações padrão e o vínculo `TenantUsers` e entrega um link de uso único para o dono definir a senha. Provedor e banco não são transacionais: o provedor é chamado primeiro e, se a gravação falhar, o usuário criado é removido (compensação).

* **Troca de fornecedor:** o código específico do Auth0 está isolado em `Infrastructure/Identity/Auth0`; o restante depende da porta `IIdentityProvider` e da validação OIDC padrão.

### 4.5. Privacidade e Dados Pessoais (LGPD) [1.3.0]

* **Papéis:** o lojista é o controlador dos dados de seus compradores; o RiskGuard é o operador. Base legal, contrato de operador e política de privacidade são responsabilidade jurídica e **não** estão cobertas pelo código.

* **Dados pessoais armazenados:** e-mail e valor do pedido (`ingestion.Orders`), e-mail e histórico de reembolsos (`risk.CustomerHistories`) e o **payload bruto do webhook**, que contém o pedido inteiro (endereço, telefone etc.) e é o dado mais sensível. Avaliações de risco e ações não guardam e-mail.

* **Retenção:** o conteúdo e os headers de webhooks já concluídos (Processed/Failed) são apagados após `Retention:RawPayloadDays` (30) por um job recorrente (`Retention:Cron`), que percorre os tenants sob RLS; ficam apenas os metadados (`PayloadPurgedAt`). Webhooks pendentes nunca são apagados.

* **Eliminação sob demanda:** `POST /api/v1/privacy/customers/anonymize` publica `CustomerAnonymizationRequested`; cada módulo trata o próprio schema (pedidos e historico recebem um token aleatório e irreversível; payloads que citam o e-mail são apagados, sem diferenciar maiúsculas e sem tratar `%`/`_` como curinga). Só atinge o tenant solicitante e é idempotente. O e-mail nunca é gravado nos logs.

* **Transparência de decisões automatizadas:** cada avaliação guarda os fatores e motivos do score, e o lojista pode liberar pedidos bloqueados (RF08).

### 4.7. Trilha de Auditoria [1.5.0]

* **O que é registrado:** ações que mudam o comportamento do sistema ou exercem direitos. Cada evento guarda **quem** (`ActorId` = `sub` do token; `system` para CLI, jobs e operações administrativas), **o quê** (`Action`), **em quê** (`Target`), um texto curto de **detalhes** e **quando**. Ações cobertas (nomes estáveis, contrato com quem consulta): `tenant.registered`, `tenant.deactivated`, `settings.updated` (limite e bloqueio automático, de → para), `connection.upserted` e `connection.removed` (plataforma, loja e se há token de acesso), `privacy.anonymization_requested` e `action.release_requested` (id da ação e do pedido).

* **O que nunca entra:** segredos (webhook, token de acesso), e-mail ou qualquer dado pessoal de comprador. Os detalhes são texto escrito pelo código, não eco do que o cliente enviou; textos longos são cortados em vez de derrubar a operação.

* **Sem mudança sem rastro:** dentro do módulo Tenant o evento vai na **mesma transação** da mudança. Nos outros módulos (liberação de pedido) e na anonimização, o evento é gravado **antes** de efetivar o efeito: se a auditoria falhar, a operação falha e nada acontece. Operação rejeitada (validação, não encontrado) não gera evento. Os módulos pedem o registro pelo contrato do Core (`RecordAuditEventCommand`); quem guarda é o Tenant (`tenant.AuditEvents`).

* **Imutável e isolada no banco:** o papel da aplicação só tem `SELECT`, `INSERT` e `DELETE`, nunca `UPDATE` nem `TRUNCATE`. A RLS tem uma policy por comando: ler e inserir só no próprio lojista (sem `app.current_tenant` não há acesso) e **apagar só eventos com mais de 180 dias**. Mesmo um bug ou uma credencial da API comprometida não reescreve a história nem apaga auditoria recente.

* **Consulta:** `GET /api/v1/audit-events` (paginado, mais recente primeiro, filtro `action`). Não existe rota de escrita.

* **Retenção:** job recorrente (mesmo `Retention:Cron`) apaga eventos mais antigos que `Retention:AuditDays` (365; mínimo 180, validado na subida e imposto pelo banco).

* **Fora de escopo (decisão):** autenticação (login, falha de login, MFA) fica nos logs do provedor de identidade (4.6); tentativas negadas e erros ficam nos logs da aplicação. A trilha cobre o que o lojista fez dentro do RiskGuard.

## 5. Estratégia de Qualidade e CI/CD (DevOps)

Para garantir a estabilidade do SaaS B2B, o sistema deve incorporar práticas estritas de validação contínua.

### 5.1. Testes Automatizados

O projeto deve conter uma pasta `/tests` separada por escopo:

* **Testes Unitários (`RiskGuard.UnitTests`):** Focados nas regras de negócio (Domínio) e no padrão Pipeline/Strategy. Utilizar **xUnit**, **NSubstitute** (para mocks) e **FluentAssertions**. Cobertura mínima exigida para o `RiskModule` é de 90%.

* **Testes de Integração (`RiskGuard.IntegrationTests`):** Focados no fluxo ponta a ponta (API -> Fila -> Banco). Utilizar o padrão **WebApplicationFactory** do ASP.NET Core e **Testcontainers** para subir instâncias efêmeras do PostgreSQL e do Redis via Docker durante a execução, garantindo testes reais.

### 5.2. Análise de Qualidade de Código (Linting & SAST)

* **Análise Estática:** Habilitar as regras restritas do compilador .NET (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` e `<AnalysisLevel>latest-All</AnalysisLevel>` no `.csproj`).

* **SonarQube / SonarCloud:** O código gerado deve ser preparado para inspeção contínua, focando em zerar Code Smells, Bugs e Vulnerabilidades.

* **Pre-commit Hooks:** Configurar o **Husky.Net** para rodar `dotnet format` e testes unitários localmente antes do commit.

### 5.3. Pipeline CI/CD (GitHub Actions)

Criar `.github/workflows/ci.yml`. O pipeline deve rodar em *Pull Requests* para a branch `main`:

1. Fazer checkout do código e instalar o .NET 8 SDK.

2. Executar a validação de formatação e análise estática.

3. Compilar a Solution (`dotnet build`).

4. Rodar todos os testes (`dotnet test`) coletando Code Coverage.

5. Validar a construção do `Dockerfile` do backend e do frontend (Dry-Run).

## 6. Critérios de Aceite (BDD - Comportamento)

### Cenário 1: Recebimento Rápido de Webhook e Rate Limiting

> **Dado** que uma plataforma parceira envia um POST para `/api/v1/webhooks/shopify/{tenantId}`
> **E** o tenant existe no cache do Redis e não excedeu o Rate Limiting
> **Quando** o sistema recebe a requisição
> **Então** o sistema deve salvar o raw payload no banco
> **E** deve agendar o processamento no Hangfire
> **E** deve retornar um HTTP 202 (Accepted) imediatamente.

### Cenário 2: Cálculo de Risco por Histórico

> **Dado** que um pedido foi recebido e normalizado via Template/Strategy
> **E** o comprador possui o e-mail "fraude@teste.com"
> **Quando** o motor processar o pipeline de regras (`IRiskRule`)
> **Então** a regra `SuspiciousEmailRule` deve adicionar 50 pontos ao score
> **E** o sistema deve publicar o evento `RiskCalculatedEvent`.

### Cenário 3: Bloqueio de Pedido com Risco Alto

> **Dado** que o `RiskCalculatedEvent` foi disparado com score de 90
> **E** a configuração do lojista (lida do Redis) define Risco Alto > 80
> **Quando** o `ActionModule` interceptar o evento
> **Então** o sistema deve consumir a API origem para pausar o pedido.

### Cenário 4 [1.2.0]: Autenticidade do Webhook

> **Dado** que o lojista conectou a plataforma e configurou o segredo do webhook
> **Quando** chega um POST sem assinatura, com assinatura de outro segredo ou com o corpo alterado
> **Então** o sistema deve responder HTTP 401 sem persistir nada
> **E** a resposta deve ser idêntica à de um lojista que nunca conectou a plataforma.

### Cenário 5 [1.2.0]: Configuração com Efeito Imediato e Liberação

> **Dado** que um pedido de risco alto foi apenas recomendado porque o bloqueio automático estava desligado
> **Quando** o lojista liga o bloqueio automático
> **Então** o próximo pedido de risco alto deve ser bloqueado imediatamente (sem esperar o TTL do cache)
> **E** o lojista deve poder liberar um pedido bloqueado, sem que o sistema libere holds criados por outra origem.

## 7. Modelagem de Dados

> **[1.2.0]** Colunas e tabelas marcadas com ➕ foram acrescentadas pela implementação; o isolamento (RLS por `TenantId`, `ENABLE ROW LEVEL SECURITY`, papel da aplicação sem superuser) vale para todas as tabelas com `TenantId`.

**Schema: `tenant`**

* `Tenants` (Id, Name, IsActive, CreatedAt, ➕ Email \[único, minúsculo; contato do dono\]) — diretório de lojistas, sem RLS. **[1.4.0]** Não há senha nem hash: credenciais vivem no provedor de identidade.

* ➕ `AuditEvents` (Id, TenantId, OccurredAt, ActorId, Action, Target, Details; índice por TenantId+OccurredAt) **[1.5.0]** — trilha imutável com RLS (ver 4.7).

* ➕ `TenantUsers` (Id, TenantId, ExternalUserId \[único; `sub` do provedor\], Email, Role \[Owner\], CreatedAt) **[1.4.0]** — vínculo usuário do provedor ↔ lojista; permite vários usuários por lojista.

* `TenantSettings` (TenantId, HighRiskThreshold, AutoBlockEnabled)

* ➕ `PlatformConnections` (Id, TenantId, Platform, AccountId, WebhookSecretProtected, AccessTokenProtected, CreatedAt, UpdatedAt) — segredos cifrados (AES-256-GCM); único por (TenantId, Platform).

**Schema: `ingestion`**

* `RawWebhooks` (Id, TenantId, Platform, Headers \[jsonb, sem credenciais\], Payload \[jsonb\], Status \[Received, Processed, Failed\], ➕ ReceivedAt, ➕ ProcessedAt, ➕ Attempts, ➕ LastError, ➕ PayloadPurgedAt) — o conteúdo é apagado por retenção/anonimização (ver 4.5).

* `Orders` (Id, TenantId, ExternalOrderId, CustomerEmail, TotalAmount, CreatedAt, ➕ Platform) — único por (TenantId, Platform, ExternalOrderId): idempotência.

**Schema: `risk`**

* `RiskEvaluations` (Id, TenantId, OrderId, Score, RiskLevel \[Low, Medium, High\], ➕ Platform, ➕ ExternalOrderId, ➕ TotalAmount, ➕ CalculatedAt) — único por OrderId; imutável.

* `RiskFactors` (Id, RiskEvaluationId, Reason, PointsAdded, ➕ TenantId) — `TenantId` permite a RLS direta.

* `CustomerHistories` (Id, TenantId, CustomerEmail, TotalRefunds)

**Schema: `action`** ➕

* `OrderActions` (Id, TenantId, EvaluationId \[único\], OrderId, Platform, ExternalOrderId, Score, Type \[Block, Flag\], Status \[Pending, Succeeded, Failed, Skipped, Releasing, Released, ReleaseFailed\], Attempts, LastError, CreatedAt, CompletedAt) — idempotência por avaliação, controle de retry e trilha de auditoria.

**Schema: `hangfire`:** armazenamento do Hangfire (configurável em `Hangfire:SchemaName`).

## 8. Contratos de API (Endpoints)

| Método | Rota | Descrição |
| ----- | ----- | ----- |
| ~~POST~~ | ~~`/api/v1/auth/login`~~ | **[1.4.0] Removido.** O login é feito no provedor de identidade (Auth0, OIDC *Authorization Code + PKCE*); a API recebe `Authorization: Bearer <access token>` |
| POST | `/api/v1/webhooks/{platform}/{tenantId}` | Recebe o evento da loja virtual (exige assinatura HMAC válida; responde 202) |
| GET | `/api/v1/risk/evaluations` | Lista pedidos e scores (Dashboard) |
| GET | `/hangfire` | Painel visual de Background Jobs (apenas em Development até existir papel de administrador) |
| GET | `/api/v1/tenants/me` ➕ | Dados do lojista autenticado e suas configurações |
| PUT | `/api/v1/tenants/me/settings` ➕ | Altera `highRiskThreshold` (0–100) e `autoBlockEnabled`; invalida o cache |
| GET | `/api/v1/tenants/me/connections` ➕ | Lista as plataformas conectadas (sem segredos) |
| PUT | `/api/v1/tenants/me/connections/{platform}` ➕ | Conecta/reconecta a plataforma (`accountId`, `webhookSecret`, `accessToken`) |
| DELETE | `/api/v1/tenants/me/connections/{platform}` ➕ | Desconecta a plataforma |
| GET | `/api/v1/actions` ➕ | Lista as ações preventivas (paginado; filtro `status`) |
| POST | `/api/v1/actions/{id}/release` ➕ | Libera um pedido bloqueado/sinalizado (assíncrono: 202) |
| GET | `/api/v1/audit-events` ➕ **[1.5.0]** | Trilha de auditoria do lojista (paginado, mais recente primeiro; filtro `action`) |
| POST | `/api/v1/privacy/customers/anonymize` ➕ | Elimina os dados de um comprador (LGPD): body `{ "email": "..." }`, 204, idempotente |
| GET | `/health/live`, `/health/ready` ➕ | Saúde da aplicação, do PostgreSQL e do Redis |

*Fora do contrato HTTP:* o cadastro de lojistas (RF01) e a desativação de um lojista existem como casos de uso (`RegisterTenantCommand`, `DeactivateTenantCommand`) sem rota pública, até que o fluxo administrativo seja definido.

## 9. Plano de Execução (Roteiro OBRIGATÓRIO para a IA)

*Instrução Suprema: Execute o código estritamente fase por fase. Gere Testes de Unidade baseados nos Critérios de Aceite e valide o funcionamento via testes antes de avançar para a próxima fase.*

1. **Fase 0 (DevOps & Qualidade):** Inicie criando a estrutura da Solution (`.sln`), os projetos de Testes (`xUnit`), configure o **Husky.Net** e crie o workflow do **GitHub Actions** (`ci.yml`) contemplando Build, Testcontainers e Code Coverage. Habilite as regras estritas de linting nos `.csproj`.

2. **Fase 1 (Base):** Configuração do Monolito Modular em .NET 8 com CQRS (MediatR), configuração do PostgreSQL (EF Core), Redis (Rate Limiting e Cache) e Hangfire.

3. **Fase 2 (Tenant):** Criação das tabelas, RLS (Row-Level Security), endpoints JWT. Escreva os **Testes de Integração** do fluxo de autenticação via Testcontainers.

4. **Fase 3 (Ingestão):** Endpoint de webhooks (`HTTP 202`), orquestração via Hangfire, e implementação do **Template Method/Strategy**. Escreva **Testes Unitários** para cada plataforma suportada pelo Parser.

5. **Fase 4 (Motor de Risco):** Implementação do padrão **Pipeline/Pipes and Filters**. Escreva **Testes Unitários** usando `FluentAssertions` validando exaustivamente cada implementação de `IRiskRule`.

6. **Fase 5 (Ações):** Consumo de APIs externas (`HttpClientFactory`) e disparo de bloqueios simulados.

   > **[1.2.0] Estado:** Fases 0–5 implementadas e cobertas por testes. A Fase 5 evoluiu de "bloqueios simulados" para integrações no formato das APIs reais (ver 4.3), exercitadas contra plataformas falsas nos testes; **ainda não foram validadas contra lojas reais** (exige apps/credenciais de homologação). A Fase 6 (Frontend) **não foi iniciada**.

7. **Fase 6 (Frontend B2B):**
Na pasta `/frontend`, crie a aplicação web utilizando Next.js (App Router) ou Vite + React.
- Utilize **Tailwind CSS** e **Shadcn/ui** para os componentes visuais.
- Utilize **React Query** para gerenciar o estado assíncrono e consumir as APIs (Login e Dashboard).
- Crie uma tela de Autenticação (Login) protegida.
  > **[1.4.0]** O login não é implementado no RiskGuard: o frontend usa o SDK do provedor (Auth0) com *Authorization Code + PKCE* e envia o token de acesso à API. Ver `docs/auth0-setup.md`.
- Crie o Dashboard Principal exibindo as métricas principais e uma Tabela de Pedidos baseada no endpoint `/api/v1/risk/evaluations`, utilizando "badges" coloridos (Verde, Amarelo, Vermelho) para indicar visualmente o nível de risco (RiskLevel) de cada pedido.
   > **[1.6.0] Estado:** implementada em `/frontend` (Vite + React + TypeScript, Tailwind v4, componentes no estilo shadcn/ui sobre Radix, React Query, React Router). O Dashboard principal (Pedidos) mostra o resumo por nível (totais lidos da API) e a tabela de `/api/v1/risk/evaluations` com badges Verde/Amarelo/Vermelho (sempre com o texto do nível), o score no trajeto com o limite do lojista e os fatores de cada pedido. Além do SDD: telas de **Ações** (liberação com confirmação), **Configurações** (limite, bloqueio automático, lojas conectadas, eliminação de dados de comprador) e **Auditoria** (`/api/v1/audit-events`). Testes com Vitest e Testing Library no CI (`frontend` job). Em aberto: hospedagem e domínio, e validação com o Auth0 real (o modo `npm run dev:demo` usa dados sintéticos).
