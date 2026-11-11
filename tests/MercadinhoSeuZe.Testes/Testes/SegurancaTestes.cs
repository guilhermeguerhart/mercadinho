using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Testes.Motor;

namespace MercadinhoSeuZe.Testes.Testes;

/// <summary>
/// Quem pode ver e mudar o quê. Tudo aqui tenta fazer o que NÃO pode (sem login, como operador, como usuário
/// desativado): o banco tem que recusar, então nada fica gravado.
/// </summary>
public static class SegurancaTestes
{
    public static Lista Montar(Ambiente amb)
    {
        var l = new Lista("Segurança");

        // ---------------------------------------------------------------- Sem login (só a chave pública)
        l.Seguranca("Sem login ninguém lê produtos, vendas, funcionários, fornecedores nem a auditoria", async () =>
        {
            var vazou = new List<string>();
            foreach (var tabela in new[] { "produtos", "lotes", "vendas", "caixas", "perfis", "fornecedores", "pedidos", "auditoria_login", "pedidos_senha", "config_mercado", "fornecedor_mensagens" })
            {
                var (status, corpo, _) = await amb.Http(HttpMethod.Get, $"rest/v1/{tabela}?select=*&limit=5", null);
                if (status == HttpStatusCode.OK && corpo.Trim() != "[]") vazou.Add($"{tabela}: {corpo[..Math.Min(80, corpo.Length)]}");
            }
            Verificar.Verdadeiro(vazou.Count == 0, "Dá para ler sem login:\n" + string.Join("\n", vazou));
        });
        l.Seguranca("Sem login as funções do banco não rodam", async () =>
        {
            var aceitou = new List<string>();
            foreach (var (funcao, args) in new (string, object)[]
                     {
                         ("listar_funcionarios", new { }), ("resumo_dia", new { p_dia = DateTime.Today.ToString("yyyy-MM-dd") }),
                         ("abrir_caixa", new { p_troco = 0 }), ("criar_funcionario", new { p_usuario = "x", p_nome = "x", p_perfil = "gerente", p_senha = "12345678" }),
                         ("reservados_hoje", new { }),
                     })
            {
                var (status, corpo, _) = await amb.Http(HttpMethod.Post, $"rest/v1/rpc/{funcao}", null, args);
                if ((int)status is >= 200 and < 300) aceitou.Add($"{funcao}: {corpo[..Math.Min(80, corpo.Length)]}");
            }
            Verificar.Verdadeiro(aceitou.Count == 0, "Funções que rodaram sem login:\n" + string.Join("\n", aceitou));
        });
        l.Seguranca("Sem login não dá para gravar produto", async () =>
        {
            var (status, corpo, _) = await amb.Http(HttpMethod.Post, "rest/v1/produtos", null,
                new { nome = Ambiente.Prefixo + " anonimo", categoria = "Teste", preco = 1 });
            Verificar.Verdadeiro((int)status >= 400, $"Gravou sem login! {(int)status} {corpo}");
        });

        // ---------------------------------------------------------------- Operador
        l.Seguranca("Operador não cadastra, altera nem apaga produto e lote", async () =>
        {
            var op = await amb.Entrar("operador");
            var g = await amb.Entrar("gerente");
            var est = new EstoqueService(amb.Api!, op);
            await Verificar.Recusa(() => est.CriarProdutoAsync(
                new DadosProduto(Ambiente.Prefixo + " do operador", "Teste", 1, 0, null, null, 1, false, null), 0, DateOnly.FromDateTime(DateTime.Today), null),
                "Operador cadastrando produto");
            var algum = (await amb.Api!.ListarAsync<JsonElement>("produtos?select=id,preco,lotes(id)&order=id&limit=1", g.Token)).FirstOrDefault();
            if (algum.ValueKind != JsonValueKind.Object) throw new PularTeste("Sem produtos no banco para tentar alterar.");
            var id = algum.Long("id");
            var preco = algum.Dec("preco");
            await NadaMuda(amb, "PATCH", $"produtos?id=eq.{id}", op.Token, new { preco = 0.01m }, "Operador mudando preço");
            await NadaMuda(amb, "DELETE", $"produtos?id=eq.{id}", op.Token, null, "Operador apagando produto");
            var lote = algum.GetProperty("lotes").EnumerateArray().Select(x => x.Long("id")).FirstOrDefault();
            if (lote > 0) await NadaMuda(amb, "DELETE", $"lotes?id=eq.{lote}", op.Token, null, "Operador apagando lote");
            var depois = (await amb.Api.ListarAsync<JsonElement>($"produtos?id=eq.{id}&select=preco", g.Token)).Single();
            Verificar.Igual(preco, depois.Dec("preco"), "Preço do produto depois das tentativas do operador");
        });
        l.Seguranca("Operador não vira gerente mudando o próprio perfil", async () =>
        {
            var op = await amb.Entrar("operador");
            await NadaMuda(amb, "PATCH", $"perfis?id=eq.{op.Id}", op.Token, new { perfil = "gerente" }, "Operador se promovendo a gerente");
            var g = await amb.Entrar("gerente");
            var p = (await amb.Api!.ListarAsync<JsonElement>($"perfis?id=eq.{op.Id}&select=perfil", g.Token)).First();
            Verificar.Igual("operador", p.Str("perfil"), "Perfil do operador depois da tentativa");
        });
        l.Seguranca("Operador não vê fornecedores, pedidos, configuração, auditoria nem mensagens", async () =>
        {
            var op = await amb.Entrar("operador");
            var vazou = new List<string>();
            var lentas = new List<string>();
            foreach (var tabela in new[] { "fornecedores", "pedidos", "pedido_itens", "config_mercado", "auditoria_login", "fornecedor_mensagens", "fechamentos_dia", "autorizacoes", "venda_item_lotes" })
            {
                var (status, corpo, _) = await amb.Http(HttpMethod.Get, $"rest/v1/{tabela}?select=*&limit=3", op.Token);
                if (corpo.Contains("57014")) lentas.Add(tabela);
                else if (status == HttpStatusCode.OK && corpo.Trim() != "[]") vazou.Add(tabela);
                else if (status != HttpStatusCode.OK) lentas.Add($"{tabela} ({(int)status})");
            }
            Verificar.Verdadeiro(vazou.Count == 0, "O operador consegue ler: " + string.Join(", ", vazou));
            Verificar.Verdadeiro(lentas.Count == 0, "Nada vazou, mas o banco deu erro em vez de lista vazia (passou do tempo limite de 8 s): " + string.Join(", ", lentas) +
                ". A regra de acesso confere \"é gerente?\" linha por linha na tabela inteira; com (select public.eh_gerente()) o banco confere uma vez só.");
        });
        l.Seguranca("Operador só vê as próprias vendas e o próprio caixa de hoje", async () =>
        {
            var op = await amb.Entrar("operador");
            var vendas = await amb.Api!.ListarAsync<JsonElement>("vendas?select=operador_id,dia&limit=500", op.Token);
            var caixas = await amb.Api.ListarAsync<JsonElement>("caixas?select=operador_id,dia&limit=500", op.Token);
            var hoje = DateTime.Today.ToString("yyyy-MM-dd");
            var outros = vendas.Concat(caixas).Count(v => v.Str("operador_id") != op.Id.ToString() || v.Str("dia") != hoje);
            Ambiente.Nota($"O operador leu {vendas.Count} vendas e {caixas.Count} caixas");
            Verificar.Igual(0, outros, "Vendas/caixas de outra pessoa ou de outro dia visíveis para o operador");
        });
        l.Seguranca("Operador não usa as funções do gerente (resumo, análises, funcionários, fechar o dia)", async () =>
        {
            var op = await amb.Entrar("operador");
            var api = amb.Api!;
            var hoje = DateTime.Today.ToString("yyyy-MM-dd");
            await Verificar.Recusa(() => api.ChamarAsync<JsonElement>("resumo_dia", new { p_dia = hoje }, op.Token), "resumo_dia", "Só o gerente");
            await Verificar.Recusa(() => api.ChamarAsync<JsonElement>("analise_vendas", new { p_inicio = hoje, p_fim = hoje, p_agrupar = "dia" }, op.Token), "analise_vendas", "Só o gerente");
            await Verificar.Recusa(() => api.ChamarAsync<JsonElement>("listar_funcionarios", new { }, op.Token), "listar_funcionarios", "Só o gerente");
            await Verificar.Recusa(() => api.ChamarAsync<JsonElement>("fechar_dia", new { p_dia = hoje, p_obs = (string?)null }, op.Token), "fechar_dia", "Só o gerente");
            await Verificar.Recusa(() => api.ChamarAsync<JsonElement>("criar_funcionario",
                new { p_usuario = "teste.auto.op", p_nome = "x", p_perfil = "gerente", p_senha = "Senha#12345" }, op.Token), "criar_funcionario", "Só o gerente");
            await Verificar.Recusa(() => api.ChamarAsync<JsonElement>("criar_pedido",
                new { p_fornecedor_id = 1, p_previsao = (string?)null, p_observacao = (string?)null, p_itens = new[] { new { produto_id = 1, quantidade = 1 } } }, op.Token), "criar_pedido", "Só o gerente");
        });
        l.Seguranca("Operador não grava venda direto na tabela (só pelas funções do caixa)", async () =>
        {
            var op = await amb.Entrar("operador");
            var (status, corpo, _) = await amb.Http(HttpMethod.Post, "rest/v1/vendas", op.Token,
                new { caixa_id = 1, operador_id = op.Id, situacao = "paga", total = 0 });
            Verificar.Verdadeiro((int)status >= 400, $"Gravou venda direto na tabela: {(int)status} {corpo}");
            var (s2, c2, _) = await amb.Http(HttpMethod.Post, "rest/v1/lotes", op.Token,
                new { produto_id = 1, quantidade_inicial = 1000, quantidade_restante = 1000, data_compra = DateTime.Today.ToString("yyyy-MM-dd") });
            Verificar.Verdadeiro((int)s2 >= 400, $"Gravou lote como operador: {(int)s2} {c2}");
        });
        l.Seguranca("Usuário desativado não abre caixa nem usa as funções", async () =>
        {
            var u = amb.Usuarios["desativado"];
            var (token, _, status) = await amb.TokenAsync(u.Usuario, u.Senha);
            if (token is null) throw new PularTeste($"{u.Usuario} não existe ou a senha mudou ({(int)status}).");
            await Verificar.Recusa(() => amb.Api!.ChamarAsync<long>("abrir_caixa", new { p_troco = 0 }, token), "Desativado abrindo caixa", "desativado");
            var linhas = await amb.Api!.ListarAsync<JsonElement>("produtos?select=id&limit=3", token);
            if (linhas.Count > 0) Ambiente.AvisoPendente("O usuário desativado ainda consegue LER os produtos com o login dele (só leitura).");
        });

        // ---------------------------------------------------------------- Chaves
        l.Seguranca("O programa só leva a chave pública do Supabase (nunca a secreta)", () =>
        {
            var config = Configuracao.Carregar();
            if (config is null) throw new PularTeste("O programa foi gerado sem appsettings.json.");
            Verificar.Verdadeiro(!config.SupabaseAnonKey.StartsWith("sb_secret", StringComparison.OrdinalIgnoreCase), "O appsettings.json embutido tem a chave SECRETA");
            Verificar.Verdadeiro(!Base64Contem(config.SupabaseAnonKey, "service_role"), "O appsettings.json embutido tem a chave service_role");
            Ambiente.Nota("Chave do programa: " + config.SupabaseAnonKey[..Math.Min(15, config.SupabaseAnonKey.Length)] + "…");
            return Task.CompletedTask;
        }, banco: false);
        l.Seguranca("Nenhum arquivo do app de celular e do Supabase tem chave secreta ou senha", () =>
        {
            var raiz = CelularTestes.PastaDoRepositorio();
            if (raiz is null) throw new PularTeste("Pasta do projeto não encontrada (o teste roda de dentro do repositório).");
            var achados = new List<string>();
            foreach (var pasta in new[] { "app-celular", "supabase", "src" })
            {
                var dir = Path.Combine(raiz, pasta);
                if (!Directory.Exists(dir)) continue;
                foreach (var arq in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                             .Where(a => !a.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !a.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !a.Contains(".temp"))
                             .Where(a => Path.GetExtension(a) is ".js" or ".ts" or ".json" or ".cs" or ".html" or ".xaml" or ".env"))
                {
                    var texto = File.ReadAllText(arq);
                    if (texto.Contains("sb_secret_", StringComparison.Ordinal) || System.Text.RegularExpressions.Regex.IsMatch(texto, @"eyJ[\w-]{10,}\.[\w-]{20,}\.[\w-]{10,}") && Base64JwtServiceRole(texto))
                        achados.Add(Path.GetRelativePath(raiz, arq));
                }
            }
            Verificar.Verdadeiro(achados.Count == 0, "Arquivos com chave secreta: " + string.Join(", ", achados));
            return Task.CompletedTask;
        }, banco: false);
        return l;
    }

    /// <summary>
    /// Tenta alterar/apagar direto no banco e confere que nenhuma linha mudou (Content-Range "*/0"). Sem permissão o
    /// Supabase não dá erro: só não muda nada.
    /// </summary>
    private static async Task NadaMuda(Ambiente amb, string metodo, string caminho, string token, object? corpo, string oQue)
    {
        var (status, _, r) = await amb.Http(new HttpMethod(metodo), "rest/v1/" + caminho, token, corpo, "return=minimal, count=exact");
        if ((int)status >= 400) { Ambiente.Nota($"{oQue}: recusado ({(int)status})"); return; }
        var faixa = r.Content.Headers.TryGetValues("Content-Range", out var v) ? v.First() : r.Headers.TryGetValues("Content-Range", out var v2) ? v2.First() : "?";
        Verificar.Verdadeiro(faixa.EndsWith("/0"), $"{oQue}: o banco ALTEROU {faixa.Split('/').Last()} linha(s)!");
        Ambiente.Nota($"{oQue}: nenhuma linha alterada");
    }

    private static bool Base64Contem(string chave, string texto)
    {
        try
        {
            var partes = chave.Split('.');
            if (partes.Length < 2) return false;
            var p = partes[1].Replace('-', '+').Replace('_', '/');
            p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(p)).Contains(texto);
        }
        catch (FormatException) { return false; }
    }

    private static bool Base64JwtServiceRole(string texto) =>
        System.Text.RegularExpressions.Regex.Matches(texto, @"eyJ[\w-]{10,}\.[\w-]{20,}\.[\w-]{10,}").Any(m => Base64Contem(m.Value, "service_role"));
}
