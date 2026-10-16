# Regras automáticas

As regras rodam **dentro do Meu Clima, no seu PC**. Elas só executam com o PC ligado, o app aberto
(na bandeja) e você **logado no Windows**. PC desligado, hibernado ou na tela de login = nenhuma regra
avalia nada. A única ação que sobrevive ao PC desligado é o timer que o **Modo noite** programa no
próprio aparelho. Na hora em que o PC desliga ou suspende, o **Ausente** desliga o ar (veja abaixo).

O app lê os aparelhos a cada 1 minuto (configurável: 1, 2 ou 5) e o clima a cada 15 minutos
(15, 30 ou 60), em **Ajustes**. As regras ligadas são avaliadas a cada poucos segundos com a última
leitura. Bloquear ou desbloquear a tela, suspender ou retomar o PC dispara uma avaliação na hora.

Ao concluir a configuração (conta, cidade e cômodo do PC), o app cria as regras **já ligadas**:
"Conforto" e "Ausente" no aparelho do cômodo do PC; "Janela aberta" e "Modo noite" em cada aparelho.
Desligue as que não quiser na central, em **Regras**. Cada regra age num aparelho, que você escolhe
ao editar.

Quem vem da v0.1: na primeira abertura, "Esfriar rápido" e "Antes do pico" viram um "Conforto" por
aparelho. Ele fica ligado se o "Esfriar rápido" estava ligado (sem ele, segue o "Antes do pico"). Herda
do "Esfriar rápido" a agenda, o "Só com alguém no PC" (ou o do "Antes do pico", se só ele existia) e a
faixa: a máxima é o alvo da fase 2 (padrão 24 °C), a mínima fica 1 °C abaixo e o turbo começa 2 °C acima.
O aviso do "Fora mais fresco" agora faz parte do Conforto, com a mesma diferença de temperatura, horário
fixo (7h–22h) e no máximo um a cada 3 h: horário e intervalo personalizados se perdem. Se o "Fora mais
fresco" estava desligado, a diferença vai para 15 °C, o que na prática cala o aviso. Quem tinha só o
"Fora mais fresco" fica sem ele até ligar um Conforto. Com duas agendas no mesmo aparelho, vale a primeira. Os padrões novos do "Ausente" também
passam a valer: desliga ao bloquear a tela e, na volta, não restaura o ajuste anterior.

Os valores abaixo são os **padrões**; os ajustáveis aparecem na configuração de cada regra.

## Como as regras convivem

- **Prioridade** — se duas regras querem mexer no mesmo aparelho no mesmo ciclo, vence a de maior
  prioridade: **Janela aberta > Modo noite > Ausente > Conforto**.
- **Seu ajuste manda** — mudou algo à mão, as automações daquele aparelho **pausam por 60 minutos**.
  Conta como manual: mudança pelo painel do Meu Clima, ou mudança no aparelho que o Meu Clima não
  comandou (controle remoto, app do celular). No painel, **Mais ajustes** → **Pausar 1 h** pausa um
  aparelho por conta própria, e o menu **⋯** → **Pausar as regras por 1 h** pausa todos. Na linha
  "Você" do aparelho, **Retomar agora** encerra a pausa antes da hora. O ajuste manual também zera o que o
  Conforto e a Janela aberta estavam fazendo: ao fim da pausa, recomeçam do zero. A pausa continua
  valendo se o app reiniciar (atualização, PC desligado e ligado de novo).
- **Leitura recente** — nenhuma regra comanda o aparelho com a última leitura mais velha que 3 min
  (com leitura a cada 1 min): o aparelho pode ter mudado sem o app ver. O painel mostra "sem leitura
  nova desde HH:mm".
- **Sem comando à toa** — comando igual ao estado atual não é enviado, e há no mínimo 20 s entre
  comandos ao mesmo aparelho.
- **Atraso normal** — o aparelho leva cerca de 30 s para refletir um comando no estado que a API informa.
- **Aviso só com comando aceito** — avisos como "Conforto ligou o ar" ou "Desliguei o ar" só aparecem
  depois que a Electrolux aceita o comando. Comando recusado não gera aviso. Sem conexão, o aviso
  espera o reenvio por até 15 min.
- **O que o aparelho aceita** — ventilação que o aparelho não declara sai do comando, e um alvo fora
  da faixa dele vai para o limite. Modo que ele não declara (ex.: Quente num aparelho só de Frio)
  cancela o comando inteiro, para não ligar o ar no modo errado. O log registra o que ficou de fora.
- **Correção do sensor** — em **Ajustes** → **Sensor de temperatura**, dá para somar de -3 a +3 °C à
  leitura de cada aparelho. Regras e painel usam o valor corrigido; o cômodo mostra ao lado a
  leitura bruta ("sensor 27"). Ao mudar a correção, a Janela aberta ignora as leituras anteriores.
  Use quando um termômetro na altura da mesa marcar 1 °C ou mais de diferença do aparelho; como
  calibrar está no [[FAQ]].

---

## Conforto

**Para quê:** manter dentro entre **23 e 24 °C** gastando o mínimo de energia.

**Quando vale:** com você presente no PC e dentro da agenda. A agenda (dias da semana e horário) é
opcional; sem ela, vale para qualquer dia e hora. "Só com alguém no PC" pode ser desligado.

**O que faz:**
- **Acima da máxima** por 5 min (por 2 min se estiver 2 °C ou mais acima): liga (ou passa) para Frio, ventilação Auto, **modo econômico**,
  alvo 24 °C.
- **Calor forte** — dentro **≥ 26 °C** por 2 min: Frio com ventilação **Turbo** até voltar a 24 °C,
  por no máximo **30 min a cada 2 h**, somando todos os turbos (sair e voltar ao PC não zera a conta).
  Um turbo novo só começa com pelo menos 10 min de saldo; até lá, Frio econômico. Esgotado o tempo,
  volta ao Frio econômico e só tenta o turbo de novo depois que dentro chegar à máxima. Se o turbo inteiro passar
  sem a leitura baixar nem 1 °C, o painel avisa que o sensor do aparelho parece travado (ele fica
  no aparelho, perto do teto, e com o turbo pode medir o ar frio que volta para ele).
- **Na mínima** por 10 min: **só ventila** (compressor desligado, ventilação baixa). Com "Só ventilar"
  desligado, desliga o ar. Volta ao frio quando dentro passar da máxima pelo mesmo tempo; enquanto espera, o painel diz
  desde quando passou e até que horas espera.
- **Antes do calor:** se a **sensação térmica** prevista para os próximos 30 min passar de **30 °C** e
  dentro já estiver na máxima, liga em Frio econômico. Uma vez por dia.
- **Lá fora mais fresco:** vale entre 7h e 22h, quando a temperatura lá fora está **3 °C abaixo** de
  dentro e o ar de fora é seco o bastante (ponto de orvalho até 18 °C). A sensação térmica não conta:
  o vento de fora não entra no cômodo.
  - **Antes de ligar:** avisa para abrir a janela e espera 20 min. Se dentro continuar acima da
    máxima, liga.
  - **Com o ar esfriando:** avisa para abrir a janela e desligar, no máximo um aviso a cada 3 h.
    **Só avisa, não comanda.**
- **Compressor:** entre ligar e desligar o compressor (Frio ou Quente de um lado; desligado ou só
  ventilando do outro), o Conforto espera pelo menos **5 min**. O painel diz até que horas. Seu ajuste
  não espera.
- Com o aparelho já ligado (antes de o app abrir, ou por você depois de terminada a pausa de 60 min),
  assume na hora, sem esperar os 5 min.
  Exceção: se você ligou em **Quente** e "Aquecer abaixo da mínima" está desligado, o Conforto não mexe.
- **Aquecendo:** quando dentro passa da mínima por 5 min, só ventila (ou desliga, com "Só ventilar"
  desligado). Nunca passa direto do Quente para o Frio.
- Durante a pausa por ajuste manual, ou com uma regra de prioridade maior no comando, o Conforto não
  avança nem avisa; depois recomeça a contagem do zero.
- **Depois de 10 min sem leitura** (PC desligado ou suspenso, app fechado), o Conforto recomeça do zero:
  não religa o ar com o que via antes e espera de novo os 5 min acima da máxima. Ar que já está só
  ventilando dentro da faixa continua ventilando.

**Por que não usa o modo Auto:** o Auto aquece sozinho quando dentro fica abaixo do alvo, o que nas
manhãs frias gasta energia sem você pedir. **Aquecer abaixo da mínima** existe como opção e vem
desligado. Em aparelho sem o modo Quente, a opção fica travada.

**Quando para:** você longe do PC ou fim da agenda. Ao parar, **não desliga** o aparelho (quem desliga
na ausência é a regra "Ausente"); se estava no turbo, só tira a ventilação do Turbo.

**Atenção:** o sensor do aparelho mede graus inteiros e às vezes salta numa leitura só; por isso
cada mudança de fase espera alguns minutos de confirmação, com pelo menos uma leitura nova do aparelho.
Depois de duas leituras do mesmo lado, uma leitura isolada do outro lado não zera a contagem; duas
seguidas zeram. Um salto de 3 °C ou mais de uma leitura para a seguinte espera os 5 min normais, mesmo
acima de 26 °C. O clima de fora vem do Open-Meteo para a sua região, não da sua janela.

---

## Ausente

**Para quê:** não refrigerar a sala vazia. Vale para o aparelho do cômodo onde fica o PC.

**Ausência:** tela bloqueada (Win+L), PC em suspensão ou 5 min sem uso de teclado e mouse.

**O que faz:**
- **Tela bloqueada:** desliga **na hora**. Dá para esperar alguns minutos antes (útil para pausas
  curtas) ou desligar essa opção.
- **PC desligando, suspendendo ou saindo da sessão:** desliga o ar na hora, antes de o PC apagar.
  Não desliga se há ajuste manual em vigor (você ligou de propósito) ou Modo noite ativo (o timer do
  aparelho cuida). A opção "Desligar quando o PC desligar ou suspender" vem ligada; desligada, o ar
  fica como está.
- **Sem uso do PC**, com a tela desbloqueada: com **20 min**, passa para Frio 26 °C (alívio); com
  **60 min**, desliga. Se estava no Turbo, o alívio também volta a ventilação para Auto. Se o ar estava
  aquecendo ou só ventilando, não alivia: só desliga no tempo.
- **Você mexeu durante a ausência** (religou pelo controle, pelo celular ou pelo painel): o Ausente não
  desliga de novo até você voltar ao PC, nem no desligamento do PC. Na volta, não restaura nada.
- **Na volta:** as regras ativas, como o Conforto, decidem se o ar liga de novo. Com "Voltar ao
  ajuste anterior" ligado, restaura o que estava antes (ligado, modo, ventilação e temperatura);
  tenta por até 15 min e, se o aparelho não aceitar nesse prazo, desiste.

Com o aparelho desligado, a regra não faz nada.

**Atenção:** é ausência **do PC**. Se você estiver na sala sem mexer no computador, a regra age. No
desligamento o Windows dá poucos segundos: o app envia um único comando, sem renovar o acesso à
Electrolux. Sem internet nesse instante, o ar fica ligado; o log registra o que aconteceu.

---

## Janela aberta

**Para quê:** não refrigerar a rua.

**O que faz:** se o aparelho está ligado em Frio há **≥ 15 min** e a temperatura do ambiente **sobe
≥ 2 °C em 15 min** e **se mantém** assim por 3 min, em pelo menos duas leituras, o app desliga o aparelho e avisa. Nos **60 min**
seguintes, mantém o aparelho desligado. O aviso sai só depois que o comando é aceito.

Não dispara quando:
- o alvo está no nível de dentro ou acima (o alívio do Ausente e o Modo noite sobem o alvo de propósito);
- o alvo mudou nos últimos 15 min;
- o aparelho está em pausa por ajuste manual.

No painel, a regra diz o motivo e o prazo, por exemplo: "Dentro subiu de 24 para 26 °C com o ar no
frio: janela ou porta aberta? Ar desligado até 11:20 ou até você religar."

**Cancela antes da hora** se você mexer no aparelho (veja "Seu ajuste manda") ou se ele for religado
e dentro voltar a ficar abaixo da temperatura que disparou a regra.

**Atenção:** é uma **suspeita**, não um sensor de janela. O sensor do aparelho mede em graus inteiros,
por isso a regra só reage a variações claras. Porta aberta, sol forte de repente ou muita gente
entrando também podem disparar.

---

## Modo noite

**Para quê:** dormir sem frio e sem o ar ligado a noite toda.

**Como ativar:**
- **Pelo painel:** clique no ícone da bandeja → **Mais ajustes** no aparelho → **Modo noite** →
  **Ativar**. O mesmo botão vira **Cancelar**.
- **Por horário (opcional):** na regra, ligue "Ativar todo dia às" e escolha a hora. No horário, se o
  aparelho estiver ligado, ativa; se estiver desligado, não ativa naquele dia (ligar depois não
  adianta). Os 30 min seguintes só cobrem o app abrir atrasado.

Ativar pelo painel também encerra uma pausa por ajuste manual daquele aparelho, e liga a regra (ou
cria uma, se não existir).

**O que faz:**
- Programa o **timer do próprio aparelho** para desligar em **4 h**. Esse timer **funciona mesmo com
  o PC desligado**.
- Enquanto o PC estiver ligado, **sobe 1 °C por hora**, a partir do alvo do momento, com teto de 28 °C.
  Aquecendo, não sobe: mantém o alvo.
- Se você mudar ou zerar o timer pelo painel do Meu Clima, o Modo noite não programa outro naquela
  noite. Timer zerado ou trocado pelo controle ou pelo celular também passa a ser seu: o app registra
  no log e não reprograma.
- Se o timer não chegar ao aparelho, o próprio app desliga o ar ao fim das 4 h (com o PC ligado).
- Ao cancelar, o app zera o timer que o Modo noite programou.

---

## Limites que valem para todas

- **Rede:** sem internet no PC, o app não lê o aparelho nem envia comando.
- **API da Electrolux:** instabilidade esporádica acontece; o app tenta de novo no ciclo seguinte.
- **Credenciais:** com o refresh token revogado ou invalidado, o app para de controlar o aparelho até
  você informar um novo.
