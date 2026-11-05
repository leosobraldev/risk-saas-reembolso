# Documentação técnica do RiskGuard

Para quem desenvolve, opera ou integra o RiskGuard. Para quem **usa** o painel, veja o [guia do usuário](../usuario/guia-do-usuario.md).

| Documento | Conteúdo |
| --- | --- |
| [Arquitetura](arquitetura.md) | Módulos, fluxo do webhook, multi-tenancy, modelo de dados, segurança em camadas |
| [Referência da API](api.md) | Endpoints, autenticação, formatos, códigos de erro, limites de taxa |
| [Configuração e operação](configuracao-e-operacao.md) | Variáveis de ambiente, Docker, deploy, migrations, observabilidade, retenção, problemas comuns |
| [Desenvolvimento](desenvolvimento.md) | Rodar localmente, testes, CI, carga, como estender (plataforma, regra de risco) |

Documentos relacionados:

- [`docs/spec/SDD.md`](../spec/SDD.md): especificação e decisões de projeto (fonte de verdade das regras).
- [`docs/auth0-setup.md`](../auth0-setup.md): configuração do Auth0.
- [`PRODUCT.md`](../../PRODUCT.md) e [`DESIGN.md`](../../DESIGN.md): produto e sistema visual do painel.

## Visão em uma página

RiskGuard recebe o webhook de cada pedido de uma loja virtual (Shopify, Nuvemshop, Wake; outras plataformas entram como adaptadores), calcula um **score de risco de reembolso** (0 a 100) e, se o score passa do limite do lojista, **bloqueia ou sinaliza** o pedido na plataforma da loja. O lojista acompanha tudo em um painel web.

- Backend: **.NET 8**, monolito modular (Clean Architecture, CQRS com MediatR), PostgreSQL 16 com Row-Level Security, Redis 7, Hangfire.
- Frontend: **Vite + React + TypeScript**, Tailwind v4, React Query.
- Identidade: **Auth0** (a API só valida o token JWT).
