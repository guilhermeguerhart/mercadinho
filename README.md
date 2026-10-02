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
| 2 | Painel | Planejado |
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
  Views/                  telas (tela inicial, login, em desenvolvimento)
  App.xaml                cores e estilos visuais
publicar.ps1              gera o .exe único
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
5. Copiar `src/MercadinhoSeuZe/appsettings.example.json` para `appsettings.json` na mesma pasta e preencher
   com a **Project URL** e a **anon public key** (Supabase > Project Settings > API).
   Esse arquivo não vai para o git.

## Rodar e gerar o .exe

```powershell
dotnet run --project src/MercadinhoSeuZe     # abrir para testar
./publicar.ps1                                # gera publish/MercadinhoSeuZe.exe
```

O .exe já leva a configuração dentro dele e roda em qualquer Windows 64 bits sem instalar nada.

## Regras do login (iguais às do projeto de referência)

- 5 senhas erradas seguidas bloqueiam aquele usuário por 5 minutos neste computador.
- Toda tentativa (certa ou errada) é gravada na tabela `auditoria_login`.
- "Esqueci minha senha" grava um pedido em `pedidos_senha` para o gerente redefinir a senha.
- "Lembrar meu usuário" guarda o último usuário em `%LocalAppData%\Mercado\preferencias.json`.
- As mensagens de "Fale com a gente" vão para a tabela `contatos`.
- Gerente e operador vão, por enquanto, para a tela "em desenvolvimento"; a partir da semana 2 o gerente vai
  para o Painel e o operador para o Caixa.

## Segurança

O .exe guarda apenas a chave pública (anon) do Supabase, que pode ser exposta. Quem protege os dados são as
regras de acesso (Row Level Security) do banco: cada colaborador entra com o próprio login e só lê o que o
seu perfil permite. **Nunca** coloque a chave `service_role` no appsettings.json.
