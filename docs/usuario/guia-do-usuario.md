# Guia do usuário do RiskGuard

Este guia é para o **lojista**: quem usa o painel do RiskGuard para acompanhar o risco de reembolso dos pedidos da loja. Não precisa de conhecimento técnico.

**Neste guia**

1. [O que é o RiskGuard](#1-o-que-é-o-riskguard)
2. [Como o risco é calculado](#2-como-o-risco-é-calculado)
3. [Primeiros passos](#3-primeiros-passos)
4. [Conectar a sua loja](#4-conectar-a-sua-loja)
5. [Usar o painel](#5-usar-o-painel)
6. [Perguntas frequentes](#6-perguntas-frequentes)
7. [Glossário](#7-glossário)

---

## 1. O que é o RiskGuard

O RiskGuard avalia **cada pedido da sua loja assim que ele chega** e dá uma nota de risco de reembolso, de 0 a 100. Se a nota passa do limite que você definiu, ele pode **reter o pedido** na plataforma da loja, para você conferir antes de enviar, ou apenas **avisar** você.

Ele funciona com a **Shopify**, a **Nuvemshop** e a **Wake**. Outras plataformas podem ser adicionadas: fale com a equipe do RiskGuard.

**O que ele faz**

- Mostra todos os pedidos avaliados, com a nota, o nível de risco e **o motivo** da nota.
- Retém (Shopify) ou marca com uma nota interna (Nuvemshop) os pedidos de risco alto, se você ligar o bloqueio automático. Na **Wake**, por enquanto, ele **avalia e recomenda**, mas não retém o pedido.
- Deixa você **liberar** com um clique um pedido que foi retido por engano.
- Guarda um **histórico de tudo que mudou** na sua conta (auditoria).

**O que ele não faz**

- Não cancela pedidos, não estorna pagamentos e não fala com o seu cliente.
- Não decide por você: a nota é um sinal para ajudar na sua decisão.
- Só enxerga o que a plataforma envia no pedido (e-mail, valor, data). Não analisa o pagamento nem o cartão.

---

## 2. Como o risco é calculado

Cada pedido recebe pontos por sinais de alerta. A nota é a **soma dos pontos**, no máximo 100.

| Sinal | Pontos |
| --- | --- |
| **E-mail suspeito**: o endereço tem palavras como `fraude`, `golpe` ou `scam`, ou é de um serviço de e-mail descartável (por exemplo, mailinator, yopmail) | 50 |
| **Valor alto**: pedido de R$ 5.000 ou mais | 10 |
| **Valor muito alto**: pedido de R$ 10.000 ou mais | 20 |
| **Reembolsos anteriores** do mesmo comprador | 10 por reembolso (máximo 40) |

### Os três níveis

O nível depende da nota e do **limite de risco alto** que você escolhe em *Configurações* (o padrão é 80):

| Nível | Quando | Cor |
| --- | --- | --- |
| **Risco Alto** | nota **maior que** o limite | vermelho |
| **Risco Médio** | nota maior que 0 e **a partir da metade do limite** | amarelo |
| **Risco Baixo** | o restante | verde |

**Exemplo com limite 60**

| Pedido | Nota | Nível |
| --- | --- | --- |
| R$ 120, e-mail normal | 0 | Baixo |
| R$ 7.000, e-mail normal | 10 | Baixo (10 é menos que 30, a metade do limite) |
| R$ 200, e-mail com a palavra "fraude" | 50 | Médio (50 está entre 30 e 60) |
| R$ 12.000, e-mail com a palavra "fraude" | 70 | Alto (70 é maior que 60) |

Uma nota **igual** ao limite **não** é risco alto: precisa ser maior.

> **Dica:** quanto **menor** o limite, mais pedidos são tratados como de risco alto. Comece com um limite mais alto e ajuste conforme observar os pedidos.

---

## 3. Primeiros passos

### Receber o acesso

A equipe do RiskGuard cadastra a sua loja e você recebe um **link de uso único** para criar a sua senha. O link vale por alguns dias e funciona **uma vez**: não o compartilhe. Se ele vencer, peça outro à equipe.

### Entrar

1. Abra o endereço do painel que a equipe informou.
2. Clique em **Entrar**. Você vai para a tela de login segura.
3. Informe o e-mail e a senha. Você volta ao painel, na tela **Pedidos**.

Para sair, use **Sair**, no canto da tela.

### Se você não consegue entrar

| Mensagem ou situação | O que fazer |
| --- | --- |
| "Esqueci a senha" | Na tela de login, use *Esqueci minha senha*. |
| "Sua conta ainda não está vinculada a uma loja" | Fale com a equipe do RiskGuard: o seu usuário não foi ligado a uma loja. |
| "Esta loja está desativada" | Fale com a equipe do RiskGuard. |
| "Sua sessão expirou" | Entre de novo. A sessão é renovada sozinha enquanto você usa o painel. |

---

## 4. Conectar a sua loja

O RiskGuard só avalia pedidos de lojas **conectadas**. A conexão tem duas partes: a loja **envia** os pedidos para o RiskGuard (webhook), e, se você quiser que ele **aja** nos pedidos, o RiskGuard recebe uma permissão para falar com a loja (token de acesso).

> Os nomes de menus e permissões das plataformas mudam com o tempo. Em caso de dúvida, consulte a ajuda da Shopify ou da Nuvemshop.

### O que você vai precisar

| Informação | Para quê | Onde conseguir |
| --- | --- | --- |
| **Identificador da loja** | dizer ao RiskGuard de qual loja são os pedidos | Shopify: o domínio `minha-loja.myshopify.com`. Nuvemshop: o número da sua loja. Wake: o nome da loja (o campo `loja` que a Wake envia no webhook). |
| **Segredo do webhook** | provar que o pedido veio mesmo da sua loja | na tela de criação do webhook, na plataforma |
| **Token de acesso** (opcional) | permitir que o RiskGuard retenha e libere pedidos | criado na plataforma, em um aplicativo próprio da loja |
| **Endereço do webhook** | para onde a loja envia os pedidos | informado pela equipe do RiskGuard |

**Sem o token de acesso**, o RiskGuard continua **avaliando** os pedidos, mas **não consegue reter nada** na loja. É uma boa forma de começar, **desde que o bloqueio automático fique desligado**: com o bloqueio ligado e sem token, as ações aparecem como *Falhou*.

### Passo a passo

1. Na plataforma, crie um webhook de **criação de pedido**, em formato JSON, apontando para o endereço que a equipe informou. Anote o **segredo** que a plataforma mostrar.
2. No painel, abra **Configurações → Lojas conectadas**.
3. No cartão da plataforma, preencha:
   - **Domínio da loja** (Shopify) ou **Id da loja** (Nuvemshop);
   - **Segredo do webhook**;
   - **Token de acesso**, se quiser que o RiskGuard aja nos pedidos.
4. Clique em **Conectar**. Aparece "Conexão salva", e o cartão passa a mostrar *Conectada*.

### Wake: o que é diferente

- **O token de acesso é obrigatório.** A Wake avisa o RiskGuard só com o **número** do pedido; o RiskGuard busca o resto (e-mail e valor) na API da Wake, com o token que você gerar no painel da Wake (*vinculado a um responsável técnico*).
- **A Wake não assina o webhook.** Em vez disso, ao inscrever o webhook você cadastra um **cabeçalho**: use o nome `X-RiskGuard-Token` e, como valor, o mesmo **segredo do webhook** que você informar no RiskGuard.
- **Inscreva só o tópico `pedido.novo`.** Outros tópicos (mudança de situação, rastreamento) são recebidos e ignorados.
- **Na Wake o RiskGuard avalia e recomenda**, mas não retém o pedido: a ação aparece como *Não aplicada*, com a explicação. Você vê o risco na tela *Pedidos* e decide.
- A inscrição do webhook é feita pela API da Wake (`POST https://api.fbits.net/webhook/inscricao`), informando a URL (HTTPS), o tópico e o cabeçalho. Consulte a documentação da Wake para o formato exato do corpo.

### Cuidados

- Os segredos **só entram**: depois de salvos, o painel não os mostra de novo. Para trocá-los, digite os novos e clique em **Substituir conexão**.
- Para parar de receber pedidos de uma loja, clique em **Desconectar**.
- Se a plataforma gerar um novo segredo, atualize-o aqui, ou os pedidos serão recusados.

---

## 5. Usar o painel

O menu lateral tem quatro telas. No celular ele aparece no alto da tela.

### Pedidos

Mostra os pedidos avaliados, **do mais recente ao mais antigo**.

**Resumo por nível.** Três "estações" (Risco Baixo, Médio e Alto) mostram o **total** de pedidos de cada nível. Clique numa estação para filtrar a tabela; clique de novo para limpar. À direita, *Ações com falha* avisa se alguma ação do RiskGuard precisa de revisão.

**Tabela.** Cada linha traz:

| Coluna | O que mostra |
| --- | --- |
| Pedido | o número do pedido e a plataforma |
| Valor | o total do pedido |
| Risco | o nível, sempre com o texto (a cor nunca é o único sinal) |
| Score | a nota, e uma régua de 0 a 100: o **traço vertical** marca o seu limite de risco alto e o **círculo** é o pedido |
| Avaliado | quando o pedido foi avaliado (passe o mouse para ver o horário exato) |

**Por que este score?** Clique na seta no início da linha para ver os **motivos** e os pontos de cada um (por exemplo, "E-mail suspeito, +50").

Você também pode filtrar pelo menu **Nível de risco** e navegar pelas páginas no fim da tabela.

### Ações

Mostra **o que o RiskGuard fez** nos pedidos de risco alto.

| Situação | Significa |
| --- | --- |
| **Aplicada** | o pedido foi retido (Shopify) ou marcado com uma nota interna (Nuvemshop) |
| **Na fila** | a ação está esperando para ser executada |
| **Falhou** | não foi possível agir (por exemplo, token inválido ou ausente, ou a loja não está conectada). A mensagem do erro aparece abaixo do pedido. |
| **Não aplicada** | o pedido passou do limite, mas o bloqueio automático está desligado: o RiskGuard só avisou |
| **Liberando** | você pediu a liberação e ela está sendo feita |
| **Liberada** | o pedido foi liberado |
| **Falha ao liberar** | não foi possível liberar; veja a mensagem e tente de novo |

O tipo da ação é **Bloqueio** (Shopify: o pedido fica retido) ou **Sinalização** (Nuvemshop: o pedido recebe a nota interna `[RiskGuard]`).

**Liberar um pedido retido por engano**

1. Na linha da ação *Aplicada*, clique em **Liberar pedido**.
2. Confirme em **Liberar pedido** (ou **Manter retido** para desistir).
3. A situação muda para *Liberando* e, em instantes, para *Liberada*. A tela se atualiza sozinha.

A liberação fica registrada na **Auditoria**. O RiskGuard só remove a **sua própria** retenção: uma retenção que você mesmo colocou na loja não é removida (a ação aparece como *Falha ao liberar*).

### Configurações

**Regras de risco**

- **Limite de risco alto (0 a 100):** pedidos com nota **maior** que o limite são de risco alto.
- **Bloquear automaticamente pedidos de risco alto:** ligado, o RiskGuard retém esses pedidos na loja; desligado, ele só avisa.
- Clique em **Salvar regras**. A mudança vale para os **próximos pedidos**; os já avaliados não são recalculados.

**Lojas conectadas:** veja [Conectar a sua loja](#4-conectar-a-sua-loja).

**Privacidade dos compradores:** se um comprador pedir a **eliminação dos dados dele** (LGPD), informe o e-mail e clique em **Eliminar dados do comprador**. Os dados pessoais dele são apagados dos pedidos e do histórico de risco, e **não é possível desfazer**. O pedido de eliminação fica na auditoria, sem o e-mail.

### Auditoria

Lista **quem mudou o quê** na sua conta, com data e hora. Dá para filtrar por tipo de evento.

| Evento | Quando acontece |
| --- | --- |
| Regras de risco alteradas | você mudou o limite ou o bloqueio automático (mostra de quanto para quanto) |
| Loja conectada ou atualizada, Loja desconectada | mudanças nas conexões (nunca mostram segredos) |
| Liberação de pedido | você liberou um pedido |
| Eliminação de dados de comprador | você pediu a eliminação |
| Loja cadastrada, Loja desativada | feitos pela equipe do RiskGuard |

A coluna **Quem** mostra *Usuário da loja* (você ou alguém da sua loja) ou *RiskGuard* (a equipe). O registro **não pode ser alterado nem apagado** por ninguém da sua loja.

---

## 6. Perguntas frequentes

**O meu pedido foi marcado como risco alto. Por quê?**
Abra a tela *Pedidos*, clique na seta da linha e veja os motivos. Cada motivo mostra os pontos que somou.

**Retive um pedido por engano. E agora?**
Vá em *Ações* e clique em **Liberar pedido**. Ele volta ao fluxo normal da loja.

**Alterei o limite. Os pedidos antigos mudam de nível?**
Não. A mudança vale para os **próximos** pedidos. As avaliações já feitas não são recalculadas.

**Por que apareceu "Não aplicada"?**
O pedido passou do limite, mas o bloqueio automático está desligado, então o RiskGuard só avisou. Ligue o bloqueio em *Configurações* se quiser que ele retenha os próximos.

**Por que uma ação "Falhou"?**
A mensagem aparece abaixo do pedido, em *Ações*. As causas mais comuns são token de acesso ausente, inválido ou vencido, ou a loja fora do ar. Corrija o token em *Configurações* e libere ou aguarde a próxima tentativa.

**Não aparecem pedidos no painel.**
Confira se a loja está **Conectada** em *Configurações*, se o webhook de pedido existe na plataforma e se o segredo é o mesmo nos dois lados. Se tudo estiver certo, fale com a equipe do RiskGuard.

**Os pedidos aparecem, mas nada é retido na loja.**
Confira se o **bloqueio automático** está ligado (desligado, as ações aparecem como *Não aplicada*) e se a conexão tem o **token de acesso** (sem ele, as ações aparecem como *Falhou*).

**Quem enxerga os dados da minha loja?**
Só os usuários da sua loja. Cada loja vê apenas os próprios dados, e isso é garantido no banco de dados, não só na tela.

**Como pedir a exclusão dos dados de um comprador?**
Em *Configurações → Privacidade dos compradores*, informe o e-mail e confirme.

**Posso ter mais de um usuário na loja?**
O painel ainda não permite convidar outros usuários; fale com a equipe do RiskGuard.

**Quem eu procuro quando algo não funciona?**
A equipe do RiskGuard. Informe o número do pedido, o que apareceu na tela e o horário.

---

## 7. Glossário

| Termo | Significado |
| --- | --- |
| **Score (nota)** | número de 0 a 100 que resume o risco do pedido |
| **Limite de risco alto** | a nota a partir da qual (acima dela) o pedido é de risco alto; você define |
| **Bloqueio automático** | quando ligado, o RiskGuard retém os pedidos de risco alto na loja |
| **Bloqueio** | na Shopify, o pedido fica em espera ("hold") até ser liberado |
| **Sinalização** | na Nuvemshop, o pedido recebe uma nota interna `[RiskGuard]` para você revisar |
| **Falso positivo** | pedido bom que foi marcado como arriscado |
| **Webhook** | aviso automático que a loja envia ao RiskGuard a cada pedido |
| **Segredo do webhook** | "senha" que prova que o aviso veio da sua loja |
| **Token de acesso** | permissão para o RiskGuard falar com a sua loja (reter e liberar pedidos) |
| **Auditoria** | histórico, inalterável, de quem mudou o quê |
| **LGPD** | Lei Geral de Proteção de Dados; garante, entre outros, o direito de eliminar os dados de um titular |
