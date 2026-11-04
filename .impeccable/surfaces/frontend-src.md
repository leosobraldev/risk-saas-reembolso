---
version: 1
slug: "frontend-src"
primary_target: "frontend/src"
related_targets: []
---

# Surface brief: painel do lojista (frontend/)

Modo: Operate. Painel logado (SPA Vite + React) para o dono/operador da loja. pt-BR. Telas: Pedidos (dashboard), Ações, Conexões e configurações, Auditoria.

Tarefa: ver o que foi avaliado e feito, liberar falso positivo, ajustar limite e bloqueio automático, conectar plataformas, consultar a trilha. Uso diário, sessões curtas, desktop primeiro, celular secundário.

Decisões registradas na revisão final: (1) o filtro por plataforma não existe porque a API de avaliações só filtra por nível; (2) a ação primária (liberar) mora em Ações, porque a avaliação não traz o id da ação, e o detalhe do pedido aponta para lá; (3) as três cores de linha só significam risco: situação das ações e erros usam azul-marinho e tinta, com símbolo e texto.

Não resolvido: hospedagem/domínio do frontend; mais de um usuário por lojista na interface; logo próprio.

## Direction contract

THESIS: O risco é uma linha de metrô. Os três níveis são as linhas Verde (baixo), Amarela (médio) e Vermelha (alto), e o score de cada pedido é uma estação ao longo do trajeto, com o limite do lojista marcado como uma estação de conexão. Recusa o dashboard-padrão de cartões de métricas com número grande e acento.

OWN-WORLD: Chão branco quente; trilho de navegação azul-marinho (#0B2A4A) como a placa esmaltada da estação; linhas de cor chapada, sem gradiente, que só significam estado: verde #007E5E, amarelo #FFD100 (texto sempre escuro sobre ele), vermelho #EE1C25. Tipo: grotesca de leitura de sinalização, números tabulares, peso e caixa carregam a hierarquia. Rótulos de estado em caixa de placa. Linhas finas e uniformes, cantos de 2px, sem sombra decorativa.

STORY: O lojista abre o painel e entende em segundos quantos pedidos pararam e por quê; reconhece o fator que pesou no score; libera um falso positivo com uma confirmação clara e vê o estado mudar de Liberando para Liberado no próprio lugar.

FIRST VIEWPORT: Trilho lateral de 220px à esquerda (marca RiskGuard, 4 itens de navegação, lojista e sair embaixo). À direita, barra de resumo do período com três contagens por linha (Verde, Amarela, Vermelha) e ações aguardando. Abaixo, tabela de pedidos (plataforma, pedido, valor, nível, score, quando) com filtro por nível e plataforma; cada linha tem o mini-trajeto do score: uma régua horizontal com estações nos limites e o marcador do pedido, e o limite do lojista identificado. Ação primária (liberar) na linha e no detalhe, nunca solta na página.

FORM: Linhas do Metrô de SP, candidato 7 da lista própria. seed:e345870af7554e4e

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance.
