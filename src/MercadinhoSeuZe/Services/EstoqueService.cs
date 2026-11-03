using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

/// <summary>Campos do formulário de produto.</summary>
public sealed record DadosProduto(string Nome, string Categoria, decimal Preco, decimal Minimo, decimal? Aviso,
    string? CodigoBarrasCaixa, decimal UnidadesPorCaixa, bool PorPeso, int? DiasValidade, long? FornecedorId = null,
    int? PrazoEntregaDias = null, bool PedirAntes = false, decimal? PrecoCusto = null, int? CodigoBalanca = null,
    bool SemPromocaoValidade = false);

/// <summary>Produtos e lotes do estoque no Supabase (tabelas do script database/003_semana3_estoque.sql).</summary>
public sealed class EstoqueService(SupabaseApi api, Sessao sessao)
{
    private const string Campos = "id,nome,categoria,preco,estoque_minimo,codigo_barras_caixa,unidades_por_caixa,estoque_aviso,unidade_medida,dias_validade";
    private const string CamposLote = "id,produto_id,quantidade_inicial,quantidade_restante,data_compra,validade";

    /// <summary>false enquanto o script 004 (fornecedores) não foi rodado: o estoque funciona sem o fornecedor do produto.</summary>
    public bool TemFornecedores { get; private set; } = true;

    /// <summary>false enquanto o script 005 (custo, vários fornecedores) não foi rodado.</summary>
    public bool TemAjustes { get; private set; } = true;

    /// <summary>false enquanto o script 006 (caixa) não foi rodado: sem o código da balança e sem reservas.</summary>
    public bool TemCaixa { get; private set; } = true;

    /// <summary>false enquanto o script 008 (promoções e fechamento do caixa) não foi rodado.</summary>
    public bool TemPromocoes { get; private set; } = true;

    /// <summary>Lê os produtos com os lotes, usando as colunas que o banco já tem (scripts 003, 004 e 005).</summary>
    public async Task<List<Produto>> ListarAsync()
    {
        if (TemAjustes)
        {
            if (TemCaixa && TemPromocoes)
            {
                try
                {
                    return await api.ListarAsync<Produto>(
                        $"produtos?select={Campos},fornecedor_id,prazo_entrega_dias,pedir_antes,preco_custo,codigo_balanca,sem_promocao_validade," +
                        $"lotes({CamposLote},preco_custo),produto_fornecedores(fornecedor_id,ultimo_custo,ultima_compra)", sessao.Token);
                }
                catch (BancoDesatualizadoException)
                {
                    TemPromocoes = false;
                }
            }
            if (TemCaixa)
            {
                try
                {
                    return await api.ListarAsync<Produto>(
                        $"produtos?select={Campos},fornecedor_id,prazo_entrega_dias,pedir_antes,preco_custo,codigo_balanca," +
                        $"lotes({CamposLote},preco_custo),produto_fornecedores(fornecedor_id,ultimo_custo,ultima_compra)", sessao.Token);
                }
                catch (BancoDesatualizadoException)
                {
                    TemCaixa = false;
                }
            }
            try
            {
                return await api.ListarAsync<Produto>(
                    $"produtos?select={Campos},fornecedor_id,prazo_entrega_dias,pedir_antes,preco_custo," +
                    $"lotes({CamposLote},preco_custo),produto_fornecedores(fornecedor_id,ultimo_custo,ultima_compra)", sessao.Token);
            }
            catch (BancoDesatualizadoException)
            {
                TemAjustes = false;
                TemCaixa = false;
                TemPromocoes = false;
            }
        }
        if (TemFornecedores)
        {
            try
            {
                return await api.ListarAsync<Produto>($"produtos?select={Campos},fornecedor_id,prazo_entrega_dias,pedir_antes,lotes({CamposLote})", sessao.Token);
            }
            catch (BancoDesatualizadoException)
            {
                TemFornecedores = false;
            }
        }
        TemCaixa = false;
        TemPromocoes = false;
        return await api.ListarAsync<Produto>($"produtos?select={Campos},lotes({CamposLote})", sessao.Token);
    }

    /// <summary>Cadastra o produto e, se houver quantidade, o primeiro lote dele.</summary>
    public async Task<long> CriarProdutoAsync(DadosProduto dados, decimal quantidade, DateOnly compra, DateOnly? validade)
    {
        var produto = await api.InserirAsync<Produto>("produtos", Linha(dados), sessao.Token);
        if (quantidade <= 0) return produto.Id;
        try
        {
            await CriarLoteAsync(produto.Id, quantidade, compra, validade);
        }
        catch (ConexaoException)
        {
            // Sem o lote, desfaz o cadastro para a pessoa poder tentar de novo com o mesmo nome.
            try { await ExcluirProdutoAsync(produto.Id); } catch (ConexaoException) { }
            throw;
        }
        return produto.Id;
    }

    /// <summary>Outros fornecedores do produto (além do principal), pela função definir_fornecedores_produto.</summary>
    public Task DefinirFornecedoresAsync(long produtoId, IEnumerable<long> fornecedores) =>
        api.ChamarAsync<object>("definir_fornecedores_produto", new { p_produto_id = produtoId, p_fornecedores = fornecedores.ToArray() }, sessao.Token);

    public Task AtualizarProdutoAsync(long id, DadosProduto dados) =>
        api.AtualizarAsync("produtos", $"id=eq.{id}", Linha(dados), sessao.Token);

    private Dictionary<string, object?> Linha(DadosProduto d)
    {
        var linha = new Dictionary<string, object?>
        {
            ["nome"] = d.Nome,
            ["categoria"] = d.Categoria,
            ["preco"] = d.Preco,
            ["estoque_minimo"] = d.Minimo,
            ["estoque_aviso"] = d.Aviso,
            ["codigo_barras_caixa"] = d.CodigoBarrasCaixa,
            ["unidades_por_caixa"] = d.UnidadesPorCaixa,
            ["unidade_medida"] = d.PorPeso ? "kg" : "un",
            ["dias_validade"] = d.DiasValidade
        };
        if (TemFornecedores)
        {
            linha["fornecedor_id"] = d.FornecedorId;
            linha["prazo_entrega_dias"] = d.PrazoEntregaDias;
            linha["pedir_antes"] = d.PedirAntes;
        }
        if (TemAjustes)
        {
            linha["preco_custo"] = d.PrecoCusto;
        }
        if (TemCaixa)
        {
            linha["codigo_balanca"] = d.CodigoBalanca;
        }
        if (TemPromocoes)
        {
            linha["sem_promocao_validade"] = d.SemPromocaoValidade;
        }
        return linha;
    }

    /// <summary>Os lotes do produto são excluídos junto (on delete cascade).</summary>
    public Task ExcluirProdutoAsync(long id) => api.ExcluirAsync("produtos", $"id=eq.{id}", sessao.Token);

    public Task CriarLoteAsync(long produtoId, decimal quantidade, DateOnly compra, DateOnly? validade) =>
        api.InserirAsync<Lote>("lotes", new
        {
            produto_id = produtoId,
            quantidade_inicial = quantidade,
            quantidade_restante = quantidade,
            data_compra = compra,
            validade
        }, sessao.Token);

    public Task AtualizarLoteAsync(long id, decimal inicial, decimal restante, DateOnly compra, DateOnly? validade) =>
        api.AtualizarAsync("lotes", $"id=eq.{id}", new
        {
            quantidade_inicial = inicial,
            quantidade_restante = restante,
            data_compra = compra,
            validade
        }, sessao.Token);

    public Task ExcluirLoteAsync(long id) => api.ExcluirAsync("lotes", $"id=eq.{id}", sessao.Token);

    /// <summary>
    /// Quanto de cada produto está reservado em vendas abertas ou em espera de hoje (semana 5): continua nos lotes, mas já não
    /// pode ser vendido. Vazio sem o script 006.
    /// </summary>
    public async Task<Dictionary<long, decimal>> ReservadosAsync()
    {
        if (!TemCaixa) return [];
        try
        {
            // Script 009: o operador não lê as vendas dos outros caixas; a função devolve só produto e quantidade
            try
            {
                var reservas = await api.ChamarAsync<List<Reserva>>("reservados_hoje", new { }, sessao.Token) ?? [];
                return reservas.Where(l => l.ProdutoId is not null).ToDictionary(l => l.ProdutoId!.Value, l => l.Quantidade);
            }
            catch (TabelaInexistenteException)
            {
                // Sem o script 009: lê pelas vendas, como antes
            }
            var hoje = DateTime.Today.ToString("yyyy-MM-dd");
            var linhas = await api.ListarAsync<Reserva>(
                $"venda_itens?select=produto_id,quantidade,vendas!inner(situacao,dia)&vendas.situacao=in.(aberta,espera)&vendas.dia=eq.{hoje}", sessao.Token);
            return linhas.Where(l => l.ProdutoId is not null).GroupBy(l => l.ProdutoId!.Value).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantidade));
        }
        catch (ConexaoException)
        {
            return [];
        }
    }

    // ------------------------------------------------------------ Promoções (script 008)

    /// <summary>
    /// Promoções por produto (ativas, agendadas e as encerradas dos últimos 60 dias) e regras de validade por categoria.
    /// Sem o script 008, listas vazias e TemPromocoes = false.
    /// </summary>
    public async Task<(List<Promocao> Promocoes, List<RegraValidade> Regras)> PromocoesAsync()
    {
        if (!TemPromocoes) return ([], []);
        try
        {
            var desde = DateTime.Today.AddDays(-60).ToString("yyyy-MM-dd");
            var promocoes = api.ListarAsync<Promocao>($"promocoes?select=*&or=(fim.is.null,fim.gte.{desde})&order=inicio.desc,id.desc", sessao.Token);
            var regras = api.ListarAsync<RegraValidade>("promocoes_validade?select=*&order=categoria", sessao.Token);
            await Task.WhenAll(promocoes, regras);
            return (promocoes.Result, regras.Result);
        }
        catch (Exception ex) when (ex is TabelaInexistenteException or BancoDesatualizadoException)
        {
            TemPromocoes = false;
            return ([], []);
        }
    }

    /// <summary>Lê produtos e promoções e liga uma coisa à outra (Produto.PromocaoAtiva e Produto.RegraValidade).</summary>
    public async Task<(List<Produto> Produtos, List<Promocao> Promocoes, List<RegraValidade> Regras)> ListarComPromocoesAsync()
    {
        var produtos = await ListarAsync();
        var (promocoes, regras) = await PromocoesAsync();
        Promocoes.Aplicar(produtos, promocoes, regras);
        return (produtos, promocoes, regras);
    }

    public Task CriarPromocaoAsync(long produtoId, string tipo, decimal valor, DateOnly inicio, DateOnly? fim) =>
        api.InserirAsync<Promocao>("promocoes", new { produto_id = produtoId, tipo, valor, inicio, fim, criada_por = sessao.Nome }, sessao.Token);

    public Task AtualizarPromocaoAsync(long id, string tipo, decimal valor, DateOnly inicio, DateOnly? fim) =>
        api.AtualizarAsync("promocoes", $"id=eq.{id}", new { tipo, valor, inicio, fim }, sessao.Token);

    /// <summary>"Encerrar agora": a promoção para de valer no caixa e fica no histórico.</summary>
    public Task EncerrarPromocaoAsync(long id) =>
        api.AtualizarAsync("promocoes", $"id=eq.{id}", new { encerrada_em = DateTimeOffset.UtcNow }, sessao.Token);

    public Task ExcluirPromocaoAsync(long id) => api.ExcluirAsync("promocoes", $"id=eq.{id}", sessao.Token);

    public Task CriarRegraValidadeAsync(string categoria, int diasAntes, decimal minimo, decimal maximo, bool ativa) =>
        api.InserirAsync<RegraValidade>("promocoes_validade",
            new { categoria, dias_antes = diasAntes, desconto_min = minimo, desconto_max = maximo, ativa }, sessao.Token);

    public Task AtualizarRegraValidadeAsync(long id, string categoria, int diasAntes, decimal minimo, decimal maximo, bool ativa) =>
        api.AtualizarAsync("promocoes_validade", $"id=eq.{id}",
            new { categoria, dias_antes = diasAntes, desconto_min = minimo, desconto_max = maximo, ativa }, sessao.Token);

    public Task ExcluirRegraValidadeAsync(long id) => api.ExcluirAsync("promocoes_validade", $"id=eq.{id}", sessao.Token);

    private sealed record Reserva(
        [property: System.Text.Json.Serialization.JsonPropertyName("produto_id")] long? ProdutoId,
        [property: System.Text.Json.Serialization.JsonPropertyName("quantidade")] decimal Quantidade);
}
