# Usuários de teste

Usuários fictícios para testar o login. São criados pelo script `database/002_usuarios_teste.sql`
(rodar no SQL Editor do Supabase depois do `001_semana1_perfis.sql`).

| Perfil   | Usuário          | Senha          | Situação   |
|----------|------------------|----------------|------------|
| Gerente  | `marcos.gerente` | `Gerente#4821` | Ativo      |
| Gerente  | `julia.gerente`  | `Gerente#7350` | Ativo      |
| Operador | `ana.caixa`      | `Caixa#1964`   | Ativo      |
| Operador | `pedro.caixa`    | `Caixa#5307`   | Ativo      |
| Operador | `carla.caixa`    | `Caixa#8142`   | Desativado (para testar o aviso de usuário desativado) |

No programa, digite só o usuário (ex.: `ana.caixa`); o sistema completa com `@mercado.local`.

Antes de usar o sistema de verdade, apague esses usuários em **Authentication > Users** no Supabase.
