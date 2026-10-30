using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Janelas pequenas do caixa (CPF na nota, desconto, consulta de preço, vendas em espera), montadas em código com
/// os mesmos estilos das outras janelas: papel com faixa verde, título, conteúdo e botões. Esc fecha.
/// </summary>
internal sealed class JanelaSimples : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly AreaInternaView _area;
    private readonly StackPanel _corpo = new();
    private readonly StackPanel _botoes = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
    private readonly Border _erro;
    private readonly TextBlock _erroTexto;

    public JanelaSimples(AreaInternaView area, string titulo, string? texto, double largura = 440)
    {
        _area = area;
        Width = largura;
        var raiz = new StackPanel();
        raiz.Children.Add(new TextBlock { Text = titulo, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        if (texto is not null)
            raiz.Children.Add(new TextBlock { Text = texto, FontSize = 13.5, Style = Estilo("TextoSuave"), Margin = new Thickness(0, 0, 0, 14) });
        _erroTexto = new TextBlock { Foreground = Pincel("Vermelho600"), FontSize = 14 };
        _erro = new Border { Style = Estilo("Aviso"), Background = Pincel("Vermelho50"), BorderBrush = Pincel("Vermelho600"), Visibility = Visibility.Collapsed, Child = _erroTexto };
        raiz.Children.Add(_erro);
        raiz.Children.Add(_corpo);
        raiz.Children.Add(_botoes);
        Content = new Border { Style = Estilo("Janela"), Padding = new Thickness(24, 22, 24, 20), Child = raiz };
    }

    public StackPanel Corpo => _corpo;

    public void Erro(string? mensagem)
    {
        _erroTexto.Text = mensagem ?? "";
        _erro.Visibility = mensagem is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public Button Botao(string texto, bool principal, Action acao)
    {
        var b = new Button { Content = texto, Style = Estilo(principal ? "BotaoPrimario" : "BotaoFantasma"), Margin = new Thickness(_botoes.Children.Count == 0 ? 0 : 10, 0, 0, 0), IsDefault = principal };
        b.Click += (_, _) => acao();
        _botoes.Children.Add(b);
        return b;
    }

    public TextBox Campo(string rotulo, string valor = "", int max = 20)
    {
        _corpo.Children.Add(new TextBlock { Text = rotulo, Style = Estilo("Rotulo") });
        var campo = new TextBox { Text = valor, MaxLength = max };
        System.Windows.Automation.AutomationProperties.SetName(campo, rotulo);
        _corpo.Children.Add(campo);
        campo.TextChanged += (_, _) => { campo.Tag = null; Erro(null); };
        return campo;
    }

    public void Fechar() => _area.FecharJanela();

    public Style Estilo(string chave) => (Style)Application.Current.FindResource(chave);

    public Brush Pincel(string chave) => (Brush)Application.Current.FindResource(chave);

    public static string Moeda(decimal v) => v.ToString("C", PtBr);
}

/// <summary>Monta as janelas do caixa.</summary>
internal static class CaixaJanelas
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    /// <summary>CPF/CNPJ na nota. aoConfirmar recebe só os dígitos (ou null = sem CPF).</summary>
    public static JanelaSimples Cpf(AreaInternaView area, string? atual, Action<string?> aoConfirmar)
    {
        var j = new JanelaSimples(area, "CPF ou CNPJ na nota", "Pergunte ao cliente se quer o CPF na nota. Ele vai junto com a venda.", 400);
        var campo = j.Campo("CPF ou CNPJ", atual is null ? "" : Documento.Formatar(atual), 18);
        void Confirmar()
        {
            var digitos = Documento.Digitos(campo.Text);
            if (digitos.Length == 0) { aoConfirmar(null); j.Fechar(); return; }
            if (!Documento.Valido(digitos))
            {
                campo.Tag = "invalido";
                j.Erro("CPF ou CNPJ inválido. Confira os números.");
                campo.Focus();
                return;
            }
            aoConfirmar(digitos);
            j.Fechar();
        }
        j.Botao("Sem CPF", false, () => { aoConfirmar(null); j.Fechar(); });
        j.Botao("Confirmar", true, Confirmar);
        j.Loaded += (_, _) => { campo.Focus(); campo.SelectAll(); };
        return j;
    }

    /// <summary>Desconto em R$ ou % sobre o valor dos itens. aoConfirmar recebe o desconto em R$.</summary>
    public static JanelaSimples Desconto(AreaInternaView area, decimal itens, decimal atual, Action<decimal> aoConfirmar)
    {
        var j = new JanelaSimples(area, "Desconto na venda",
            $"Itens: {JanelaSimples.Moeda(itens)}. Digite o desconto em reais (ex.: 5,00) ou em porcentagem (ex.: 10%). O gerente precisa autorizar.", 420);
        var campo = j.Campo("Desconto", atual > 0 ? atual.ToString("N2", PtBr) : "", 10);
        var previa = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = j.Pincel("Verde700"), Margin = new Thickness(0, 10, 0, 0) };
        j.Corpo.Children.Add(previa);
        decimal? Ler()
        {
            var t = campo.Text.Trim();
            if (t.Length == 0) return 0;
            if (t.EndsWith('%'))
                return decimal.TryParse(t.TrimEnd('%').Trim(), NumberStyles.Number, PtBr, out var pct) && pct is >= 0 and <= 100
                    ? Math.Round(itens * pct / 100, 2) : null;
            return Formulario.LerPreco(t);
        }
        campo.TextChanged += (_, _) => previa.Text = Ler() is { } d && d <= itens ? $"Total com desconto: {JanelaSimples.Moeda(itens - d)}" : "";
        j.Botao("Voltar", false, j.Fechar);
        j.Botao("Aplicar desconto", true, () =>
        {
            if (Ler() is not { } d || d > itens)
            {
                campo.Tag = "invalido";
                j.Erro($"Use um valor de R$ 0,00 até {JanelaSimples.Moeda(itens)}, ou uma porcentagem.");
                return;
            }
            j.Fechar();
            aoConfirmar(d);
        });
        j.Loaded += (_, _) => { campo.Focus(); campo.SelectAll(); };
        return j;
    }

    /// <summary>Consulta de preço (F3): lê o código ou o nome e mostra preço, estoque e validade, sem pôr na venda.</summary>
    public static JanelaSimples ConsultaPreco(AreaInternaView area, IReadOnlyList<Produto> produtos)
    {
        var j = new JanelaSimples(area, "Consulta de preço", "Passe o leitor ou digite o nome. Nada entra na venda.", 460);
        var campo = j.Campo("Código ou nome do produto", "", 60);
        var resultado = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        j.Corpo.Children.Add(resultado);
        void Mostrar(Produto? p, decimal? valorEtiqueta = null)
        {
            resultado.Children.Clear();
            if (p is null)
            {
                resultado.Children.Add(new TextBlock { Text = "Nenhum produto encontrado.", FontSize = 14, Style = j.Estilo("TextoSuave") });
                return;
            }
            resultado.Children.Add(new TextBlock { Text = p.Nome, FontSize = 18, FontWeight = FontWeights.Bold });
            var cat = CategoriaVisual.Pilula(j, p.Categoria, 12);
            cat.Margin = new Thickness(0, 4, 0, 10);
            resultado.Children.Add(cat);
            var preco = new TextBlock { Text = JanelaSimples.Moeda(p.Preco) + (p.PorPeso ? " / kg" : ""), FontSize = 28, FontWeight = FontWeights.ExtraBold };
            resultado.Children.Add(new Border { Style = j.Estilo("EtiquetaPreco"), Padding = new Thickness(12, 2, 12, 4), Child = preco });
            var linhas = new List<string>
            {
                p.DisponivelParaVenda > 0 ? $"Disponível: {p.Formatar(p.DisponivelParaVenda)}" : p.QuantidadeVencida > 0 && p.Vendavel == 0 ? "Só tem lote vencido: venda bloqueada" : "Sem estoque",
            };
            if (p.Reservado > 0) linhas.Add($"Reservado em vendas abertas: {p.Formatar(p.Reservado)}");
            if (p.Situacao is { } s) linhas.Add($"Próxima validade: {p.ProximaValidade:dd/MM/yyyy} ({s.Texto.ToLower(PtBr)})");
            if (valorEtiqueta is { } v) linhas.Add($"Etiqueta da balança: {JanelaSimples.Moeda(v)} ({Math.Round(v / p.Preco, 3).ToString("0.000", PtBr)} kg)");
            foreach (var l in linhas)
                resultado.Children.Add(new TextBlock { Text = l, FontSize = 14, Margin = new Thickness(0, 8, 0, 0) });
        }
        campo.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            var t = campo.Text.Trim();
            if (t.Length == 0) return;
            if (t.All(char.IsAsciiDigit))
            {
                var leitura = CodigoBarras.Ler(produtos, t);
                Mostrar(leitura?.Produto, leitura?.ValorEtiqueta);
            }
            else
            {
                var n = Normalizar(t);
                Mostrar(produtos.Where(p => Normalizar(p.Nome).Contains(n)).OrderByDescending(p => Normalizar(p.Nome).StartsWith(n)).FirstOrDefault());
            }
            campo.SelectAll();
        };
        j.Botao("Fechar", true, j.Fechar);
        j.Loaded += (_, _) => campo.Focus();
        return j;
    }

    /// <summary>Vendas em espera do caixa, com "Retomar" em cada uma.</summary>
    public static JanelaSimples Espera(AreaInternaView area, IReadOnlyList<Venda> vendas, Func<Venda, Task> retomar)
    {
        var j = new JanelaSimples(area, "Vendas em espera", "Os itens continuam reservados até a venda ser retomada ou cancelada.", 520);
        foreach (var v in vendas)
        {
            var linha = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
            var botao = new Button { Content = "Retomar", Style = j.Estilo("BotaoPrimario"), Height = 40, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(12, 0, 0, 0) };
            var venda = v;
            botao.Click += async (_, _) =>
            {
                botao.IsEnabled = false;
                try { await retomar(venda); j.Fechar(); }
                catch (Services.ConexaoException ex) { j.Erro(ex.Message); botao.IsEnabled = true; }
            };
            DockPanel.SetDock(botao, Dock.Right);
            linha.Children.Add(botao);
            var total = new Border { Style = j.Estilo("EtiquetaPreco"), Margin = new Thickness(12, 0, 0, 0), Child = new TextBlock { Text = JanelaSimples.Moeda(v.Itens.Sum(i => i.Subtotal)), FontWeight = FontWeights.Bold } };
            DockPanel.SetDock(total, Dock.Right);
            linha.Children.Add(total);
            var texto = new StackPanel();
            texto.Children.Add(new TextBlock { Text = $"Venda nº {v.Id} · desde {v.CriadaEm.ToLocalTime():HH:mm}", FontSize = 14, FontWeight = FontWeights.SemiBold });
            texto.Children.Add(new TextBlock { Text = v.ResumoItens, FontSize = 12.5, Style = j.Estilo("TextoSuave"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
            linha.Children.Add(texto);
            j.Corpo.Children.Add(new Border { BorderBrush = j.Pincel("Linha"), BorderThickness = new Thickness(0, 0, 0, 1), Child = linha });
        }
        j.Botao("Fechar", false, j.Fechar);
        return j;
    }

    private static string Normalizar(string texto)
    {
        var decomposto = texto.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }
}
