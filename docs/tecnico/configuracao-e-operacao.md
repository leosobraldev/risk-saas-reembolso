# Configuração e operação

## 1. Como a configuração funciona

A API lê `appsettings.json`, `appsettings.{Ambiente}.json`, `user-secrets` (**só em Development**), variáveis de ambiente e argumentos. Em variável de ambiente, `:` vira `__` (ex.: `Identity__Auth0__Domain`). Toda seção é **validada na subida**: valor inválido faz a API não iniciar, com a mensagem no log.

**Segredos nunca vão no Git.** Em desenvolvimento use `dotnet user-secrets`; em produção, o gerenciador de segredos da plataforma (por exemplo, Secrets Manager injetado como variável de ambiente).

## 2. Referência de configuração

### Banco de dados (`Postgres`)

| Chave | Obrigatória | Descrição |
| --- | --- | --- |
| `ConnectionString` | sim | Conexão do papel **`riskguard_app`** (runtime, sujeito à RLS). |
| `JobsConnectionString` | recomendada | Conexão do papel `riskguard_jobs` (só o schema do Hangfire). Sem ela, o Hangfire usa a conexão da API e a API registra um aviso. |
| `AdminConnectionString` | só nas migrations | Conexão do **dono** do schema. Use apenas no passo `--migrate`; **não a configure no processo da API** (a API avisa se encontrar). |
| `MigrateOnStartup` | não (padrão `false`) | Aplica as migrations ao subir. Só em desenvolvimento. |

### Redis (`Redis`)

| Chave | Descrição |
| --- | --- |
| `ConnectionString` | ex.: `redis:6379` |
| `InstanceName` | prefixo das chaves (padrão `riskguard:`) |

Usado para o cache do lojista (limite, bloqueio, ativo) e para o limite de taxa. Sem Redis a API continua no ar: o limite de taxa deixa de valer (fail-open) e o cache cai para o banco.

### Segurança (`Security`)

| Chave | Descrição |
| --- | --- |
| `EncryptionKey` | **Base64 de exatamente 32 bytes** (AES-256-GCM). Gere com `openssl rand -base64 32`. Cifra os segredos das conexões. **Não há rotação automática**: trocar a chave torna os segredos já gravados ilegíveis, e as conexões precisam ser cadastradas de novo. Guarde-a com o mesmo cuidado de uma senha de banco. |

### Identidade (`Identity`)

| Chave | Descrição |
| --- | --- |
| `Audience` | Identificador da API no Auth0 (ex.: `https://api.riskguard.app`). |
| `TenantClaim` | Nome do claim do lojista (padrão `https://riskguard.app/tenant_id`); tem de ser igual ao da Action do Auth0. |
| `Authority` | Opcional. Se vazia, é derivada do domínio do Auth0. Sempre HTTPS. |
| `Auth0:Domain` | Domínio do tenant, **sem** `https://` (ex.: `meu-tenant.us.auth0.com`). |
| `Auth0:ClientId` / `Auth0:ClientSecret` | Aplicação **Machine to Machine** (escopos `create/update/delete:users` e `create:user_tickets`). |
| `Auth0:Connection` | Conexão de usuários (padrão `Username-Password-Authentication`). |
| `Auth0:PasswordTicketTtlSeconds` | Validade do link de definição de senha (padrão 432000 = 5 dias). |

Passo a passo: [`docs/auth0-setup.md`](../auth0-setup.md).

### Borda HTTP

| Chave | Descrição |
| --- | --- |
| `Cors:AllowedOrigins` | Lista de origens do painel (ex.: `https://painel.exemplo.com`). Vazia = CORS fechado. Permite `GET`, `POST`, `PUT`, `DELETE`, sem cookies. |
| `Https:RedirectToHttps` | Redireciona HTTP para HTTPS. Deixe `false` atrás de um balanceador que já termina o TLS. |
| `Proxy:KnownProxies` / `Proxy:KnownNetworks` | IPs e redes (CIDR) dos proxies confiáveis. **Sem isso, `X-Forwarded-*` é ignorado** e a API vê o IP do balanceador. Configure sempre que houver proxy reverso (ALB, Cloudflare Tunnel, nginx), senão o limite por IP trata todos os clientes como um só. |

### Limite de taxa (`RateLimiting`)

| Chave | Padrão | Faixa | Descrição |
| --- | --- | --- | --- |
| `PermitLimit` | 100 | 1 a 1.000.000 | requisições por janela, por IP, nas rotas `/api` |
| `WindowSeconds` | 60 | 1 a 3600 | tamanho da janela |
| `WebhookIpPermitLimit` | 3000 | 1 a 10.000.000 | por IP nas rotas de webhook |
| `WebhookTenantPermitLimit` | 600 | 1 a 10.000.000 | por lojista na ingestão |

### APIs das plataformas (`PlatformApis`)

| Chave | Descrição |
| --- | --- |
| `TimeoutSeconds` | Timeout das chamadas às lojas (1 a 120, padrão 10). |
| `UserAgent` | A Nuvemshop exige o nome do app e um **contato real**. Ex.: `RiskGuard/1.0 (suporte@suaempresa.com)`. |
| `Platforms:{plataforma}:BaseUrl` | Opcional: sobrescreve a URL da API (homologação, testes). Vazio = URL real (Wake: `https://api.fbits.net/`). |
| `Platforms:shopify:ApiVersion` | Versão da API da Shopify. Versões são aposentadas periodicamente: mantenha em configuração. |

### Retenção (`Retention`) e jobs (`Hangfire`)

| Chave | Padrão | Descrição |
| --- | --- | --- |
| `Retention:Enabled` | `true` | Liga a limpeza recorrente. |
| `Retention:RawPayloadDays` | 30 | Dias de guarda do conteúdo bruto dos webhooks (dados pessoais). Depois disso o conteúdo é apagado e ficam só os metadados. Webhooks pendentes nunca são apagados. |
| `Retention:AuditDays` | 365 | Guarda da auditoria. **Mínimo 180** (imposto também pelo banco). |
| `Retention:Cron` | `0 3 * * *` | Horário (UTC) das duas limpezas. |
| `Hangfire:WorkerCount` | 5 | Workers por processo (1 a 100). |
| `Hangfire:MaxRetryAttempts` | 5 | Tentativas automáticas (0 a 20). |
| `Hangfire:ServerEnabled` | `true` | Desligue para uma instância "só API" (sem processar jobs). |
| `Hangfire:SchemaName` | `hangfire` | Instâncias com schemas diferentes não dividem filas. |

## 3. Comandos da API (CLI)

```bash
# aplica as migrations (use a conexão do dono; termina ao acabar)
dotnet RiskGuard.Api.dll --migrate

# cadastra um lojista e o usuário dono no Auth0; imprime o link de uso único para definir a senha
dotnet RiskGuard.Api.dll --provision-tenant "Nome da Loja" dono@loja.com
```

O link impresso é uma **credencial de uso único**: entregue-o só ao dono. Se o cadastro no banco falhar depois de criar o usuário no Auth0, o usuário é removido (compensação); se a compensação também falhar, o log pede a remoção manual.

Desativar um lojista (`DeactivateTenantCommand`) é uma operação administrativa: o efeito na API é imediato e os usuários dele são bloqueados no Auth0 (melhor esforço).

## 4. Execução com Docker

O repositório traz um ambiente completo local:

```bash
cp .env.example .env     # preencha (o .env não é versionado)
docker compose up -d --build
```

| Serviço | Papel | Porta |
| --- | --- | --- |
| `postgres` | PostgreSQL 16; o `docker/postgres/init-roles.sql` cria os papéis `riskguard_app` e `riskguard_jobs` na **primeira** inicialização do volume | 5432 |
| `redis` | Redis 7 | 6379 |
| `migrate` | roda `--migrate` com a credencial do dono e termina | |
| `api` | API em **Production** (painel do Hangfire negado), sem a credencial do dono | 5080 |
| `web` | painel estático servido pelo nginx | 5173 |

A imagem da API usa usuário sem privilégios e escuta na porta 8080. As credenciais de banco do compose são **só de desenvolvimento**.

## 5. Implantação em produção (checklist)

1. **PostgreSQL 16** gerenciado, em rede privada; crie os papéis `riskguard_app` e `riskguard_jobs` (sem superuser, sem `BYPASSRLS`) com senhas fortes, conforme `docker/postgres/init-roles.sql`.
2. **Redis 7** gerenciado, em rede privada.
3. **Auth0 de produção** (tenant próprio): API, aplicação M2M, aplicação SPA, Action de login, *Disable Sign Ups*. Veja o guia.
4. **Segredos** no gerenciador da plataforma: `Security__EncryptionKey`, strings de conexão, `Identity__Auth0__ClientSecret`.
5. **Migrations** como passo separado antes de cada deploy (`--migrate`, com a conexão do dono). Em seguida suba a API **sem** `AdminConnectionString` e com `MigrateOnStartup=false`.
6. **Borda:** HTTPS no balanceador, `Proxy__KnownNetworks__0` com a rede do balanceador, `Cors__AllowedOrigins__0` com o domínio do painel, `PlatformApis__UserAgent` com um contato real.
7. **Painel:** `npm run build` e hospedagem estática (S3 + CloudFront, Netlify, Cloudflare Pages ou o container nginx). As variáveis `VITE_*` são públicas e entram no bundle em tempo de build.
8. **Webhooks:** o ALB/proxy precisa aceitar `POST` público em `/api/v1/webhooks/*`.
9. **Réplicas:** 1 ou 2 tasks no início; o Hangfire coordena pelo PostgreSQL e os jobs recorrentes são idempotentes.

Uma API em Production **nega o painel do Hangfire** (`/hangfire` só abre em Development), então não há nada a proteger ali.

## 6. Observabilidade

- **Logs:** vão para `stdout` (o orquestrador coleta). Nível padrão `Information`; os comandos SQL do EF ficam em `Warning`. Webhooks recusados, falhas de jobs e falha de compensação do cadastro aparecem como `Warning` ou `Error`. **E-mails de compradores e segredos nunca são logados.**
- **Saúde:** `/health/live` para *liveness*; `/health/ready` (inclui PostgreSQL e Redis) para *readiness*.
- **Estado dos webhooks e jobs no banco:**

```sql
-- webhooks por status (Received = aguardando, Processed, Failed)
select "Status", count(*) from ingestion."RawWebhooks" group by 1;
-- avaliações por nível
select "Level", count(*) from risk."RiskEvaluations" group by 1;
-- ações por tipo e situação
select "Type", "Status", count(*) from action."OrderActions" group by 1, 2;
```

- **Auditoria:** quem mudou configurações, conexões, liberou pedidos ou pediu anonimização: `GET /api/v1/audit-events` ou a tela *Auditoria* do painel.

## 7. Dados e retenção

- **Dados pessoais:** o e-mail do comprador fica em `ingestion.Orders`, no payload bruto e em `risk.CustomerHistories`. O conteúdo bruto é apagado após `Retention:RawPayloadDays`. A anonimização sob demanda troca o e-mail por um token irreversível e apaga os payloads que o citam.
- **Backup:** faça `pg_dump` (ou o snapshot do serviço gerenciado) do banco. A auditoria e as avaliações são a parte "histórica"; as filas do Hangfire podem ser reconstruídas.
- **Segredos cifrados:** o backup traz os segredos das conexões **cifrados**; sem a `Security:EncryptionKey` correspondente eles não servem. Guarde a chave separadamente.

## 8. Problemas comuns

| Sintoma | Causa provável | O que fazer |
| --- | --- | --- |
| API não sobe, log de validação de `Identity:*` | Falta `Identity:Audience`, `Auth0:Domain`, `ClientId` ou `ClientSecret` | Configure e reinicie. |
| `401` em todas as rotas | `Identity:Audience` diferente do *Identifier* da API, ou domínio errado | Confira com o painel do Auth0. |
| `403` com token válido | A Action não está no fluxo de login, ou o claim não bate com `Identity:TenantClaim`, ou o usuário não tem `app_metadata.tenant_id` | Veja a seção 9 de `docs/auth0-setup.md`. |
| `403 tenant.inactive` | Lojista desativado ou usuário órfão | Reative o lojista ou cadastre-o de novo. |
| Painel com erro de CORS | A origem do painel não está em `Cors:AllowedOrigins` | Adicione e reinicie. |
| Webhook responde `401` | Segredo diferente do cadastrado na conexão, ou corpo alterado no caminho | Confira o segredo; a assinatura vale sobre os bytes exatos do corpo. |
| Webhook responde `404` | Lojista inexistente, plataforma desconhecida ou **sem conexão** cadastrada | Cadastre a conexão em *Configurações*. |
| Wake: webhook `202` mas o pedido não aparece | Token de acesso da API inválido, ou tópico diferente de `pedido.novo` (esses são ignorados) | Veja o webhook em `ingestion.RawWebhooks` (`LastError`); confira o token e o tópico inscrito. |
| Webhook responde `429` | Passou de 600/min por lojista ou do limite por IP | Veja `Retry-After`; ajuste `RateLimiting:*` se for esperado. |
| Rate limit trata todos como um só IP | `Proxy:*` não configurado atrás do balanceador | Configure `Proxy:KnownNetworks`. |
| Webhooks `Received` não saem da fila | Hangfire parado (`ServerEnabled=false`, sem workers ou sem acesso ao schema) | Confira `Hangfire:*` e o papel `riskguard_jobs`. |
| Ação `Failed` ou `ReleaseFailed` | Token de acesso inválido, loja fora do ar ou recusa da plataforma | Leia `lastError` na tela *Ações*; corrija o token e libere de novo. |
| Segredos de conexão "sumiram" | `Security:EncryptionKey` foi trocada | Restaure a chave antiga ou cadastre as conexões de novo. |
| Primeira requisição lenta após reiniciar | Aquecimento da aplicação | Normal; as seguintes voltam ao tempo habitual. |
