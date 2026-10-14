# Regras automáticas

As regras rodam **dentro do Meu Clima, no seu PC**. Elas só executam com o PC ligado, o app aberto
(na bandeja) e você **logado no Windows**. PC desligado, hibernado ou na tela de login = nenhuma regra
avalia nada. A única ação que sobrevive ao PC desligado é o timer que o **Modo noite** programa no
próprio aparelho.

O app lê os aparelhos a cada 1 minuto (configurável: 1, 2 ou 5) e o clima a cada 15 minutos
(15, 30 ou 60), em **Ajustes**. As regras ligadas são avaliadas a cada poucos segundos com a última
leitura. Bloquear ou desbloquear a tela dispara uma avaliação na hora.

Ao concluir a configuração (conta, cidade e cômodo do PC), o app cria as seis regras **já ligadas**:
"Esfriar rápido", "Saí do PC" e "Antes do pico" no aparelho do cômodo do PC; "Janela aberta" e
"Modo noite" em cada aparelho; "Fora mais fresco" para todos. Desligue as que não quiser na central,
em **Regras**. Cada regra age num aparelho, que você escolhe ao editar.

Os valores abaixo são os **padrões**; os ajustáveis aparecem na configuração de cada regra.

## Como as regras convivem

- **Prioridade** — se duas regras querem mexer no mesmo aparelho no mesmo ciclo, vence a de maior
  prioridade: **Janela aberta > Modo noite > Saí do PC > Esfriar rápido > Antes do pico**.
  "Fora mais fresco" só avisa, não comanda.
- **Seu ajuste manda** — mudou algo à mão, as automações daquele aparelho **pausam por 60 minutos**.
  Conta como manual: mudança pelo painel do Meu Clima, ou mudança no aparelho que o Meu Clima não
  comandou (controle remoto, app do celular). No painel, **Mais ajustes** → **Pausar 1 h** pausa por
  conta própria, e **Retomar** encerra a pausa antes da hora.
- **Sem comando à toa** — comando igual ao estado atual não é enviado, e há no mínimo 20 s entre
  comandos ao mesmo aparelho.
- **Atraso normal** — o aparelho leva cerca de 30 s para refletir um comando no estado que a API informa.

---

## Esfriar rápido

**Para quê:** chegar a um ambiente fresco depressa nos dias quentes, sem deixar o ar no máximo o dia todo.

**Quando dispara:** temperatura lá fora **≥ 24 °C**, com você presente no PC e dentro da agenda.

**Agenda (opcional):** dias da semana e faixa de horário. Sem agenda, vale para qualquer dia e hora.
Ao ligar a agenda, o padrão é segunda a sexta, das 8h às 19h.

**O que faz:**
1. **Fase 1 (impulso):** liga em Frio, ventilação Turbo, alvo 22 °C, até o ambiente chegar a 22 °C
   (ou por no máximo 60 min).
2. **Fase 2 (manutenção):** muda para Auto, 24 °C, ventilação Auto, com o modo econômico ligado.
   O modo (Auto ou Frio) e o econômico são ajustáveis.

Se ao disparar o ambiente já estiver ≤ 22 °C, vai direto para a fase 2.

Enquanto você estiver longe do PC, a regra não envia comandos. "Só com alguém no PC" pode ser desligado.

**Quando para:** lá fora abaixo de 23 °C por 30 min, ou fim da agenda. Ao parar, **não mexe** no aparelho.

---

## Saí do PC

**Para quê:** não refrigerar a sala vazia. Vale para o aparelho do cômodo onde fica o PC.

**Ausência:** tela bloqueada (Win+L) ou 5 min sem uso de teclado e mouse. O tempo conta desde o
bloqueio ou o último uso.

**O que faz:**
- Com **20 min** de ausência e aparelho ligado: muda para Auto 26 °C (economia).
- Com **60 min** de ausência: desliga.
- Na volta: restaura o que estava antes (ligado, modo, ventilação e temperatura). Tenta por até
  15 min; se o aparelho não aceitar nesse prazo, desiste.

Com o aparelho desligado, a regra não faz nada e não religa na volta.

**Atenção:** é ausência **do PC**. Se você estiver na sala sem mexer no computador, a regra age.

---

## Antes do pico

**Para quê:** chegar à hora mais quente com o ambiente já fresco.

**O que faz:** olha a previsão horária do Open-Meteo e acha a **primeira hora de hoje com ≥ 32 °C**.
**30 min antes**, se o aparelho estiver desligado e você estiver presente, liga em Auto 24 °C e mantém
assim até essa hora. Age no máximo uma vez por dia: se nessa janela o aparelho já estiver ligado, ou
você estiver longe do PC, a regra não faz nada naquele dia.

**Atenção:** depende da previsão. Se a previsão errar, a regra erra junto. Dia sem previsão de 32 °C
não aciona.

---

## Janela aberta

**Para quê:** não refrigerar a rua.

**O que faz:** se o aparelho está ligado em Frio há **≥ 15 min** e a temperatura do ambiente **sobe
≥ 2 °C em 15 min**, o app desliga o aparelho e avisa. Nos **60 min** seguintes, mantém o aparelho
desligado. Se você religar à mão, o seu ajuste vale (veja "Seu ajuste manda").

**Atenção:** é uma **suspeita**, não um sensor de janela. O sensor do aparelho mede em graus inteiros,
por isso a regra só reage a variações claras. Porta aberta, sol forte de repente ou muita gente
entrando também podem disparar.

---

## Modo noite

**Para quê:** dormir sem frio e sem o ar ligado a noite toda.

**Como ativar:**
- **Pelo painel:** clique no ícone da bandeja → **Mais ajustes** no aparelho → **Modo noite** →
  **Ativar**. O mesmo botão vira **Cancelar**.
- **Por horário (opcional):** na regra, ligue "Ativar todo dia às" e escolha a hora. Ativa só se o
  aparelho estiver ligado nos 30 min seguintes a esse horário.

Ativar pelo painel também encerra uma pausa por ajuste manual daquele aparelho.

**O que faz:**
- Programa o **timer do próprio aparelho** para desligar em **4 h**. Esse timer **funciona mesmo com
  o PC desligado**.
- Enquanto o PC estiver ligado, **sobe 1 °C por hora**, a partir do alvo do momento, com teto de 28 °C.
- Se o timer não chegar ao aparelho, o próprio app desliga o ar ao fim das 4 h (com o PC ligado).
- Ao cancelar, o app zera o timer que o Modo noite programou.

---

## Fora mais fresco

**Para quê:** aproveitar o ar de fora quando ele já está melhor que o de dentro.

**O que faz:** se algum aparelho está ligado em Frio e lá fora está **pelo menos 3 °C mais fresco** que dentro,
o app **avisa** — sugere desligar e abrir a janela. **Não comanda o aparelho.**

**Limites:** só entre 7h e 22h, e no máximo um aviso a cada 3 h.

**Atenção:** a temperatura de fora é a da sua região (modelo meteorológico), não a da sua janela.

---

## Limites que valem para todas

- **Rede:** sem internet no PC, o app não lê o aparelho nem envia comando.
- **API da Electrolux:** instabilidade esporádica acontece; o app tenta de novo no ciclo seguinte.
- **Credenciais:** com o refresh token revogado ou invalidado, o app para de controlar o aparelho até
  você informar um novo.
