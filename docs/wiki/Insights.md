# Insights

O Meu Clima guarda, **no seu PC**, um histórico do estado dos aparelhos, do clima e das ações das
regras. A tela de insights resume esse histórico.

## O que aparece

| Insight | O que mostra |
|---|---|
| **Horas ligado** | Tempo com o aparelho ligado hoje e ontem. |
| **Energia estimada** | Horas ligado × potência média (padrão 800 W) e o custo pela tarifa (padrão R$ 0,85/kWh). Ajuste os dois para o seu aparelho e a sua conta de luz. |
| **Minutos para baixar 1 °C** | Quanto o aparelho demora para esfriar o ambiente um grau, últimos 7 dias comparados com os últimos 30. Piora contínua pode indicar filtro sujo ou falta de gás. |
| **Quedas de conexão** | Quantas vezes o aparelho ficou offline nos últimos 7 dias. |
| **Pico previsto de amanhã** | A temperatura máxima prevista e a hora — útil para planejar com "Antes do pico". |
| **Economia do "Saí do PC"** | Tempo de aparelho desligado graças à regra. |

## Limites

- **Energia é estimativa.** A API da Electrolux não informa consumo. O número vem de horas ligado ×
  potência média, não de medição.
- **Só o que o PC viu.** O histórico só é gravado com o app aberto. Horas com o PC desligado ficam em
  branco; comandos feitos pelo celular nesse período não entram.
- **Temperatura interna é a do aparelho**, medida perto dele e em graus inteiros.

## Privacidade

O histórico fica num banco local (SQLite) no seu perfil do Windows. Nada é enviado para servidor do
Meu Clima — não existe servidor do Meu Clima.
