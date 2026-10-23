using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

/// <summary>Campos do formulário de fornecedor.</summary>
public sealed record DadosFornecedor(string Nome, string Telefone, string? Whatsapp, string? Email, string? Contato,
    string Categorias, int? PrazoEntregaDias, string? ModeloMensagem, string? Observacao, string EnvioPreferido = "whatsapp",
    string? DiasEntrega = null, decimal? PedidoMinimo = null);

/// <summary>Item do pedido novo: produto, quantidade e custo (opcional).</summary>
public sealed record ItemNovoPedido(long ProdutoId, decimal Quantidade, decimal? Custo = null);

/// <summary>O que chegou de cada item na entrega, a validade e o custo do lote e o motivo se veio diferente.</summary>
public sealed record ItemEntrega(long ItemId, decimal Recebido, DateOnly? Validade, decimal? Custo = null, string? Motivo = null);

/// <summary>
/// Fornecedores e pedidos no Supabase (scripts database/004_semana4_fornecedores.sql e 005_semana4_ajustes.sql).
/// Sem o 005 rodado, funciona como antes (sem custo, nota, entrega parcial e dias de entrega).
/// </summary>
public sealed class FornecedorService(SupabaseApi api, Sessao sessao)
{
    private const string CamposPedido004 =
        "id,fornecedor_id,data_pedido,previsao_entrega,situacao,entregue_em,observacao," +
        "itens:pedido_itens(id,produto_id,nome_produto,unidade_medida,quantidade,quantidade_recebida)";
    private const string CamposPedido005 =
        "id,fornecedor_id,data_pedido,previsao_entrega,situacao,entregue_em,observacao,numero_nota,pedido_origem_id," +
        "itens:pedido_itens(id,produto_id,nome_produto,unidade_medida,quantidade,quantidade_recebida,preco_custo,motivo_diferenca)";
    private const string CamposFornecedor =
        "id,nome,telefone,whatsapp,email,contato,categorias,prazo_entrega_dias,modelo_mensagem,observacao,ativo,envio_preferido";

    /// <summary>Nome de quem está usando o programa (vai na mensagem de pedido).</summary>
    public string Usuario => sessao.Nome;

    /// <summary>false enquanto o script 005 não foi rodado.</summary>
    public bool TemAjustes { get; private set; } = true;

    /// <summary>Tenta a consulta com as colunas do 005; se o banco ainda não tem, usa a do 004.</summary>
    private async Task<List<T>> ListarComAjustesAsync<T>(string com005, string sem005)
    {
        if (TemAjustes)
        {
            try { return await api.ListarAsync<T>(com005, sessao.Token); }
            catch (BancoDesatualizadoException) { TemAjustes = false; }
        }
        return await api.ListarAsync<T>(sem005, sessao.Token);
    }

    public Task<List<Fornecedor>> ListarAsync() =>
        ListarComAjustesAsync<Fornecedor>($"fornecedores?select={CamposFornecedor},dias_entrega,pedido_minimo&order=nome",
            $"fornecedores?select={CamposFornecedor}&order=nome");

    public Task<List<Pedido>> ListarPedidosAsync() =>
        ListarComAjustesAsync<Pedido>($"pedidos?select={CamposPedido005}&order=data_pedido.desc,id.desc",
            $"pedidos?select={CamposPedido004}&order=data_pedido.desc,id.desc");

    public Task<List<Pedido>> ListarPendentesAsync() =>
        ListarComAjustesAsync<Pedido>($"pedidos?select={CamposPedido005}&situacao=eq.pendente&order=previsao_entrega",
            $"pedidos?select={CamposPedido004}&situacao=eq.pendente&order=previsao_entrega");

    public Task CriarAsync(DadosFornecedor dados) => api.InserirAsync<Fornecedor>("fornecedores", Linha(dados), sessao.Token);

    public Task AtualizarAsync(long id, DadosFornecedor dados) =>
        api.AtualizarAsync("fornecedores", $"id=eq.{id}", Linha(dados), sessao.Token);

    private Dictionary<string, object?> Linha(DadosFornecedor d)
    {
        var linha = new Dictionary<string, object?>
        {
            ["nome"] = d.Nome,
            ["telefone"] = d.Telefone,
            ["whatsapp"] = d.Whatsapp,
            ["email"] = d.Email,
            ["contato"] = d.Contato,
            ["categorias"] = d.Categorias,
            ["prazo_entrega_dias"] = d.PrazoEntregaDias,
            ["modelo_mensagem"] = d.ModeloMensagem,
            ["observacao"] = d.Observacao,
            ["envio_preferido"] = d.EnvioPreferido
        };
        if (TemAjustes)
        {
            linha["dias_entrega"] = d.DiasEntrega;
            linha["pedido_minimo"] = d.PedidoMinimo;
        }
        return linha;
    }

    /// <summary>Desativado sai da lista principal, mas o histórico de pedidos continua.</summary>
    public Task AtivarAsync(long id, bool ativo) =>
        api.AtualizarAsync("fornecedores", $"id=eq.{id}", new { ativo }, sessao.Token);

    /// <summary>Só para fornecedor sem pedidos (o banco não deixa apagar quem tem histórico).</summary>
    public Task ExcluirAsync(long id) => api.ExcluirAsync("fornecedores", $"id=eq.{id}", sessao.Token);

    private static IEnumerable<object> Itens(IEnumerable<ItemNovoPedido> itens) =>
        itens.Select(i => new { produto_id = i.ProdutoId, quantidade = i.Quantidade, custo = i.Custo });

    /// <summary>Cria o pedido com todos os itens de uma vez (função criar_pedido do banco). Devolve o id.</summary>
    public async Task<long> CriarPedidoAsync(long fornecedorId, DateOnly? previsao, string? observacao, IEnumerable<ItemNovoPedido> itens) =>
        await api.ChamarAsync<long>("criar_pedido", new
        {
            p_fornecedor_id = fornecedorId,
            p_previsao = previsao,
            p_observacao = observacao,
            p_itens = Itens(itens)
        }, sessao.Token);

    /// <summary>Troca previsão, observação e itens de um pedido pendente (função editar_pedido, script 005).</summary>
    public Task EditarPedidoAsync(long pedidoId, DateOnly? previsao, string? observacao, IEnumerable<ItemNovoPedido> itens) =>
        api.ChamarAsync<object>("editar_pedido", new
        {
            p_pedido_id = pedidoId,
            p_previsao = previsao,
            p_observacao = observacao,
            p_itens = Itens(itens)
        }, sessao.Token);

    /// <summary>
    /// Guarda o que chegou, cria os lotes no estoque (com o custo) e marca o pedido como entregue, tudo de uma vez.
    /// Com restoPendente, o que faltou vira um pedido novo. Sem o 005, usa a função antiga (sem nota, custo e resto).
    /// </summary>
    public async Task RegistrarEntregaAsync(long pedidoId, DateOnly entregueEm, IEnumerable<ItemEntrega> itens,
        string? numeroNota = null, bool restoPendente = false, DateOnly? previsaoResto = null)
    {
        if (!TemAjustes)
        {
            await api.ChamarAsync<object>("registrar_entrega", new
            {
                p_pedido_id = pedidoId,
                p_entregue_em = entregueEm,
                p_itens = itens.Select(i => new { item_id = i.ItemId, recebido = i.Recebido, validade = i.Validade })
            }, sessao.Token);
            return;
        }
        await api.ChamarAsync<long?>("registrar_recebimento", new
        {
            p_pedido_id = pedidoId,
            p_entregue_em = entregueEm,
            p_numero_nota = numeroNota,
            p_itens = itens.Select(i => new { item_id = i.ItemId, recebido = i.Recebido, validade = i.Validade, custo = i.Custo, motivo = i.Motivo }),
            p_resto_pendente = restoPendente,
            p_previsao_resto = previsaoResto
        }, sessao.Token);
    }

    public Task CancelarPedidoAsync(long id) =>
        api.AtualizarAsync("pedidos", $"id=eq.{id}&situacao=eq.pendente", new { situacao = "cancelado" }, sessao.Token);
}
