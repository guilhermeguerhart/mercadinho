using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

/// <summary>
/// MODO DE TESTE (semana 2): vendas, pedidos e caixa de exemplo em memória (o estoque já vem do banco desde a semana 3), copiados de js/dados.js do projeto de referência.
/// Nada é lido nem gravado no banco. Quando as tabelas existirem, troque por uma classe que leia do Supabase.
/// </summary>
public static class DadosExemplo
{
    public static ResumoDia ResumoDoDia() => new(1284.50m, 32);

    public static DadosPainel Painel()
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        return new DadosPainel(
            ResumoDoDia(),
            [
                new("Laticínios Real", hoje.AddDays(-3), hoje.AddDays(2)),
                new("Grãos & Cia", hoje.AddDays(-41), hoje.AddDays(-34)),
            ],
            new SituacaoCaixa(true, "Marcos", "08:02"));
    }
}
