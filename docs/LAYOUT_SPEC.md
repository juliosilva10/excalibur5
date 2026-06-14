# Excalibur5 — Especificação de Layout da Interface

Documento de referência do layout visual do programa (WPF). Reúne a paleta de cores,
estilos compartilhados (botões, campos, comboboxes, barras de rolagem, etc.) e a estrutura
de cada tela/painel. Todos os valores de cor são hexadecimais como aparecem no XAML.

Arquivos de origem:
- `App.xaml` — dicionário global de recursos (estilos e paleta)
- `Views/MainWindow.xaml` — janela principal (header + área de conteúdo)
- `Views/Controls/SidebarControl.xaml` — barra lateral de navegação
- `Views/Controls/MarketTabView.xaml` — aba de mercado (gráfico + ticks + painéis)
- `Views/Controls/ContractPanelView.xaml` — painel de contratos (Call/Put, Bot, Recover, Virtual)
- `Views/Controls/StrategyPanelView.xaml` — painel de estratégia automatizada
- `Views/Controls/DiversityPanelView.xaml` — painel Diversity (grupo de contratos)
- `Views/Controls/HistoryPanelView.xaml` — histórico de operações
- `Views/Controls/PerformancePanelView.xaml` — performance da sessão
- `Views/Controls/OpenPositionsView.xaml` — tabela de posições abertas

---

## 1. Paleta de cores

### Fundo e superfícies
| Função | Hex |
|--------|-----|
| Fundo da janela (`DerivBgColor` / `BackgroundBrush`) | `#07121a` |
| Superfície (`SurfaceBrush`) | `#0f2030` |
| Cartão / input (`DerivCardBg` / `Surface2Brush` / `DerivInputBg`) | `#153342` |
| Topo de degradê de cartão | `#0c1e2a` |
| Variantes de fundo escuro | `#0a1821`, `#08141a`, `#0d1f28`, `#112935` |

### Bordas
| Função | Hex |
|--------|-----|
| Borda padrão (`DerivCardBorder` / `BorderBrush`) | `#1d4957` |
| Borda de botão de ícone (sidebar) | `#2a5568` |

### Texto
| Função | Hex |
|--------|-----|
| Texto principal / valor (`TextBrush` / `DerivValueColor`) | `#f3fbff` |
| Texto claro de valores | `#e0eef8` |
| Texto secundário (`SubtleBrush` / `DerivTextColor`) | `#a3b8cc` |
| Rótulos de seção | `#90b5c9` |
| Rótulos/menores e dicas | `#6090a8` |

### Destaques (acento)
| Função | Hex |
|--------|-----|
| Ciano (acento principal, `AccentBrush`) | `#00f0ff` |
| Teal (valores/sinais) | `#00e8c8` |
| Verde (sucesso/ganho, `SuccessBrush`) | `#34c759` |
| Vermelho (perda) | `#FF6B6B` / `#ff6b6b` |
| Âmbar/dourado (alertas, win-rate, uptime) | `#f0c000` / `#f0c040` |
| Badge real | `#1a4a28`; Badge virtual | `#1a3a6b` |

### Fonte
- Janela: `Segoe UI`, tamanho base 13.
- Valores numéricos/monoespaçados: `Consolas`.

---

## 2. Estilos compartilhados (App.xaml)

### 2.1 Barra de rolagem (scrollbar)
Padrão global fino, arredondado, que só aparece ao passar o mouse sobre o `ScrollViewer`.

- **`SlimScrollBarThumb`** (`Thumb`): `CornerRadius="3"`, `Margin="1"`, fundo `#60a0b8c8`; ao passar o mouse (`IsMouseOver`) muda para `#9000f0ff`.
- **`ScrollBar`** (estilo implícito): `OverridesDefaultStyle="True"`, largura/altura `6px` (`MinWidth/MinHeight=6`). Template é apenas um `Track` com o thumb `SlimScrollBarThumb` (sem botões de seta). Orientação horizontal troca `Width` por `Height=6`.
- **`ScrollViewer`** (estilo implícito): barras verticais e horizontais com `Opacity="0"` por padrão; ao passar o mouse, animam para opacidade 1 em 0.2s (entrada) e voltam a 0 em 0.4s (saída).

> Padrão de uso: contêineres roláveis devem usar um `ScrollViewer` real para herdar este visual. Listas baseadas em `ItemsControl` precisam ser envolvidas por um `ScrollViewer` (ou ter o template substituído por um), pois o `ItemsControl` puro não cria barra de rolagem.

Caso especial — `MarketTabView` (lista "Ticks Stream"): define inline um `ScrollBar` de `8px` de largura usando o mesmo `SlimScrollBarThumb`.

### 2.2 Botões

Todos os botões "cheios" seguem a mesma estrutura em camadas dentro de um `Grid`:
1. `GlowBorder` — brilho desfocado (`BlurEffect`) atrás, baixa opacidade em repouso, aumenta no hover/press.
2. `ShadowBorder` — sombra preta inferior para dar volume (`Margin="0,2,0,-2"`).
3. `MainBorder` — corpo com `CornerRadius`, borda de 1.5px e fundo em degradê vertical.
4. Faixa de brilho superior branca translúcida (`Opacity≈0.10`) e o `ContentPresenter` centralizado.

Gatilhos comuns: `IsMouseOver` (intensifica glow, clareia borda e degradê), `IsPressed` (reduz sombra, escurece degradê), `IsEnabled=False` (`MainBorder Opacity≈0.45`).

| Estilo | Cor / uso | CornerRadius | Tamanho padrão |
|--------|-----------|--------------|----------------|
| `DerivToggleButtonStyle` (App.xaml) | Âmbar — Pausar / toggle | 8 | 70×30 |
| `DerivCallButtonStyle` | Verde — comprar/Call/Iniciar | 8 | 70×30 |
| `DerivPutButtonStyle` | Vermelho — Put/Parar | 8 | 70×30 |
| `DerivAddButtonStyle` | Ciano/teal — adicionar | 8 | altura 30 |
| `DerivSellProfitButtonStyle` | Verde — vender no lucro | 4 | altura 20 |
| `DerivSellLossButtonStyle` | Vermelho — vender no prejuízo | 4 | altura 20 |

Degradês principais (repouso → hover):
- Âmbar: `#6e5a1e/#5c4a18/#423510` → `#8b7028/#6b5820/#4a3d14`; borda `#c9a83e`→`#f5d060`.
- Verde: `#2d6e48/#235c3d/#18422d` → `#3d8b5e/#2b6b48/#1a472f`; borda `#5ec97b`→`#9bfbb6`.
- Vermelho: `#6e2d2d/#5c2323/#421818` → `#8b3d3d/#6b2b2b/#471a1a`; borda `#e07070`→`#ffaaaa`.
- Ciano/teal (Add): `#185463/#123f4c/#0c2a34` → `#206b7e/#164f5e/#0d3340`; borda `#2a8da3`→`#00f0ff`; glow `#00f0ff`.

> O botão de conexão da `MainWindow` usa uma variante de `DerivToggleButtonStyle` controlada por `Tag` (`Disconnected`/`Connecting`/`Connected`) que troca o degradê para azul/amarelo/verde.

Os botões da barra lateral e os de ícone (fechar, atualizar, copiar) usam templates inline próprios — descritos nas seções dos respectivos controles.

### 2.3 Campos de entrada (TextBox) — `DerivTextBox`
- Fundo afundado em degradê: `#08141a/#0d1f28/#153342/#153342`; borda `#1d4957`; `CornerRadius="8"`.
- `Foreground="White"`, `CaretBrush="White"`, `FontFamily="Consolas"`, `FontSize=12`, `Padding="8,4"`.
- `GlowBorder` ciano (`DropShadowEffect #00f0ff`) que anima opacidade 0→1 em 0.3s no hover; fica em 1 quando `IsFocused`.
- Desabilitado: texto `#66ffffff`, corpo `Opacity=0.7`.

### 2.4 ComboBox — `DerivComboBox` + `DerivComboBoxItem`
- Botão (toggle) com `CornerRadius="8"`, borda `#1d4957`, fundo degradê `#08141a→#153342`, seta (`Path`) ciano `#00f0ff`. Hover muda borda para `#00f0ff`.
- `FontFamily="Consolas"`, `FontSize=12`, altura padrão 30, largura padrão 160.
- Popup: `Border` `CornerRadius="6"`, fundo `#0f2030`, borda `#1d4957`, animação `Slide`.
- Itens (`DerivComboBoxItem`): `CornerRadius="4"`, texto `#f3fbff`; hover `#1a4a5e`; selecionado `#1d4957`.

### 2.5 CheckBox
- Caixa 16×16, `CornerRadius="4"`, borda `#1d4957`, fundo radial `#08141a/#0d1f28/#153342`.
- Marca de seleção (`Path`) ciano `#00f0ff` (opacidade 0→1 quando marcado). Hover/marcado: borda `#00f0ff`.
- Texto `#a3b8cc`, `FontSize=11`, `Cursor="Hand"`. Desabilitado `Opacity=0.5`.

### 2.6 RadioButton
- Anel 16×16 (`CornerRadius="8"`), borda `#1d4957`, fundo radial `#08141a/#0d1f28/#153342`.
- Ponto central radial `#00f0ff→#009db3` (opacidade 0→1 quando marcado). Hover/marcado: borda `#00f0ff`.
- Texto `#a3b8cc`, `FontSize=11`, `Cursor="Hand"`.

### 2.7 Slider de confiança — `ConfidenceSliderStyle`
- Faixa (`Min=0.3`, `Max=1.0`, passo 0.05) com trilho `#0a1a24` borda `#1d4957`.
- Parte preenchida colorida por `ConfidenceToColor` (vermelho→laranja→amarelo→azul→verde).
- Thumb: elipse 16×16 `#0c1e2a` com borda ciano `#00f0ff` e miolo `#00f0ff`.

### 2.8 DatePicker / Calendar — `DerivDatePicker` + `DerivCalendarStyle`
- Campo `CornerRadius="8"`, fundo degradê `#08141a→#153342`, borda `#1d4957` (hover `#00f0ff`), ícone de calendário ciano.
- Popup do calendário: fundo `#0f2030`, cabeçalho e dias em `#00f0ff`.
- Dia de hoje: fundo `#1d4957`; dia selecionado: fundo `#00f0ff` com texto `#07121a`; hover `#1a4a5e`.

### 2.9 ToolTip
- Fundo `#0f2030`, texto `#e0eef8`, borda `#1d4957`, `Padding="8,5"`, `FontSize=11`.

### 2.10 PasswordBox — `TokenPasswordBox` (MainWindow)
- Mesmo visual afundado do `DerivTextBox` (degradê `#08141a…#153342`, `CornerRadius=8`), texto ciano `#00f0ff`, glow ciano no hover/focus. Mostra a `Tag` como marca d'água via `Viewbox`.

---

## 3. Janela principal (MainWindow)

- Janela `780×1130` (mín. `400×900`), centralizada, fundo degradê `#0a1821→#061018`.
- Estrutura em 2 linhas: **Header (Auto)** e **Conteúdo (\*)**.

### 3.1 Header card
- `Border` `CornerRadius="8"`, altura 46, `Margin="8,8,8,0"`, fundo degradê `#234e63/#153342/#112935`, sombra projetada.
- Overlays de brilho (azul para conta virtual, dourado para conta real), exibidos por `ShowVirtualGlow`/`ShowRealGlow`.
- Grid de colunas: Token (200) | espaço (12) | Botão (76) | Uptime (Auto) | flex (\*) | Conta (400) | Status (240).
  - **Token**: `PasswordBox` (`TokenPasswordBox`), altura 30.
  - **Botão conectar/desconectar**: `DerivToggleButtonStyle` com `Tag` dinâmico (Conectar/Aguarde.../Desconectar).
  - **Uptime**: rótulo "On" + valor `Consolas` âmbar `#f0c000`.
  - **Conta**: Saldo Inicial (com botão de atualizar giratório), Saldo (`#00e8c8`), Tipo, Conta/LoginId.
  - **Status**: "Servidor" com bolinha verde pulsante (`#34c759`), hora UTC e Ping em `Consolas`.
- Mensagem de status (erros): texto `#FF6B6B`, `FontSize=10`, no rodapé do header.

### 3.2 Área de conteúdo
- `Border` `CornerRadius="8"`, `Margin="8"`, fundo degradê `#153342→#0f2030`, sombra.
- Duas colunas: **Sidebar (Auto)** + **Conteúdo (\*)**.
- Conteúdo em 2 linhas: barra de abas de mercado (Auto) e a view selecionada (\*).
  - **Barra de abas de mercado**: `ScrollViewer` horizontal com `ItemsControl` de abas. Cada aba é um `Button` com template `Border` `CornerRadius="6,6,0,0"`, fundo degradê azul; hover e selecionado mudam borda/texto para `#00f0ff`.
  - **Placeholder** "Selecione um mercado" (texto `SubtleBrush`) quando nenhuma aba está ativa.
  - **Painel de Log** (alternável): `Border` `CornerRadius="6"`, fundo degradê `#0a1821→#07121a`, com título "Log", botão copiar (ícone de duas folhas, hover `#1a3a4a`) e `TextBox` somente-leitura `Consolas` com rolagem automática.

---

## 4. Barra lateral (SidebarControl)

- `Border` largura 62, `CornerRadius="0,8,8,0"`, borda direita `#1d4957`, fundo degradê horizontal `#0c1e2a/#112935/#153342`, sombra.
- Botões de ícone (40×40, `CornerRadius="10"`), todos com o mesmo template:
  - `GlowBg` radial ciano (opacidade 0 em repouso → anima para 1 no hover, 0.7 quando o painel está ativo).
  - `MainBody` degradê `#1e465a/#153342/#112935`, borda `#2a5568` (→ `#00f0ff` no hover/ativo), faixa de brilho superior, sombra interna.
  - Ícone vetorial ciano `#00f0ff` (exceto Diversity).
- Botões (de cima para baixo): **Markets** (gráfico), **Recover** (setas circulares), **Virtual** (monitor), **Diversity** (nós divergentes — ciano/verde/vermelho), **Bot** (robô), **History** (tabela), **Perform.** (barras); no rodapé: **Log** (linhas).
- Cada botão tem um rótulo `FontSize=9` `#e0eef8` abaixo.

---

## 5. Aba de mercado (MarketTabView)

Grid de 3 linhas: barra de cotação (Auto) | gráfico+ticks (200px) | painéis empilhados (\*).

- **Barra de cotação** (linha 0): `Border` degradê `#0f2030→#0a1821`; variação e cotação atual em `Consolas` (12 e 20px) com cor dinâmica via `DirToColor`.
- **Gráfico** (linha 1, col 0): `Border` `CornerRadius="6"` borda `#1d4957`, degradê `#0a1821→#07121a`. Contém `ChartCanvas`, `YAxisCanvas` (52px) e `XAxisCanvas` (16px). 
  - Botão de tipo de gráfico (`Border` 26×26 `CornerRadius="4"`, fundo `#1a2e3d`, borda `#2a5568`) com ícone de mini-gráfico verde; abre um `Popup` (fundo `#0f2030`, `CornerRadius="6"`) com 3 opções: linha, candles, tick candles.
- **Ticks Stream** (linha 1, col 1, 140px): `Border` `CornerRadius="6"`, degradê `#0c1e2a→#07121a`. Lista `ItemsControl` virtualizada com `ScrollBar` fino de 8px (`SlimScrollBarThumb`); cada tick é um `TextBlock` `Consolas` 11px com cor por direção.
- **Painéis empilhados** (linha 2): ContractPanelView (padrão), HistoryPanelView, PerformancePanelView, DiversityPanelView — visibilidade mutuamente exclusiva.

---

## 6. Painel de contratos (ContractPanelView)

`Border` de fundo `CornerRadius="6"`, degradê `#0c1e2a→#07121a`, borda `#1d4957`. Grid de conteúdo com 3 linhas: abas (Auto) | seletor de tipo (Auto) | conteúdo 5 colunas (\*).

- **Abas** (linha 0): 4 "abas" (`Border` `CornerRadius="4"`, borda `#00f0ff`, degradê `#0f2030→#1a4a5e`, texto `#00f0ff` SemiBold) — Call/Put, Recover, Bot, Virtual (exclusivas por visibilidade).
- **Seletor de tipo** (linha 1): rótulo "Tipo de contrato:" + `ComboBox` (`DerivComboBox`, 140×24, texto `#00f0ff`).
- **Conteúdo** (linha 2): colunas 220 | 24 | 160 | 16 | \*.

### 6.1 Layout padrão Call/Put
- **Coluna esquerda**: alternância Duração/Hora de Término (RadioButtons); em Duração, unidades (Ticks/Seg/Min/Hrs/Dias) filtradas por contrato + `DerivTextBox` de duração + faixa (`#6090a8`); em Hora de Término, `DerivDatePicker`. CheckBox "Permitir Equals". Strike (`DerivComboBox`). Stake (`DerivTextBox` 120×30) + Recover (`DerivComboBox`).
- **Coluna direita**: pagamento por ponto (teal `#00e8c8`) com badge de ajuda "?". Bloco **CALL** (borda `#2a5a3a`, fundo `#0d2818`@0.6, payout verde `#34c759`, botão `DerivCallButtonStyle` 120px). Bloco **PUT** (borda `#5a2a2a`, fundo `#280d0d`@0.6, payout vermelho `#FF6B6B`, botão `DerivPutButtonStyle` 120px).

### 6.2 Painel Bot (dentro de `ScrollViewer` vertical)
Tipo de contrato, alternância de expiração, unidades, modo de estratégia, Equals, strike, amostra, direção (Call/Put/Ambos), grade Stake/TP/SL/Máx., Recover, indicadores (grade 2×4 de CheckBoxes), slider de confiança, Trailing Stop, status (bolinha + texto) e botões **Iniciar** (verde) / **Pausar** (âmbar) / **Parar** (vermelho), alinhados à esquerda.

### 6.3 Painel Recover
ComboBox de modo + CheckBox "Ativar Recuperação". Campos Martingale (Stake, Fator, Nível) ou Deficit (Stake Máxima, Trades p/ Recuperar), com textos de dica `#6090a8`.

### 6.4 Painel Virtual
"Sequência Alvo": `ContentControl` 300×30 com template próprio (borda `CornerRadius=8`, degradê `#08141a…#153342`, glow ciano no hover) + resumo. "Tolerância" (`DerivTextBox` 80×30). Botões **W** (`DerivCallButtonStyle`, texto verde), **L** (`DerivPutButtonStyle`, texto vermelho) e **⌫ Apagar** (template prateado: degradê `#747c84/#a2a8ae/#d2d6da`, borda `#c0c4c8`).

### 6.5 Tabela de posições (coluna 4)
`OpenPositionsView` (ver seção 10).

---

## 7. Painel de estratégia (StrategyPanelView)

`Border` largura 310, `CornerRadius="6"`, degradê `#0c1e2a→#07121a`, borda `#1d4957`, sombra. Todo o conteúdo dentro de um `ScrollViewer` vertical, `StackPanel` `Margin="12,10"`.

Seções (de cima para baixo): cabeçalho (ícone de robô ciano + "Estratégia Automatizada"); **Estratégia** (`DerivComboBox` 120×22); **Amostra** (`DerivTextBox`, só modo Trend); **Candle Dynamics** (Min Streak, Cooldown, slider de confiança); **Direção** (RadioButtons Call/Put/Ambos); grades **Duração/Stake**, **Take Profit/Stop Loss**, **Máx. contratos/Recover**; **Deficit Recovery** (Stake Máxima, Trades); separadores (`Border` 1px `#1d4957`); **Indicadores** (7 CheckBoxes) + **Confiança** (slider) + **Trailing Stop**; **Status** (bolinha `#4a6a7a` idle / `#34c759` ativo / `#f0c040` pausado + textos de sinal/estatísticas/último trade); botões **Iniciar/Pausar/Parar** centralizados.

Botões usam ícones vetoriais: triângulo (play), duas barras (pause), quadrado (stop), todos em branco.

---

## 8. Painel Diversity (DiversityPanelView)

`Border` `CornerRadius="6"`, borda `#1d4957`, degradê `#0a1821→#07121a`, `Margin="0,4,0,0"`. Grid de 6 linhas: título | descrição | lista (\*) | adicionar | cobertura | ações.

- **Cabeçalho**: "Diversity" (`#e0eef8`, 13px, SemiBold) + descrição (`#7da0b8`, 10px).
- **Lista de contratos** (linha 2): `ScrollViewer` (`MaxHeight=280`, vertical Auto, horizontal Disabled — usa a barra de rolagem fina global) envolvendo um `ItemsControl`. Cada linha é um `Border` `CornerRadius="4"`, fundo `#0e2230`, borda `#1d4957`, com:
  - **Mercado**: `ComboBox` (`DerivComboBox`, 120×30) listando `MarketInfo.SyntheticMarkets`, exibindo `DisplayName` (apenas o nome do mercado, ex.: "V10"; `MarketInfo.ToString()` também retorna `DisplayName`), valor selecionado por `Symbol`.
  - **Tipo**: `ComboBox` (130×30) — Digit Over/Under/Even/Odd/Match/Diff, Rise, Fall.
  - **Dígito**: `ComboBox` (64×30, 0–9), visível só para contratos de dígito.
  - **Duração**: `DerivTextBox` (48×30) + `ComboBox` de unidade (90×30).
  - **Entrada (Stake)**: `DerivTextBox` (90×30).
  - **Botão fechar (✕)** (26×26): template próprio — fundo arredondado `CornerRadius="6"` que aparece no hover (`#33ff5555`, e `#55ff4040` ao pressionar) com brilho vermelho (`CloseGlow` `#ff5555`); o "✕" passa de `#ff6b6b` para `#ff5555` no hover.
- **Adicionar contrato** (linha 3): `Button` "+ Adicionar contrato" usando `DerivAddButtonStyle` (cantos arredondados `CornerRadius=8` + degradê ciano/teal), altura 30, texto branco.
- **Cobertura/orientação** (linha 4): `Border` `CornerRadius="4"`, fundo `#0e2230`, texto `#c0d8e8` (oculto quando vazio).
- **Ações** (linha 5): botão **Comprar Grupo** (`DerivToggleButtonStyle`, 130×32) + CheckBox "Modo por sinal" + campo "Conf. mín." (`DerivTextBox` 50×28) + texto de status (`#a3b8cc`).

---

## 9. Histórico (HistoryPanelView)

`Border` `CornerRadius="6"`, borda `#1d4957`, degradê `#0c1e2a→#07121a`, `Margin="0,4,0,0"`. Grid de 2 linhas: cabeçalho (Auto) | DataGrid (\*).

- **Cabeçalho**: "Histórico" (`#00f0ff`, 12px, SemiBold) + " — Contratos" (`#6090a8`, 11px) + botão atualizar (ícone "⟳" `#6090a8`, fundo hover `#1a3a4a`, `CornerRadius="4"`, ToolTip "Atualizar").
- **Texto de carregamento** "Carregando histórico..." (`SubtleBrush`), exibido enquanto `IsLoading`.
- **DataGrid**: somente-leitura, linhas `#0c1e2a`/alternadas `#0f2535`, texto `#e0eef8` `Consolas` 11px, linhas de grade `#1d4957`, rolagem vertical/horizontal automática.
  - Cabeçalhos: fundo `#112935`, texto `#00f0ff`, 10px SemiBold.
  - Célula selecionada: fundo `#1a3f52`; linha em hover: `#153342`.
  - Colunas: Operação, Estratégia, Tipo, Nº Ref., Compra (`dd/MM HH:mm:ss`), Stake (`F2`), Venda, Entry Spot, Exit Spot, Valor (`F2`), Lucro/Perda (`F2`).

---

## 10. Posições abertas (OpenPositionsView)

`Border` `CornerRadius="6"`, borda `#1d4957`, degradê `#0c1e2a→#07121a`. Grid de 3 linhas: título | cabeçalhos | lista (\*), `Margin="12,8"`.

- **Título** "Posições Abertas" (`#00f0ff`, 13px, SemiBold).
- **Cabeçalhos** (8 colunas): Mercado, Tipo, Tempo, Entrada, Valor, Abertura, Lucro/Perda, e coluna de 58px para o botão Vender — todos `#6090a8`, 11px.
- **Lista**: `ItemsControl` com template substituído por `ScrollViewer` (vertical Auto, horizontal Disabled). Cada linha:
  - Mercado (`#e0eef8`, `Consolas`), Tipo (cor dinâmica via `ProfitToBrush`).
  - Tempo: mini barra de progresso (trilho `#1a2e3d`, `CornerRadius="1.5"`), vira vermelha `#ff4444` perto do vencimento.
  - Entrada/Valor (`#e0eef8`), Abertura (`#a3b8cc`), Lucro/Perda (cor dinâmica, Bold).
  - Botão **Vender**: `DerivSellProfitButtonStyle` (lucro) ou `DerivSellLossButtonStyle` (prejuízo), visível só em posições reais.
- **Estado vazio**: ícone (Segoe MDL2) + "Nenhuma posição aberta" (`SubtleBrush`).

---

## 11. Performance (PerformancePanelView)

`Border` `CornerRadius="6"`, borda `#1d4957`, degradê `#0c1e2a→#07121a`, `Margin="0,4,0,0"`. Grid de 2 linhas: cabeçalho (Auto) | `ScrollViewer` (\*, vertical Auto, horizontal Disabled — barra de rolagem padrão).

- **Cabeçalho**: "Performance" (`#00f0ff`, 14px, SemiBold) + " — Sessão Atual" (`#6090a8`, 13px).
- **Cartões** (`Padding="10,8"`, `CornerRadius="4"`, fundo `#112935`, borda `#1d4957`):
  1. **Total de Operações** — valor teal `#00e8c8`, 22px Bold.
  2. **Maior Stake** — detalhes (stake `#f0c000`) + mini gráfico de candles (`Canvas` 90px, fundo `#0a1821`).
  3. **Drawdown Máximo** — rebaixamento `#FF6B6B`, pico `#00e8c8`, vale `#FF6B6B` + mini gráfico.
  4. **Maior Sequência de Loss** — contagem `#FF6B6B`, lista de stakes, "Total perdido" (caixa `#1a0a0a` borda `#4a2020`) + mini gráfico.
  5. **Performance por Estratégia** — itens (fundo `#0c1e2a`) com Ops, W (`#34c759`), L (`#FF6B6B`), P/L (verde/vermelho conforme sinal) e WR (`#f0c000`).

---

## 12. Convenções de design

- **Tema**: escuro "Deriv", base azul-petróleo com acento ciano `#00f0ff`.
- **Cantos arredondados**: cartões/painéis `CornerRadius="6"`; controles e botões cheios `CornerRadius="8"`; itens menores `CornerRadius="4"`.
- **Bordas**: 1px `#1d4957` no padrão; realce ciano `#00f0ff` em hover/foco/seleção.
- **Degradês verticais**: superfícies escurecem de cima para baixo (`#0c1e2a`/`#0a1821` → `#07121a`).
- **Sombra/brilho**: botões cheios têm sombra inferior + glow desfocado; botões de ícone têm glow radial; campos têm glow ciano.
- **Tipografia**: rótulos em `Segoe UI`; valores numéricos e monoespaçados em `Consolas`.
- **Cores semânticas**: verde `#34c759` (ganho/Call), vermelho `#FF6B6B` (perda/Put), âmbar `#f0c000`/`#f0c040` (atenção/pausa), teal `#00e8c8` (saldo/sinal).
- **Barras de rolagem**: finas (6px), arredondadas, autoocultáveis — aplicadas via `ScrollViewer` real.
