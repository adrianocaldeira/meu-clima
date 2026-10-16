# Changelog

Todas as mudanças relevantes deste projeto são documentadas neste arquivo.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto adota [Semantic Versioning](https://semver.org/lang/pt-BR/).

## [Não lançado]

### Corrigido
- Desligar, suspender o PC ou sair da sessão deixava o ar ligado. O "Ausente" agora desliga o ar
  nesse momento, com um único comando. Não desliga com ajuste manual em vigor ou Modo noite ativo, e
  a opção "Desligar quando o PC desligar ou suspender" permite manter o ar como está.
- Ao ligar o PC de manhã, o "Conforto" retomava a fase da véspera e religava o ar em Frio com dentro
  abaixo da faixa. Depois de 10 min sem leitura ele recomeça do zero, e uma fase de ar ligado nunca
  religa sozinha um ar que está desligado.
- Ao fim do turbo com dentro já na mínima, o "Conforto" ia para o Frio econômico; agora vai para só
  ventilar (ou desliga, conforme a regra).
- Nenhuma regra comanda o aparelho com leitura mais velha que 3 min.
- A pausa de 1 h por ajuste manual se perdia quando o app reiniciava (atualização, PC desligado).
- Um `settings.json` gravado por versão mais nova fazia o app recriar as regras padrão e gravar por
  cima; agora ele abre só para leitura, sem regras, e avisa.

### Mudado
- O log registra cada mudança de fase do "Conforto" com o motivo, o recomeço depois de uma lacuna,
  bloqueio e desbloqueio da tela, suspensão e retomada do PC e o que foi feito no desligamento.

## [0.2.3] — 2026

### Corrigido
- Sair e voltar ao PC liberava um novo turbo do "Conforto" mesmo depois de o turbo anterior ter
  esgotado os 30 min sem esfriar.
- Turbo inteiro sem a leitura de dentro baixar 1 °C: o "Conforto" não repete o turbo e o painel
  avisa que o sensor do aparelho parece travado.
- Qualquer ajuste de regra recusado logo após outro ajuste aceito no mesmo envio (ex.: turbo
  recusado logo após desligar o modo econômico) é reenviado no ciclo seguinte, sem aviso.

### Mudado
- O log registra os campos de temperatura que a Electrolux devolve, quando mudam.

## [0.2.2] — 2026

### Corrigido
- Ao sair do turbo, a Electrolux recusava o modo econômico enviado logo após a troca de ventilação:
  o alvo ficava em 23 °C (mais frio que a faixa), aparecia o aviso "Comando não aplicado" e o
  Conforto ficava 10 min sem agir. Agora qualquer ajuste recusado logo após trocar modo ou
  ventilação é reenviado no ciclo seguinte, sem aviso.

## [0.2.1] — 2026

### Mudado
- "Conforto" reage mais rápido ao calor forte: com dentro 2 °C ou mais acima da máxima, volta ao frio
  (ou entra no turbo) em 2 min, não em 5. Enquanto espera, o painel diz desde quando passou da máxima
  e até que horas espera.
- O log (`%LocalAppData%\MeuClima\meuclima.log`) registra cada leitura que mudou, o que cada regra
  decidiu e cada comando aplicado, para diagnosticar o comportamento das regras.

### Corrigido
- Ao sair de "só ventilar", a Electrolux recusava a temperatura alvo enviada logo após a troca de
  modo: aparecia o aviso "Comando não aplicado" e o Conforto ficava 10 min sem comandar o aparelho.
  Agora o alvo é reenviado no ciclo seguinte, sem aviso.
- "Sem conexão desde" mostra o dia quando a queda não é de hoje ("ontem 17:53").

## [0.2.0] — 2026

### Adicionado
- **Regra "Conforto"**: mantém dentro numa faixa (padrão 23–24 °C) gastando o mínimo. Acima da
  máxima, Frio econômico com alvo na máxima; na mínima, só ventila (compressor desligado); turbo
  só a partir de 26 °C e por no máximo 30 min. Liga antes do calor pela sensação térmica prevista.
  Avisa quando a sensação lá fora está mais fresca que dentro. Não usa o modo Auto, que aquece
  sozinho; aquecer é opcional e vem desligado. Não troca o aquecimento que você ligou. Durante a
  pausa por ajuste manual, não avança nem avisa. Ao sair por ausência ou fim da agenda, tira o turbo.
- **Projeção do custo no mês** nos Insights, pela média dos últimos 7 dias.
- **Quem manda no aparelho**: no painel da bandeja, cada aparelho mostra quem está no comando e por
  quê, numa frase: você (com "Retomar agora"), uma regra agindo ("Dentro está 25 °C, acima de 24 °C.
  Esfriando no modo econômico até 24 °C."), uma regra de olho, em espera ou nenhuma.
- Menu **⋯** no topo do painel: pausar as regras por 1 h, verificar atualizações e sair.

### Mudado
- "Conforto" substitui "Esfriar rápido", "Antes do pico" e "Fora mais fresco". As regras antigas
  migram sozinhas na primeira abertura, mantendo agenda, aparelho, "Só com alguém no PC" e a faixa
  do "Esfriar rápido". O Conforto fica ligado ou desligado como o "Esfriar rápido" estava.
- "Saí do PC" passa a se chamar **"Ausente"**. Bloquear a tela ou suspender o PC desliga o ar na
  hora (ou após N min, configurável). Na volta, as regras ativas decidem se o ar liga; voltar ao
  ajuste anterior virou opção. Sem uso do PC, alivia em Frio 26 °C, não mais em Auto.
- "Janela aberta" explica o motivo no painel ("Dentro subiu de 24 para 26 °C com o ar no frio:
  janela ou porta aberta?") e até quando mantém o ar desligado.
- O chip "aguardando" virou "enviando ao ar…": o comando foi enviado e a Electrolux leva cerca de
  30 s para confirmar.
- Quando a Electrolux recusa a leitura (HTTP 401/403), o aviso diz que pode ser a API key ou o
  limite de chamadas, e o log registra a resposta para diagnóstico.
- O insight "Pico previsto de amanhã" usa a sensação térmica, a mesma que o Conforto observa.
- Energia estimada por modo: só ventilar conta ~40 W, não a potência média do compressor.
- Fechar a central aberta pelo painel da bandeja volta ao painel.
- "Sair do Meu Clima" saiu do rodapé do painel e foi para o menu ⋯. O painel ganhou mais espaço
  entre os blocos.

### Corrigido
- "Janela aberta" disparava com um salto isolado do sensor; agora a subida precisa aparecer em
  duas leituras, por pelo menos 3 min. Cancela antes da hora com ajuste manual, ou com o ar religado
  e dentro abaixo da temperatura que disparou a regra.

## [0.1.1] — 2026

### Adicionado
- **Procurar atualizações automaticamente** em Ajustes: desligado, o app só consulta o GitHub
  quando você pede.
- Política de privacidade (`PRIVACIDADE.md`), mostrada pelo instalador antes de instalar.

### Mudado
- O instalador pergunta se o app deve iniciar com o Windows (vem marcado). A versão portable
  começa com essa opção desligada.
- As fontes das telas vêm de dentro do app; nada é carregado de servidores externos ao abrir
  as janelas.

### Corrigido
- A janela ficava em "Carregando…" até terminar a primeira leitura de todos os aparelhos; agora
  abre na hora com o último estado e mostra o clima assim que ele chega.
- Na abertura, o painel mostra "Lendo os seus aparelhos…" em vez de "Nenhum aparelho encontrado"
  enquanto a primeira leitura não termina.

## [0.1.0] — 2026

### Adicionado
- **App de bandeja do Windows**: o clique no ícone abre o painel com o clima, as regras em execução e
  o controle rápido; o menu do ícone abre a central, liga ou desliga o início com o Windows,
  verifica atualizações e encerra o app.
- **Controle do ar-condicionado Electrolux com Wi-Fi** pela Electrolux Group API oficial, para todos
  os aparelhos da conta: ligar, desligar, temperatura, modo, ventilação, oscilação, modo econômico e
  timer de desligamento. Funciona só com aparelhos Electrolux cadastrados no app Electrolux.
- **Credenciais do portal de desenvolvedor** (API key + refresh token) informadas pelo usuário,
  guardadas com DPAPI no perfil do Windows, com renovação automática do token.
- **Clima externo** via Open-Meteo: temperatura atual e previsão horária para a cidade configurada.
- **Regras automáticas**: "Esfriar rápido", "Saí do PC", "Antes do pico", "Janela aberta",
  "Modo noite" e "Fora mais fresco", cada uma ligada a um aparelho, com prioridade entre elas e pausa
  de 60 min após ajuste manual. Criadas e ligadas ao concluir a configuração. "Esfriar rápido" aceita
  agenda por dias da semana e horário; "Modo noite" pode ativar todo dia num horário.
- **Insights**: horas ligado, energia e custo estimados, minutos para baixar 1 °C, gráfico fora ×
  dentro de 24 h ou 7 dias e avisos de quedas de conexão, filtro, pico previsto de amanhã e economia
  do "Saí do PC".
- **Tema claro, escuro ou do sistema**: no modo sistema, segue o tema do Windows.
- **Inicia com o Windows**, registrado pelo próprio app no usuário atual. Vem ligado; desliga em
  Ajustes ou no menu do ícone da bandeja.
- **Atualização automática**: verifica novas versões no GitHub ao iniciar e a cada 30 min, e avisa
  pela bandeja. "Verificar atualizações" no menu do ícone da bandeja verifica na hora. Ao instalar,
  baixa o instalador, valida a integridade (SHA-256), reinstala em silêncio e reabre o app. A versão
  portable abre a página da release.
- Instalador (Inno Setup), sem exigir administrador.

[Não lançado]: https://github.com/adrianocaldeira/meu-clima/compare/v0.2.3...HEAD
[0.2.3]: https://github.com/adrianocaldeira/meu-clima/releases/tag/v0.2.3
[0.2.2]: https://github.com/adrianocaldeira/meu-clima/releases/tag/v0.2.2
[0.2.1]: https://github.com/adrianocaldeira/meu-clima/releases/tag/v0.2.1
[0.2.0]: https://github.com/adrianocaldeira/meu-clima/releases/tag/v0.2.0
[0.1.1]: https://github.com/adrianocaldeira/meu-clima/releases/tag/v0.1.1
[0.1.0]: https://github.com/adrianocaldeira/meu-clima/releases/tag/v0.1.0
