# FAQ — perguntas frequentes

## Compatibilidade

### Funciona com o meu ar-condicionado?

Só se ele for **Electrolux com Wi-Fi** e estiver cadastrado e funcionando no **app Electrolux**. Outras
marcas não funcionam. Aparelho Electrolux sem Wi-Fi também não.

### O Meu Clima é da Electrolux?

**Não.** É um projeto independente e de código aberto que usa a API pública oficial da Electrolux com
as suas credenciais. Não tem afiliação, patrocínio nem suporte da Electrolux.

### As regras funcionam com o PC desligado?

**Não.** As regras rodam no app, no seu PC. PC desligado, hibernado, ou sem ninguém logado no Windows
= nenhuma regra executa. O ar-condicionado fica no último estado que recebeu. Duas exceções:
o **Ausente** desliga o ar no momento em que o PC desliga ou suspende, e o **Modo noite** programa o
timer do próprio aparelho, que desliga mesmo com o PC desligado.

---

## Credenciais

### Preciso pagar alguma coisa no portal da Electrolux?

Até onde sabemos, criar API key e token no [developer.electrolux.one](https://developer.electrolux.one/)
é gratuito para uso pessoal. Os termos são da Electrolux; confira no portal.

### O app diz que as credenciais são inválidas

Causas mais comuns:

1. **Conta diferente** — o portal foi acessado com outra conta, não a do app Electrolux.
2. **Refresh token usado em outro lugar** — o token rotaciona a cada renovação; se outro programa
   renovou primeiro, o do Meu Clima deixou de valer. Gere um token novo só para o Meu Clima.
3. **Token ou key revogados** no portal.

Solução: gere um novo refresh token no portal e informe no app.

### "A Electrolux recusou a leitura (HTTP 403)"

O 403 vem tanto de API key inválida quanto do limite de chamadas da API. O app tenta de novo em 5 min;
se o aviso some sozinho, era o limite. Se continuar, confira a API key no portal e informe de novo em
**Conta e local**. O motivo exato que a Electrolux devolveu fica em `%LocalAppData%\MeuClima\meuclima.log`.

### Uma regra fez (ou não fez) algo e eu quero entender por quê

Abra `%LocalAppData%\MeuClima\meuclima.log`. Ele registra cada leitura que mudou ("Leitura Escritório:
ligado Cool Auto alvo 24 dentro 26"), cada mudança no que uma regra diz sobre um aparelho e cada
comando aplicado. Ao reportar um problema, anexe o trecho do horário em questão.

### Onde ficam minha API key e meu token?

Em `%APPDATA%\MeuClima\`, criptografadas com a **DPAPI** do Windows: só o seu usuário, naquele PC,
consegue ler. Nenhum token vai para o log. O Meu Clima não tem servidor; as credenciais só trafegam
entre o seu PC e a API da Electrolux.

---

## Instalação e primeira execução

### O Windows SmartScreen bloqueia o instalador

**Causa:** o `.exe` não tem assinatura comercial (certificado de code-signing custa caro pra projeto pessoal).

**Solução:** clique em **"Mais informações"** → **"Executar assim mesmo"**.

Para verificar a autenticidade, compare o `SHA256` do `.exe` baixado com o publicado na página da release.

### Janela em branco

Falta o [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (Evergreen Bootstrapper). No Windows 11 já vem; no Windows 10 antigo, pode estar ausente.

### Não acho o ícone

Está nos ícones ocultos da bandeja (seta `^` ao lado do relógio). Arraste para a barra para fixar.

---

## Uso

### Mudei a temperatura pelo celular e o app desfez

Não deveria: uma mudança que o Meu Clima não comandou conta como ajuste manual e **pausa as
automações daquele aparelho por 60 min**. Mas o app só percebe a mudança na leitura seguinte (a cada
1 min, por padrão) e o aparelho leva ~30 s para refletir comandos. Se uma regra agiu na mesma janela,
ela pode ter vencido. Veja em [[Regras]] e, se acontecer de novo, abra uma issue.

### Os comandos demoram para chegar

O comando vai do PC para a nuvem da Electrolux e de lá para o aparelho. O estado informado pela API
costuma refletir o comando em cerca de 30 s — isso é normal. Se demora minutos, confira se o aparelho aparece online no app Electrolux.

### A temperatura externa não bate com a do termômetro da varanda

O Open-Meteo dá a temperatura da região (modelo meteorológico), não a do seu prédio. Diferenças de
alguns graus são esperadas.

---

## Dados e privacidade

### O app envia dados pra algum lugar?

Só para onde precisa: **API da Electrolux** (estado e comandos do seu aparelho), **Open-Meteo** (a
localização que você informou, para buscar o clima) e **GitHub** (verificar se há versão nova). Sem
telemetria, sem analytics, sem servidor próprio.

### Como resetar tudo?

Desinstale o app e apague `%APPDATA%\MeuClima\` e `%LocalAppData%\MeuClima\`. Depois revogue a API key no portal da
Electrolux, se não for mais usar.

---

## Bug encontrado?

Antes de abrir issue:

1. Confirme se está na **última versão** (https://github.com/adrianocaldeira/meu-clima/releases)
2. Confirme que o aparelho responde pelo **app Electrolux**
3. Veja se já existe [issue aberta sobre o mesmo](https://github.com/adrianocaldeira/meu-clima/issues)

Pra reportar: [nova issue de bug](https://github.com/adrianocaldeira/meu-clima/issues/new?template=bug_report.yml). **Não cole credenciais, número de série nem ID do aparelho.**
