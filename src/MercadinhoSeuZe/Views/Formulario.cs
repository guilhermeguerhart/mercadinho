using System.Globalization;
using System.Windows.Controls;

namespace MercadinhoSeuZe.Views;

/// <summary>Leitura e validação dos campos das janelas de cadastro (mesmas regras do site de referência).</summary>
internal static class Formulario
{
    private static readonly CultureInfo PtBr = new("pt-BR");
    private static readonly string[] FormatosData = ["d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy", "ddMMyyyy"];

    /// <summary>"8,50", "R$ 1.234,90" → valor; null se inválido.</summary>
    public static decimal? LerPreco(string texto)
    {
        var limpo = texto.Replace("R$", "").Replace(" ", "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(limpo, @"^\d{1,3}(\.?\d{3})*(,\d{1,2})?$")) return null;
        return decimal.TryParse(limpo, NumberStyles.Number, PtBr, out var v) ? v : null;
    }

    /// <summary>Número inteiro de 0 para cima; null se inválido.</summary>
    public static int? LerInteiro(string texto) =>
        int.TryParse(texto.Trim(), NumberStyles.None, PtBr, out var v) ? v : null;

    /// <summary>
    /// Quantidade de 0 para cima: inteira nos produtos por unidade; com até 3 casas (ex.: "12,350") nos
    /// produtos por quilo. Null se inválida.
    /// </summary>
    public static decimal? LerQuantidade(string texto, bool porPeso)
    {
        if (!porPeso) return LerInteiro(texto);
        var limpo = texto.Replace("kg", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (!limpo.Contains(',')) limpo = limpo.Replace('.', ',');   // "12.5" também vale
        if (!System.Text.RegularExpressions.Regex.IsMatch(limpo, @"^\d{1,6}(,\d{1,3})?$")) return null;
        return decimal.Parse(limpo, NumberStyles.Number, PtBr);
    }

    /// <summary>Quantidade para mostrar num campo: "12" ou "12,35".</summary>
    public static string Quantidade(decimal valor) => valor.ToString("0.###", PtBr);

    /// <summary>Data no formato dd/mm/aaaa; null se inválida.</summary>
    public static DateOnly? LerData(string texto) =>
        DateOnly.TryParseExact(texto.Trim(), FormatosData, PtBr, DateTimeStyles.None, out var d) ? d : null;

    public static string Data(DateOnly? data) => data?.ToString("dd/MM/yyyy", PtBr) ?? "";

    public static string Preco(decimal valor) => valor.ToString("N2", PtBr);

    /// <summary>Marca o campo como inválido (borda vermelha) e mostra a mensagem embaixo dele.</summary>
    public static bool Validar(Control campo, TextBlock erro, bool ok, string mensagem)
    {
        campo.Tag = ok ? null : "invalido";
        erro.Text = ok ? "" : mensagem;
        return ok;
    }

    /// <summary>
    /// O leitor de código de barras "digita" o código e aperta Enter. Neste campo o Enter só passa para o
    /// próximo campo, para não salvar a janela sem querer.
    /// </summary>
    public static void EnterVaiParaProximo(TextBox campo) =>
        campo.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            e.Handled = true;
            campo.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
        };

    /// <summary>
    /// Validade pré-definida: preenche o campo de validade com compra + dias sempre que a data de compra
    /// (ou os dias) mudar, até a pessoa digitar outra validade à mão. Devolve a função que recalcula.
    /// </summary>
    public static Action PreencherValidade(TextBox compra, TextBox validade, Func<int?> dias, TextBlock dica,
        string origem = "validade padrão do produto")
    {
        string? automatica = null;
        var preenchendo = false;
        void Recalcular()
        {
            var manual = validade.Text.Trim().Length > 0 && validade.Text != automatica;
            var d = dias();
            if (d is null || LerData(compra.Text) is not { } dataCompra)
            {
                dica.Text = "";
                return;
            }
            dica.Text = manual
                ? $"Pela {origem} seriam {d} dias (você mudou a data)."
                : $"Preenchida pela {origem} ({d} dias depois da compra).";
            if (manual) return;
            preenchendo = true;
            validade.Text = automatica = Data(dataCompra.AddDays(d.Value));
            preenchendo = false;
        }
        compra.TextChanged += (_, _) => Recalcular();
        validade.TextChanged += (_, _) => { if (!preenchendo) Recalcular(); };
        return Recalcular;
    }

    /// <summary>Limpa o erro do campo assim que a pessoa digita.</summary>
    public static void LimparAoDigitar(TextBox campo, TextBlock erro) =>
        campo.TextChanged += (_, _) => { campo.Tag = null; erro.Text = ""; };
}
