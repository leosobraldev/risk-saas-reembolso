# RiskGuard: painel do lojista (frontend)

SPA em **Vite + React + TypeScript**, com **Tailwind CSS v4**, componentes no estilo **shadcn/ui** (Radix), **TanStack React Query** e **React Router**. O login é feito no **Auth0** (Authorization Code + PKCE); a API recebe o token de acesso no `Authorization: Bearer`.

Telas: **Pedidos** (avaliações de risco, com o score no trajeto), **Ações** (o que o RiskGuard fez e liberação de falsos positivos), **Configurações** (limite de risco, bloqueio automático, lojas conectadas, eliminação de dados de comprador) e **Auditoria**.

## Requisitos

Node 24 (ou 22+). Com o Volta: `volta pin node@24` na pasta, ou rode os comandos com `volta run --node 24 npm ...`.

## Como rodar

```bash
cd frontend
npm install
cp .env.example .env.local   # preencha com os dados do app SPA no Auth0
npm run dev                  # http://localhost:5173
```

A origem do frontend precisa estar em `Cors:AllowedOrigins` da API, e o Auth0 precisa listá-la em *Allowed Callback URLs*, *Allowed Logout URLs* e *Allowed Web Origins*. Passo a passo em [`docs/auth0-setup.md`](../docs/auth0-setup.md) (seção do frontend).

### Modo demonstração (sem Auth0 nem API)

```bash
npm run dev:demo   # dados SINTÉTICOS em memória, para revisar o visual e os fluxos
```

Existe só em desenvolvimento: não entra no build de produção.

## Scripts

| Comando | O que faz |
| --- | --- |
| `npm run dev` | servidor de desenvolvimento |
| `npm test` | testes (Vitest + Testing Library) |
| `npm run lint` | Oxlint |
| `npm run typecheck` | TypeScript (`tsc -b`) |
| `npm run build` | typecheck + build de produção em `dist/` |

## Decisões

- **Token só em memória** (`cacheLocation: "memory"`) com refresh token rotativo: nada de token em `localStorage`. Se o navegador bloquear cookies de terceiros, recarregar a página pede um novo login (instantâneo se a sessão do Auth0 estiver ativa).
- **Nenhum número inventado**: totais e contagens vêm da API (as contagens do resumo leem o `total` de uma consulta filtrada).
- **Segredos só entram**: as chaves de webhook e tokens de acesso das lojas são campos de senha, limpos depois do envio, e a API nunca os devolve.
- **Mensagens por código de erro**: a interface mostra texto em português por `code` da API e nunca o texto técnico do servidor.
- **Risco nunca só por cor**: cada nível tem texto e símbolo próprios, além da cor da linha (Verde, Amarela, Vermelha).
- `PRODUCT.md` (raiz) registra o produto; `.impeccable/surfaces/` e `DESIGN.md`, quando existirem, registram o design.
