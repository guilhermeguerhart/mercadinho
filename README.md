# Mercado · Sistema de gestão

Sistema de gestão do mercado: caixa, estoque, fornecedores, fechamento do dia e previsão de compras.
Programa para Windows feito em **C# (.NET 10 + WPF)**, com banco de dados **PostgreSQL no Supabase** (nuvem),
distribuído como **um único arquivo .exe**.

O visual, os textos e os perfis seguem o projeto de referência
[kethleensilva06/Projeto_mercado](https://github.com/kethleensilva06/Projeto_mercado) (telas no Figma, em `figma/`).

## Cronograma

| Semana | Entrega | Situação |
|---|---|---|
| 1 | Tela inicial e login com perfis gerente e operador | Feito |
| 2 | Painel (resumo do dia, avisos de estoque e pedidos) | Feito |
| 3 | Estoque (produtos, lotes e validade) | Feito |
| 4 | Fornecedores (cadastro, pedidos pelo WhatsApp/e-mail, entregas) | Feito |
| 5 | Caixa padrão (reserva, baixa por lote, pagamento dividido, maquininha emulada, modo caixa), funcionários e meu perfil | Feito |
| 6 | Fechamento do dia, análises, resumo por e-mail, mensagens com fornecedores e app de celular da gerência | Feito (falta ligar o e-mail) |
| 7 | Previsão de compras e pedidos automáticos | Planejado |

## Estrutura

```
database/                 scripts SQL para rodar no Supabase, em ordem
supabase/functions/       Edge Functions de e-mail (enviar-email, ler-emails), semana 6
app-celular/              app de celular da gerência (página instalável, vira .apk no PWABuilder)
src/MercadinhoSeuZe/
  Models/                 tipos do sistema (Perfil, Sessao, Produto, Fornecedor, Pedido, Venda)
  Services/               configuração, acesso ao Supabase, login e preferências locais
  Views/                  telas (tela inicial, login, área interna com menu, painel, caixa, estoque, fornecedores, funcionários)
  App.xaml                cores e estilos visuais
publicar.ps1              gera o .exe único
INSTRUCOES_PARA_IA.txt    resumo do projeto, próximos passos e restrições (leia antes de alterar)
```

## Como preparar

1. **Instalar o .NET 10 SDK**: `winget install Microsoft.DotNet.SDK.10`
2. **Criar o projeto no Supabase** (https://supabase.com, plano grátis).
3. No Supabase, abrir **SQL Editor** e executar os scripts de `database/` em ordem
   (`001_semana1_perfis.sql`, depois `003_semana3_estoque.sql`, `004_semana4_fornecedores.sql`,
   `005_semana4_ajustes.sql`, `006_semana5_caixa.sql`, `007_semana5_caixa_padrao.sql`, `008_promocoes_fechamento.sql` e
   `009_semana6_fechamento_mensagens.sql` e `012_semana6_ajustes.sql`; o `002` só cria usuários de teste; o `010` só depois de ligar o e-mail, abaixo).
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

## Rodar e gerar o .exe

```powershell
dotnet run --project src/MercadinhoSeuZe     # abrir para testar
./publicar.ps1                                # gera publish/MercadinhoSeuZe.exe
./publicar.ps1 -Desenvolvimento               # gera publish-dev/ com o atalho de teste (Alt+1)
```

Na versão de desenvolvimento (ou rodando em Debug), **Alt+1 na tela de login** abre a lista dos usuários de
teste no canto inferior esquerdo; clicar num deles preenche usuário e senha. **Alt+2 na tela de Estoque** abre um
leitor de código de barras simulado (catálogo de produtos; cada clique gera um código de caixa novo).
**Alt+2 na tela de Fornecedores** cadastra fornecedores de teste (telefones e e-mails fictícios).
**Alt+2 no Caixa** passa no leitor o código de um produto com estoque (ou uma etiqueta da balança).
O .exe normal não tem esses atalhos.

O .exe já leva a configuração dentro dele e roda em qualquer Windows 64 bits sem instalar nada.

## Dados de exemplo (para testar)

`database/011_dados_de_exemplo.sql` enche o banco com um mercado fictício: 113 produtos, 6 fornecedores, pedidos
toda semana e **5 anos de vendas, caixas e fechamentos do dia** (umas 95 mil vendas). Rodar no SQL Editor, depois do
001 ao 009 e do 002 (usuários de teste):

```sql
-- 1. rodar o arquivo 011 inteiro (cria as funções); depois:
select public.gerar_dados_exemplo();            -- leva cerca de 1 minuto
select public.gerar_fechamentos_exemplo(null);  -- fechamento de cada dia
```

Vendas a mais nos feriados e datas festivas (Natal, Ano-Novo, Páscoa, São João, Dia das Mães, Copa de 2022...),
depois de rodar o `012_semana6_ajustes.sql` (calendário de datas especiais) e o arquivo `013_dados_exemplo_datas_festivas.sql`:

```sql
select public.gerar_datas_festivas_exemplo();
```

Se o SQL Editor der tempo esgotado, gere menos anos (`select public.gerar_dados_exemplo(2);`).
**Antes do uso real**, apague tudo (os usuários ficam): `select public.limpar_dados_exemplo('APAGAR TUDO');`

## Ligar o e-mail (semana 6)

O resumo do dia por e-mail e as respostas dos fornecedores usam uma conta de e-mail só do mercado. A senha fica
**só nos segredos do Supabase**, nunca no .exe nem no git. Sem isso o programa funciona: o resumo e o pedido abrem
no programa de e-mail do computador, como antes.

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
5. No programa: **Fechamento do dia > Alterar** e cadastrar os e-mails que recebem o resumo.

## App de celular da gerência (semana 6)

`app-celular/` é o app do gerente para o celular (só leitura: vendas do dia, caixas, análises e avisos), com o mesmo
login do programa. Para virar um app instalável:

1. Colocar a pasta `app-celular/` num endereço https (ex.: GitHub Pages do repositório da Keth, ou arrastar a pasta
   em https://app.netlify.com/drop).
2. Abrir https://www.pwabuilder.com, colar o endereço e escolher **Android** (gera o `.apk` para instalar direto e o
   `.aab` da Play Store). No iPhone, abrir o endereço no Safari e "Adicionar à Tela de Início".

## Regras do login (iguais às do projeto de referência)

- 5 senhas erradas seguidas bloqueiam aquele usuário por 5 minutos neste computador.
- Toda tentativa (certa ou errada) é gravada na tabela `auditoria_login`.
- "Esqueci minha senha" grava um pedido em `pedidos_senha` para o gerente redefinir a senha.
- "Lembrar meu usuário" guarda o último usuário em `%LocalAppData%\Mercado\preferencias.json`.
- As mensagens de "Fale com a gente" vão para a tabela `contatos`.
- Depois do login, o gerente vai para o Painel e o operador para o Caixa.
- Cada um troca a própria senha em **Meu perfil** (clicar no nome, no rodapé do menu).

## Segurança

O .exe guarda apenas a chave pública (anon) do Supabase, que pode ser exposta. Quem protege os dados são as
regras de acesso (Row Level Security) do banco: cada colaborador entra com o próprio login e só lê o que o
seu perfil permite. Por isso o `appsettings.json` pode ficar no git. **Nunca** coloque nele a chave
secreta (`sb_secret` / `service_role`).
