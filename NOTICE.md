# Notice

## Não afiliação

O **Meu Clima** é um projeto independente e de código aberto. **Não é afiliado, patrocinado,
endossado nem mantido pela Electrolux** ou por qualquer empresa do Electrolux Group. "Electrolux" é
marca registrada de seus respectivos titulares e aparece aqui apenas para indicar compatibilidade.

O uso da Electrolux Group API está sujeito aos termos do portal
[developer.electrolux.one](https://developer.electrolux.one/), aceitos por cada usuário ao criar
as próprias credenciais. O Meu Clima não distribui credenciais e não intermedia a comunicação: o app
fala direto com a API da Electrolux usando a chave do próprio usuário.

## Fontes de dados

### Open-Meteo

- Uso: temperatura e previsão do tempo externas
- Licença dos dados: [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)
- Atribuição: **Weather data by [Open-Meteo.com](https://open-meteo.com/)**
- Termos: <https://open-meteo.com/en/terms>

A atribuição também aparece na central do app, em Ajustes.

### Electrolux Group API

- Uso: leitura de estado e envio de comandos ao ar-condicionado do usuário
- Portal: <https://developer.electrolux.one/>
- Termos: os do portal de desenvolvedor, aceitos por cada usuário

## Componentes de terceiros

Pacotes NuGet embutidos no executável. Cada pacote declara a própria licença; versões exatas nos
`.csproj` (`src/MeuClima/MeuClima.csproj`, `src/MeuClima.Core/MeuClima.Core.csproj`).

| Componente | Uso | Licença |
|---|---|---|
| [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) | Interface (HTML) dentro da janela WPF | Licença do pacote (ver `LICENSE.txt` no NuGet) |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) | Banco local (histórico, insights) | MIT |
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) (dependência do Microsoft.Data.Sqlite) | Ponte nativa para o SQLite | Apache-2.0 |
| [SQLite](https://sqlite.org/) (via SQLitePCLRaw) | Motor do banco | Domínio público |

O ícone da bandeja usa o `NotifyIcon` do próprio Windows Forms, sem biblioteca extra.

O **WebView2 Runtime** (Microsoft Edge) não é distribuído com o app: usa-se o runtime Evergreen
instalado no Windows.
