using System.Net;
using System.Net.Http;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Testes.Motor;

namespace MercadinhoSeuZe.Testes.Testes;

/// <summary>
/// Entrar no sistema pelo mesmo AuthService do programa: perfis, senha errada, bloqueio de 5 tentativas, usuário
/// desativado, renovação do token e pedido de nova senha. As senhas erradas usam um usuário que não existe, para não
/// travar ninguém de verdade; o arquivo de preferências deste computador volta como estava no fim.
/// </summary>
public static class LoginTestes
{
    private const string UsuarioInexistente = "teste.automatico.login";

    public static Lista Montar(Ambiente amb)
    {
        var l = new Lista("Login");
        AuthService Auth() => new(amb.Api!, new PreferenciasLocais());

        l.Certo("Gerente entra e cai no perfil Gerente", async () =>
        {
            var u = amb.Usuarios["gerente"];
            var r = await Verificar.Rapido(() => Auth().EntrarAsync(u.Usuario, u.Senha), TimeSpan.FromSeconds(4), "Login");
            Verificar.Verdadeiro(r.Ok, $"Não entrou: {r.Erro}");
            Verificar.Igual(Perfil.Gerente, r.Sessao!.Perfil, "Perfil");
            Verificar.Verdadeiro(r.Sessao.RefreshToken is not null, "Sem refresh_token: o programa não conseguiria renovar o login depois de 1 hora");
        });
        l.Certo("Operador entra e cai no perfil Operador", async () =>
        {
            var u = amb.Usuarios["operador"];
            var r = await Auth().EntrarAsync(u.Usuario, u.Senha);
            Verificar.Verdadeiro(r.Ok, $"Não entrou: {r.Erro}");
            Verificar.Igual(Perfil.Operador, r.Sessao!.Perfil, "Perfil");
        });
        l.Certo("Usuário com maiúsculas e espaços (\"  Marcos.Gerente \") também entra", async () =>
        {
            var u = amb.Usuarios["gerente"];
            var r = await Auth().EntrarAsync("  " + u.Usuario.ToUpperInvariant() + " ", u.Senha);
            Verificar.Verdadeiro(r.Ok, $"Não entrou: {r.Erro}");
        });
        l.Certo("Token é renovado antes de vencer (turno de mais de 1 hora)", async () =>
        {
            var u = amb.Usuarios["gerente"];
            var auth = Auth();
            var r = await auth.EntrarAsync(u.Usuario, u.Senha);
            var antigo = r.Sessao!.Token;
            Verificar.Verdadeiro(await auth.RenovarAsync(r.Sessao), "RenovarAsync devolveu false");
            Verificar.Verdadeiro(r.Sessao.Token != antigo, "O token não mudou");
            await amb.Api!.ListarAsync<object>("perfis?select=id&limit=1", r.Sessao.Token);
        });
        l.Errado("Senha errada: \"Usuário ou senha inválidos\" e conta as tentativas", async () =>
        {
            var auth = Auth();
            var r = await auth.EntrarAsync(UsuarioInexistente, "senha-errada");
            Verificar.Verdadeiro(!r.Ok, "Entrou com senha errada");
            Verificar.Igual("Usuário ou senha inválidos.", r.Erro, "Mensagem");
            Verificar.Igual(4, r.TentativasRestantes, "Tentativas restantes");
        });
        l.Limite("5 senhas erradas bloqueiam o usuário por 5 minutos; a 6ª nem tenta", async () =>
        {
            var auth = Auth();
            ResultadoLogin r = new();
            for (var i = 1; i <= 5; i++) r = await auth.EntrarAsync(UsuarioInexistente + ".b", "errada" + i);
            Verificar.Verdadeiro(r.Bloqueado is { } b && b > TimeSpan.FromMinutes(4.9), "A 5ª senha errada deveria bloquear por 5 minutos");
            var sexta = await auth.EntrarAsync(UsuarioInexistente + ".b", "errada6");
            Verificar.Verdadeiro(sexta.Bloqueado is not null, "A 6ª tentativa deveria continuar bloqueada");
            Verificar.Verdadeiro(auth.TempoBloqueado(UsuarioInexistente) == TimeSpan.Zero, "O bloqueio de um usuário não pode travar outro");
        });
        l.Errado("Usuário desativado não entra", async () =>
        {
            var u = amb.Usuarios["desativado"];
            var r = await Auth().EntrarAsync(u.Usuario, u.Senha);
            Verificar.Verdadeiro(!r.Ok, $"{u.Usuario} entrou, mas deveria estar desativado (USUARIOS_TESTE.md)");
            Verificar.Verdadeiro(r.Erro?.Contains("desativado") == true || r.Erro?.Contains("inválidos") == true, $"Mensagem: {r.Erro}");
        });
        l.Errado("Usuário e senha vazios, texto estranho e tentativa de injeção não entram", async () =>
        {
            foreach (var (usuario, senha) in new[] { ("", ""), ("marcos.gerente", ""), ("' or 1=1 --", "' or '1'='1"), ("marcos.gerente\"; drop table perfis;--", "x"), ("<script>", "x") })
            {
                var r = await Auth().EntrarAsync(usuario, senha);
                Verificar.Verdadeiro(!r.Ok, $"Entrou com \"{usuario}\" / \"{senha}\"");
            }
        });
        l.Limite("Usuário com 300 letras e senha com 1.000 letras: recusa sem travar", async () =>
        {
            var r = await Auth().EntrarAsync(new string('a', 300), new string('b', 1000));
            Verificar.Verdadeiro(!r.Ok, "Entrou");
            Verificar.Verdadeiro(r.Erro is not null || r.Bloqueado is not null, "Sem mensagem para a pessoa");
        });
        l.Seguranca("Token inventado ou de outro sistema é recusado pelo banco", async () =>
        {
            var falso = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIiwicm9sZSI6ImF1dGhlbnRpY2F0ZWQifQ.assinatura-falsa";
            var (status, corpo, _) = await amb.Http(HttpMethod.Get, "rest/v1/produtos?select=id&limit=1", falso);
            Verificar.Verdadeiro(status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"Token falso: {(int)status} {corpo}");
        });
        l.Certo("\"Esqueci a senha\" grava o pedido para o gerente", async () =>
        {
            var g = await amb.Entrar("gerente");
            var usuario = "teste.automatico.senha";
            amb.ApagarNoFim("pedido de senha de teste", () => amb.Api!.ExcluirAsync("pedidos_senha", $"usuario=eq.{usuario}", g.Token));
            await Auth().SolicitarRecuperacaoAsync(usuario);
            var n = await amb.ContarAsync($"pedidos_senha?usuario=eq.{usuario}", g.Token);
            Verificar.Verdadeiro(n >= 1, "O pedido de nova senha não foi gravado");
        });
        l.Seguranca("Cada tentativa de login vai para a auditoria", async () =>
        {
            var g = await amb.Entrar("gerente");
            await Auth().EntrarAsync(UsuarioInexistente + ".auditoria", "x");
            await Task.Delay(1500); // a auditoria é gravada sem esperar o login
            var n = await amb.ContarAsync($"auditoria_login?usuario=eq.{UsuarioInexistente}.auditoria", g.Token);
            Verificar.Verdadeiro(n >= 1, "A tentativa não apareceu em auditoria_login");
        });
        return l;
    }
}
