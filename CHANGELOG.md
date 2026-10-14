# Changelog

Todas as mudanças relevantes deste projeto são documentadas neste arquivo.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto adota [Semantic Versioning](https://semver.org/lang/pt-BR/).

## [Não lançado]

### Adicionado
- **App de bandeja do Windows**: ícone na área de notificação com o estado do ar-condicionado e
  ações rápidas; a janela abre pelo ícone.
- **Controle do ar-condicionado Electrolux com Wi-Fi** pela Electrolux Group API oficial: ligar,
  desligar, temperatura, modo e ventilação. Funciona só com aparelhos Electrolux cadastrados no app
  Electrolux.
- **Credenciais do portal de desenvolvedor** (API key + refresh token) informadas pelo usuário,
  guardadas com DPAPI no perfil do Windows, com renovação automática do token.
- **Clima externo** via Open-Meteo: temperatura atual e previsão horária para a cidade configurada.
- **Regras automáticas**: "Esfriar rápido", "Saí do PC", "Antes do pico", "Janela aberta",
  "Modo noite" e "Fora mais fresco", com prioridade entre elas e pausa de 60 min após ajuste manual.
- **Insights**: horas ligado, energia estimada, minutos para baixar 1 °C, quedas de conexão, pico
  previsto de amanhã e economia do "Saí do PC".
- **Inicia com o Windows** (opcional), registrado pelo próprio app no usuário atual.
- **Atualização automática**: verifica novas versões no GitHub, baixa o instalador, valida a
  integridade (SHA-256) e reinstala em silêncio, reabrindo o app.
- Instalador (Inno Setup), sem exigir administrador.

[Não lançado]: https://github.com/adrianocaldeira/meu-clima/commits/main
