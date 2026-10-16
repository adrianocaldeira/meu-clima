# Plano — confiabilidade das regras (16/10/2026)

Fontes:
- Auditoria do código: 16 testes temporários que afirmavam o defeito. Os 16 passaram, depois foram apagados.
- Forense do `history.db` e do log de 14/10 17:53 a 16/10 08:50: 1.229 leituras e 55 eventos.
- Pesquisa externa: Home Assistant, Versatile Thermostat, SDK oficial da Electrolux, `ha-electrolux`, Daikin, Trane e LBNL.

Legenda: **[P]** provado por teste ou dado; **[H]** hipótese.

## Por que estamos indo e voltando

1. **Estado velho.** O motor decide em cima de três coisas que podem estar desatualizadas:
   - a fase salva em disco;
   - as marcas "desde quando";
   - a última leitura, mesmo velha.

   Nenhuma delas tem prazo de validade. Cada correção até a v0.2.3 tratou um sintoma dessa mesma falha.
2. **Ligar não é uma decisão explícita.** As fases Manter, Ventilar, Aquecer e a saída do Turbo mandam `Power(true)` toda vez que rodam. Não existe uma passagem única de "desligado" para "ligado", com condições conferidas, que seja o único caminho para ligar o ar.
3. **Sem rede de proteção.** Não há teste que reproduza a sequência real nem regras fixas checadas a cada passo. O bug só aparece no aparelho.

## Fase 0 — ferramentas para achar falha antes do usuário

| # | Item | O que entrega |
|---|---|---|
| 0.1 | **Log de decisão** por ciclo e por aparelho | Uma linha só quando algo muda, com: idade da leitura; fase antes e depois e o motivo; marcas "desde quando"; bloqueios; comandos planejados e enviados. Mais uma linha para cada evento do sistema: boot, suspensão, volta da suspensão, desligamento, lock/unlock e pausa |
| 0.2 | **Tempo injetado** (`TimeProvider`) no motor e no `ClimateService` | Permite simular horas de funcionamento em milissegundos |
| 0.3 | **Replay** do `history.db` | Script que exporta um trecho do histórico, sem IDs, para uma fixture. Um teste roda o motor sobre a fixture e compara os comandos. Os casos de 15–16/10 viram testes golden: retomada às 08:42, os dois turbos, a janela de 10:21 e o ligar com o aviso de abrir a janela às 20:31 |
| 0.4 | **Testes de propriedade** (gerador próprio com semente fixa, sem dependência nova; a falha imprime a semente), com um simulador simples do cômodo | O simulador tem: modelo de 1ª ordem, leitura arredondada para 1 °C, 30 s de atraso no comando e viés do sensor. O gerador cria sequências com saltos de relógio (suspensão), recusas 406, ajustes manuais, aparelho sem conexão e reinício do app. As invariantes do anexo são checadas a cada passo |
| 0.5 | Prova de sabotagem | Cada invariante tem que falhar com o defeito de hoje antes da correção |

## Fase 1 — críticos (v0.2.4)

| # | Defeito | Evidência | Correção |
|---|---|---|---|
| 1.1 | **Desligar ou suspender o PC não desliga o ar** | [P] última leitura 15/10 20:32 ligado; nenhum comando até 08:42. Não existe tratamento de `SessionEnding` | Um caminho próprio, fora do ciclo:<br>• gatilhos `SessionEnding` + `ShutdownBlockReasonCreate`, e suspensão como melhor esforço;<br>• um único `Power(false)` com timeout de 2–3 s, **sem renovar o token** (a renovação gira o refresh token e o processo pode morrer no meio);<br>• respeita pausa manual e Modo noite;<br>• opção "manter o ar ligado ao desligar o PC";<br>• log síncrono do que fez |
| 1.2 | **Fase salva liga o ar sem conferir a leitura** | [P] 16/10 08:42: Manter retomado, dentro 22 °C, mínima 23 °C, fora 19,8 °C → `Power On`. A auditoria provou o mesmo para Ventilar e para a saída do Turbo | • `AvaliadoEm` no estado; com mais de 10 min sem avaliar, Conforto recomeça em Parado.<br>• Ar desligado com fase ≠ Parado volta a Parado.<br>• **Só se liga pela passagem Parado → (Manter/Turbo)** |
| 1.3 | **"Desde quando" sobrevive à noite** | [P] 18:00 a 25 °C; às 06:00 do dia seguinte, 25 °C → liga sem os 5 min de confirmação. Com 26 °C vai direto para o turbo e marca o sensor como travado sem ter havido turbo | Com mais de 10 min sem leitura (ou 2× a idade máxima, o que for maior), o Conforto recomeça do zero. 3 min derrubava a confirmação a cada falha de leitura de 1–2 ciclos |
| 1.4 | **Regras agem sobre leitura velha** | [P] código: avalia sempre que existe alguma leitura | Leitura com mais de 3 min não gera comando |
| 1.5 | **Saída do turbo vai sempre para Manter** | [P] turbo com dentro 22 °C pede Frio | Dentro ≤ mínima → ventilar ou parar |
| 1.6 | **Pausa manual se perde ao reiniciar** | [P] código: fica só em memória. Em 15/10, a reinstalação silenciosa reiniciou o app | Persistir a pausa junto com `rules-state.json` |
| 1.7 | **`settings.json` ilegível volta às regras padrão** | [P] 15/10 12:31: "unrecognized type discriminator 'conforto'" e `settings.json.corrompido` gravado às 12:03. Depois disso, o "Esfriar rápido" (removido na v0.2.0) agiu | [P] confirmado pelo `update.log`: às 12:03 um build de desenvolvimento migrou o arquivo; às 12:31 a v0.1.1 instalada o leu, falhou e voltou ao padrão; a atualização para a 0.2.0 veio às 12:31:56. Correção feita: JSON válido mas não reconhecido abre **só leitura**, sem regras, e avisa. Correção original: leitura que falha **nunca** recria as regras padrão nem sobrescreve o arquivo; o app mantém as regras em memória e avisa |

## Fase 2 — altos (pode ir na v0.2.4 ou na v0.2.5)

| # | Defeito | Evidência | Correção |
|---|---|---|---|
| 2.1 | Liga e no mesmo segundo avisa "lá fora mais fresco, desligue" | [P] 15/10 20:31: fora 20 °C, 50 min sem efeito | Não ligar quando fora está mais fresco pela diferença configurada: no lugar, avisar |
| 2.2 | O Ausente desliga de novo a cada hora depois que o usuário religa | [P] teste | O ajuste manual durante a ausência vale até a presença voltar |
| 2.3 | O teto de 30 min do turbo é burlado com 5 min de ociosidade | [P] 53 min de turbo | Somar o tempo de turbo numa janela de 2 h, guardado mesmo quando o Conforto recomeça |
| 2.4 | A Janela aberta dispara com o alívio do Ausente (alvo 26 °C) | [P] teste | Ignorar a subida quando o alvo ≥ dentro, ou quando o alvo mudou dentro da janela observada |
| 2.5 | A Janela aberta usa um salto de uma leitura só e age atrasada | [P] 15/10 10:03: 22→24→23. O desligamento saiu às 10:21, preso na pausa de 1 h. O aviso "Desliguei" saiu às 10:03, antes de agir | Inclinação sustentada (+1 °C em ≤ 20 min, confirmada). Aviso só depois do envio aceito. Expirou a pausa e a condição já passou: não age |
| 2.6 | O ajuste do usuário logo depois de um comando da regra é desfeito | [P] ventilação Alta → `SetFan(Auto)` | Contar como efeito colateral só os valores que o aparelho põe sozinho |
| 2.7 | Aviso "Conforto ligou o ar" sem comando enviado | [P] aparelho sem conexão | Aviso só depois do envio aceito |
| 2.8 | Timeout no envio vira "ajuste manual" e pausa 1 h | [H] nuvem aplica depois do timeout | Registrar como pendente o comando que deu timeout |
| 2.9 | Modo noite reenviou o timer que o usuário zerou no mesmo segundo | [P] 14/10 18:27:46 | Ajuste manual do timer vale até a próxima noite |

## Fase 3 — algoritmo (v0.3.0)

| # | Mudança | Base |
|---|---|---|
| 3.1 | **Limiares em graus inteiros, com 2–3 leituras seguidas** do mesmo lado. Hoje um único 24 no meio de uma hora de 25 prende o ar em Ventilar | [P] teste com oscilação 24/25. Home Assistant / Better Thermostat: uma tolerância de 0,3 °C não existe com sensor de 1 °C |
| 3.2 | **Tempo mínimo do compressor:** ≥ 5 min desligado antes de religar e ≥ 5 min ligado antes de desligar. Ação do usuário passa por cima | Trane 3/3 min; `min_cycle_duration` e `cycle_cooldown` do Home Assistant. Hoje lock/unlock repetido gera um ciclo de cerca de 5 min |
| 3.3 | **Offset manual do sensor**, limitado a ±3 °C. O painel mostra o valor bruto e o corrigido. Opcional: offset separado para "compressor ligado" e "ventilando" | [P] 138 min de frio sem cair 1 °C; desligado, a leitura caiu 2 °C em 20 min. Calibrar quando chegar o termômetro com sonda |
| 3.4 | Leitura implausível (salto ≥ 3 °C em 1 min) não dispara turbo sem confirmação | [P] 15/10 15:59: 23→26 só ventilando disparou o turbo |
| 3.5 | Sugerir "abrir a janela" por **temperatura externa + ponto de orvalho**, não pela sensação térmica | Open-Meteo `dew_point_2m`; o vento de fora não entra no cômodo |
| 3.6 | Aquecer: passagem para Piso ao chegar à mínima e tempo mínimo entre Quente e Frio. Modo noite não sobe o alvo em Heat. O alívio do Ausente não troca "só ventilar" por Frio | [P] testes da auditoria (só com "Aquecer" ligado) |

## Fase 4 — investigação (sem prazo)

- **SSE `configurations/livestream`.** É o push oficial da Electrolux, usado pelo `ha-electrolux`.
  - Leitura e confirmação em segundos, em vez de até 60 s.
  - Menos chamadas à API.
  - Exige um spike para medir a estabilidade.
- **Capabilities com `triggers` e valores `disabled`.** Validar o comando antes de enviar, em vez de descobrir pela recusa.
- **Temperatura de fora por estação do INMET** (a mais próxima), como alternativa ao Open-Meteo. O "agora" do Open-Meteo vem da previsão horária de um modelo global (grade de 9–25 km) e diverge 1–2 °C do que o celular mostra.

## Situação na v0.3.0

Fases 0 e 1 saíram na v0.2.4. Na v0.3.0:

- **Feitos:** 2.1, 2.2, 2.3, 2.4, 2.7, 2.8, 2.9, 3.1 a 3.6 e as capabilities da Fase 4 (modo e ventilação
  fora da lista saem do plano; o alvo vai para a faixa). Cada um tem teste e prova de sabotagem.
- **2.5, em parte.** Feito: aviso só depois do envio aceito; não dispara em pausa manual; não dispara
  com alvo ≥ dentro ou com alvo mudado na janela. A exigência de 3 leituras foi testada e revertida:
  sem evidência de falso positivo com 2, e quem lê a cada 5 min esperaria 15 min.
- **2.6, desmentido.** As leituras de 14/10 mostram o próprio aparelho mudando ventilação e alvo 1 a 3
  leituras depois da troca de modo (18:16 Auto → 18:18 ventilação Alta → 18:19 alvo 24; de novo às
  18:42 → 18:44/18:45). Tratar isso como ajuste do usuário pausaria a regra a cada troca de modo.
  O comportamento atual fica.
- **Invariantes 5 e 10** viraram checagem do simulador: turbo ≤ 30 min em 2 h e compressor do
  Conforto sem troca em menos de 5 min (fora de pausa manual, recusa da nuvem e lacuna de leitura).
- **Adiados:** SSE (exige spike com a API real, e o refresh token rotaciona) e INMET (rotas de
  observação fora do ar em 10/2026 e exige token).
- **Revisão da v0.2.4:** regra sem `tipo` no `settings.json` agora abre só leitura, como o JSON não
  reconhecido. Campo desconhecido continua aceito em silêncio.
- **Node 20 nas actions:** resolvido (checkout v5, setup-dotnet v5, upload-artifact v6,
  action-gh-release v3).

## Achados da revisão da v0.2.4

- Volta da suspensão contava como ajuste manual: corrigido (`MaxObservationGap`).
- Plano de desligamento ficava velho depois de ação do usuário: recalculado a cada ação.
- Desligamento ignorava a idade da leitura: leitura antiga não desliga.
- Modo só leitura gravava `rules-state.json`: não grava mais.
- **Não corrigido (anterior a este diff):** campo desconhecido no `settings.json` é aceito em silêncio, e regra sem `tipo` gera `NotSupportedException`.

## Anexo — invariantes (viram testes da Fase 0)

1. Nunca pedir Frio ou Turbo com dentro ≤ mínima.
2. Ligar um ar desligado só pela passagem confirmada nesta sessão, com ≥ 2 leituras novas.
3. Nenhuma marca "desde quando" sobrevive a mais de 10 min sem leitura (`ConfortoRule.Lacuna`). Estado carregado do disco com mais de 10 min equivale a Parado.
4. Leitura com mais de 3 min (ou 2× o intervalo + 1 min) não gera comando.
5. Turbo acumulado ≤ 30 min por janela de 2 h.
6. A mesma avaliação nunca emite "lá fora mais fresco" e uma transição para ligar.
7. Aviso de ação só com comando aceito.
8. Dentro da pausa manual, nenhum comando de regra. A pausa sobrevive a reinício.
9. Depois de um ajuste manual durante a ausência, o Ausente não desliga de novo até a presença voltar.
10. Nenhum ciclo automático liga/desliga em menos de 5 min.
11. Mudança do usuário sem comando pendente nunca é revertida na mesma janela.
12. A Janela aberta não dispara com alvo ≥ dentro.
13. O Modo noite nunca sobe o alvo em Heat.
14. No desligamento do PC, com Ausente ativo, ar ligado e sem pausa: um único `Power(false)`, sem renovar o token.
15. Leitura anterior sem conexão, ou ausente, não deixa uma fase salva religar o ar.
16. Uma leitura de `settings.json` que falha nunca recria as regras padrão.

## Fora do plano, por enquanto

- Origem do desligamento do ar durante a noite de 15→16/10: sem registro. O log da Fase 0 passa a cobrir isso.
