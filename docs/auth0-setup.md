# Configuração do Auth0 para o RiskGuard

O RiskGuard **não guarda senhas, não emite tokens e não implementa MFA, renovação de sessão, recuperação de senha nem detecção de
força bruta**. Tudo isso é do Auth0. O RiskGuard faz só duas coisas com a identidade:

1. **Valida o token de acesso** (JWT RS256) emitido pelo Auth0, usando o emissor e o JWKS descobertos em
   `https://{DOMINIO}/`. As chaves de assinatura giram sozinhas: não há chave para guardar nem para rotacionar.
2. **Provisiona e bloqueia usuários** pela Management API (cadastro do lojista e desativação).

> Os passos abaixo seguem a documentação do Auth0 na data desta escrita. Os nomes de menus mudam; confira no painel.

## 1. Tenant e API

1. Crie um tenant do Auth0 (escolha a região mais próxima; para a LGPD, avalie a localização dos dados).
2. **Applications → APIs → Create API**
   - **Identifier** (é a *audience*): por exemplo `https://api.riskguard.app`. Não precisa resolver como URL.
   - **Signing Algorithm:** `RS256`.
   - Em *Settings*, deixe o **Access Token Lifetime curto** (sugestão: 900 s = 15 min). Tokens de acesso não podem ser revogados
     depois de emitidos, então o tempo de vida é o seu limite de exposição. A desativação de um lojista vale na hora na API
     (veja `ActiveTenantMiddleware`), independentemente do tempo do token.

## 2. Aplicação Machine-to-Machine (backend → Management API)

1. **Applications → Create Application → Machine to Machine** (nome sugerido: `RiskGuard Backend`).
2. Autorize-a na **Auth0 Management API** com **somente** estes escopos:
   `create:users`, `update:users`, `delete:users`, `create:user_tickets`.
3. Guarde o **Client ID** e o **Client Secret** (o segredo vai para o gerenciador de segredos, nunca para o Git).

## 3. Conexão de banco de dados (usuários e senhas)

Em **Authentication → Database → Username-Password-Authentication**:

- **Disable Sign Ups: ligado.** Só o RiskGuard cria usuários (cadastro de lojista). Sem isso qualquer pessoa se cadastra no seu login.
- **Password Policy:** *Good* ou *Excellent*.
- Habilite a aplicação do frontend (quando existir) nesta conexão.

## 4. Proteções do Auth0 (sem código no RiskGuard)

- **Security → Attack Protection:** *Brute-force Protection*, *Suspicious IP Throttling* e *Breached Password Detection* ligados.
- **Security → Multi-factor Auth:** habilite *One-time Password* (TOTP) e, se quiser, *WebAuthn* (passkeys/chaves de segurança) e
  defina a política (por exemplo, *Require* para todos, ou *Adaptive*).
- **Monitoring → Logs / Log Streams:** os eventos de login, MFA e senha ficam aqui; configure um *log stream* para guardá-los pelo
  prazo que você precisar (a auditoria de **negócio** do RiskGuard é separada).

## 5. Action de login: levar o lojista para o token

O RiskGuard grava o id do lojista em `app_metadata.tenant_id` ao criar o usuário. O token de acesso precisa carregá-lo em um claim
**com namespace** (o Auth0 descarta claims privados sem namespace em tokens de API).

**Actions → Library → Build Custom → Trigger: Login / Post Login** e cole:

```js
exports.onExecutePostLogin = async (event, api) => {
  const namespace = 'https://riskguard.app/';

  // app_metadata so pode ser alterado por servidor (nunca pelo proprio usuario). NAO use user_metadata aqui.
  const tenantId = event.user.app_metadata && event.user.app_metadata.tenant_id;

  if (!tenantId) {
    api.access.deny('Usuario sem lojista associado.');
    return;
  }

  api.accessToken.setCustomClaim(namespace + 'tenant_id', tenantId);
};
```

Faça o **Deploy** e arraste a Action para **Actions → Flows → Login**. O nome do claim (`https://riskguard.app/tenant_id`) tem de ser
igual a `Identity:TenantClaim` na configuração do RiskGuard. O RiskGuard converte esse claim no claim interno `tenant_id`; um token que
já traga `tenant_id` "cru" é ignorado (o claim interno só nasce da conversão).

> Se outros usuários do mesmo tenant do Auth0 não pertencem a lojistas (por exemplo, administradores), o `deny` os bloqueia nesta API.
> Nesse caso troque o `deny` por um `return` e deixe o RiskGuard recusar (sem o claim, a API responde 403).

## 6. Configurar o RiskGuard

Use **user-secrets** em desenvolvimento (a pasta do projeto da API) e variáveis de ambiente/gerenciador de segredos em produção:

```bash
cd src/RiskGuard.Api
dotnet user-secrets set "Identity:Auth0:Domain" "minha-empresa.us.auth0.com"
dotnet user-secrets set "Identity:Audience" "https://api.riskguard.app"
dotnet user-secrets set "Identity:Auth0:ClientId" "<client id da aplicacao M2M>"
dotnet user-secrets set "Identity:Auth0:ClientSecret" "<client secret da aplicacao M2M>"
```

Em produção os mesmos valores vêm de `Identity__Auth0__Domain`, `Identity__Audience`, `Identity__Auth0__ClientId`,
`Identity__Auth0__ClientSecret`. Sem eles a API **não sobe** (a configuração é validada na inicialização).

`Identity:TenantClaim`, `Identity:Auth0:Connection` e `Identity:Auth0:PasswordTicketTtlSeconds` têm padrões no `appsettings.json`.

## 7. Cadastrar o primeiro lojista

Não há rota pública de cadastro. O cadastro é uma operação sua (dono da plataforma), por linha de comando:

```bash
dotnet run --project src/RiskGuard.Api -- --provision-tenant "Loja Exemplo" dono@loja.com
```

Isso cria o lojista, as configurações padrão, o usuário dono no Auth0 (sem senha conhecida, com `app_metadata.tenant_id`) e imprime um
**link de uso único** para o dono definir a senha. O link é uma credencial: entregue-o só ao dono. Se ele não for gerado, use
*User Management → Users → ... → Send password reset email* no painel do Auth0.

Se o Auth0 estiver indisponível ou a criação no banco falhar, o cadastro é desfeito (o usuário recém-criado no Auth0 é removido).

## 8. Obter um token para testar a API

O token de usuário vem do fluxo *Authorization Code + PKCE* (Universal Login). Para testes manuais, crie uma aplicação **Single Page
Application** no Auth0 com callback `https://oauth.pstmn.io/v1/callback` e use o Postman (*OAuth 2.0 → Authorization Code with PKCE*,
informando `audience`). Tokens de *Machine to Machine* **não** têm o claim de lojista e a API os recusa (403).

## 9. Se algo falhar

| Sintoma | Causa provável |
|---|---|
| `401` em tudo | `Identity:Audience` diferente do *Identifier* da API, ou `Identity:Auth0:Domain` errado |
| `403` com token válido | A Action não está no fluxo de Login, ou o nome do claim difere de `Identity:TenantClaim`, ou o usuário não tem `app_metadata.tenant_id` |
| `403 tenant.inactive` | O lojista foi desativado (ou o usuário é órfão: existe no Auth0 e não no RiskGuard) |
| `503 identity.unavailable` ao cadastrar | Credenciais M2M erradas, escopos faltando ou Auth0 fora do ar |
| API não sobe | Veja a mensagem de validação de `Identity:*` no log de inicialização |

## O que é do RiskGuard e o que é do Auth0

| Do Auth0 | Do RiskGuard |
|---|---|
| Senhas, política de senha, senhas vazadas | Vínculo usuário ↔ lojista (`tenant.TenantUsers`) |
| MFA (TOTP, WebAuthn), recuperação de conta | Autorização (policy `Tenant`) e isolamento por tenant (EF + RLS) |
| Emissão, renovação e rotação de chaves dos tokens | Validação do JWT e conversão do claim do lojista |
| Detecção de força bruta e IPs suspeitos | Rate limit por IP da API |
| Logs de autenticação | Auditoria de negócio (configurações, conexões, anonimização, liberação) |

## Trocar de fornecedor depois

O código específico do Auth0 está em `src/RiskGuard.Infrastructure/Identity/Auth0/`. A API depende apenas da interface
`IIdentityProvider` (Core) e da validação OIDC genérica. Para outro provedor: um novo adaptador de `IIdentityProvider`, um `case` em
`IdentityAuthentication.AddIdentity` e as opções do novo fornecedor. A validação do token é padrão OIDC (`Authority` + JWKS).
