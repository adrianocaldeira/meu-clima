; Inno Setup Script - Meu Clima
; Para compilar: baixe Inno Setup em https://jrsoftware.org/isinfo.php
;
; O instalador NÃO registra o início com o Windows: o próprio app grava/remove o valor
; "MeuClima" em HKCU\Software\Microsoft\Windows\CurrentVersion\Run conforme a preferência
; do usuário. Na desinstalação, o [Code] abaixo apaga esse valor para o Windows não tentar
; abrir um .exe que não existe mais.

#define AppName "Meu Clima"
; Versão vem do build-installer.bat / release.yml via ISCC /DAppVersion=X.Y.Z (lê de Directory.Build.props).
; Default só pra IDE/abertura direta no Inno Setup Studio (build real sobrescreve).
#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#define AppPublisher "Adriano Caldeira"
#define AppURL "https://github.com/adrianocaldeira/meu-clima"
#define AppExeName "MeuClima.exe"
#define AppDescription "Controle do ar-condicionado Electrolux pela bandeja do Windows"
; Precisa bater com o nome do valor que o app grava em ...\CurrentVersion\Run (Host/WindowsServices.cs).
#define StartupValueName "MeuClima"
; Precisa bater com o mutex de instância única do app (App.xaml.cs).
#define AppMutexName "MeuClima.SingleInstance"

[Setup]
AppId={{3BAC9C4B-CF00-469B-BF7A-21EFD6D98F10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
LicenseFile=
OutputDir=dist
OutputBaseFilename=MeuClima-Setup-v{#AppVersion}
SetupIconFile=..\src\MeuClima\Resources\app-icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}
; App de bandeja fica sempre aberto: o Restart Manager fecha a instância em execução numa
; instalação manual. No auto-update o app já se encerrou antes do setup rodar.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar ícone na área de trabalho"; GroupDescription: "Ícones adicionais:"; Flags: unchecked

[Files]
; Executável principal (publish single-file)
Source: "..\src\MeuClima\bin\Release\net10.0-windows\win-x64\publish\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Registro do app — lido pelo InstallContext para saber se a instalação suporta auto-update
; e se exige elevação (per-machine).
Root: HKA; Subkey: "Software\MeuClima"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\MeuClima"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"
Root: HKA; Subkey: "Software\MeuClima"; ValueType: string; ValueName: "InstallScope"; ValueData: "{code:GetInstallScope}"

[Run]
; Install interativo: checkbox "Executar Meu Clima" na tela final.
Filename: "{app}\{#AppExeName}"; Description: "Executar {#AppName}"; Flags: nowait postinstall skipifsilent
; Install silencioso (caso do auto-update do app): relança direto na bandeja, sem abrir janela.
; runasoriginaluser garante que o app não herda privilégios elevados em install per-machine.
Filename: "{app}\{#AppExeName}"; Parameters: "--bandeja"; Flags: nowait shellexec runasoriginaluser; Check: WizardSilent

[UninstallRun]
; App de bandeja segura o .exe aberto; encerra antes de remover os arquivos.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExeName}"; Flags: runhidden; RunOnceId: "KillMeuClima"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function GetInstallScope(Param: string): string;
begin
  if IsAdminInstallMode then
    Result := 'machine'
  else
    Result := 'user';
end;

// No auto-update o app tem ~2 s para encerrar antes do setup começar; se demorar mais
// (WebView2, ícone da bandeja), esperamos o mutex sumir em vez de cair no Restart Manager.
function InitializeSetup(): Boolean;
var
  Waited: Integer;
begin
  Waited := 0;
  while CheckForMutexes('{#AppMutexName}') and (Waited < 30000) do
  begin
    Sleep(500);
    Waited := Waited + 500;
  end;
  Result := True;
end;

const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Users: TArrayOfString;
  I: Integer;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  RegDeleteValue(HKCU, RunKey, '{#StartupValueName}');
  // Desinstalação per-machine roda elevada: HKCU pode ser de outro admin. Limpa os perfis carregados.
  if IsAdminInstallMode and RegGetSubkeyNames(HKU, '', Users) then
    for I := 0 to GetArrayLength(Users) - 1 do
      RegDeleteValue(HKU, Users[I] + '\' + RunKey, '{#StartupValueName}');
end;
