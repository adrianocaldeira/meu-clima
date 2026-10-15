# Regras automáticas

As regras rodam **dentro do Meu Clima, no seu PC**. Elas só executam com o PC ligado, o app aberto
(na bandeja) e você **logado no Windows**. PC desligado, hibernado ou na tela de login = nenhuma regra
avalia nada. A única ação que sobrevive ao PC desligado é o timer que o **Modo noite** programa no
próprio aparelho.

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
  Conforto e a Janela aberta estavam fazendo: ao fim da pausa, recomeçam do zero.
- **Sem comando à toa** — comando igual ao estado atual não é enviado, e há no mínimo 20 s entre
  comandos ao mesmo aparelho.
- **Atraso normal** — o aparelho leva cerca de 30 s para refletir um comando no estado que a API informa.

---

## Conforto

**Para quê:** manter dentro entre **23 e 24 °C** gastando o mínimo de energia.

**Quando vale:** com você presente no PC e dentro da agenda. A agenda (dias da semana e horário) é
opcional; sem ela, vale para qualquer dia e hora. "Só com alguém no PC" pode ser desligado.

**O que faz:**
- **Acima da máxima** por 5 min: liga (ou passa) para Frio, ventilação Auto, **modo econômico**,
  alvo 24 °C.
- **Calor forte** — dentro **≥ 26 °C** por 5 min: Frio com ventilação **Turbo** até voltar a 24 °C,
  por no máximo **30 min**. Esgotado o tempo, volta ao Frio econômico e só tenta o turbo de novo
  depois que dentro chegar à máxima.
- **Na mínima** por 10 min: **só ventila** (compressor desligado, ventilação baixa). Com "Só ventilar"
  desligado, desliga o ar. Volta ao frio quando dentro passar da máxima por 5 min.
- **Antes do calor:** se a **sensação térmica** prevista para os próximos 30 min passar de **30 °C** e
  dentro já estiver na máxima, liga em Frio econômico. Uma vez por dia.
- **Lá fora mais fresco:** com o ar esfriando, se a sensação lá fora estiver **3 °C abaixo** de
  dentro, avisa para abrir a janela e desligar. Entre 7h e 22h, no máximo um aviso a cada 3 h.
  **Só avisa, não comanda.**
- Com o aparelho já ligado (antes de o app abrir, ou por você depois de terminada a pausa de 60 min),
  assume na hora, sem esperar os 5 min.
  Exceção: se você ligou em **Quente** e "Aquecer abaixo da mínima" está desligado, o Conforto não mexe.
- Durante a pausa por ajuste manual, ou com uma regra de prioridade maior no comando, o Conforto não
  avança nem avisa; depois recomeça a contagem do zero.

**Por que não usa o modo Auto:** o Auto aquece sozinho quando dentro fica abaixo do alvo, o que nas
manhãs frias gasta energia sem você pedir. **Aquecer abaixo da mínima** existe como opção e vem
desligado.

**Quando para:** você longe do PC ou fim da agenda. Ao parar, **não desliga** o aparelho (quem desliga
na ausência é a regra "Ausente"); se estava no turbo, só tira a ventilação do Turbo.

**Atenção:** o sensor do aparelho mede graus inteiros e às vezes salta numa leitura só; por isso
cada mudança de fase espera alguns minutos de confirmação, com pelo menos uma leitura nova do aparelho. A sensação térmica vem do Open-Meteo para
a sua região, não da sua janela.

---

## Ausente

**Para quê:** não refrigerar a sala vazia. Vale para o aparelho do cômodo onde fica o PC.

**Ausência:** tela bloqueada (Win+L), PC em suspensão ou 5 min sem uso de teclado e mouse.

**O que faz:**
- **Tela bloqueada ou PC suspenso:** desliga **na hora**. Dá para esperar alguns minutos antes (útil
  para pausas curtas) ou desligar essa opção.
- **Sem uso do PC**, com a tela desbloqueada: com **20 min**, passa para Frio 26 °C (alívio); com
  **60 min**, desliga. Se estava no Turbo, o alívio também volta a ventilação para Auto. Se o ar estava aquecendo, não alivia: só desliga no tempo.
- **Na volta:** as regras ativas, como o Conforto, decidem se o ar liga de novo. Com "Voltar ao
  ajuste anterior" ligado, restaura o que estava antes (ligado, modo, ventilação e temperatura);
  tenta por até 15 min e, se o aparelho não aceitar nesse prazo, desiste.

Com o aparelho desligado, a regra não faz nada.

**Atenção:** é ausência **do PC**. Se você estiver na sala sem mexer no computador, a regra age. A
suspensão só desliga o ar se o app tiver tempo de enviar o comando antes de o PC dormir.

---

## Janela aberta

**Para quê:** não refrigerar a rua.

**O que faz:** se o aparelho está ligado em Frio há **≥ 15 min** e a temperatura do ambiente **sobe
≥ 2 °C em 15 min** e **se mantém** assim por 3 min, em pelo menos duas leituras, o app desliga o aparelho e avisa. Nos **60 min**
seguintes, mantém o aparelho desligado.

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
- Se o timer não chegar ao aparelho, o próprio app desliga o ar ao fim das 4 h (com o PC ligado).
- Ao cancelar, o app zera o timer que o Modo noite programou.

---

## Limites que valem para todas

- **Rede:** sem internet no PC, o app não lê o aparelho nem envia comando.
- **API da Electrolux:** instabilidade esporádica acontece; o app tenta de novo no ciclo seguinte.
- **Credenciais:** com o refresh token revogado ou invalidado, o app para de controlar o aparelho até
  você informar um novo.
