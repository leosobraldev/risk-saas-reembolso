# RiskGuard

SaaS B2B de **prevenção de risco de reembolso** para lojas virtuais. A loja envia cada pedido por webhook (hoje **Shopify, Nuvemshop e Wake**; novas plataformas entram como adaptadores, ver [como estender](docs/tecnico/desenvolvimento.md#adicionar-uma-plataforma)); o RiskGuard calcula um score de risco, e, quando o pedido passa do limite do lojista, **bloqueia ou sinaliza** o pedido na própria plataforma. O lojista acompanha tudo em um painel web, libera falsos positivos e consulta uma trilha de auditoria.

> Especificação completa: [`docs/spec/SDD.md`](docs/spec/SDD.md). Este README é o ponto de partida; o SDD é a fonte de verdade das regras.

## Como funciona

1. A plataforma da loja envia o webhook do pedido (com assinatura HMAC, validada pelo RiskGuard).
2. O pedido é gravado, de forma idempotente, e processado em segundo plano (Hangfire, com retry).
3. As regras de risco (pipeline de filtros) calculam o score e o nível: **Baixo, Médio ou Alto**.
4. Se o nível é Alto e o bloqueio automático está ligado, o RiskGuard bloqueia o pedido na loja; se estiver desligado, só sinaliza.
5. O lojista vê pedidos e ações no painel e pode **liberar** um pedido retido por engano.

## Arquitetura

Monolito modular em **.NET 8**, Clean Architecture, com CQRS (MediatR). Cada módulo tem o próprio schema no PostgreSQL e só depende de `Core` e `Infrastructure`: os módulos conversam por contratos e eventos, nunca por acesso direto ao banco do outro (há um teste de arquitetura que garante isso).

```
src/
  RiskGuard.Api/             composition root: Minimal APIs, middlewares, CLI
  RiskGuard.Core/            abstrações e contratos compartilhados
  RiskGuard.Infrastructure/  EF Core, Redis, Hangfire, Auth0, criptografia
  Modules/
    RiskGuard.Modules.Tenant/      lojistas, configurações, conexões, auditoria
    RiskGuard.Modules.Ingestion/   webhooks, parsers, assinatura HMAC
    RiskGuard.Modules.Risk/        motor de regras e avaliações
    RiskGuard.Modules.Action/      bloqueio, sinalização e liberação nas lojas
frontend/                    painel do lojista (Vite + React)
tests/                       unitários e de integração (Testcontainers)
docs/                        SDD, guia do Auth0, guia de revisão de código
```

**Isolamento entre lojistas** em duas camadas: filtro global do EF Core e *Row-Level Security* no PostgreSQL. O banco usa papéis separados: o dono (só migrations), `riskguard_app` (API, sujeito à RLS) e `riskguard_jobs` (só o schema do Hangfire).

**Segurança:** assinatura HMAC nos webhooks, segredos cifrados com AES-256-GCM, proteção contra SSRF, rate limit por IP no Redis, cabeçalhos de segurança, CORS fechado por padrão, retenção e anonimização de dados pessoais (LGPD) e **trilha de auditoria imutável** (o papel da aplicação nunca consegue alterar o histórico). Detalhes nas seções 4.3 a 4.7 do SDD.

**Identidade:** delegada ao **Auth0**. A API só valida o token (JWT RS256) e lê o lojista de um claim; não há senha, login nem MFA no código. Guia de configuração: [`docs/auth0-setup.md`](docs/auth0-setup.md).

## Como rodar localmente

Requisitos: .NET 8 SDK, Docker e Node 24 (para o painel).

```bash
# 1. PostgreSQL e Redis
docker compose up -d

# 2. API (aplica as migrations ao subir, só em Development)
dotnet run --project src/RiskGuard.Api
# http://localhost:5080
```

A API precisa dos dados do Auth0 (`Identity:*`) para validar tokens e cadastrar lojistas. Guarde-os em `user-secrets`, nunca em arquivo versionado; o passo a passo está em [`docs/auth0-setup.md`](docs/auth0-setup.md).

**Cadastrar um lojista** (não há rota pública de cadastro; é uma operação do dono da plataforma):

```bash
dotnet run --project src/RiskGuard.Api -- --provision-tenant "Nome da Loja" dono@loja.com
```

O comando cria o lojista e o usuário no Auth0 e imprime um link de uso único para o dono definir a senha.

**Painel web:**

```bash
cd frontend
npm install
cp .env.example .env.local   # dados públicos do app SPA no Auth0
npm run dev                  # http://localhost:5173
```

Para ver o painel sem Auth0 nem API, com dados sintéticos: `npm run dev:demo`. A origem do painel precisa estar em `Cors:AllowedOrigins` da API. Mais detalhes em [`frontend/README.md`](frontend/README.md).

## Testes

```bash
# backend (os de integração usam Testcontainers: Docker precisa estar rodando)
dotnet test RiskGuard.sln

# gate de cobertura do módulo de risco (mínimo de 90%)
pwsh ./scripts/Test-RiskCoverage.ps1 -Threshold 90

# frontend
cd frontend && npm test
```

O CI (GitHub Actions, em pull requests para `main`) roda: restauração, checagem de pacotes vulneráveis, `dotnet format`, build com avisos como erro, testes com cobertura e o gate de 90%, além do lint, typecheck, testes e build do frontend. O hook de pré-commit (Husky.Net) confere o formato e roda os testes unitários.

## Documentação

| Documento | Conteúdo |
| --- | --- |
| [`docs/usuario/guia-do-usuario.md`](docs/usuario/guia-do-usuario.md) | **Guia do usuário** (lojista): como o risco é calculado, conectar a loja, usar o painel, perguntas frequentes |
| [`docs/tecnico/`](docs/tecnico/README.md) | **Documentação técnica**: [arquitetura](docs/tecnico/arquitetura.md), [API](docs/tecnico/api.md), [configuração e operação](docs/tecnico/configuracao-e-operacao.md), [desenvolvimento](docs/tecnico/desenvolvimento.md) |
| [`docs/spec/SDD.md`](docs/spec/SDD.md) | Especificação: requisitos, regras, segurança, modelo de dados, contratos de API |
| [`docs/auth0-setup.md`](docs/auth0-setup.md) | Configuração do Auth0 (API, aplicação, Action, painel) |
| [`docs/engineering/code-review-and-quality.md`](docs/engineering/code-review-and-quality.md) | Guia de revisão de código e qualidade |
| [`PRODUCT.md`](PRODUCT.md) e [`DESIGN.md`](DESIGN.md) | Produto e sistema visual do painel |

## Estado do projeto

Fases 0 a 6 do SDD implementadas (backend, trilha de auditoria e painel). Em aberto:

- Validação das integrações contra lojas reais (hoje exercitadas contra plataformas simuladas nos testes).
- Fluxo de instalação por OAuth das plataformas e GraphQL da Shopify.
- Hospedagem e domínio do painel.
- Validação do painel com o Auth0 real.
