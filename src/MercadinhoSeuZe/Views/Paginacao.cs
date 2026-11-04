using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Paginação das listas longas (pedido do Guilherme): "Mostrar [10] por página" à esquerda e « ‹ 1 2 … 9 › » à direita.
/// Guarda só a página e o tamanho; a tela recorta a lista com Recortar e põe a Barra() embaixo dela a cada desenho.
/// O tamanho escolhido fica lembrado por lista (chave) enquanto o programa estiver aberto.
/// </summary>
public sealed class Paginacao(string chave, int[] tamanhos, int padrao)
{
    private static readonly CultureInfo PtBr = new("pt-BR");
    private static readonly Dictionary<string, int> Lembrados = [];

    /// <summary>Página atual, começando em 1.</summary>
    public int Pagina { get; private set; } = 1;

    public int PorPagina => Lembrados.GetValueOrDefault(chave, padrao);

    /// <summary>Itens da lista inteira (antes do recorte).</summary>
    public int Total { get; private set; }

    public int Paginas => Math.Max(1, (Total + PorPagina - 1) / PorPagina);

    /// <summary>Trocou a página ou o tamanho: a tela desenha a lista de novo.</summary>
    public event Action? Mudou;

    /// <summary>Volta para a primeira página (busca, filtro ou ordem mudou).</summary>
    public void Inicio() => Pagina = 1;

    /// <summary>Os itens da página atual. Se a página deixou de existir (ex.: excluiu o último da página), vai para a última.</summary>
    public List<T> Recortar<T>(IReadOnlyCollection<T> itens)
    {
        Total = itens.Count;
        Pagina = Math.Clamp(Pagina, 1, Paginas);
        return itens.Skip((Pagina - 1) * PorPagina).Take(PorPagina).ToList();
    }

    /// <summary>"11–20 de 137" (ou só "137" com uma página).</summary>
    public string Faixa()
    {
        if (Paginas == 1) return Total.ToString(PtBr);
        var inicio = (Pagina - 1) * PorPagina + 1;
        var fim = Math.Min(Pagina * PorPagina, Total);
        return $"{inicio.ToString(PtBr)}–{fim.ToString(PtBr)} de {Total.ToString(PtBr)}";
    }

    /// <summary>
    /// A barra de baixo da lista. Sem itens, não aparece; com uma página só, fica só o tamanho.
    /// Compacta (cartões estreitos, como os avisos do Painel): só as páginas, sem o "Mostrar [5] por página".
    /// </summary>
    public FrameworkElement Barra(bool compacta = false)
    {
        var barra = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        if (Total <= tamanhos[0] || (compacta && Paginas == 1)) { barra.Visibility = Visibility.Collapsed; return barra; }
        if (compacta) { var so = BotoesPaginas(); so.Margin = new Thickness(0, 6, 0, 4); return so; }

        var tamanho = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        tamanho.Children.Add(Texto("Mostrar"));
        var escolha = new ComboBox { ItemsSource = tamanhos, SelectedItem = PorPagina, Width = 86, MinHeight = 38, Margin = new Thickness(8, 0, 8, 0) };
        AutomationProperties.SetName(escolha, "Itens por página");
        escolha.SelectionChanged += (_, _) =>
        {
            if (escolha.SelectedItem is not int n || n == PorPagina) return;
            var primeiro = (Pagina - 1) * PorPagina;   // continua mostrando o mesmo primeiro item
            Lembrados[chave] = n;
            Pagina = primeiro / n + 1;
            Mudou?.Invoke();
        };
        tamanho.Children.Add(escolha);
        tamanho.Children.Add(Texto("por página"));
        DockPanel.SetDock(tamanho, Dock.Left);
        barra.Children.Add(tamanho);

        if (Paginas > 1)
        {
            var botoes = BotoesPaginas();
            DockPanel.SetDock(botoes, Dock.Right);
            barra.Children.Add(botoes);
        }
        return barra;
    }

    private WrapPanel BotoesPaginas()
    {
        var botoes = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        botoes.Children.Add(Botao("‹", Pagina - 1, "Página anterior", Pagina > 1));
        foreach (var p in Numeros())
            botoes.Children.Add(p is null ? Texto("…", new Thickness(2, 0, 8, 0)) : Botao(p.Value.ToString(PtBr), p.Value, $"Página {p}", true, p == Pagina));
        botoes.Children.Add(Botao("›", Pagina + 1, "Próxima página", Pagina < Paginas));
        return botoes;
    }

    /// <summary>1 … 4 5 6 … 12: a primeira, a última e as vizinhas da atual; null = reticências.</summary>
    private IEnumerable<int?> Numeros()
    {
        var mostrar = new SortedSet<int> { 1, Paginas, Pagina - 1, Pagina, Pagina + 1 };
        // Nas pontas mostra três seguidas, para a barra não ficar com uma página só entre reticências
        if (Pagina <= 3) mostrar.UnionWith([2, 3, 4]);
        if (Pagina >= Paginas - 2) mostrar.UnionWith([Paginas - 3, Paginas - 2, Paginas - 1]);
        var anterior = 0;
        foreach (var p in mostrar.Where(p => p >= 1 && p <= Paginas))
        {
            if (p - anterior == 2) yield return p - 1;   // só uma escondida: mostra o número em vez de "…"
            else if (p - anterior > 2) yield return null;
            yield return p;
            anterior = p;
        }
    }

    private Button Botao(string texto, int pagina, string nome, bool ativo, bool atual = false)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = texto, HorizontalAlignment = HorizontalAlignment.Center, FontSize = texto.Length == 1 && !char.IsDigit(texto[0]) ? 18 : 14 },
            Style = (Style)Application.Current.FindResource("Chip"), MinWidth = 38, MinHeight = 38, Margin = new Thickness(0, 0, 6, 0),
            Tag = atual ? "ativo" : null, IsEnabled = ativo, Opacity = ativo ? 1 : 0.4
        };
        AutomationProperties.SetName(b, atual ? $"{nome} (atual)" : nome);
        b.Click += (_, _) =>
        {
            if (atual) return;
            Pagina = pagina;
            Mudou?.Invoke();
        };
        return b;
    }

    private static TextBlock Texto(string texto, Thickness? margem = null) => new()
    {
        Text = texto, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = margem ?? new Thickness(0),
        Foreground = (Brush)Application.Current.FindResource("Suave")
    };
}
