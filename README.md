# Mercadinho Seu Zé

Sistema de gestão do mercado: caixa, estoque, fornecedores, fechamento do dia e previsão de compras.
Programa para Windows feito em **C# (.NET 10 + WPF)**, com banco de dados **PostgreSQL no Supabase** (nuvem),
distribuído como **um único arquivo .exe**.

## Cronograma

| Semana | Entrega | Situação |
|---|---|---|
| 1 | Tela inicial e login com perfis caixa e dono | Feito |
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
  Models/                 tipos do sistema (Perfil, Papel)
  Services/               configuração e login no Supabase
  Views/                  telas (tela inicial, login, área do usuário)
  App.xaml                cores e estilos visuais
publicar.ps1              gera o .exe único
```

## Como preparar

1. **Instalar o .NET 10 SDK**: `winget install Microsoft.DotNet.SDK.10`
2. **Criar o projeto no Supabase** (https://supabase.com, plano grátis).
3. No Supabase, abrir **SQL Editor** e executar `database/001_semana1_perfis.sql`.
4. Em **Authentication > Users > Add user**, criar os usuários (marcar *Auto Confirm User*).
   Todo usuário novo entra como **caixa**. Para tornar alguém **dono**, rodar no SQL Editor:
   ```sql
   update public.perfis set papel = 'dono'
   where id = (select id from auth.users where email = 'dono@mercadinho.com');
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

## Segurança

O .exe guarda apenas a chave pública (anon) do Supabase, que pode ser exposta. Quem protege os dados são as
regras de acesso (Row Level Security) do banco: cada colaborador entra com o próprio login e só lê o que o
seu perfil permite. **Nunca** coloque a chave `service_role` no appsettings.json.
