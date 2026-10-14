# Changelog

Todas as mudanças relevantes deste projeto são documentadas neste arquivo.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto adota [Semantic Versioning](https://semver.org/lang/pt-BR/).

## [Não lançado]

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

[Não lançado]: https://github.com/adrianocaldeira/meu-clima/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/adrianocaldeira/meu-clima/releases/tag/v0.1.0
