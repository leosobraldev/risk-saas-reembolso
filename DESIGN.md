---
name: RiskGuard
description: Painel do lojista onde o risco de reembolso e lido como linhas do Metro de Sao Paulo.
colors:
  ground: "#faf9f6"
  surface: "#ffffff"
  sunken: "#f1efe9"
  rule: "#d9d5ca"
  rule-strong: "#b9b3a4"
  ink: "#14202b"
  ink-muted: "#4f5b69"
  rail: "#0b2a4a"
  rail-hover: "#12406d"
  rail-line: "#27496d"
  rail-ink: "#e8eef5"
  rail-muted: "#a9bbcf"
  rail-tint: "#e3ebf4"
  focus: "#1b66c9"
  line-green: "#007e5e"
  line-green-soft: "#e3f3ee"
  line-yellow: "#ffd100"
  line-yellow-soft: "#fff6cc"
  line-red: "#d71920"
  line-red-soft: "#fdeaea"
typography:
  title:
    fontFamily: "Barlow, ui-sans-serif, system-ui, sans-serif"
    fontSize: "1.5rem"
    fontWeight: 700
    lineHeight: 1.33
    letterSpacing: "-0.025em"
  headline:
    fontFamily: "Barlow, ui-sans-serif, system-ui, sans-serif"
    fontSize: "1.125rem"
    fontWeight: 700
    lineHeight: 1.55
  body:
    fontFamily: "Barlow, ui-sans-serif, system-ui, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.43
  body-strong:
    fontFamily: "Barlow, ui-sans-serif, system-ui, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 600
    lineHeight: 1.43
  label:
    fontFamily: "Barlow Semi Condensed, Barlow, ui-sans-serif, system-ui, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 600
    lineHeight: 1.33
    letterSpacing: "0.025em"
rounded:
  sm: "2px"
  md: "3px"
  lg: "4px"
  full: "9999px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "24px"
  rail-width: "220px"
components:
  button-primary:
    backgroundColor: "{colors.rail}"
    textColor: "{colors.surface}"
    rounded: "{rounded.md}"
    height: "36px"
    padding: "0 16px"
  button-primary-hover:
    backgroundColor: "{colors.rail-hover}"
  button-outline:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.md}"
    height: "36px"
    padding: "0 16px"
  button-danger:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.surface}"
    rounded: "{rounded.md}"
    height: "36px"
    padding: "0 16px"
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.md}"
    height: "36px"
    padding: "0 12px"
  risk-badge-low:
    backgroundColor: "{colors.line-green}"
    textColor: "{colors.surface}"
    rounded: "{rounded.sm}"
    height: "24px"
    padding: "0 8px"
  risk-badge-medium:
    backgroundColor: "{colors.line-yellow}"
    textColor: "{colors.ink}"
    rounded: "{rounded.sm}"
    height: "24px"
    padding: "0 8px"
  risk-badge-high:
    backgroundColor: "{colors.line-red}"
    textColor: "{colors.surface}"
    rounded: "{rounded.sm}"
    height: "24px"
    padding: "0 8px"
  status-badge-succeeded:
    backgroundColor: "{colors.rail}"
    textColor: "{colors.surface}"
    rounded: "{rounded.sm}"
    height: "24px"
    padding: "0 8px"
  nav-rail:
    backgroundColor: "{colors.rail}"
    textColor: "{colors.rail-ink}"
    width: "220px"
  table-head:
    backgroundColor: "{colors.sunken}"
    textColor: "{colors.ink-muted}"
    typography: "{typography.label}"
    height: "40px"
---

# Design System: RiskGuard

## Overview

**Creative North Star: "As Linhas do Metro"**

O risco e uma linha de metro. Os tres niveis sao as linhas Verde (baixo), Amarela (medio) e Vermelha (alto); o score de cada pedido e uma estacao ao longo de um trajeto de 0 a 100, e o limite do lojista e a estacao de conexao. O chao e branco quente, a navegacao e a placa esmaltada azul-marinho da estacao, e tudo o mais e tinta sobre papel: linhas finas e uniformes, cantos de 2 a 4px, cor chapada sem gradiente.

A densidade e de painel de uso diario: tabela compacta, rotulos de estado em caixa de placa, numeros tabulares. Peso e caixa carregam a hierarquia, nao tamanho nem acento. A interface e desktop primeiro; no celular o trilho vira uma faixa horizontal e colunas secundarias somem.

**Key Characteristics:**
- Tres cores de linha que so significam risco; todo o resto e azul-marinho e tinta.
- Cor nunca e o unico sinal: texto do nivel mais simbolo de forma diferente (traco, mais, x).
- Plano por padrao: sem sombra decorativa, sem gradiente, bordas de 1px.
- Grotesca de sinalizacao (Barlow) com Barlow Semi Condensed em caixa alta para rotulos.
- Unico movimento: a linha que muda de estado acende uma vez no lugar.

## Colors

Chao quente, tinta azulada, azul-marinho institucional e tres cores de linha chapadas reservadas ao risco.

### Primary
- **Placa Azul-Marinho** (rail, #0b2a4a): trilho de navegacao, botao primario, selecao de texto, interruptor ligado, estado "Liberado/Sucesso", fundo do overlay de dialogo (a 60%). Hover do botao: rail-hover (#12406d). Divisores e trilho vertical no trilho: rail-line (#27496d); texto no trilho: rail-ink (#e8eef5) e rail-muted (#a9bbcf). Realce de linha recem-mudada e estado "Liberando": rail-tint (#e3ebf4).

### Secondary (as linhas, risco apenas)
- **Linha Verde** (line-green, #007e5e): risco baixo. Texto branco sobre ele. Tom suave: line-green-soft (#e3f3ee).
- **Linha Amarela** (line-yellow, #ffd100): risco medio. Texto sempre ink sobre ele. Tom suave: line-yellow-soft (#fff6cc).
- **Linha Vermelha** (line-red, #d71920): risco alto e o trecho acima do limite no trajeto. Texto branco. Tom suave: line-red-soft (#fdeaea). O contrato previa #EE1C25; o build usa #d71920 e o build vale.

### Neutral
- **Chao Quente** (ground, #faf9f6): fundo da pagina. **Superficie** (surface, #ffffff): tabelas, cartoes, campos. **Rebaixado** (sunken, #f1efe9): cabecalho de tabela, hover, linha expandida.
- **Tinta** (ink, #14202b): texto e contornos fortes. **Tinta Suave** (ink-muted, #4f5b69): rotulos e texto secundario.
- **Filete** (rule, #d9d5ca) e **Filete Forte** (rule-strong, #b9b3a4): bordas de 1px; o forte em campos, tabelas de cabecalho e trilho neutro do score.
- **Foco** (focus, #1b66c9): unico uso, anel de foco de 2px com offset de 2px.

### Named Rules
**The Linhas So Dizem Risco Rule.** Verde, amarelo e vermelho aparecem apenas para nivel de risco (selo, estacao do resumo, trajeto, marca). Situacao de acao e erro usam azul-marinho e tinta com simbolo e texto; falha e contorno de 2px em tinta, nunca vermelho.
**The Texto Escuro no Amarelo Rule.** Sobre #ffd100 o texto e sempre ink.
**The Cor Nao Basta Rule.** Todo estado colorido leva texto e um simbolo de forma propria.

## Typography

**Display/Body Font:** Barlow (com ui-sans-serif, system-ui, sans-serif)
**Label Font:** Barlow Semi Condensed (com Barlow como reserva)

**Character:** grotesca de leitura de sinalizacao; neutra e legivel a distancia. Numeros tabulares em todo o corpo (`font-variant-numeric: tabular-nums`) para que scores, valores e contagens se alinhem.

### Hierarchy
- **Title** (700, 1.5rem, tracking -0.025em): titulo de pagina (h1) e marca no login.
- **Headline** (700, 1.125rem): marca no trilho, titulo de dialogo, contagem por linha no resumo.
- **Body** (400/600, 0.875rem na maior parte; base do documento 15px, line-height 1.5): tabelas, campos, botoes (600). Texto corrido com max-width de prose.
- **Label** (Semi Condensed 600, 0.75rem, tracking 0.025em, caixa alta): cabecalhos de coluna, selos de risco e situacao, rotulos do resumo.

### Named Rules
**The Peso Carrega Rule.** Hierarquia por peso (400, 600, 700) e caixa do rotulo; a escala de tamanhos e curta (12, 14, 18, 24).

## Layout

Tela em duas colunas a partir de md: trilho fixo de 220px (sticky, altura da janela) e conteudo com max-width 6xl (72rem), padding 32px em md, 16px abaixo. Ritmo de espacamento em passos de 4px: 8, 12, 16, 24; cabecalho de pagina com 24px de margem inferior, blocos empilhados com gap de 16px. Linhas de tabela com 10px de padding vertical e celulas de 12px horizontal.

Responsivo: abaixo de md o trilho vira uma faixa no topo com os itens em rolagem horizontal, a barra do lojista e o botao Sair vao para uma barra fina. Colunas Valor (abaixo de sm) e Avaliado (abaixo de md) somem da tabela; o trajeto do score encolhe de 120px para 64px. O texto "limite N" do cabecalho de Score some abaixo de sm; o tique do limite permanece e seu valor e nomeado so no aria-label (pendencia conhecida: sem rotulo visivel no celular).

## Elevation & Depth

Plano. A profundidade vem de bordas de 1px (rule), fundos tonais (surface sobre ground, sunken para cabecalho e hover) e do trilho azul-marinho. As unicas sombras sao funcionais: o dialogo de confirmacao (shadow-lg, sobre overlay rail a 60%) e o polegar do interruptor (shadow-sm com anel de 1px).

### Named Rules
**The Chao Plano Rule.** Superficies em repouso nao tem sombra; sombra so onde algo flutua sobre a pagina (dialogo) ou e peca movel (polegar).

## Shapes

Cantos quase retos, de placa: 2px em selos (rounded-sm), 3px em botoes, campos e itens de navegacao (rounded-md), 4px em tabelas, cartoes e dialogo (rounded-lg). Circulos completos so para estacoes (marcador do pedido, aneis da navegacao, pontos do resumo) e o interruptor. Bordas de 1px; 2px em tinta para erro, falha e campo invalido (peso do contorno, nao cor, distingue). Estado vazio usa borda tracejada.

## Components

### Buttons
- **Shape:** cantos de 3px, altura 36px (32px sm, 36px quadrado icone), texto 14px semibold, padding horizontal 16px.
- **Primary:** azul-marinho com texto branco; hover rail-hover (#12406d). Acao primaria (liberar) mora na linha de Acoes e no dialogo, nunca solta na pagina.
- **Outline:** superficie com borda rule-strong, hover sunken. **Danger:** tinta com texto branco (nao vermelho). **Ghost:** transparente, hover sunken.
- **Disabled:** 50% de opacidade, sem ponteiro.

### Selo de risco e selo de situacao
Altura 24px, canto 2px, rotulo Semi Condensed caixa alta, simbolo de 12px a esquerda. Risco: preenchimento chapado da linha, simbolo traco (baixo), mais (medio), x (alto), "Risco Baixo/Medio/Alto". Situacao (Pendente, Sucesso, Falha, Ignorada, Liberando, Liberado): nunca cores de linha; combinam rail, sunken e tinta, com contorno de 2px para falha, e simbolo proprio (relogio, check, exclamacao, traco, seta circular).

### Inputs / Fields
Altura 36px, canto 3px, borda rule-strong, fundo surface, texto 14px, placeholder em ink-muted. Invalido: borda de 2px em ink. Select nativo com o mesmo estilo. Foco: anel global de 2px em focus.

### Navigation
Trilho azul-marinho; marca (simbolo + "RiskGuard" 18px bold) no topo, quatro itens (Pedidos, Acoes, Configuracoes, Auditoria) com 44px de altura, lojista e Sair embaixo. No desktop um trilho vertical de 2px (rail-line) liga as estacoes: anel vazio para o item inativo, preenchido em branco para a pagina atual, texto branco. No celular, o ativo e uma pastilha branca com texto azul-marinho.

### Tabela
Borda rule, canto 4px, cabecalho sunken com rotulos Label em ink-muted (40px), linhas separadas por filete rule, linha expandida em sunken com os fatores do score ("Por que este score", pontos em semibold).

### Trajeto do score (assinatura)
Regua SVG 120x24: trecho neutro (rule-strong) de 0 ao limite, trecho vermelho do limite a 100, tique vertical de 2px em tinta no limite do lojista (estacao de conexao), e circulo de raio 5.5 com a cor do nivel e contorno de tinta marcando o pedido. Descrito por aria-label com score e limite.

### Resumo por linha
Cartao com tres estacoes (ponto de 16px com contorno de tinta na cor da linha, rotulo Label, contagem 18px bold) ligadas por um trilho de 4px verde-amarelo-vermelho; cada estacao e botao que filtra a tabela (aria-pressed, selecionada com sunken e anel de 1px em ink). Ao lado, "Acoes com falha".

### Dialogo de confirmacao
Canto 4px, borda rule-strong, padding 24px, largura maxima 28rem, titulo 18px bold, rodape com Cancelar (outline) e Confirmar (primario). Overlay azul-marinho a 60%.

### Movimento
`station-change`: a linha que muda de estado acende em rail-tint e se esvazia em 1,6s (ease-out expo), uma vez, no lugar, sem reordenar. Transicoes de cor de botao e interruptor curtas. Com prefers-reduced-motion toda animacao cai a 0,01ms.

## Do's and Don'ts

### Do:
- **Do** usar verde, amarelo e vermelho apenas para o nivel de risco, sempre com texto e simbolo.
- **Do** usar azul-marinho (#0b2a4a) e tinta para acao, situacao e erro; falha se distingue por contorno de 2px.
- **Do** manter cantos de 2 a 4px, bordas de 1px e superficies planas.
- **Do** usar Barlow Semi Condensed 600 em caixa alta, 12px, para rotulos de estado e cabecalhos de coluna.
- **Do** manter numeros tabulares e identificar o limite do lojista no trajeto (tique e aria-label).
- **Do** usar a marca da placa (anel mais trecho de linha de 2px em azul-marinho) apenas no topo de PageHeader.

### Don't:
- **Don't** usar as cores de linha para decoracao, destaque de marca ou estado de acao.
- **Don't** montar o painel como cartoes de metricas com numero grande e acento; o resumo e um trajeto de estacoes.
- **Don't** usar gradiente, sombra decorativa ou cantos arredondados acima de 4px (exceto estacoes e interruptor).
- **Don't** escrever texto claro sobre o amarelo #ffd100.
- **Don't** depender so da cor para indicar nivel ou situacao.
