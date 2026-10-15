# Insights

O Meu Clima guarda, **no seu PC**, um histórico do estado dos aparelhos, do clima e das ações das
regras. A tela de insights resume esse histórico. Abra pela central → **Insights**.

## Números do topo

| Insight | O que mostra |
|---|---|
| **Ligado hoje** | Tempo com ar ligado hoje, somando todos os aparelhos, e o total de ontem. |
| **Energia hoje** | Horas com o compressor ligado × potência média (padrão 800 W), mais as horas só ventilando a ~40 W, e o custo pela tarifa (padrão R$ 0,85/kWh). Mostra também a projeção do mês: média dos últimos 7 dias × dias do mês, a partir do primeiro dia de histórico. Ajuste potência e tarifa em **Ajustes** → **Estimativa de energia**. |
| **Para baixar 1 °C** | Quanto o aparelho do cômodo do PC demora para esfriar o ambiente um grau no Frio: média dos últimos 7 dias, comparada com a dos últimos 30. |

## Gráfico

Temperatura de fora × temperatura de cada aparelho, nas últimas **24 h** ou nos últimos **7 dias**.
A faixa destacada marca quando o ar estava ligado.

## O que notamos

Avisos que aparecem só quando há algo a dizer:

| Aviso | Quando aparece |
|---|---|
| **Sem conexão** | O aparelho está offline agora, ou caiu 3 vezes ou mais nos últimos 7 dias. |
| **Filtro** | O aparelho informa que o filtro precisa de limpeza ou troca. |
| **Alerta do aparelho** | O aparelho reportou algum alerta. |
| **Esfriando mais devagar** | A média de 7 dias para baixar 1 °C está mais de 15% acima da de 30 dias. Costuma ser filtro sujo. |
| **Mais tempo ligado hoje** | Hoje passou de 30% acima de ontem (com ontem acima de 1 h). |
| **Pico previsto de amanhã** | A máxima prevista e a hora. Pela sensação térmica prevista, diz se o "Conforto" vai ligar o ar antes do calor. |
| **Economia do "Ausente"** | Tempo que a regra manteve o ar desligado nos últimos 7 dias, com a energia estimada. Aparece a partir de 30 min. |

## Limites

- **Energia é estimativa.** A API da Electrolux não informa consumo. O número vem de horas ligado ×
  potência média, não de medição.
- **Tarifa:** use o total da conta de luz ÷ kWh do mês. Esse valor já inclui impostos e bandeira
  tarifária, que mudam de um mês para outro.
- **Só o que o PC viu.** O histórico só é gravado com o app aberto. Horas com o PC desligado ficam em
  branco; comandos feitos pelo celular nesse período não entram.
- **Temperatura interna é a do aparelho**, medida perto dele e em graus inteiros.
- **O histórico guarda 90 dias.** O mais antigo é apagado sozinho.

## Privacidade

O histórico fica num banco local (SQLite) no seu perfil do Windows. Nada é enviado para servidor do
Meu Clima — não existe servidor do Meu Clima.
