# Contribuindo

Obrigado por considerar contribuir! Bug, ideia ou PR é bem-vindo.

## Como reportar bug ou pedir feature

- **Bugs** → [Issue templates](https://github.com/adrianocaldeira/meu-clima/issues/new/choose) → "Bug report"
- **Ideias** → [Issue templates](https://github.com/adrianocaldeira/meu-clima/issues/new/choose) → "Feature request" (alinhe antes de codar)
- **Vulnerabilidade de segurança** → não abra issue pública. Veja [SECURITY.md](SECURITY.md).

> [!WARNING]
> **Nunca cole API key, access token, refresh token, número de série ou ID de aparelho** em issue,
> PR ou log anexado. Se colou sem querer, revogue a credencial no portal
> [developer.electrolux.one](https://developer.electrolux.one/) e gere outra.

## Setup local

```powershell
git clone https://github.com/adrianocaldeira/meu-clima.git
cd meu-clima
dotnet build
dotnet run --project src/MeuClima/MeuClima.csproj
```

**Requisitos:**
- Windows 10 (1809+) ou Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022 17.12+ ou Rider 2024.3+ (qualquer IDE com suporte a .NET 10)
- Para testar contra um aparelho real: um ar-condicionado **Electrolux com Wi-Fi** cadastrado no app
  Electrolux e credenciais próprias do portal de desenvolvedor (passo a passo na
  [Wiki → Primeiros passos](https://github.com/adrianocaldeira/meu-clima/wiki/Primeiros-passos)).
  Sem aparelho, os testes de `MeuClima.Core` rodam normalmente — eles não chamam a API real.

### Credenciais locais

Credenciais de desenvolvimento ficam **fora do repo**. O `.gitignore` já bloqueia `*.cred.json` e
`appsettings.local.json`; o pre-commit com gitleaks é a segunda barreira.

### Ativar o pre-commit hook (recomendado)

O repo já está configurado pra usar `.githooks/`. Garante que o hook ativa:

```powershell
git config core.hooksPath .githooks
```

Para o hook efetivamente varrer segredos antes do commit, instale `gitleaks`:

```powershell
winget install gitleaks
```

## Rodar testes

```powershell
dotnet test
```

Os testes ficam em `tests/MeuClima.Core.Tests/` e usam **xUnit**.

## Padrão de commits

Seguimos [Conventional Commits](https://www.conventionalcommits.org/pt-br/) em português:

```
<tipo>(<escopo opcional>): descrição curta no imperativo

[corpo opcional explicando o porquê]
```

**Tipos:** `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `perf`, `style`, `build`, `ci`.

Exemplos:

```
feat(Regras): "Antes do pico" usa a previsão horária do Open-Meteo
fix(Electrolux): salva o refresh token rotacionado antes de usar o novo access token
refactor(Bandeja): menu de contexto vira lista de ações
chore: release v0.2.0
```

## Estilo de código

- Siga o estilo existente nos arquivos que está alterando
- C# 12+ features são bem-vindas (.NET 10 suporta)
- Lógica de domínio (regras, cliente Electrolux, clima, insights) vai em `MeuClima.Core`, sem dependência de WPF — é o que permite testar sem aparelho
- Nullable habilitado — trate `null` explicitamente

## Fluxo de PR

1. Fork → branch a partir de `main`
2. Commits pequenos e focados, seguindo o padrão acima
3. Adicione/atualize testes quando aplicável
4. `dotnet build` e `dotnet test` devem passar
5. Atualize a seção `[Não lançado]` do `CHANGELOG.md` se a mudança for visível ao usuário
6. Abra o PR — descreva **o porquê** da mudança, não só o quê
7. Mencione issue relacionada se houver (`Closes #123`)

## Versionamento

Usamos [SemVer](https://semver.org/lang/pt-BR/):

- **MAJOR** — quebra de compatibilidade real (ex.: mudança no formato do banco local ou das regras salvas sem migração)
- **MINOR** — feature compatível (ex.: nova regra automática)
- **PATCH** — bugfix

Enquanto a versão for `0.x`, MINOR pode trazer mudança incompatível — será sempre descrita no CHANGELOG.

Para subir versão antes de release:

```powershell
.\scripts\bump-version.ps1 0.2.0
```

O script edita só `Directory.Build.props` (fonte única da versão); o `.csproj`, o instalador e o
verificador de atualizações derivam dali.

## Dúvidas?

Abra uma [issue](https://github.com/adrianocaldeira/meu-clima/issues/new/choose).
