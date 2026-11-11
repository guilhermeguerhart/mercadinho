using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Testes.Motor;

namespace MercadinhoSeuZe.Testes.Testes;

/// <summary>
/// Funcionários (só o gerente) e "Meu perfil": criar, entrar com o usuário novo, editar, redefinir e trocar senha,
/// remover. Os usuários de teste começam com "teste.auto." e são removidos no fim (nunca abrem caixa, então são apagados).
/// </summary>
public static class FuncionariosTestes
{
    public static Lista Montar(Ambiente amb)
    {
        var l = new Lista("Funcionários");
        async Task<FuncionarioService> Servico() => new(amb.Api!, await amb.Entrar("gerente"));

        async Task<Funcionario> Criar(string perfil, string senha)
        {
            var srv = await Servico();
            var usuario = $"teste.auto.{Guid.NewGuid().ToString("N")[..8]}";
            await srv.CriarAsync(usuario, $"{Ambiente.Prefixo} {perfil}", perfil, senha);
            var f = (await srv.ListarAsync()).FirstOrDefault(x => x.Usuario == usuario) ?? throw new FalhaTeste("O funcionário criado não aparece na lista.");
            amb.ApagarNoFim($"funcionário {usuario}", () => srv.RemoverAsync(f.Id));
            return f;
        }

        l.Certo("Criar operador, entrar com ele, editar o nome e remover (sem vendas = apagado)", async () =>
        {
            var srv = await Servico();
            var f = await Criar("operador", "Senha#Teste1");
            Verificar.Verdadeiro(f.Ativo && !f.Gerente, "Deveria ser operador ativo");
            var (token, _, status) = await amb.TokenAsync(f.Usuario, "Senha#Teste1");
            Verificar.Verdadeiro(token is not null, $"O usuário novo não consegue entrar ({(int)status})");
            await srv.EditarAsync(f.Id, $"{Ambiente.Prefixo} Nome Novo", "operador", true);
            Verificar.Igual($"{Ambiente.Prefixo} Nome Novo", (await srv.ListarAsync()).First(x => x.Id == f.Id).Nome, "Nome depois de editar");
            Verificar.Igual("excluido", await srv.RemoverAsync(f.Id), "Remover quem nunca vendeu");
            var (depois, _, _) = await amb.TokenAsync(f.Usuario, "Senha#Teste1");
            Verificar.Nulo(depois, "Token de quem foi removido");
        });
        l.Certo("Redefinir a senha pelo gerente e trocar a própria senha em \"Meu perfil\"", async () =>
        {
            var srv = await Servico();
            var f = await Criar("operador", "Senha#Inicial1");
            await srv.RedefinirSenhaAsync(f.Id, "Senha#Gerente2");
            var (token, id, _) = await amb.TokenAsync(f.Usuario, "Senha#Gerente2");
            Verificar.Verdadeiro(token is not null, "Não entrou com a senha redefinida");
            var proprio = new FuncionarioService(amb.Api!, new Models.Sessao(id, f.Usuario, f.Nome, Models.Perfil.Operador, token!));
            await proprio.TrocarMinhaSenhaAsync("Senha#Gerente2", "Senha#Minha3");
            Verificar.Verdadeiro((await amb.TokenAsync(f.Usuario, "Senha#Minha3")).Token is not null, "Não entrou com a senha trocada");
            Verificar.Verdadeiro((await amb.TokenAsync(f.Usuario, "Senha#Gerente2")).Token is null, "A senha antiga continua valendo");
            var perfil = await proprio.MeuPerfilAsync();
            Verificar.Verdadeiro(perfil is not null, "meu_perfil não respondeu");
        });
        l.Certo("Gerente novo enxerga a lista de funcionários", async () =>
        {
            var f = await Criar("gerente", "Senha#Gerente9");
            var (token, id, _) = await amb.TokenAsync(f.Usuario, "Senha#Gerente9");
            var srv = new FuncionarioService(amb.Api!, new Models.Sessao(id, f.Usuario, f.Nome, Models.Perfil.Gerente, token!));
            Verificar.Verdadeiro((await srv.ListarAsync()).Count >= 2, "O gerente novo não vê os funcionários");
        });

        l.Errado("Senha curta, usuário inválido ou repetido, nome vazio e função inventada são recusados", async () =>
        {
            var srv = await Servico();
            await Verificar.Recusa(() => srv.CriarAsync("teste.auto.curta", "x", "operador", "1234567"), "Senha de 7", "8 caracteres");
            await Verificar.Recusa(() => srv.CriarAsync("ab", "x", "operador", "Senha#12345"), "Usuário de 2 letras", "Usuário inválido");
            await Verificar.Recusa(() => srv.CriarAsync("joão silva", "x", "operador", "Senha#12345"), "Usuário com acento e espaço", "Usuário inválido");
            await Verificar.Recusa(() => srv.CriarAsync(amb.Usuarios["gerente"].Usuario, "x", "operador", "Senha#12345"), "Usuário repetido", "Já existe");
            await Verificar.Recusa(() => srv.CriarAsync("teste.auto.semnome", "  ", "operador", "Senha#12345"), "Nome vazio", "nome");
            await Verificar.Recusa(() => srv.CriarAsync("teste.auto.admin", "x", "administrador", "Senha#12345"), "Função inventada", "função");
        });
        l.Errado("Gerente não tira o próprio acesso nem remove a si mesmo", async () =>
        {
            var srv = await Servico();
            var eu = srv.Sessao;
            await Verificar.Recusa(() => srv.EditarAsync(eu.Id, eu.Nome, "operador", true), "Gerente virando operador", "próprio acesso");
            await Verificar.Recusa(() => srv.EditarAsync(eu.Id, eu.Nome, "gerente", false), "Gerente se desativando");
            await Verificar.Recusa(() => srv.RemoverAsync(eu.Id), "Gerente se removendo", "a si mesmo");
        });
        l.Errado("Trocar a própria senha com a senha atual errada é recusado", async () =>
        {
            var f = await Criar("operador", "Senha#Certa1");
            var (token, id, _) = await amb.TokenAsync(f.Usuario, "Senha#Certa1");
            var proprio = new FuncionarioService(amb.Api!, new Models.Sessao(id, f.Usuario, f.Nome, Models.Perfil.Operador, token!));
            await Verificar.Recusa(() => proprio.TrocarMinhaSenhaAsync("errada", "Senha#Nova12"), "Senha atual errada", "senha atual");
            await Verificar.Recusa(() => proprio.TrocarMinhaSenhaAsync("Senha#Certa1", "Senha#Certa1"), "Senha nova igual à atual");
        });
        l.Errado("Editar, redefinir senha e remover funcionário que não existe", async () =>
        {
            var srv = await Servico();
            await Verificar.Recusa(() => srv.EditarAsync(Guid.NewGuid(), "x", "operador", true), "Editar inexistente", "não existe");
            await Verificar.Recusa(() => srv.RedefinirSenhaAsync(Guid.NewGuid(), "Senha#12345"), "Redefinir inexistente", "não existe");
            await Verificar.Recusa(() => srv.RemoverAsync(Guid.NewGuid()), "Remover inexistente", "não existe");
        });

        l.Limite("Senha com 8 entra; usuário de 3 e de 40 letras entram, de 41 é recusado", async () =>
        {
            var srv = await Servico();
            var curto = "t" + Guid.NewGuid().ToString("N")[..2];
            var longo = ("teste.auto." + Guid.NewGuid().ToString("N") + "xxxxxxxx")[..40];
            foreach (var u in new[] { curto, longo })
            {
                await srv.CriarAsync(u, Ambiente.Prefixo + " limite", "operador", "Senha#12");
                var f = (await srv.ListarAsync()).First(x => x.Usuario == u);
                amb.ApagarNoFim($"funcionário {u}", () => srv.RemoverAsync(f.Id));
            }
            await Verificar.Recusa(() => srv.CriarAsync(longo + "x", "x", "operador", "Senha#12345"), "Usuário de 41 letras", "Usuário inválido");
        });
        l.Limite("Trocar a própria senha para uma de 7 letras", async () =>
        {
            var f = await Criar("operador", "Senha#Sete1");
            var (token, id, _) = await amb.TokenAsync(f.Usuario, "Senha#Sete1");
            var proprio = new FuncionarioService(amb.Api!, new Models.Sessao(id, f.Usuario, f.Nome, Models.Perfil.Operador, token!));
            try
            {
                await proprio.TrocarMinhaSenhaAsync("Senha#Sete1", "Ab#1234");
                throw new AvisoTeste("O servidor aceitou senha de 7 letras: só a janela \"Meu perfil\" exige 8. Para valer em tudo, suba o mínimo em Authentication > Policies no Supabase.");
            }
            catch (ConexaoException ex)
            {
                Ambiente.Nota($"Recusado: \"{ex.Message}\"");
            }
        });
        return l;
    }
}
