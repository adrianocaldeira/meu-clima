# Privacidade do Meu Clima

O Meu Clima roda no seu computador. Ele não tem conta própria, não coleta dados de uso,
não mostra anúncios e não envia nada para o autor do app.

## Com quem o app se comunica

1. Electrolux Group API (api.developer.electrolux.one)
   Para ler e ajustar os seus aparelhos. Usa a API key e o refresh token que você cria no
   portal da Electrolux. Recebe os comandos que você ou as regras enviam. Os dados seguem
   os termos da Electrolux.

2. Open-Meteo (api.open-meteo.com e geocoding-api.open-meteo.com)
   Para o clima de fora. Recebe o nome que você digita ao buscar a cidade e, depois, a
   latitude e a longitude da cidade escolhida. Nada que identifique você.

3. GitHub (api.github.com, github.com e os servidores de download do GitHub)
   Para saber se há versão nova: ao iniciar e a cada 30 minutos. Desligue em
   Ajustes > "Procurar atualizações automaticamente"; aí a consulta só acontece quando
   você pedir. O instalador novo só é baixado quando você manda instalar.

4. Interruptor Sonoff da luz do escritório (só se você configurar)
   Pela rede local, sem passar pela internet: o app ouve os anúncios do interruptor
   (mDNS, porta 5353) e manda acender ou apagar na porta 8081 dele. A chave do interruptor
   fica criptografada com DPAPI em %AppData%\MeuClima\ewelink.dat.

Nada além disso: as telas e as fontes vêm de dentro do próprio app.

Toda requisição leva o endereço IP do seu computador, como qualquer acesso à internet,
e o nome do app (em parte delas, também a versão).

A janela do app usa o Microsoft Edge WebView2, componente do Windows. O envio de dados de
diagnóstico dele segue as configurações de privacidade do Windows.

## O que fica no seu computador

- Configurações, regras e histórico de leituras (90 dias): %AppData%\MeuClima
- API key e refresh token, criptografados com a proteção de dados do Windows (DPAPI),
  que só a sua conta do Windows consegue abrir: %AppData%\MeuClima\credentials.dat
- Registros de funcionamento, sem credenciais: %LocalAppData%\MeuClima

## Mudanças no sistema

- Iniciar com o Windows: o instalador pergunta. Se marcado, o app grava a entrada
  "MeuClima" em HKCU\Software\Microsoft\Windows\CurrentVersion\Run. Mude em Ajustes ou
  no menu do ícone da bandeja.
- O instalador grava em Software\MeuClima (no seu usuário, ou na máquina se instalado para
  todos) o local, a versão e o tipo da instalação, usados pela atualização automática, e a
  resposta sobre iniciar com o Windows.

## Desinstalar

Configurações > Aplicativos > Meu Clima > Desinstalar. Remove o app, a entrada de início
com o Windows e o registro da instalação. Numa instalação para todos os usuários, a entrada
de início com o Windows sai dos perfis abertos no momento; nos outros, o Windows apenas
ignora o atalho que ficou. Para apagar também os seus dados, exclua as
pastas %AppData%\MeuClima e %LocalAppData%\MeuClima.

Dúvidas: https://github.com/adrianocaldeira/meu-clima/issues
