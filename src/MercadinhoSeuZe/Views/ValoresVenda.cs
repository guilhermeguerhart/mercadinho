using System.Globalization;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Valores de venda (pedido do Guilherme em 03/11): só o gerente vê, e o interruptor "Ocultar vendas" troca os valores
/// por "R$ •••••" para quem estiver olhando a tela. A escolha fica lembrada neste computador e vale para todas as telas.
/// </summary>
public static class ValoresVenda
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public static bool Ocultos { get; private set; } = PreferenciasLocais.Carregar().OcultarVendas;

    /// <summary>O interruptor mudou: cada tela mostra os valores de novo.</summary>
    public static event Action? Mudou;

    public static void Definir(bool ocultar)
    {
        if (Ocultos == ocultar) return;
        Ocultos = ocultar;
        var prefs = PreferenciasLocais.Carregar();
        prefs.OcultarVendas = ocultar;
        prefs.Salvar();
        Mudou?.Invoke();
    }

    public static string Moeda(decimal valor) => Ocultos ? "R$ •••••" : valor.ToString("C", PtBr);

    public static string Numero(int valor) => Ocultos ? "•••" : valor.ToString("N0", PtBr);
}
