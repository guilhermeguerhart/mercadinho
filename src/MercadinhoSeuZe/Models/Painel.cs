namespace MercadinhoSeuZe.Models;

/// <summary>Totais de vendas do dia (o que todos os perfis podem ver).</summary>
public record ResumoDia(decimal Total, int QuantidadeVendas)
{
    public decimal TicketMedio => QuantidadeVendas > 0 ? Total / QuantidadeVendas : 0;
}

public record ProdutoEstoque(string Nome, int Quantidade, int Minimo)
{
    public bool EstoqueBaixo => Quantidade < Minimo;
}

public record PedidoPendente(string Fornecedor, DateOnly Data, DateOnly Previsao)
{
    public bool Atrasado => Previsao < DateOnly.FromDateTime(DateTime.Today);
}

public record SituacaoCaixa(bool Aberto, string? AbertoPor, string? AbertoAs);

/// <summary>Tudo que o painel do gerente mostra.</summary>
public record DadosPainel(
    ResumoDia Resumo,
    IReadOnlyList<ProdutoEstoque> Produtos,
    IReadOnlyList<PedidoPendente> PedidosPendentes,
    SituacaoCaixa Caixa)
{
    public IEnumerable<ProdutoEstoque> EstoqueBaixo => Produtos.Where(p => p.EstoqueBaixo);
}
