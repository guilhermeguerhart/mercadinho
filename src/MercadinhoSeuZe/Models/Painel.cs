namespace MercadinhoSeuZe.Models;

/// <summary>Totais de vendas do dia (o que todos os perfis podem ver).</summary>
public record ResumoDia(decimal Total, int QuantidadeVendas)
{
    public decimal TicketMedio => QuantidadeVendas > 0 ? Total / QuantidadeVendas : 0;
}
