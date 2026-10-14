# Arquitetura

> **A preencher.** Página reservada para quando a estrutura estabilizar.

## Visão geral (rascunho)

| Projeto | Papel |
|---|---|
| `src/MeuClima` | App WPF: ícone de bandeja, janela com WebView2, início com o Windows, atualização automática |
| `src/MeuClima.Core` | Domínio sem WPF: cliente da Electrolux Group API, modelos de aparelho, regras, clima (Open-Meteo), insights, persistência SQLite |
| `tests/MeuClima.Core.Tests` | Testes xUnit do Core (sem chamar a API real) |

## Integrações

- **Electrolux Group API** — `https://api.developer.electrolux.one/api/v1`, autenticação com `x-api-key`
  + Bearer; o access token é renovado pelo refresh token, que **rotaciona** a cada renovação.
- **Open-Meteo** — clima atual e previsão horária, sem chave.
- **GitHub Releases** — verificação de atualização (`releases/latest`). Ver [[Versao-e-atualizacoes]].

## A documentar

- Ciclo de vida do app (bandeja, janela, instância única)
- Motor de regras: agendamento, conflito entre regras, prioridade do comando manual
- Persistência: schema do SQLite, configurações, proteção das credenciais
- Ponte WebView2 ↔ .NET
