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
| 2 | Painel (modo de teste, com dados de exemplo) | Em teste |
| 3 | Estoque | Planejado |
| 4 | Fornecedores | Planejado |
| 5 | Caixa | Planejado |
| 6 | Fechamento do dia | Planejado |
| 7 | Previsão de compras e pedidos automáticos | Planejado |

## Estrutura

```
database/                 scripts SQL para rodar no Supabase, em ordem
src/MercadinhoSeuZe/
  Models/                 tipos do sistema (Perfil, Sessao)
  Services/               configuração, acesso ao Supabase, login e preferências locais
  Views/                  telas (tela inicial, login, área interna com menu, painel, caixa provisório)
  App.xaml                cores e estilos visuais
publicar.ps1              gera o .exe único
INSTRUCOES_PARA_IA.txt    resumo do projeto, próximos passos e restrições (leia antes de alterar)
```

## Como preparar

1. **Instalar o .NET 10 SDK**: `winget install Microsoft.DotNet.SDK.10`
2. **Criar o projeto no Supabase** (https://supabase.com, plano grátis).
3. No Supabase, abrir **SQL Editor** e executar `database/001_semana1_perfis.sql`.
4. Em **Authentication > Users > Add user**, criar os usuários marcando *Auto Confirm User*.
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
teste no canto inferior esquerdo; clicar num deles preenche usuário e senha. O .exe normal não tem esse atalho.

O .exe já leva a configuração dentro dele e roda em qualquer Windows 64 bits sem instalar nada.

## Regras do login (iguais às do projeto de referência)

- 5 senhas erradas seguidas bloqueiam aquele usuário por 5 minutos neste computador.
- Toda tentativa (certa ou errada) é gravada na tabela `auditoria_login`.
- "Esqueci minha senha" grava um pedido em `pedidos_senha` para o gerente redefinir a senha.
- "Lembrar meu usuário" guarda o último usuário em `%LocalAppData%\Mercado\preferencias.json`.
- As mensagens de "Fale com a gente" vão para a tabela `contatos`.
- Depois do login, o gerente vai para o Painel e o operador para o Caixa (provisório até a semana 5).

## Segurança

O .exe guarda apenas a chave pública (anon) do Supabase, que pode ser exposta. Quem protege os dados são as
regras de acesso (Row Level Security) do banco: cada colaborador entra com o próprio login e só lê o que o
seu perfil permite. Por isso o `appsettings.json` pode ficar no git. **Nunca** coloque nele a chave
secreta (`sb_secret` / `service_role`).
