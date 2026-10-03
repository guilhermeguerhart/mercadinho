using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

/// <summary>
/// MODO DE TESTE (semana 2): dados de exemplo em memória, copiados de js/dados.js do projeto de referência.
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
                new("Arroz 5kg", 42, 10),
                new("Feijão 1kg", 8, 10),
                new("Óleo de soja", 30, 10),
                new("Sabão em pó", 15, 6),
                new("Leite integral 1L", 4, 12),
                new("Café 500g", 3, 8),
                new("Iogurte natural 170g", 18, 10),
            ],
            [
                new("Laticínios Real", hoje.AddDays(-3), hoje.AddDays(2)),
                new("Grãos & Cia", hoje.AddDays(-41), hoje.AddDays(-34)),
            ],
            new SituacaoCaixa(true, "Marcos", "08:02"));
    }
}
