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

    /// <summary>
    /// Campo de dinheiro como na maquininha: só entram números, que empurram da direita para a esquerda
    /// (1, 5, 0, 0, 0 → 0,01 · 0,15 · 1,50 · 15,00 · 150,00; acima de mil, 1.500,00). Colar "R$ 150" vira 150,00.
    /// vazio = começa vazio (o campo pode ficar em branco); senão começa em 0,00. Até R$ 99.999,99.
    /// </summary>
    public static void MascaraDinheiro(TextBox campo, bool vazio = false)
    {
        const long Maximo = 9_999_999;   // em centavos
        long centavos = LerPreco(campo.Text) is { } inicial ? (long)(inicial * 100) : 0;
        campo.MaxLength = 0;
        campo.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Right;

        void Mostrar()
        {
            campo.Text = vazio && centavos == 0 ? "" : Preco(centavos / 100m);
            campo.CaretIndex = campo.Text.Length;
        }
        void Digitar(string texto)
        {
            foreach (var c in texto.Where(char.IsAsciiDigit))
            {
                var novo = centavos * 10 + (c - '0');
                if (novo > Maximo) break;
                centavos = novo;
            }
        }

        campo.PreviewTextInput += (_, e) =>
        {
            e.Handled = true;
            if (campo.SelectionLength > 0 && campo.SelectionLength == campo.Text.Length) centavos = 0;
            Digitar(e.Text);
            Mostrar();
        };
        campo.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.Back:
                    centavos = campo.SelectionLength > 0 && campo.SelectionLength == campo.Text.Length ? 0 : centavos / 10;
                    break;
                case System.Windows.Input.Key.Delete:
                    centavos = 0;
                    break;
                case System.Windows.Input.Key.Space:
                    break;
                default:
                    return;
            }
            e.Handled = true;
            Mostrar();
        };
        System.Windows.DataObject.AddPastingHandler(campo, (_, e) =>
        {
            e.CancelCommand();
            if (e.DataObject.GetData(typeof(string)) is not string colado) return;
            var limpo = colado.Replace("R$", "").Trim();
            if (LerPreco(limpo) is { } valor && valor * 100 <= Maximo) centavos = (long)(valor * 100);
            else { centavos = 0; Digitar(limpo); }
            Mostrar();
        });
        campo.GotKeyboardFocus += (_, _) => campo.Dispatcher.BeginInvoke(() => campo.SelectAll());
        // Quem muda o texto por código (limpar o campo, preencher um valor) também atualiza o número guardado
        campo.TextChanged += (_, _) =>
        {
            var lido = campo.Text.Length == 0 ? 0 : LerPreco(campo.Text) is { } v ? (long)(v * 100) : centavos;
            centavos = Math.Min(lido, Maximo);
        };
        Mostrar();
    }

    /// <summary>Limpa o erro do campo assim que a pessoa digita.</summary>
    public static void LimparAoDigitar(TextBox campo, TextBlock erro) =>
        campo.TextChanged += (_, _) => { campo.Tag = null; erro.Text = ""; };
}
