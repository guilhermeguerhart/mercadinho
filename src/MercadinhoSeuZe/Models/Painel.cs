namespace MercadinhoSeuZe.Models;

/// <summary>Totais de vendas do dia (o que todos os perfis podem ver).</summary>
public record ResumoDia(decimal Total, int QuantidadeVendas)
{
    public decimal TicketMedio => QuantidadeVendas > 0 ? Total / QuantidadeVendas : 0;
}

public record SituacaoCaixa(bool Aberto, string? AbertoPor, string? AbertoAs);

/// <summary>O que o painel ainda mostra de exemplo (vendas e caixa). Estoque e pedidos vêm do banco.</summary>
public record DadosPainel(
    ResumoDia Resumo,
    SituacaoCaixa Caixa);
