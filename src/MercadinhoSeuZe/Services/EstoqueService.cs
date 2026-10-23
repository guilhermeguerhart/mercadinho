using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

/// <summary>Campos do formulário de produto.</summary>
public sealed record DadosProduto(string Nome, string Categoria, decimal Preco, decimal Minimo, decimal? Aviso,
    string? CodigoBarrasCaixa, decimal UnidadesPorCaixa, bool PorPeso, int? DiasValidade, long? FornecedorId = null,
    int? PrazoEntregaDias = null, bool PedirAntes = false, decimal? PrecoCusto = null);

/// <summary>Produtos e lotes do estoque no Supabase (tabelas do script database/003_semana3_estoque.sql).</summary>
public sealed class EstoqueService(SupabaseApi api, Sessao sessao)
{
    private const string Campos = "id,nome,categoria,preco,estoque_minimo,codigo_barras_caixa,unidades_por_caixa,estoque_aviso,unidade_medida,dias_validade";
    private const string CamposLote = "id,produto_id,quantidade_inicial,quantidade_restante,data_compra,validade";

    /// <summary>false enquanto o script 004 (fornecedores) não foi rodado: o estoque funciona sem o fornecedor do produto.</summary>
    public bool TemFornecedores { get; private set; } = true;

    /// <summary>false enquanto o script 005 (custo, vários fornecedores) não foi rodado.</summary>
    public bool TemAjustes { get; private set; } = true;

    /// <summary>Lê os produtos com os lotes, usando as colunas que o banco já tem (scripts 003, 004 e 005).</summary>
    public async Task<List<Produto>> ListarAsync()
    {
        if (TemAjustes)
        {
            try
            {
                return await api.ListarAsync<Produto>(
                    $"produtos?select={Campos},fornecedor_id,prazo_entrega_dias,pedir_antes,preco_custo," +
                    $"lotes({CamposLote},preco_custo),produto_fornecedores(fornecedor_id,ultimo_custo,ultima_compra)", sessao.Token);
            }
            catch (BancoDesatualizadoException)
            {
                TemAjustes = false;
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
}
