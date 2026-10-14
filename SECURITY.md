# Política de Segurança

## Versões suportadas

Apenas a última versão minor recebe correções de segurança.

| Versão | Suportada |
|---|---|
| 0.1.x  | ✅ |

## Reportar uma vulnerabilidade

**Por favor, não abra issues públicas para vulnerabilidades.**

Use o canal privado do GitHub:

1. Acesse https://github.com/adrianocaldeira/meu-clima/security/advisories/new
2. Descreva a falha, impacto e passos de reprodução
3. **Não inclua** sua API key, tokens, número de série ou ID do aparelho — use valores fictícios

Você receberá uma resposta em até **5 dias úteis**. Se a vulnerabilidade for confirmada:

- Reconhecemos o reporte
- Combinamos prazo para disclosure
- Publicamos correção em release patch
- Damos crédito ao reporter (se desejar) no advisory

## Escopo

O Meu Clima é um app Windows local que guarda credenciais da Electrolux Group API e envia comandos
a um ar-condicionado real. Os principais vetores que monitoramos:

- **Vazamento de credenciais** (API key, access token, refresh token): leitura fora da proteção DPAPI
  em `%APPDATA%\MeuClima\`, ou presença em log, arquivo de diagnóstico, mensagem de erro ou no repositório
- **Comando indevido ao aparelho**: regra automática ou entrada da UI que permita ligar, desligar ou
  alterar o ar-condicionado fora do que o usuário configurou
- **Interface WebView2**: conteúdo que cause execução de código (XSS, escape de sandbox, ponte
  JavaScript ↔ .NET expondo mais do que deveria)
- **Canal de atualização**: download ou execução de instalador que não seja a release oficial
  (o app valida tamanho e SHA-256 publicados pelo GitHub antes de executar)
- **Persistência local** (banco SQLite, configurações) sendo lida ou desserializada de forma insegura
- Vulnerabilidades em dependências (WebView2 SDK, Microsoft.Data.Sqlite e demais pacotes NuGet)

## Fora de escopo

- Falhas na Electrolux Group API, no app Electrolux ou no firmware do aparelho — reporte à Electrolux
- Falhas na API do Open-Meteo
- Falhas em runtimes/SO de terceiros (Windows, .NET runtime, WebView2 runtime do Edge)
- Engenharia social
- Vulnerabilidades em forks
