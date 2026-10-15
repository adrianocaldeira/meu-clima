# Versão e atualizações

## Política de versionamento

Seguimos [Semantic Versioning](https://semver.org/lang/pt-BR/) (SemVer): `MAJOR.MINOR.PATCH`.

| Tipo | Quando subir | Exemplo |
|---|---|---|
| **MAJOR** (`1.0.0`) | Quebra de compatibilidade real | Mudar o formato do banco local ou das regras salvas sem migração |
| **MINOR** (`0.2.0`) | Nova feature compatível | Nova regra automática |
| **PATCH** (`0.1.1`) | Bugfix | Corrigir renovação do token |

Enquanto a versão for `0.x`, uma MINOR pode trazer mudança incompatível — sempre descrita no CHANGELOG.

## Como saber qual versão você tem

Central → **Ajustes** → **Versão**. A versão também aparece no rodapé da barra lateral da central.

## Onde baixar releases

Todas as releases ficam em https://github.com/adrianocaldeira/meu-clima/releases.

Cada release inclui:
- `MeuClima-Setup-vX.Y.Z.exe` — instalador
- `MeuClima.exe` — single-file portable
- **Release notes** extraídas do [CHANGELOG.md](https://github.com/adrianocaldeira/meu-clima/blob/main/CHANGELOG.md)

## Atualização automática

O app consulta a release mais recente no GitHub:

1. **Automaticamente, em silêncio**: ~10 s depois de iniciar e depois a cada 30 minutos enquanto está
   aberto. Se houver versão nova, avisa uma vez pela bandeja com o resumo das novidades. Para
   instalar, abra a central → **Ajustes** → **Instalar versão X.Y.Z**.
2. **Manualmente**, pelo menu do ícone da bandeja ou pelo menu **⋯** do painel → **Verificar
   atualizações**, ou pelo botão
   **Verificar atualização** em Ajustes.

### Versão instalada (via instalador)

"Instalar versão X.Y.Z" baixa o `MeuClima-Setup-vX.Y.Z.exe` da release, confere o tamanho e o **SHA-256**
publicados pelo GitHub, fecha o app e roda o instalador em silêncio. O app reabre sozinho na versão
nova — as regras voltam a rodar em segundos.

- **Per-user** (padrão, sem UAC): totalmente silencioso.
- **Per-machine** ("instalar para todos os usuários"): o Windows pede permissão de administrador (UAC).

### Versão portable

Não se atualiza sozinha: não há instalador para executar. O app avisa e abre a página da release
para você baixar o `MeuClima.exe` novo e substituir o antigo (no mesmo caminho, se o início com o
Windows estiver ligado).

### Diagnóstico

Eventos de atualização ficam em `%LocalAppData%\MeuClima\update.log` (append-only). Os instaladores
baixados ficam em `%LocalAppData%\MeuClima\Updates\` e são apagados na atualização seguinte.

### Quero saber de novas versões antes de abrir o app

**Watch** no repositório (Watch → Custom → "Releases"), ou consulte a [página de releases](https://github.com/adrianocaldeira/meu-clima/releases).

## Para devs — publicar versão

A fonte única de versão é `Directory.Build.props`. Antes, mova as notas de `[Não lançado]` para a
seção da versão no `CHANGELOG.md`; o workflow de release recusa tag sem essa seção. Depois:

```powershell
.\scripts\bump-version.ps1 0.2.0
git push origin main
git push origin v0.2.0
```

O push da tag dispara `.github/workflows/release.yml`, que:
1. Compila e roda os testes
2. Faz `dotnet publish` (single-file)
3. Confere que a tag bate com `Directory.Build.props` (a atualização automática depende disso)
4. Instala o Inno Setup e gera o instalador
5. Cria a GitHub Release com as notas da seção `[X.Y.Z]` do CHANGELOG
6. Anexa o instalador e o `.exe` portable

## Histórico de versões

Resumo no [CHANGELOG.md](https://github.com/adrianocaldeira/meu-clima/blob/main/CHANGELOG.md). Detalhes nas [releases](https://github.com/adrianocaldeira/meu-clima/releases).
