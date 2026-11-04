# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Stack
Vite + React (SPA) em `/frontend`, escolhido pelo usuário. Tailwind CSS, shadcn/ui e TanStack React Query (exigidos pelo SDD, fase 6). Login pelo SDK SPA do Auth0 (Authorization Code + PKCE). A API é o backend .NET deste repositório, em outra origem.

## Users
Dono ou operador de uma loja virtual (Shopify, Nuvemshop) que vende B2B/B2C e sofre com pedidos fraudulentos e reembolsos. Abre o painel no dia a dia, em sessões curtas, quase sempre no desktop e às vezes no celular. Interface em português do Brasil. Cada usuário pertence a um único lojista (tenant) e só vê os dados dele.

## Product Purpose
RiskGuard calcula o risco de reembolso de cada pedido assim que o webhook da loja chega, e age: bloqueia ou sinaliza pedidos de risco alto na plataforma. O painel existe para o lojista ver o que foi avaliado e feito, liberar falsos positivos, ajustar o limite de risco e o bloqueio automático, conectar as plataformas e consultar quem mudou o quê. Sucesso: o lojista confia no que o sistema fez e corrige um falso positivo em poucos cliques.

## Positioning
Atua antes do reembolso, não depois: avalia o pedido na entrada e pode segurá-lo na própria plataforma da loja, com trilha de auditoria de tudo que foi feito.

## Operating Context
Dados vêm da API do repositório (`/api/v1/...`): avaliações de risco (`/risk/evaluations`), ações preventivas e liberação (`/actions`, `/actions/{id}/release`), configurações e conexões (`/tenants/me/...`), anonimização de comprador (`/privacy/customers/anonymize`) e auditoria (`/audit-events`). Autenticação por token JWT do Auth0 (RS256) com claim de tenant; não há rota de login na API. Níveis de risco: Low, Medium, High. Estados de ação: Pending, Succeeded, Failed, Skipped, Releasing, Released, ReleaseFailed. A liberação é assíncrona (202).

## Capabilities and Constraints
- O SDD exige: tela de login protegida, dashboard com métricas principais e tabela de pedidos a partir de `/api/v1/risk/evaluations`, com badges coloridos (verde, amarelo, vermelho) por RiskLevel.
- A API é paginada (máximo 100 por página), isolada por lojista, e responde ProblemDetails com `code`.
- Segredos de conexão (webhook, token de acesso) só entram; nenhuma resposta os devolve.
- CORS da API é fechado por padrão: o frontend precisa estar em `Cors:AllowedOrigins`.
- Ainda não definido: hospedagem do frontend e domínio; Dockerfile do frontend; suporte a mais de um usuário por lojista na interface.

## Brand Commitments
Nome: RiskGuard. Idioma: português do Brasil. Não há logo, paleta ou tipografia existentes; a identidade visual é decisão da fase de design.

## Evidence on Hand
Nenhum cliente, depoimento, número de mercado ou logo real. Não inventar métricas: todo número exibido vem da API. Contratos e regras em `docs/spec/SDD.md` e `docs/auth0-setup.md`.

## Product Principles
1. Confiança antes de beleza: o lojista precisa entender por que um pedido foi marcado (fatores do score) e o que o sistema fez.
2. Corrigir rápido: liberar um falso positivo é a ação mais importante e deve ser clara e segura.
3. Nunca mostrar o que não veio da API: sem números ou estados inventados.
4. Segurança visível, sem alarme: o painel nunca exibe segredos e deixa claro quando algo falhou.

## Accessibility & Inclusion
Português do Brasil. Contraste e navegação por teclado adequados (WCAG AA como piso). O risco nunca é comunicado só por cor: o badge também traz o texto do nível.
