using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

/// <summary>
/// Previsão de compras (semana 7, scripts database/016_semana7_previsao.sql e 019_semana7_tela_previsao.sql).
/// O cálculo pesado roda de madrugada no banco (previsao_calcular, pg_cron); aqui o programa só lê o que ficou pronto.
/// Sem o 019 rodado as funções não existem: FuncaoInexistenteException (a tela explica qual script rodar).
/// </summary>
public sealed class PrevisaoService(SupabaseApi api, Sessao sessao)
{
    public async Task<PrevisaoTela> TelaAsync() =>
        await api.ChamarAsync<PrevisaoTela>("previsao_tela", new { }, sessao.Token)
        ?? throw new ConexaoException("Resposta inválida do servidor.");

    /// <summary>Pedido sugerido de cada fornecedor ativo (o mesmo que o simulador usa).</summary>
    public async Task<List<PedidoSugerido>> SugeridosAsync() =>
        await api.ChamarAsync<List<PedidoSugerido>>("pedido_sugerido", new { }, sessao.Token) ?? [];

    public Task<PrevisaoProduto?> ProdutoAsync(long produtoId) =>
        api.ChamarAsync<PrevisaoProduto>("previsao_produto", new { p_produto = produtoId }, sessao.Token);

    public async Task<List<AlertaPrevisao>> AlertasAsync() =>
        await api.ChamarAsync<List<AlertaPrevisao>>("previsao_alertas", new { }, sessao.Token) ?? [];

    /// <summary>
    /// Laboratório (versão de desenvolvimento, script 020): o teste do último ano por dia, semana ou mês.
    /// escopo: "todos", "categoria", "produto" ou "loja"; agrupar: "dia", "semana" ou "mes".
    /// </summary>
    public async Task<LaboratorioPrevisao> LaboratorioAsync(string escopo, string? categoria, long? produto, string agrupar) =>
        await api.ChamarAsync<LaboratorioPrevisao>("previsao_laboratorio",
            new { p_escopo = escopo, p_categoria = categoria, p_produto = produto, p_agrupar = agrupar }, sessao.Token)
        ?? throw new ConexaoException("Resposta inválida do servidor.");
}
