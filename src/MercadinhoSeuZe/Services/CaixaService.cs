using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

/// <summary>
/// Caixa e vendas no Supabase (scripts database/006_semana5_caixa.sql, 007_semana5_caixa_padrao.sql e
/// 008_promocoes_fechamento.sql). Abrir e fechar o caixa, reservar, pagar, cancelar, desconto, venda em espera, sangria
/// e suprimento passam pelas funções do banco, que conferem estoque, validade, promoções, perfil e a autorização do
/// gerente de uma vez só.
/// </summary>
public sealed class CaixaService(SupabaseApi api, Sessao sessao)
{
    private const string CamposVenda = "id,caixa_id,operador_id,operador_nome,situacao,forma_pagamento,total,recebido,troco,criada_em,paga_em," +
                                       "cancelada_por,cpf_nota,desconto,desconto_autorizado_por";
    private const string CamposItemBase = "id,produto_id,nome_produto,unidade_medida,quantidade,preco_unitario,subtotal";
    private string CamposItem => TemPromocoes ? CamposItemBase + ",subtotal_normal,desconto_promocao,promocao" : CamposItemBase;
    private const string CamposPagamento = "forma,valor,recebido,troco,bandeira,nsu,autorizacao,parcelas,final_cartao";

    public Sessao Sessao => sessao;

    /// <summary>false enquanto o script 008 (promoções, sangria e fechamento) não foi rodado.</summary>
    public bool TemPromocoes { get; private set; } = true;

    /// <summary>Lê com as colunas do 008; sem elas, lê de novo sem (o caixa funciona como antes).</summary>
    private async Task<List<T>> ListarAsync<T>(Func<string> consulta)
    {
        if (TemPromocoes)
        {
            try
            {
                return await api.ListarAsync<T>(consulta(), sessao.Token);
            }
            catch (BancoDesatualizadoException)
            {
                TemPromocoes = false;
            }
        }
        return await api.ListarAsync<T>(consulta(), sessao.Token);
    }

    private static string Hoje => DateTime.Today.ToString("yyyy-MM-dd");

    /// <summary>O caixa de hoje de quem está logado (null = ainda não abriu).</summary>
    public async Task<Caixa?> MeuCaixaAsync() =>
        (await api.ListarAsync<Caixa>($"caixas?operador_id=eq.{sessao.Id}&dia=eq.{Hoje}&select=*", sessao.Token)).FirstOrDefault();

    /// <summary>Caixas abertos hoje (todos, para o Painel).</summary>
    public Task<List<Caixa>> CaixasDeHojeAsync() =>
        api.ListarAsync<Caixa>($"caixas?dia=eq.{Hoje}&select=*&order=aberto_em", sessao.Token);

    public async Task<long> AbrirAsync(decimal troco) =>
        await api.ChamarAsync<long>("abrir_caixa", new { p_troco = troco }, sessao.Token);

    /// <summary>A venda aberta do caixa (a que ficou no meio, se o programa foi fechado), ou null.</summary>
    public async Task<Venda?> VendaAbertaAsync(long caixaId) =>
        (await ListarAsync<Venda>(() =>
            $"vendas?caixa_id=eq.{caixaId}&situacao=eq.aberta&select={CamposVenda},venda_itens({CamposItem})&venda_itens.order=id")).FirstOrDefault();

    /// <summary>Vendas em espera do caixa (suspensas para atender outro cliente).</summary>
    public Task<List<Venda>> VendasEmEsperaAsync(long caixaId) =>
        ListarAsync<Venda>(() =>
            $"vendas?caixa_id=eq.{caixaId}&situacao=eq.espera&dia=eq.{Hoje}&select={CamposVenda},venda_itens({CamposItem})&order=id");

    /// <summary>Vendas pagas (e as pagas que depois foram canceladas) de hoje, de todos os caixas, da mais nova para a mais antiga.</summary>
    public Task<List<Venda>> VendasDeHojeAsync() =>
        ListarAsync<Venda>(() =>
            $"vendas?dia=eq.{Hoje}&situacao=in.(paga,cancelada)&paga_em=not.is.null" +
            $"&select={CamposVenda},venda_itens({CamposItem}),venda_pagamentos({CamposPagamento})&order=id.desc");

    /// <summary>Totais do dia (vendas pagas), para o Caixa e o Painel.</summary>
    public static ResumoDia Resumo(IEnumerable<Venda> vendas)
    {
        var pagas = vendas.Where(v => v.Paga).ToList();
        return new ResumoDia(pagas.Sum(v => v.Total), pagas.Count);
    }

    /// <summary>
    /// Põe/muda o produto na venda e reserva. somar = leitor (+ quantidade ou + peso da etiqueta). Diminuir ou tirar
    /// um item já lido precisa da autorização do gerente (id de AutorizarAsync; o gerente logado não precisa).
    /// </summary>
    public async Task<ResultadoReserva> ReservarAsync(long produtoId, decimal quantidade, bool somar, decimal? valorEtiqueta = null, long? autorizacao = null) =>
        await api.ChamarAsync<ResultadoReserva>("reservar_item",
            new { p_produto_id = produtoId, p_quantidade = quantidade, p_somar = somar, p_valor = valorEtiqueta, p_autorizacao = autorizacao },
            sessao.Token)
        ?? throw new ConexaoException("Resposta inválida do servidor.");

    /// <summary>Gerente digita usuário e senha no caixa. Devolve o id da autorização, ou null se estiverem errados.</summary>
    public Task<long?> AutorizarAsync(string usuario, string senha, string acao, long? vendaId) =>
        api.ChamarAsync<long?>("autorizar_gerente", new { p_usuario = usuario, p_senha = senha, p_acao = acao, p_venda_id = vendaId }, sessao.Token);

    /// <summary>Desconto em R$ na venda aberta; devolve o total novo.</summary>
    public async Task<decimal> AplicarDescontoAsync(long vendaId, decimal valor, long? autorizacao) =>
        await api.ChamarAsync<decimal>("aplicar_desconto", new { p_venda_id = vendaId, p_valor = valor, p_autorizacao = autorizacao }, sessao.Token);

    /// <summary>Recebe os pagamentos (podem ser várias formas) e dá baixa nos lotes.</summary>
    public async Task<ResultadoVenda> FinalizarAsync(long vendaId, IEnumerable<PagamentoVenda> pagamentos, string? cpf) =>
        await api.ChamarAsync<ResultadoVenda>("finalizar_venda",
            new { p_venda_id = vendaId, p_pagamentos = pagamentos.ToList(), p_cpf = cpf }, sessao.Token)
        ?? throw new ConexaoException("Resposta inválida do servidor.");

    /// <summary>Venda aberta ou em espera (com itens, precisa do gerente); venda paga só o gerente logado.</summary>
    public Task CancelarAsync(long vendaId, long? autorizacao = null) =>
        api.ChamarAsync<object>("cancelar_venda", new { p_venda_id = vendaId, p_autorizacao = autorizacao }, sessao.Token);

    public Task SuspenderAsync(long vendaId) =>
        api.ChamarAsync<object>("suspender_venda", new { p_venda_id = vendaId }, sessao.Token);

    public Task RetomarAsync(long vendaId) =>
        api.ChamarAsync<object>("retomar_venda", new { p_venda_id = vendaId }, sessao.Token);

    // ------------------------------------------------------------ Sangria, suprimento e fechamento (script 008)

    /// <summary>Resumo do caixa: vendas, formas, sangrias, suprimentos e o dinheiro que deveria estar na gaveta.</summary>
    public async Task<ResumoCaixa> ResumoAsync(long caixaId) =>
        await api.ChamarAsync<ResumoCaixa>("resumo_caixa", new { p_caixa_id = caixaId }, sessao.Token)
        ?? throw new ConexaoException("Resposta inválida do servidor.");

    /// <summary>Sangria (precisa da autorização do gerente) ou suprimento no caixa aberto de quem está logado.</summary>
    public async Task<ResumoCaixa> MovimentoAsync(string tipo, decimal valor, string? motivo, long? autorizacao) =>
        await api.ChamarAsync<ResumoCaixa>("registrar_movimento",
            new { p_tipo = tipo, p_valor = valor, p_motivo = motivo, p_autorizacao = autorizacao }, sessao.Token)
        ?? throw new ConexaoException("Resposta inválida do servidor.");

    /// <summary>Fecha o caixa com o dinheiro contado na gaveta; devolve o resumo com a sobra ou a falta.</summary>
    public async Task<ResumoCaixa> FecharAsync(long caixaId, decimal contado, string? observacao) =>
        await api.ChamarAsync<ResumoCaixa>("fechar_caixa", new { p_caixa_id = caixaId, p_contado = contado, p_obs = observacao }, sessao.Token)
        ?? throw new ConexaoException("Resposta inválida do servidor.");

    /// <summary>Só o gerente: reabre um caixa de hoje fechado por engano.</summary>
    public Task ReabrirAsync(long caixaId) =>
        api.ChamarAsync<object>("reabrir_caixa", new { p_caixa_id = caixaId }, sessao.Token);
}
