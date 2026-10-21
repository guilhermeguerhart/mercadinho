using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

/// <summary>
/// MODO DE TESTE (semana 2): vendas e caixa de exemplo em memória (o estoque vem do banco desde a semana 3 e os
/// pedidos desde a semana 4), copiados de js/dados.js do projeto de referência.
/// Nada é lido nem gravado no banco. Quando as tabelas existirem, troque por uma classe que leia do Supabase.
/// </summary>
public static class DadosExemplo
{
    public static ResumoDia ResumoDoDia() => new(1284.50m, 32);

    public static DadosPainel Painel()
    {
        return new DadosPainel(ResumoDoDia(), new SituacaoCaixa(true, "Marcos", "08:02"));
    }
}
