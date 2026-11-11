# Manual do sistema Mercado

Programa de gestão do mercado para Windows (um único arquivo `MercadinhoSeuZe.exe`), com banco de dados na nuvem
(Supabase) e um app de celular para o gerente. Este manual explica como cada parte funciona **hoje** e as regras
que o sistema segue. No fim há um apêndice técnico e o guia do programa de testes.

Atualizado em 06/11/2026 (semana 6).

---

## Sumário

1. [Visão geral](#1-visão-geral)
2. [Entrar no sistema](#2-entrar-no-sistema)
3. [Painel](#3-painel)
4. [Estoque](#4-estoque)
5. [Promoções](#5-promoções)
6. [Fornecedores e pedidos](#6-fornecedores-e-pedidos)
7. [Caixa](#7-caixa)
8. [Funcionários e Meu perfil](#8-funcionários-e-meu-perfil)
9. [Fechamento do dia e Análises](#9-fechamento-do-dia-e-análises)
10. [App de celular](#10-app-de-celular)
11. [Problemas comuns](#11-problemas-comuns)
12. [Programa de testes](#12-programa-de-testes)
13. [Apêndice técnico](#13-apêndice-técnico)

---

## 1. Visão geral

| Perfil | O que vê | Tela depois do login |
| --- | --- | --- |
| **Gerente** | Tudo: Painel, Estoque, Fornecedores, Caixa, Funcionários, Fechamento do dia, app de celular | Painel |
| **Operador** | Só o Caixa (as próprias vendas do dia, sem os totais do mercado) | Caixa |

- Vários funcionários usam ao mesmo tempo, em computadores diferentes. Tudo fica no banco na nuvem.
- O programa precisa de internet para ler e gravar. Sem internet, as telas abrem e avisam; nada é gravado.
- Quem decide o que cada perfil pode ver e mudar é o próprio banco (regras de acesso por perfil). Mesmo que alguém
  mexa no programa, o operador não consegue ler vendas de outros caixas, mudar preços ou ver fornecedores.
- O programa só carrega a chave **pública** do Supabase. A chave secreta nunca vai para o computador do mercado.

**Interruptor "Ocultar vendas"** (Painel, Caixa e Fechamento): troca os valores por `R$ •••••` neste computador, para
o gerente abrir a tela na frente de clientes. Fica lembrado no computador.

---

## 2. Entrar no sistema

- Digite só o **usuário** (ex.: `ana.caixa`). O sistema completa sozinho com `@mercado.local`. Maiúsculas e espaços
  no começo/fim não atrapalham.
- **Mostrar senha** (ícone do olho) e aviso de **Caps Lock ligado**.
- **Lembrar usuário**: o próximo login já vem com o usuário preenchido.
- **5 senhas erradas seguidas** bloqueiam aquele usuário por **5 minutos** neste computador. A tela mostra quantas
  tentativas restam. Cada tentativa (certa ou errada) fica gravada na auditoria, que o gerente pode consultar.
- **Usuário desativado** não entra (mensagem "Usuário desativado. Fale com o gerente.").
- **Esqueci a senha**: grava um pedido para o gerente. O gerente vê o aviso amarelo na tela Funcionários e redefine.
  A tela sempre responde igual, para não revelar quais usuários existem.
- O login vale 1 hora no Supabase; o programa pede um novo sozinho 10 minutos antes de vencer. Se mesmo assim vencer
  (ficou sem internet), a próxima gravação mostra "Sua sessão expirou": é só sair e entrar de novo.

---

## 3. Painel

Só para o gerente. Atualiza sozinho a cada 30 segundos.

- **Indicadores**: vendido hoje, número de vendas, ticket médio, estoque baixo, pedidos pendentes e caixas abertos.
- **Acesso rápido**: Novo produto (abre o Estoque já no cadastro), Novo pedido (abre Fornecedores já no pedido),
  Caixa, Fechamento.
- **Avisos recentes**, separados em tópicos que abrem e fecham: Estoque, Promoções, Pedidos e fornecedores, Mensagens
  e Caixa. Fechado, cada tópico mostra só quantos avisos tem, na cor do mais grave:
  - **vermelho** (urgente): sem estoque, lote vencido, pedido atrasado, falta de dinheiro no caixa, erro ao ler;
  - **âmbar** (atenção): estoque baixo, aviso antecipado, validade próxima, hoje é o último dia para pedir;
  - **neutro**: informações (lotes em promoção de validade, fornecedor respondeu).
  Aberto, mostra 5 avisos por página. Tópico sem aviso não aparece.

---

## 4. Estoque

Só para o gerente.

### Produtos

| Campo | Regra |
| --- | --- |
| Nome | 1 a 80 letras; não pode repetir (nem com outra maiúscula) |
| Categoria | 1 a 40 letras. Açougue, Frios, Grãos, Mercearia, Padaria, Laticínios, Bebidas, Congelados, Hortifrúti, Limpeza, Higiene e Pet têm ícone e cor próprios |
| Preço | maior que zero, até R$ 99.999.999,99 (duas casas) |
| Por unidade ou por quilo | por quilo: preço por kg e quantidades com até 3 casas (ex.: 18,745 kg) |
| Estoque mínimo / aviso | aviso vazio = mínimo + 50% |
| Código de barras da caixa | até 40 caracteres, não pode repetir. Opcional |
| Unidades por caixa | maior que zero |
| Validade padrão (dias) | 1 a 3650. Preenche a validade dos lotes novos |
| Fornecedor, prazo de entrega, "Pedir antes de acabar", custo | ver capítulo 6 |
| Código na balança (PLU) | 1 a 99999, só para produto por quilo |
| Não entra na promoção de validade | tira o produto da promoção da categoria |

O cadastro já pode criar o **primeiro lote** (quantidade 0 = sem lote). Se o lote der erro, o produto é desfeito, para
a pessoa poder tentar de novo com o mesmo nome.

### Lotes

Cada compra é um lote: quantidade comprada, quanto ainda resta, data da compra, validade e custo.

- Quantidade do produto = soma do que resta nos lotes.
- A validade não pode ser antes da compra. "Vendido" = comprado − restante.
- A validade já vem preenchida (compra + validade padrão, ou o mesmo prazo do último lote).
- Excluir o produto exclui os lotes.

### Leitor de código de barras

Campo **"Chegou mercadoria?"** no topo, já com o foco. O leitor digita o código e manda Enter.
- Código conhecido abre "Novo lote" (caixas recebidas × unidades por caixa).
- Código desconhecido procura o produto nos bancos abertos (Open Food Facts, Open Beauty Facts, Open Products Facts) e
  abre o cadastro com nome e categoria preenchidos para conferir. Sem internet ou sem resultado em 6 s, abre vazio.
- Se o produto está num pedido pendente, a janela oferece "Registrar a entrega do pedido".

### Situações e avisos

| Situação | Regra |
| --- | --- |
| Sem estoque | quantidade 0 |
| Estoque baixo | tem unidades, mas menos que o mínimo |
| Repor em breve (aviso antecipado) | acima do mínimo, mas igual ou abaixo do aviso |
| Validade próxima | lote com unidades vencendo de hoje até 30 dias |
| Vencido | lote com unidades e validade antes de hoje. O caixa não vende |
| Reservado no caixa | está numa venda aberta ou em espera de hoje; continua no lote, mas não pode ser vendido de novo |

**Filtros** (botão "Filtros"): situação, categoria, unidade/quilo e ordem (nome A-Z, validade mais próxima, menor
quantidade). Cada opção mostra quantos produtos aparecem. A lista tem páginas de 5, 10, 25, 50 ou 100 produtos.

---

## 5. Promoções

Estoque > aba **Promoções**.

### Promoção por produto

- **Preço fixo** ("de R$ 8,90 por R$ 6,99") ou **porcentagem** (menos de 100%).
- Início e fim (fim vazio = até encerrar). Situação: Agendada, Ativa ou Encerrada.
- O banco recusa: preço promocional igual ou maior que o normal; duas promoções do mesmo produto no mesmo período.
- "Encerrar agora" para a promoção e deixa no histórico. Encerradas podem ser excluídas.
- Aviso amarelo quando a promoção vende abaixo do custo (não bloqueia).

### Promoção de validade por categoria

Uma regra por categoria: começa **N dias antes** do vencimento (1 a 60) com o **desconto mínimo** e sobe por igual até
o **máximo** (até 90%) um dia antes. No dia do vencimento continua o máximo. Exemplo com 5 dias, 10% a 50%:

| Dias para vencer | 6 | 5 | 4 | 3 | 2 | 1 | 0 (hoje) | já venceu |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Desconto | 0 | 10% | 20% | 30% | 40% | 50% | 50% | não vende |

### No caixa

O preço é calculado pelo banco, lote por lote, na ordem em que saem (o que vence primeiro). Em cada lote vale o
**maior** desconto entre a promoção do produto e a de validade (não soma). A venda mostra a etiqueta da promoção, o
preço normal riscado e a "Economia nas promoções".

---

## 6. Fornecedores e pedidos

Só para o gerente.

### Cadastro

| Campo | Regra |
| --- | --- |
| Nome | 1 a 80 letras, não repete |
| Telefone | 10 ou 11 dígitos; WhatsApp separado opcional |
| E-mail | opcional, formato válido |
| O que entrega | categorias do estoque separadas por vírgula |
| Prazo de entrega | 0 a 90 dias (padrão 3) |
| Dias de entrega | ex.: seg e qua. A previsão cai no próximo dia de entrega |
| Pedido mínimo | em R$; pedido abaixo mostra aviso, não bloqueia |
| Envio preferido | WhatsApp ou e-mail |
| Modelo de mensagem | até 1.000 letras, com {contato}, {fornecedor}, {itens}, {previsao} e {usuario} |

Fornecedor com pedidos não é apagado: fica **desativado** e o histórico continua.

### Produtos de cada fornecedor

São os ligados a ele no cadastro do produto, os de "Também compro de" e, sem ligação, os da categoria que ele entrega.
**Pedir antes de acabar**: o aviso vira o ponto de pedido = mínimo + (o que sai por dia × prazo de entrega).

### Pedido

1. **Pedir o que falta** monta o pedido com os produtos sem estoque, abaixo do mínimo ou no aviso antecipado
   (quantidade sugerida = 2 × mínimo − atual, em caixas inteiras). Ou **Novo pedido** com a busca/leitor.
2. A mensagem sai pronta ("Pedido nº 42 ...") para **Enviar pelo WhatsApp** (abre o WhatsApp com o texto) ou
   **Enviar por e-mail** (pela caixa de e-mail do mercado, se ligada; senão abre o e-mail do computador).
3. Pedido pendente pode ser **editado** (gera "Atualização do pedido") ou **cancelado**.
4. **Registrar entrega**: o que chegou de cada item, validade, custo, número da nota e motivo da diferença (faltou,
   avariado, vencido, produto errado, outro). Cada item recebido vira um lote. "O que faltou continua pendente" cria
   um pedido novo com o resto. **Importar XML da nota** preenche quantidades, custo e validade a partir da NF-e.

### Mensagens

Ícone de e-mail no cartão do fornecedor, com o número de não lidas. Mostra a conversa (e-mails que chegaram pela caixa
de pedidos e respostas do WhatsApp coladas à mão). O Painel avisa quando um fornecedor responde.

---

## 7. Caixa

### Abrir

Cada pessoa abre o **próprio caixa do dia** com o troco inicial (R$ 0,00 a R$ 100.000,00). Vendas que ficaram abertas
em dias anteriores são canceladas ao abrir.

### Vender

- Leitor (Enter) ou busca por nome. O código da caixa (DUN-14) e o da unidade (EAN-13) funcionam.
- **"6*"** + Enter multiplica a próxima leitura por 6 (por quilo, vira o peso).
- **Etiqueta da balança** (EAN-13 que começa com 2, com preço): peso = valor ÷ preço por kg.
- Produto por quilo sem etiqueta: o campo de peso abre em foco.
- Cada item entra **reservado**: ninguém mais vende a mesma unidade. Lote vencido nunca é vendido.
- Máximo de 99.999 unidades (ou kg) por item.

### Pagar

| Tecla | Ação |
| --- | --- |
| F2 | leitor / busca |
| F3 | consulta de preço (sem pôr na venda) |
| F4 | receber / finalizar |
| F5 a F9, Shift+F9 | Dinheiro, Pix, Débito, Crédito (1 a 12 parcelas), Vale-alimentação, Vale-refeição |
| F11 | CPF/CNPJ na nota (dígitos conferidos) |
| F12 | venda em espera |
| Ctrl+D | desconto (R$ ou %) |
| Esc | cancelar o último item (com o gerente) |

- Dá para juntar formas (ex.: R$ 50 em dinheiro + o resto no crédito). Só o dinheiro tem troco. Finaliza quando a soma
  fecha exatamente com o total.
- **Maquininha**: hoje é **emulada** (botões de teste Aprovar / Recusar / Sem comunicação). Nenhum pagamento real é
  feito. O Pix mostra um "copia e cola" de teste.

### Senha do gerente

Cancelar ou diminuir item, cancelar a venda, dar desconto e fazer sangria pedem **usuário e senha de um gerente**. A
autorização vale uma vez, para aquela venda, por 10 minutos. 5 senhas erradas em 5 minutos bloqueiam aquele gerente.
O gerente logado não precisa digitar. Venda já paga só o gerente cancela (o estoque volta aos mesmos lotes).

### Venda em espera

F12 com itens: a venda fica suspensa (os itens continuam reservados) e o caixa atende outro cliente. Para retomar,
a venda atual precisa estar terminada ou em espera.

### Sangria, suprimento e fechamento

- **Suprimento**: troco colocado na gaveta. **Sangria**: dinheiro tirado para o cofre (com o gerente; não passa do que
  há na gaveta). Acima de R$ 500 na gaveta aparece o aviso para fazer sangria.
- Dinheiro na gaveta = troco inicial + dinheiro das vendas + suprimentos − sangrias.
- **Fechar o caixa**: termina a venda atual (as em espera são canceladas), conta a gaveta (nota por nota ou o total) e
  mostra a **sobra ou falta**; com diferença, pede uma observação. Fechado, o caixa não vende mais no dia. Só o gerente
  reabre (no mesmo dia).

### Modo caixa

Interruptor no topo: tela cheia de balcão, letras e botões maiores. Fica lembrado no computador.

---

## 8. Funcionários e Meu perfil

**Funcionários** (só gerente): lista com função, último acesso e pedidos de nova senha.

| Ação | Regra |
| --- | --- |
| Novo | usuário de 3 a 40 letras sem acento (letras, números, ponto, traço); nome de 1 a 60; senha com 8 ou mais |
| Editar | nome e função |
| Redefinir senha | resolve os pedidos de "Esqueci a senha" |
| Remover | quem já abriu caixa fica **desativado** (não entra, "Reativar" volta); quem nunca vendeu é apagado |

O gerente não rebaixa, desativa nem remove a si mesmo, e o mercado sempre fica com pelo menos um gerente ativo.

**Meu perfil** (todos, clicando no nome no rodapé do menu): nome, função, usuário, acesso anterior, minhas vendas de
hoje (só gerente) e **trocar a própria senha** (pede a senha atual; a nova precisa ter 8 ou mais letras e ser diferente).

---

## 9. Fechamento do dia e Análises

Só para o gerente.

### Fechamento do dia

- Escolha o dia pelas setas ou clicando na data (calendário).
- Indicadores: vendido × mesmo dia da semana passada, vendas, ticket médio, descontos, margem (onde há custo nos
  lotes), sobra/falta dos caixas.
- Caixas do dia (o gerente fecha daqui o caixa de quem esqueceu), formas de pagamento, mais vendidos, vendas por hora
  e cancelamentos.
- **Fechar o dia**: só com todos os caixas fechados. Grava a foto do resumo e manda o e-mail para os endereços
  cadastrados (botão "Alterar"). Fechado: "Enviar de novo" e "Reabrir o dia".
- **Dia que já passou**: fechar, reabrir ou fechar um caixa pedem usuário e senha de um gerente, mesmo para o gerente
  logado.
- Às 23h, se ninguém fechou, o resumo pode sair sozinho por e-mail (quando o e-mail estiver ligado).

### Análises

Por dia (30 dias), por semana (12) e por mês (12): total, vendas, ticket médio, comparação com o período anterior,
vendas por categoria, por forma de pagamento, média por dia da semana, mais vendidos, perdas (lotes vencidos, pelo
custo) e cancelamentos. O período máximo de uma análise é de 800 dias.

---

## 10. App de celular

App do gerente (pasta `app-celular/`), instalável pelo navegador (PWA) e convertido em `.apk` pelo PWABuilder. Precisa
estar publicado num endereço https.

- Login com o mesmo usuário do programa. **Só o gerente** entra; 5 erros bloqueiam por 5 minutos no aparelho.
- Trava com digital, rosto ou PIN.
- Abas: **Hoje** (resumo do dia), **Análises**, **Avisos** (mensagens, pedidos atrasados, caixas com diferença,
  estoque), **Estoque**, **Pedir** (monta o pedido e envia pelo WhatsApp) e **Conta**. Interruptor "Ocultar".

---

## 11. Problemas comuns

| Mensagem ou situação | O que fazer |
| --- | --- |
| "As tabelas desta tela ainda não foram criadas no banco" | Rodar o script SQL da semana no Supabase (capítulo 13) e entrar de novo |
| "Não foi possível conectar ao servidor. Verifique a internet." | Conferir a internet; nada foi gravado |
| "Sua sessão expirou" | Sair e entrar de novo |
| "Seu perfil não tem permissão para esta ação" | A ação é só do gerente |
| Leitor não lê | O leitor precisa enviar Enter no fim; o campo precisa estar com o foco (F2 no caixa) |
| "Só há lote vencido" no caixa | Retirar o produto da prateleira e dar baixa no lote |
| Usuário bloqueado | Esperar 5 minutos ou o gerente redefinir a senha |
| E-mail abre o programa do computador em vez de enviar | As funções de e-mail do Supabase ainda não foram ligadas (README, "Ligar o e-mail") |

---

## 12. Programa de testes

`MercadinhoSeuZe.Testes.exe` (pasta `publish-testes/`) confere o sistema inteiro e abre um relatório.

### O que ele testa

| Módulo | Exemplos |
| --- | --- |
| Lógica | campos de preço, quantidade e data; máscara de dinheiro; códigos de barras e balança; CPF/CNPJ; Pix; validade; promoções; nota fiscal; paginação |
| APIs e banco | login do Supabase, tabelas e funções de todos os scripts, Edge Functions de e-mail, Storage, Open Food Facts, relógio |
| Login | perfis, senha errada, bloqueio de 5 tentativas, desativado, injeção, token renovado, auditoria |
| Segurança | sem login, operador e desativado tentando ler e mudar o que não podem; chaves secretas nos arquivos |
| Estoque, Promoções, Fornecedores, Funcionários | uso certo, uso errado e limites de cada campo |
| Caixa | sem gravar: recusas do banco. Com movimento: venda completa, pagamentos, gerente, espera, sangria, fechamento, dois caixas na última unidade |
| Fechamento e Análises | resumo de qualquer dia, análises, dia futuro, dia passado sem senha, limites de período |
| App de celular | arquivos do PWA, mesma chave pública, funções do banco que ele usa |
| Integridade dos dados | vendas × itens × pagamentos, baixa nos lotes, gaveta, resumo × vendas, fechamentos, promoções, pendências |
| Volume | listas com mais de 1.000 registros, tempos de leitura |
| Telas | abre cada tela com os dados de verdade, como gerente, operador e sem internet; guarda uma foto de cada |

### Como rodar

Dois cliques em `MercadinhoSeuZe.Testes.exe`. No fim abre `relatorio-testes/<data>/relatorio.html`. Opções (no
PowerShell):

| Opção | Efeito |
| --- | --- |
| `--sem-banco` | só lógica e telas, sem internet |
| `--rapido` | pula os 1.200 produtos do teste de volume |
| `--modulo Caixa` | só um módulo |
| `--movimento` | no banco do mercado, testa também abrir caixa, vender, cancelar e fechar o dia (grava vendas e caixas que não dá para apagar) |
| `--sem-pausa` | não espera o Enter e não abre o relatório |

### Onde ele roda

- **Sem `testes.json`**: usa o banco do mercado. Lê tudo, tenta fazer tudo errado (o banco recusa e nada fica gravado)
  e só cria registros que ele mesmo apaga no fim (nome começando com `TESTE AUTOMATICO`; funcionários `teste.auto.`).
  As tentativas de login ficam na auditoria (são só registro). Vendas, caixas e fechamento do dia ficam como "pulados".
- **Com `testes.json`** ao lado do .exe (modelo em `testes.example.json`): usa outro projeto Supabase, só de teste,
  e roda tudo, inclusive vendas e fechamento do dia. O arquivo leva só a URL e a chave **pública**; o programa recusa
  a chave secreta. O `testes.json` não vai para o git.
- O teste de login usa o mesmo componente do programa, que guarda bloqueios neste computador; o arquivo de
  preferências volta como estava no fim.

Para montar o projeto de teste: criar um projeto novo no Supabase, rodar no SQL Editor os scripts 001 a 009, 011, 012
013, 014 e 015 (o 010 não), e o 002 para os usuários de teste. Para ter vendas de exemplo: `select gerar_dados_exemplo(1, 40);` e depois `select preencher_analise_cache();`.

---

## 13. Apêndice técnico

### Tecnologia

- C# com .NET 10 e WPF; um único `.exe` (Release, self-contained).
- Banco PostgreSQL no Supabase, acessado por HTTP (Auth + REST + funções `rpc`). Sem servidor próprio.
- E-mail por Edge Functions do Supabase (Deno/TypeScript), com a senha só nos segredos do Supabase.
- App de celular em HTML/CSS/JS (PWA).

### Pastas

| Pasta | Conteúdo |
| --- | --- |
| `src/MercadinhoSeuZe/Models` | regras e contas (estoque, caixa, promoções, fornecedores, fechamento) |
| `src/MercadinhoSeuZe/Services` | acesso ao Supabase (`SupabaseApi`) e um serviço por módulo |
| `src/MercadinhoSeuZe/Views` | uma tela por arquivo e as janelas por cima das telas |
| `database/` | scripts SQL numerados |
| `supabase/functions/` | Edge Functions de e-mail |
| `app-celular/` | app do gerente |
| `tests/MercadinhoSeuZe.Testes` | programa de testes |
| `docs/` | este manual |

### Scripts do banco (rodar em ordem no SQL Editor)

| Script | O que cria |
| --- | --- |
| 001 | perfis, auditoria de login, pedidos de senha, contatos |
| 002 | usuários de teste (apagar antes do uso real) |
| 003 | produtos e lotes |
| 004 | fornecedores, pedidos, itens do pedido |
| 005 | custo, entrega parcial, nota, dias de entrega, pedido mínimo, vários fornecedores por produto |
| 006 | caixas, vendas, itens, baixa por lote, funções de funcionários |
| 007 | caixa padrão: pagamentos, CPF, desconto, espera, autorizações do gerente |
| 008 | promoções, sangria, suprimento, fechamento do caixa |
| 009 | vendas só para o gerente, fechamento do dia, análises, mensagens, anexos |
| 010 | agendamentos do e-mail (só depois de publicar as Edge Functions) |
| 011, 013 | dados de exemplo (só para teste) |
| 012 | senha do gerente em dia passado, calendário de datas festivas |
| 014 | velocidade: índice dos itens da venda e regras de acesso conferidas uma vez por consulta |
| 015 | análises rápidas: resumo de cada dia guardado pronto (depois de gerar dados de exemplo, rodar `select public.preencher_analise_cache();`) |

### Segurança

- Toda tabela tem regras de acesso por perfil (Row Level Security) com `eh_gerente()`.
- Venda, caixa, pedido, funcionário e fechamento só gravam pelas funções do banco, que conferem perfil, estoque,
  validade, promoções e autorização do gerente numa única operação (tudo ou nada).
- O programa e o app usam só a chave pública. Nenhuma senha de e-mail fica no computador.

### Gerar os executáveis

```powershell
.\publicar.ps1                  # publish\MercadinhoSeuZe.exe (o do mercado)
.\publicar.ps1 -Desenvolvimento # publish-dev\ com os atalhos de teste (Alt+1, Alt+2)
.\publicar.ps1 -Testes          # publish-testes\MercadinhoSeuZe.Testes.exe
```
