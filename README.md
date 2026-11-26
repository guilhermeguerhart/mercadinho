# Mercado · Sistema de gestão

Sistema de gestão do mercado: caixa, estoque, fornecedores, fechamento do dia e previsão de compras.
Programa para Windows feito em **C# (.NET 10 + WPF)**, com banco de dados **PostgreSQL no Supabase** (nuvem),
distribuído como **um único arquivo .exe**. O gerente também acompanha o mercado pelo **app de celular**.

O visual, os textos e os perfis seguem o projeto de referência
[kethleensilva06/Projeto_mercado](https://github.com/kethleensilva06/Projeto_mercado) (telas no Figma, em `figma/`).

- **Manual de uso, tela por tela:** [`docs/MANUAL_DO_SISTEMA.md`](docs/MANUAL_DO_SISTEMA.md)
- **Usuários de teste:** [`USUARIOS_TESTE.md`](USUARIOS_TESTE.md)
- **App de celular da gerência:** https://guilhermeguerhart.github.io/App/
- **Antes de alterar o código:** leia [`INSTRUCOES_PARA_IA.txt`](INSTRUCOES_PARA_IA.txt)

## O que o sistema faz

| Perfil | O que vê | Tela depois do login |
|---|---|---|
| **Gerente** | Tudo: Painel, Estoque, Fornecedores, Caixa, Funcionários, Calendário e o app de celular | Painel |
| **Operador** | Só o Caixa (as próprias vendas do dia) | Caixa |

- **Painel:** resumo do dia, avisos de estoque, pedidos atrasados, respostas de fornecedores e o que a previsão manda pedir hoje.
- **Estoque:** produtos, lotes e validade, leitor de código de barras, promoções por produto e por validade,
  e a coluna **Dura até** (até quando o estoque aguenta, pela previsão).
- **Fornecedores:** cadastro, pedidos (a quantidade já vem pela previsão), entregas parciais, XML da NF-e e mensagens.
  Ao criar ou alterar um pedido, **o e-mail sai sozinho** pela caixa do mercado, com o botão do WhatsApp ao lado.
- **Caixa:** venda com baixa por lote, pagamento dividido, desconto e cancelamento com senha do gerente, venda em espera,
  sangria, suprimento, fechamento do caixa, modo caixa (tela cheia), **Faltou produto (F10)** e maquininha
  **Stone Ton** (o operador digita o valor na Ton e confirma no caixa).
- **Calendário:** junta as antigas telas Fechamento do dia e Previsão de compras. Mostra o mês com o vendido e a previsão,
  os dias de compra de cada fornecedor e as datas festivas. Clicando num dia, fecha o dia (e manda o resumo por e-mail)
  ou vê o que pedir. Embaixo ficam as **Análises de vendas** e os **Dados completos da previsão**.
- **Funcionários e Meu perfil:** o gerente cadastra e desativa funcionários; cada um troca a própria senha.
- **App de celular:** abas Hoje, Calendário, Análises, Avisos, Estoque e Pedir (cria e envia o pedido), com trava por digital, rosto ou PIN.

## Cronograma

| Semana | Entrega | Situação |
|---|---|---|
| 1 | Tela inicial e login com perfis gerente e operador | Feito |
| 2 | Painel (resumo do dia, avisos de estoque e pedidos) | Feito |
| 3 | Estoque (produtos, lotes e validade) | Feito |
| 4 | Fornecedores (cadastro, pedidos pelo WhatsApp/e-mail, entregas) | Feito |
| 5 | Caixa padrão (reserva, baixa por lote, pagamento dividido, maquininha, modo caixa), funcionários e meu perfil | Feito |
| 6 | Fechamento do dia, análises, resumo por e-mail, mensagens com fornecedores e app de celular da gerência | Feito |
| 7 | Previsão de compras, pedido sugerido, simulador da loja, laboratório da previsão e testes | Feito |
| 8 | Previsão no dia a dia, tela Calendário, acessibilidade, maquininha Stone Ton, pedido automático por e-mail e app de celular atualizado | Feito |

**Falta para o uso real (lado do mercado):** gerar o `.apk` do app de celular, senha mínima de 8 letras no Supabase,
desligar o simulador e apagar os dados de exemplo e os usuários de teste, instalar o `.exe` nos computadores,
cadastrar o estoque real e treinar a equipe.

## Estrutura

```
database/                 scripts SQL para rodar no Supabase, em ordem (001 a 023)
supabase/functions/       Edge Functions de e-mail (enviar-email, ler-emails)
app-celular/              app de celular da gerência (página instalável, publicada no GitHub Pages)
docs/                     manual do sistema
src/MercadinhoSeuZe/
  Models/                 tipos do sistema (Perfil, Sessao, Produto, Fornecedor, Pedido, Venda, Previsao...)
  Services/               configuração, acesso ao Supabase, login, previsão e preferências locais
  Views/                  telas (tela inicial, login, painel, caixa, estoque, fornecedores, funcionários, calendário)
  App.xaml                cores e estilos visuais
tests/MercadinhoSeuZe.Testes/  programa que testa o sistema inteiro e gera um relatório
publicar.ps1              gera o .exe único (normal, de desenvolvimento ou o de testes)
USUARIOS_TESTE.md         usuários de teste criados pelo script 002
INSTRUCOES_PARA_IA.txt    resumo do projeto, próximos passos e restrições (leia antes de alterar)
```

## Como preparar

1. **Instalar o .NET 10 SDK**: `winget install Microsoft.DotNet.SDK.10`
2. **Criar o projeto no Supabase** (https://supabase.com, plano grátis).
3. No Supabase, abrir **SQL Editor** e executar os scripts de `database/` nesta ordem:

   | Ordem | Scripts | Para quê |
   |---|---|---|
   | 1 | `001`, `003`, `004`, `005` | perfis e login, estoque, fornecedores e ajustes da semana 4 |
   | 2 | `006`, `007`, `008` | caixa, caixa padrão, promoções e fechamento do caixa |
   | 3 | `009`, `012`, `014`, `015` | fechamento do dia, mensagens, datas especiais, desempenho e análise rápida |
   | 4 | `016`, `019`, `022`, `023` | previsão de compras, tela da previsão, previsão no dia a dia e Calendário |
   | Só para teste | `002`, `011`, `013`, `017`, `018`, `020`, `021` | usuários de teste, dados de exemplo, simulador e laboratório da previsão (o `021` corrige o `020`) |
   | Depois do e-mail | `010` | agendamentos do e-mail (veja "Ligar o e-mail") |

4. Criar o primeiro gerente em **Authentication > Users > Add user**, marcando *Auto Confirm User*
   (os outros funcionários o gerente cadastra no próprio programa, na tela **Funcionários**).
   O login é por **nome de usuário**: crie cada um com o e-mail interno `<usuario>@mercado.local`
   (ex.: `ana.caixa@mercado.local`); no programa a pessoa digita só `ana.caixa`.
   Todo usuário novo entra como **operador**. Para tornar alguém **gerente**, rodar no SQL Editor:
   ```sql
   update public.perfis set perfil = 'gerente' where usuario = 'marcos.gerente';
   ```
5. Configuração: `src/MercadinhoSeuZe/appsettings.json` já vem no repositório com a URL e a chave
   pública (publishable) do Supabase do projeto, então quem baixar não precisa configurar nada.
   Para usar outro projeto do Supabase, troque os dois valores (Project Settings > API).

> Toda função SQL chamada pelo programa precisa de `WHERE` em `UPDATE` e `DELETE` (nem que seja `where true`):
> o Supabase recusa sem isso.

## Rodar e gerar o .exe

```powershell
dotnet run --project src/MercadinhoSeuZe     # abrir para testar
./publicar.ps1                                # gera publish/MercadinhoSeuZe.exe
./publicar.ps1 -Desenvolvimento               # gera publish-dev/ com os atalhos de teste
./publicar.ps1 -Testes                        # gera publish-testes/MercadinhoSeuZe.Testes.exe
```

Na versão de desenvolvimento (ou rodando em Debug), **Alt+1 na tela de login** abre a lista dos usuários de
teste no canto inferior esquerdo; clicar num deles preenche usuário e senha. **Alt+2 na tela de Estoque** abre um
leitor de código de barras simulado (catálogo de produtos; cada clique gera um código de caixa novo).
**Alt+2 na tela de Fornecedores** cadastra fornecedores de teste (telefones e e-mails fictícios).
**Alt+2 no Caixa** passa no leitor o código de um produto com estoque (ou uma etiqueta da balança).
Ela também começa com a maquininha de teste e mostra o laboratório da previsão no Calendário.
O .exe normal não tem esses atalhos e já começa na maquininha Stone Ton.

O .exe já leva a configuração dentro dele e roda em qualquer Windows 64 bits sem instalar nada.

## Programa de testes

`MercadinhoSeuZe.Testes.exe` confere o sistema inteiro (lógica, banco, login, segurança, cada tela, caixa, Calendário,
app de celular, integridade dos dados e volume) e abre um relatório em `relatorio-testes/<data>/relatorio.html`.

```powershell
./publish-testes/MercadinhoSeuZe.Testes.exe              # tudo, no banco do mercado, sem deixar nada gravado
./publish-testes/MercadinhoSeuZe.Testes.exe --sem-banco  # só lógica e telas, sem internet
./publish-testes/MercadinhoSeuZe.Testes.exe --modulo Caixa
```

Para rodar também vendas e fechamento do dia sem mexer no banco do mercado, use um projeto Supabase só de teste:
copie `tests/MercadinhoSeuZe.Testes/testes.example.json` para `testes.json` ao lado do .exe (só URL e chave pública).
Detalhes no capítulo 12 do [manual](docs/MANUAL_DO_SISTEMA.md).

## Dados de exemplo e simulador (só para testar)

`database/011_dados_de_exemplo.sql` enche o banco com um mercado fictício: 113 produtos, 6 fornecedores, pedidos
toda semana e **5 anos de vendas, caixas e fechamentos do dia** (umas 95 mil vendas). Rodar no SQL Editor, depois dos
scripts principais e do 002 (usuários de teste):

```sql
-- 1. rodar o arquivo 011 inteiro (cria as funções); depois:
select public.gerar_dados_exemplo();            -- leva cerca de 1 minuto
select public.gerar_fechamentos_exemplo(null);  -- fechamento de cada dia
select public.preencher_analise_cache();        -- resumo pronto das análises (script 015)
```

Vendas a mais nos feriados e datas festivas (Natal, Ano-Novo, Páscoa, São João, Dia das Mães...), depois do `012`
e do `013_dados_exemplo_datas_festivas.sql`:

```sql
select public.gerar_datas_festivas_exemplo();
```

Se o SQL Editor der tempo esgotado, gere menos anos (`select public.gerar_dados_exemplo(2);`).

O **simulador** (`018_simulador.sql`) faz a loja andar sozinha a cada 30 minutos (vendas, faltas, pedidos pela
previsão, entregas e fechamentos). No programa aparece a faixa "Modo simulação". Liga e desliga com
`select public.simulacao_ligar(true);` / `select public.simulacao_ligar(false);`.

**Antes do uso real**, desligue o simulador e apague tudo (os usuários ficam):
`select public.limpar_dados_exemplo('APAGAR TUDO');`

## Ligar o e-mail

O resumo do dia, o envio automático dos pedidos e as respostas dos fornecedores usam uma conta de e-mail só do
mercado. A senha fica **só nos segredos do Supabase**, nunca no .exe nem no git. Sem isso o programa funciona: o
resumo e o pedido abrem no programa de e-mail do computador.

1. Criar uma conta Gmail para o mercado (ex.: `pedidos.mercado@gmail.com`), ligar a **verificação em duas etapas**
   e criar uma **senha de app** em https://myaccount.google.com/apppasswords (16 letras).
2. No painel do Supabase, **Edge Functions > Secrets**, criar:
   `EMAIL_USUARIO` (o e-mail), `EMAIL_SENHA_APP` (a senha de app) e `SEGREDO_AGENDAMENTO` (um texto comprido
   qualquer, inventado por você, com pelo menos 16 letras).
3. Publicar as funções (precisa do Node.js; o comando pede para entrar na conta do Supabase pelo navegador):
   ```powershell
   npx supabase login
   npx supabase functions deploy enviar-email --no-verify-jwt --project-ref fswyphqpswrsxvbblhyf
   npx supabase functions deploy ler-emails --no-verify-jwt --project-ref fswyphqpswrsxvbblhyf
   ```
   (`--no-verify-jwt` porque as funções conferem o login e o gerente por dentro.)
4. No SQL Editor, guardar o mesmo `SEGREDO_AGENDAMENTO` no Vault e depois rodar `010_semana6_agendamentos.sql`:
   ```sql
   select vault.create_secret('o-mesmo-texto-do-passo-2', 'segredo_agendamento');
   ```
   O 010 liga a leitura da caixa a cada 5 minutos e o resumo automático das 23h.
5. No programa: **Calendário**, clicar num dia até hoje e, no fechamento, **Cadastrar e-mails** (ou **Alterar**) para escolher quem recebe o resumo.

Com o e-mail ligado, ao criar ou alterar um pedido o e-mail vai na hora para o fornecedor e a janela mostra se foi
enviado (com "Tentar de novo" se der erro). E-mails de teste (`.example`) nunca recebem envio automático.

## App de celular da gerência

`app-celular/` é o app do gerente para o celular, com o mesmo login do programa (só o gerente entra). Está publicado
em **https://guilhermeguerhart.github.io/App/** (cópia da pasta no repositório
[guilhermeguerhart/App](https://github.com/guilhermeguerhart/App)). Ao mudar o app aqui, copie a pasta para lá.

- Abas: **Hoje**, **Calendário**, **Análises**, **Avisos**, **Estoque** (com "Dura até" e 10 produtos por página) e
  **Pedir** (pedido pela previsão, enviado por e-mail e WhatsApp).
- Trava com digital, rosto ou PIN e interruptor "Ocultar" para esconder os valores.

Para instalar: no Android, abrir o endereço no Chrome e "Instalar app", ou gerar o `.apk` em
https://www.pwabuilder.com (colar o endereço e escolher **Android**). No iPhone, abrir no Safari e
"Adicionar à Tela de Início".

## Regras do login (iguais às do projeto de referência)

- 5 senhas erradas seguidas bloqueiam aquele usuário por 5 minutos neste computador.
- Toda tentativa (certa ou errada) é gravada na tabela `auditoria_login`.
- "Esqueci minha senha" grava um pedido em `pedidos_senha` para o gerente redefinir a senha.
- "Lembrar meu usuário" guarda o último usuário em `%LocalAppData%\Mercado\preferencias.json`.
- As mensagens de "Fale com a gente" vão para a tabela `contatos`.
- Depois do login, o gerente vai para o Painel e o operador para o Caixa.
- Cada um troca a própria senha em **Meu perfil** (clicar no nome, no rodapé do menu).

## Segurança

O .exe e o app de celular guardam apenas a chave pública (publishable) do Supabase, que pode ser exposta. Quem
protege os dados são as regras de acesso (Row Level Security) do banco: cada colaborador entra com o próprio login e
só lê o que o seu perfil permite. Por isso o `appsettings.json` pode ficar no git. **Nunca** coloque nele a chave
secreta (`sb_secret` / `service_role`). A senha do e-mail fica só nos segredos do Supabase.
