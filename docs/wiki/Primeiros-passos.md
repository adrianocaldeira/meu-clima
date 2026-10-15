# Primeiros passos

Dez minutos do download ao primeiro comando no ar-condicionado.

## 1. Confira o aparelho no app Electrolux

Abra o **app Electrolux** no celular. O ar-condicionado precisa:

- estar **cadastrado na sua conta**;
- aparecer **online** (conectado ao Wi-Fi);
- responder a um comando pelo próprio app (teste ligar e desligar).

Se não funciona no app oficial, não vai funcionar no Meu Clima — resolva primeiro com a Electrolux.

## 2. Crie as credenciais no portal de desenvolvedor

O Meu Clima usa a **Electrolux Group API** oficial com credenciais **suas**. Elas ficam só no seu PC,
criptografadas pelo Windows (DPAPI) para o seu usuário.

1. Acesse **[developer.electrolux.one](https://developer.electrolux.one/)**.
2. Entre com **a mesma conta do app Electrolux** (mesmo e-mail e senha). Conta diferente = nenhum
   aparelho aparece.
3. Aceite os termos de uso do portal, se for o primeiro acesso.
4. No painel, crie uma **API key**. Dê um nome que lembre o uso (ex.: "Meu Clima - PC do escritório").
   Copie o valor e guarde até o passo 6.
5. Ainda no painel, **gere um token**. O portal mostra dois valores:
   - **Access token** — curto prazo; o Meu Clima **não** precisa dele.
   - **Refresh token** — é este que você copia.
6. Abra o Meu Clima (clique no ícone da bandeja). Na tela de configuração, cole a **API key** e o
   **refresh token**.
7. Clique em **Conectar**. O app busca os aparelhos da conta e controla todos eles.

> [!WARNING]
> **O refresh token muda a cada renovação.** O Meu Clima guarda o novo valor sozinho. Se você usar o
> mesmo refresh token em outro lugar (outro PC, script, automação residencial), um pode invalidar o
> outro. Gere um token separado para cada uso.

> [!CAUTION]
> Não cole API key nem token em issue, print, chat ou e-mail. Se vazar, revogue no portal e gere outro.

> [!NOTE]
> Os nomes de menus e botões do portal são da Electrolux e podem mudar. Se algum passo não bater com
> o que você vê, [abra uma issue de docs](https://github.com/adrianocaldeira/meu-clima/issues/new?labels=documentation).

## 3. Informe a sua localização para o clima externo

O clima de fora vem do [Open-Meteo](https://open-meteo.com/), gratuito e sem cadastro. Busque a sua
cidade na configuração. Ela só é usada para consultar o tempo.

## 4. Diga em qual cômodo fica o PC

Ainda na configuração, escolha o aparelho do cômodo onde fica este PC. "Conforto" e "Ausente" agem
nesse aparelho.

## 5. Teste o controle

Pelo ícone da bandeja:

- **Clique** — abre o painel com o estado atual do ar, o clima lá fora e o controle rápido. Abaixo de
  cada aparelho, uma linha diz quem está no comando e por quê: você, uma regra agindo, uma regra de olho,
  uma regra em espera (você longe do PC, fora da agenda, ar em Quente) ou nenhuma. O menu **⋯** no topo pausa as regras por 1 h, verifica atualizações e encerra o app.
  Fechar a central aberta pelo painel volta ao painel.
- **Clique direito** — menu para abrir a central, as regras, ligar ou desligar o início com o
  Windows, verificar atualizações e sair.

Mude a temperatura e confira no app Electrolux do celular que o comando chegou.

## 6. Revise as regras

Ao concluir a configuração, o app cria as regras **já ligadas**. Abra a central → **Regras**,
desligue as que não fazem sentido para você e ajuste os valores das outras.

As duas que fazem o grosso do trabalho:

- **[Conforto](Regras#conforto)** — mantém dentro entre 23 e 24 °C gastando o mínimo.
- **[Ausente](Regras#ausente)** — desliga o ar quando você bloqueia a tela ou se afasta do PC.

Explicação de todas em [[Regras]].

## 7. Iniciar com o Windows

**"Iniciar com o Windows"** é escolhido no instalador (vem marcado) e deixa as regras ativas sempre
que você fizer login. Na versão portable ele começa desligado. Mude em **Ajustes** ou no menu do
ícone da bandeja. Lembre: **sem login no Windows, nenhuma
regra roda.**

## 8. Tema

Em **Ajustes** → **Tema**, escolha Claro, Escuro ou Sistema. Sistema segue o tema do Windows.

## Próximos passos

- [[Regras]] — o que cada regra faz e como ajustar
- [[Insights]] — o que os números mostram
- [[FAQ]] — soluções para problemas comuns
