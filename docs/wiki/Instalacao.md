# Instalação

Três caminhos. Escolha o que mais combina com você.

## ✅ Requisitos

- **Ar-condicionado Electrolux com Wi-Fi**, cadastrado e online no **app Electrolux** do celular
- **Windows 10** (build 1809 ou superior) ou **Windows 11**, arquitetura **x64**
- **WebView2 Runtime** (já vem no Windows 11; no Windows 10 mais antigo, [baixe aqui](https://developer.microsoft.com/microsoft-edge/webview2/))
- Internet no PC (o app fala com a API da Electrolux e com o Open-Meteo)

> [!NOTE]
> Não precisa instalar o .NET. O Meu Clima vem **self-contained** — o runtime está empacotado no `.exe`.

> [!IMPORTANT]
> As regras automáticas rodam **dentro do app, no seu PC**. Com o PC desligado, hibernado, ou sem
> ninguém logado no Windows, nenhuma regra executa — o ar-condicionado fica no último estado.

---

## 1. Instalador (recomendado)

1. Acesse https://github.com/adrianocaldeira/meu-clima/releases/latest
2. Baixe `MeuClima-Setup-vX.Y.Z.exe`
3. Execute. O wizard pergunta:
   - Para quem instalar: só para você (padrão, sem administrador) ou para todos os usuários (pede administrador)
   - Pasta de instalação (padrão: `%LocalAppData%\Programs\Meu Clima`, ou `Program Files` se escolher instalar para todos)
   - Criar ícone na área de trabalho (opcional)
4. Na última tela, deixe marcado **"Executar Meu Clima"**. O ícone aparece na bandeja (perto do relógio).

O instalador **não** configura o início com o Windows: quem faz isso é o próprio app. Ele vem ligado
na primeira execução. Para desligar, use a central → **Ajustes** → **Iniciar com o Windows**, ou o
menu do ícone da bandeja.

> [!IMPORTANT]
> Na primeira execução o **Windows SmartScreen** pode bloquear (o `.exe` não tem assinatura comercial). Clique em **"Mais informações" → "Executar assim mesmo"**. Veja [[FAQ]].

### Desinstalar

Configurações do Windows → Aplicativos → "Meu Clima" → Desinstalar. O desinstalador fecha o app e
remove a entrada de início com o Windows.

---

## 2. Portable (sem instalar)

1. Baixe `MeuClima.exe` na [página de releases](https://github.com/adrianocaldeira/meu-clima/releases/latest)
2. Salve numa pasta fixa ("Iniciar com o Windows" vem ligado e o Windows vai procurar o `.exe` nesse caminho)
3. Execute com duplo clique

Desvantagens:
- Sem ícone no Menu Iniciar
- **Sem atualização automática** — o app só avisa e abre a página da release. Veja [[Versao-e-atualizacoes]].

---

## 3. Compilar do código-fonte

```powershell
# Requisitos: .NET 10 SDK (https://dotnet.microsoft.com/download/dotnet/10.0)
git clone https://github.com/adrianocaldeira/meu-clima.git
cd meu-clima

# Rodar em modo dev
dotnet run --project src/MeuClima/MeuClima.csproj

# Publicar single-file
dotnet publish src/MeuClima/MeuClima.csproj -c Release
# Output: src/MeuClima/bin/Release/net10.0-windows/win-x64/publish/MeuClima.exe
```

Para gerar o instalador localmente, instale o [Inno Setup 6](https://jrsoftware.org/isinfo.php) e rode:

```powershell
.\installer\build-installer.bat
```

O instalador sai em `installer/dist/`.

---

## Onde o Meu Clima grava dados

- Credenciais (API key e refresh token): `%APPDATA%\MeuClima\credentials.dat`, criptografadas com DPAPI (só o seu usuário lê)
- Configurações e regras: `%APPDATA%\MeuClima\settings.json`; progresso das regras: `rules-state.json`
- Histórico de leituras (insights): `%APPDATA%\MeuClima\history.db` (SQLite, guarda 90 dias)
- Log do app: `%LocalAppData%\MeuClima\meuclima.log` · cache do WebView2: `%LocalAppData%\MeuClima\WebView2\`
- Log de atualizações: `%LocalAppData%\MeuClima\update.log`
- Instaladores baixados pela atualização automática: `%LocalAppData%\MeuClima\Updates\`
- Início com o Windows: valor `MeuClima` em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- Registro da instalação: `HKCU\Software\MeuClima` (ou `HKLM`, se instalado para todos)

---

## Problemas comuns

- **Janela em branco** → WebView2 Runtime ausente. [Baixe aqui](https://developer.microsoft.com/microsoft-edge/webview2/).
- **SmartScreen bloqueia** → "Mais informações" → "Executar assim mesmo".
- **Ícone não aparece na bandeja** → pode estar nos ícones ocultos (seta `^` ao lado do relógio). Arraste para a barra para fixar.

Outros, veja [[FAQ]].
