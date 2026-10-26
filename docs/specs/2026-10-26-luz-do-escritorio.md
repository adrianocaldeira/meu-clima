# Luz do escritório — especificação

Data: 26/10/2026 · Status: aprovada (módulo dentro do Meu Clima; bandeja na direção B do protótipo
`docs/prototipos/2026-10-26-luz-escritorio/`; central · Tomadas como no protótipo)

## Objetivo

Ligar e desligar pelo PC a luz do escritório, ligada a um interruptor Sonoff (eWeLink). Apagar a luz
quando o PC for bloqueado, suspenso ou desligado com ela acesa.

## Por que dentro do Meu Clima

Presença (bloqueio, suspensão, desligamento), bandeja, ajustes, credenciais com DPAPI, início com o
Windows, instalador e atualização automática já existem. Um app separado duplicaria tudo isso, teria
um segundo ícone na bandeja e um segundo executável sem assinatura (pedido SignPath rejeitado em
15/10). O módulo é separado do motor de regras do ar: `IClimateProvider` e `RuleEngine` são
específicos de ar-condicionado (modo, alvo, ventilação).

## Fatos medidos (POC de 26/10/2026)

| Fato | Medição |
|---|---|
| Os interruptores anunciam `_ewelink._tcp.local` por mDNS e escutam HTTP na 8081 | consulta mDNS na rede do escritório: 2 TX1C responderam |
| O anúncio traz o estado em `data1..data4` (base64), `iv` e `encrypt=true` | TXT decodificado |
| A cifra é AES-128-CBC, PKCS#7, chave = MD5(devicekey), IV aleatório de 16 bytes por mensagem | decifrado com sucesso: `{"switch":"off","startup":"off",...}` |
| O comando é `POST http://<ip>:8081/zeroconf/switch` com `{sequence, deviceid, selfApikey:"123", iv, encrypt:true, data}` | ligou e desligou o TX1C; resposta `{"error":0}`; o anúncio seguinte mostrou o estado novo |
| TX1C é uiid 6 (um canal, campo `switch`); multicanal usa `switches[{switch,outlet}]` em `/zeroconf/switches` | lista da nuvem |
| A `devicekey` só sai da nuvem: `GET /v2/device/thing` depois de OAuth2 (app próprio em dev.ewelink.cc, papel Standard) | resposta com `itemData.devicekey` |
| O papel Standard também libera `POST /v2/device/thing/status` (controle pela nuvem) | lista de APIs do papel |
| O `code` do OAuth expira em minutos | `405 invalid code` depois de ~3 min |
| Uma porta de retorno ocupada não dá erro no Windows: outro processo responde | `http.server` de outra sessão na 8765 devolveu 404 |
| Esta máquina usa NordVPN (interface NordLynx) | `Get-NetIPAddress` |

## Comportamento (critérios de aceite)

| # | Situação | Resultado esperado |
|---|---|---|
| CA1 | Usuário escolhe "Acender/apagar a luz" na bandeja (fatia 1) ou na faixa (fatia 2) | A luz troca de estado em até 1 s e o novo estado aparece no próximo anúncio |
| CA2 | PC bloqueado com a luz acesa e "apagar ao bloquear" ligado | Apaga depois da espera configurada (padrão 0 s). Se o PC for desbloqueado antes do fim da espera, não apaga |
| CA3 | PC suspendendo ou desligando com a luz acesa | Um único comando pela rede local, sem consulta prévia: usa o IP e o estado do último anúncio. Prazo de 2 s (suspensão) e 4 s (desligamento), dentro do que o `App` já segura |
| CA4 | Luz já apagada ao bloquear ou desligar | Nenhum comando |
| CA5 | Com o PC bloqueado, alguém acende no interruptor da parede | Não apaga de novo até o próximo desbloqueio |
| CA6 | Aparelho sem anúncio há mais de 10 min | Nenhum comando automático no desligamento; o log diz "luz sem anúncio recente" |
| CA7 | Decifra falha (chave trocada depois de reparear o aparelho) | Log e aviso único: "sincronize os aparelhos eWeLink"; nada de comando |
| CA8 (fatia 2) | Desbloqueio, com "reacender" em "Se escuro" | Reacende só se foi o app que apagou e se já passou do pôr do sol |

## Arquitetura

`MeuClima.Core/Switches/` (sem WPF e sem pacote novo: `System.Security.Cryptography.Aes`, `UdpClient`, `HttpClient`)
- `EwelinkCrypto`: cifra e decifra.
- `EwelinkAnnouncement` e `MdnsBrowser`: pergunta `_ewelink._tcp.local`, escuta 224.0.0.251:5353 na
  interface da rede local e devolve anúncios (deviceid, IP, `seq`, estado decifrado).
- `EwelinkLanClient`: `SetSwitchAsync(ip, deviceid, devicekey, on, timeout)`.
- `SwitchKeys`: deviceid → nome, devicekey, uiid. Persistência cifrada fica no app, como o `ITokenStore`.
- `LuzPolicy`: decisão pura (estado, presença, origem da última mudança, idade do anúncio) → comando ou não, com motivo.

`MeuClima` (app)
- `SwitchService`: mantém o último anúncio por aparelho, aplica `LuzPolicy` em `PresenceMonitor.Changed`
  e expõe `TurnOffForShutdownAsync(motivo, prazo)`. `App.DesligarAntesDeSair` chama ar e luz em paralelo.
- Chaves em `ewelink.dat` (DPAPI, entropia própria).
- Ajustes em `AppSettings.Luz`.

## Segurança e dados

- `devicekey`, appSecret e tokens nunca vão para log, tela ou teste. No log, o deviceid sai mascarado (`1001…35`).
- Testes usam chave e deviceid inventados; o vetor de cifra é gerado com IV fixo e chave falsa.
- `settings.json` é lido em modo estrito: uma versão antiga do app que encontrar `luz` abre só leitura.
  É o comportamento existente para campo novo e vai no CHANGELOG.

## Fatias

1. **Núcleo, sem tela nova:** Core completo com testes; `SwitchService`; apagar no bloqueio, na
   suspensão e no desligamento; item "Acender/apagar a luz" no menu da bandeja; chaves importadas
   por `--importar-ewelink <keys.json>` (o arquivo gerado pela POC). CA1–CA7.
2. **Tela e conta:** faixa B na bandeja; central · Tomadas; login eWeLink no app (porta de retorno
   aberta em modo exclusivo, com aviso se estiver ocupada); reserva pela nuvem; atalho global
   Ctrl+Alt+L; "reacender se escuro" (nascer e pôr do sol no `OpenMeteoClient`); insights. CA8.

Fora de escopo: cenas por cômodo (direção C rejeitada), tomadas de outras marcas, firmware alternativo.

## Riscos

| Risco | Mitigação |
|---|---|
| VPN capturando multicast ou bloqueando a rede local | Escolher a interface pela sub-rede do aparelho; diagnóstico explícito na fatia 2; reserva pela nuvem |
| IP muda (DHCP) | Nunca gravar IP; usar o do último anúncio |
| Rede cai cedo no desligamento | O comando pela rede local leva ~0,1 s; cena "apagar às 20h" no app eWeLink como rede de segurança, sem código |
| Firmware muda o protocolo local | Reserva pela nuvem (fatia 2) |
