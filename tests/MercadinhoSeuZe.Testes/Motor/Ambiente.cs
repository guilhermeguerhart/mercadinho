using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Testes.Motor;

/// <summary>Usuário de teste (de USUARIOS_TESTE.md ou do testes.json).</summary>
public sealed record UsuarioTeste(string Usuario, string Senha);

/// <summary>
/// Onde os testes rodam: qual banco, quais usuários, o que pode gravar e o que precisa ser apagado no fim.
///
/// Banco do mercado (o do appsettings.json embutido no programa): os testes leem, conferem as APIs e a integridade,
/// tentam fazer tudo errado (o banco recusa e nada fica gravado) e só criam registros que eles mesmos apagam no fim
/// (produtos, lotes, promoções, fornecedores, pedidos e funcionários marcados com "TESTE AUTOMÁTICO").
/// Vendas, caixas e fechamento do dia não podem ser apagados, então só rodam com --movimento ou num projeto de teste.
///
/// Projeto Supabase de teste: testes.json ao lado do .exe com a URL e a chave PÚBLICA. Nunca a chave secreta.
/// </summary>
public sealed class Ambiente
{
    /// <summary>Identificador do projeto Supabase do mercado de verdade.</summary>
    public const string ProjetoDoMercado = "fswyphqpswrsxvbblhyf";

    /// <summary>Prefixo de tudo o que o teste cria (para achar e apagar depois).</summary>
    public const string Prefixo = "TESTE AUTOMATICO";

    public static Ambiente Atual { get; private set; } = null!;

    public Configuracao? Config { get; }
    public SupabaseApi? Api { get; }
    public string Url => Config?.SupabaseUrl ?? "(sem banco)";
    public bool TemBanco => Api is not null;

    /// <summary>O banco é o do mercado (não é um projeto de teste).</summary>
    public bool BancoDoMercado => Url.Contains(ProjetoDoMercado, StringComparison.OrdinalIgnoreCase);

    /// <summary>Pode abrir caixa, vender, cancelar e fechar o dia (registros que não dá para apagar).</summary>
    public bool PodeMovimentar { get; }

    public bool Rapido { get; }
    public bool SemTelas { get; }
    public string PastaSaida { get; }
    public string Marca { get; } = $"{Prefixo} {DateTime.Now:ddMMHHmmss}";

    public Dictionary<string, UsuarioTeste> Usuarios { get; } = new()
    {
        // Mesmos usuários do database/002_usuarios_teste.sql (lista em USUARIOS_TESTE.md).
        ["gerente"] = new("marcos.gerente", "Gerente#4821"),
        ["gerente2"] = new("julia.gerente", "Gerente#7350"),
        ["operador"] = new("ana.caixa", "Caixa#1964"),
        ["operador2"] = new("pedro.caixa", "Caixa#5307"),
        ["desativado"] = new("carla.caixa", "Caixa#8142"),
    };

    public string? OrigemConfig { get; }

    private readonly Dictionary<string, Sessao> _sessoes = [];
    private readonly List<(string Descricao, Func<Task> Acao)> _limpeza = [];
    private byte[]? _preferenciasOriginais;
    private bool _tinhaPreferencias;

    private Ambiente(string[] args)
    {
        Rapido = args.Contains("--rapido");
        SemTelas = args.Contains("--sem-telas");
        var pastaExe = AppContext.BaseDirectory;
        PastaSaida = Path.Combine(pastaExe, "relatorio-testes", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        Directory.CreateDirectory(PastaSaida);

        var arquivo = Path.Combine(pastaExe, "testes.json");
        var permitirMovimento = args.Contains("--movimento");
        if (!args.Contains("--sem-banco"))
        {
            if (File.Exists(arquivo))
            {
                (Config, permitirMovimento) = LerArquivo(arquivo, permitirMovimento);
                OrigemConfig = arquivo;
            }
            else
            {
                Config = Configuracao.Carregar();
                OrigemConfig = "appsettings.json embutido no programa do mercado";
            }
        }
        if (Config is not null) Api = new SupabaseApi(Config);
        // No banco do mercado só com --movimento (ou a opção do testes.json); num projeto de teste, sempre.
        PodeMovimentar = TemBanco && (!BancoDoMercado || permitirMovimento);
    }

    public static Ambiente Criar(string[] args) => Atual = new Ambiente(args);

    private (Configuracao?, bool) LerArquivo(string arquivo, bool movimento)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(arquivo));
        var raiz = doc.RootElement;
        Configuracao? config = null;
        if (raiz.TryGetProperty("Supabase", out var s))
        {
            var url = s.GetProperty("Url").GetString() ?? "";
            var chave = s.GetProperty("AnonKey").GetString() ?? "";
            if (chave.StartsWith("sb_secret", StringComparison.OrdinalIgnoreCase) || chave.Contains("service_role", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O testes.json tem a chave SECRETA do Supabase. Use só a chave pública (sb_publishable).");
            if (!url.Contains("SEU-PROJETO"))
                config = (Configuracao)typeof(Configuracao)
                    .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, [typeof(string), typeof(string)])!
                    .Invoke([url, chave]);
        }
        if (raiz.TryGetProperty("Usuarios", out var u))
            foreach (var p in u.EnumerateObject())
                Usuarios[p.Name] = new UsuarioTeste(p.Value.GetProperty("usuario").GetString()!, p.Value.GetProperty("senha").GetString()!);
        if (raiz.TryGetProperty("PermitirMovimentoNoBancoDoMercado", out var m) && m.GetBoolean()) movimento = true;
        return (config ?? Configuracao.Carregar(), movimento);
    }

    // ------------------------------------------------------------ Usuários

    /// <summary>Entra com o usuário de teste do papel ("gerente", "operador"...) e guarda a sessão.</summary>
    public async Task<Sessao> Entrar(string papel)
    {
        if (Api is null) throw new PularTeste("Sem banco configurado.");
        if (_sessoes.TryGetValue(papel, out var s)) return s;
        var u = Usuarios[papel];
        var (token, id, status) = await TokenAsync(u.Usuario, u.Senha);
        if (token is null)
            throw new PularTeste($"Não consegui entrar com {u.Usuario} ({status}). Confira USUARIOS_TESTE.md ou o testes.json.");
        var perfil = (await Api.ListarAsync<JsonElement>($"perfis?id=eq.{id}&select=usuario,nome,perfil,ativo", token)).FirstOrDefault();
        if (perfil.ValueKind != JsonValueKind.Object) throw new PularTeste($"{u.Usuario} não tem perfil no banco.");
        s = new Sessao(id, u.Usuario, perfil.GetProperty("nome").GetString()!,
            perfil.GetProperty("perfil").GetString() == "gerente" ? Perfil.Gerente : Perfil.Operador, token);
        _sessoes[papel] = s;
        return s;
    }

    /// <summary>Pede o token direto ao Supabase Auth (sem passar pelo bloqueio local do programa).</summary>
    public async Task<(string? Token, Guid Id, HttpStatusCode Status)> TokenAsync(string usuario, string senha)
    {
        var resposta = await Api!.Http.PostAsJsonAsync("auth/v1/token?grant_type=password",
            new { email = AuthService.EmailDoUsuario(usuario), password = senha });
        if (!resposta.IsSuccessStatusCode) return (null, Guid.Empty, resposta.StatusCode);
        var json = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("access_token").GetString(), Guid.Parse(json.GetProperty("user").GetProperty("id").GetString()!), resposta.StatusCode);
    }

    /// <summary>Requisição crua (para testar o que o programa não faz: sem token, token falso, consultas diretas).</summary>
    public async Task<(HttpStatusCode Status, string Corpo, HttpResponseMessage Resposta)> Http(HttpMethod metodo, string caminho,
        string? token, object? corpo = null, string? prefer = null)
    {
        using var req = new HttpRequestMessage(metodo, caminho);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (corpo is not null) req.Content = JsonContent.Create(corpo);
        if (prefer is not null) req.Headers.Add("Prefer", prefer);
        var r = await Api!.Http.SendAsync(req);
        return (r.StatusCode, await r.Content.ReadAsStringAsync(), r);
    }

    /// <summary>Quantas linhas a consulta tem no banco (sem trazer as linhas): Prefer count=exact.</summary>
    public async Task<long> ContarAsync(string tabelaEFiltro, string token)
    {
        var sep = tabelaEFiltro.Contains('?') ? "&" : "?";
        var (status, corpo, r) = await Http(HttpMethod.Head, $"rest/v1/{tabelaEFiltro}{sep}select=*", token, prefer: "count=exact");
        if (status is not (HttpStatusCode.OK or HttpStatusCode.PartialContent)) throw new FalhaTeste($"Contar {tabelaEFiltro}: {(int)status} {corpo}");
        var faixa = r.Content.Headers.TryGetValues("Content-Range", out var v) ? v.First() : r.Headers.TryGetValues("Content-Range", out var v2) ? v2.First() : "*/0";
        return long.Parse(faixa.Split('/').Last());
    }

    // ------------------------------------------------------------ Limpeza

    /// <summary>Guarda o que apagar no fim (roda de trás para a frente, mesmo se o teste falhar).</summary>
    public void ApagarNoFim(string descricao, Func<Task> acao) => _limpeza.Add((descricao, acao));

    public async Task<List<string>> LimparAsync()
    {
        var erros = new List<string>();
        for (var i = _limpeza.Count - 1; i >= 0; i--)
        {
            try { await _limpeza[i].Acao(); }
            catch (Exception ex) when (ex is ConexaoException or HttpRequestException) { }
            catch (Exception ex) { erros.Add($"{_limpeza[i].Descricao}: {ex.Message}"); }
        }
        _limpeza.Clear();
        // Sobras de rodadas que pararam no meio (fecharam a janela): tudo com o prefixo
        if (TemBanco && _sessoes.TryGetValue("gerente", out var g))
            erros.AddRange(await ApagarSobrasAsync(g.Token));
        return erros;
    }

    /// <summary>Apaga tudo o que tem o prefixo dos testes (produtos, fornecedores, pedidos, funcionários, regras).</summary>
    public async Task<List<string>> ApagarSobrasAsync(string token)
    {
        var erros = new List<string>();
        var filtro = Uri.EscapeDataString(Prefixo + "*");
        async Task Apagar(string caminho)
        {
            var (status, corpo, _) = await Http(HttpMethod.Delete, $"rest/v1/{caminho}", token);
            if ((int)status >= 400 && (int)status != 404) erros.Add($"Apagar {caminho.Split('?')[0]}: {(int)status} {corpo}");
        }
        try
        {
            // Pedidos dos fornecedores de teste (o fornecedor não sai enquanto tiver pedido)
            var fornecedores = await Api!.ListarAsync<JsonElement>($"fornecedores?nome=like.{filtro}&select=id", token);
            if (fornecedores.Count > 0)
            {
                var ids = string.Join(",", fornecedores.Select(f => f.GetProperty("id").GetInt64()));
                await Apagar($"pedidos?fornecedor_id=in.({ids})");
                await Apagar($"fornecedores?id=in.({ids})");
            }
            await Apagar($"promocoes_validade?categoria=like.{filtro}");
            await Apagar($"produtos?nome=like.{filtro}");
            // Funcionários de teste: remover_funcionario apaga o usuário do Auth (ou desativa se já abriu caixa)
            var funcionarios = await Api.ChamarAsync<List<Funcionario>>("listar_funcionarios", new { }, token) ?? [];
            foreach (var f in funcionarios.Where(f => f.Usuario.StartsWith("teste.auto", StringComparison.Ordinal) && (f.Ativo || !f.TemVendas)))
            {
                try { await Api.ChamarAsync<string>("remover_funcionario", new { p_id = f.Id }, token); }
                catch (ConexaoException ex) { erros.Add($"Remover {f.Usuario}: {ex.Message}"); }
            }
        }
        catch (Exception ex) when (ex is ConexaoException or HttpRequestException)
        {
            erros.Add("Limpeza das sobras: " + ex.Message);
        }
        return erros;
    }

    // ------------------------------------------------------------ Preferências locais do programa

    /// <summary>
    /// O teste do login usa o AuthService do programa, que grava bloqueios em %LocalAppData%\Mercado\preferencias.json.
    /// Guarda o arquivo antes e devolve igual no fim, para não travar ninguém neste computador.
    /// </summary>
    public void GuardarPreferencias()
    {
        var arq = ArquivoPreferencias;
        _tinhaPreferencias = File.Exists(arq);
        if (_tinhaPreferencias) _preferenciasOriginais = File.ReadAllBytes(arq);
    }

    public void DevolverPreferencias()
    {
        var arq = ArquivoPreferencias;
        try
        {
            if (_tinhaPreferencias && _preferenciasOriginais is not null) File.WriteAllBytes(arq, _preferenciasOriginais);
            else if (File.Exists(arq)) File.Delete(arq);
        }
        catch (IOException) { }
    }

    private static string ArquivoPreferencias => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mercado", "preferencias.json");

    // ------------------------------------------------------------ Notas do teste em andamento

    [ThreadStatic] private static List<string>? _notas;
    [ThreadStatic] private static List<string>? _avisos;

    public static void ComecarTeste()
    {
        _notas = [];
        _avisos = [];
    }

    /// <summary>Detalhe que vai para o relatório (tempo medido, mensagem do sistema...).</summary>
    public static void Nota(string texto) => (_notas ??= []).Add(texto);

    /// <summary>O teste continua, mas termina como aviso.</summary>
    public static void AvisoPendente(string texto) => (_avisos ??= []).Add(texto);

    public static (List<string> Notas, List<string> Avisos) TerminarTeste() => (_notas ?? [], _avisos ?? []);
}

/// <summary>Leitura simples de JSON solto (respostas das funções que não têm classe no programa).</summary>
public static class Json
{
    public static decimal Dec(this JsonElement e, string campo) =>
        e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0;

    public static long Long(this JsonElement e, string campo) =>
        e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    public static string? Str(this JsonElement e, string campo) =>
        e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static readonly JsonSerializerOptions Opcoes = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}
