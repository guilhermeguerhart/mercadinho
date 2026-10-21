using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

/// <summary>Campos do formulário de fornecedor.</summary>
public sealed record DadosFornecedor(string Nome, string Telefone, string? Whatsapp, string? Email, string? Contato,
    string Categorias, int? PrazoEntregaDias, string? ModeloMensagem, string? Observacao, string EnvioPreferido = "whatsapp");

/// <summary>Item do pedido novo: produto e quantidade.</summary>
public sealed record ItemNovoPedido(long ProdutoId, decimal Quantidade);

/// <summary>O que chegou de cada item na entrega e a validade do lote que será criado.</summary>
public sealed record ItemEntrega(long ItemId, decimal Recebido, DateOnly? Validade);

/// <summary>Fornecedores e pedidos no Supabase (tabelas do script database/004_semana4_fornecedores.sql).</summary>
public sealed class FornecedorService(SupabaseApi api, Sessao sessao)
{
    private const string CamposPedido =
        "id,fornecedor_id,data_pedido,previsao_entrega,situacao,entregue_em,observacao," +
        "itens:pedido_itens(id,produto_id,nome_produto,unidade_medida,quantidade,quantidade_recebida)";

    /// <summary>Nome de quem está usando o programa (vai na mensagem de pedido).</summary>
    public string Usuario => sessao.Nome;

    public Task<List<Fornecedor>> ListarAsync() =>
        api.ListarAsync<Fornecedor>("fornecedores?select=id,nome,telefone,whatsapp,email,contato,categorias,prazo_entrega_dias," +
                                    "modelo_mensagem,observacao,ativo,envio_preferido&order=nome", sessao.Token);

    public Task<List<Pedido>> ListarPedidosAsync() =>
        api.ListarAsync<Pedido>($"pedidos?select={CamposPedido}&order=data_pedido.desc,id.desc", sessao.Token);

    public Task<List<Pedido>> ListarPendentesAsync() =>
        api.ListarAsync<Pedido>($"pedidos?select={CamposPedido}&situacao=eq.pendente&order=previsao_entrega", sessao.Token);

    public Task CriarAsync(DadosFornecedor dados) => api.InserirAsync<Fornecedor>("fornecedores", Linha(dados), sessao.Token);

    public Task AtualizarAsync(long id, DadosFornecedor dados) =>
        api.AtualizarAsync("fornecedores", $"id=eq.{id}", Linha(dados), sessao.Token);

    private static object Linha(DadosFornecedor d) => new
    {
        nome = d.Nome,
        telefone = d.Telefone,
        whatsapp = d.Whatsapp,
        email = d.Email,
        contato = d.Contato,
        categorias = d.Categorias,
        prazo_entrega_dias = d.PrazoEntregaDias,
        modelo_mensagem = d.ModeloMensagem,
        observacao = d.Observacao,
        envio_preferido = d.EnvioPreferido
    };

    /// <summary>Desativado sai da lista principal, mas o histórico de pedidos continua.</summary>
    public Task AtivarAsync(long id, bool ativo) =>
        api.AtualizarAsync("fornecedores", $"id=eq.{id}", new { ativo }, sessao.Token);

    /// <summary>Só para fornecedor sem pedidos (o banco não deixa apagar quem tem histórico).</summary>
    public Task ExcluirAsync(long id) => api.ExcluirAsync("fornecedores", $"id=eq.{id}", sessao.Token);

    /// <summary>Cria o pedido com todos os itens de uma vez (função criar_pedido do banco). Devolve o id.</summary>
    public async Task<long> CriarPedidoAsync(long fornecedorId, DateOnly? previsao, string? observacao, IEnumerable<ItemNovoPedido> itens) =>
        await api.ChamarAsync<long>("criar_pedido", new
        {
            p_fornecedor_id = fornecedorId,
            p_previsao = previsao,
            p_observacao = observacao,
            p_itens = itens.Select(i => new { produto_id = i.ProdutoId, quantidade = i.Quantidade })
        }, sessao.Token);

    /// <summary>Guarda o que chegou, cria os lotes no estoque e marca o pedido como entregue (tudo ou nada).</summary>
    public Task RegistrarEntregaAsync(long pedidoId, DateOnly entregueEm, IEnumerable<ItemEntrega> itens) =>
        api.ChamarAsync<object>("registrar_entrega", new
        {
            p_pedido_id = pedidoId,
            p_entregue_em = entregueEm,
            p_itens = itens.Select(i => new { item_id = i.ItemId, recebido = i.Recebido, validade = i.Validade })
        }, sessao.Token);

    public Task CancelarPedidoAsync(long id) =>
        api.AtualizarAsync("pedidos", $"id=eq.{id}&situacao=eq.pendente", new { situacao = "cancelado" }, sessao.Token);
}
