using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Novo pedido de reposição com vários itens. Produtos do fornecedor que precisam de reposição aparecem primeiro,
/// com a quantidade sugerida. Depois de criar, abre a janela de envio com a mensagem pronta.
/// </summary>
public partial class PedidoJanela : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private sealed record Item(Produto Produto, TextBox Quantidade, TextBlock Erro);

    private readonly AreaInternaView _area;
    private readonly FornecedorService _servico;
    private readonly IReadOnlyList<Fornecedor> _fornecedores;
    private readonly IReadOnlyList<Produto> _produtos;
    private readonly Func<Produto, bool> _jaPedido;
    private readonly Func<string, Fornecedor, string, Task> _aoCriar;
    private readonly List<Item> _itens = [];
    private string? _previsaoAutomatica;
    private bool _preenchendoPrevisao;

    /// <param name="jaPedido">Produto que já está num pedido pendente.</param>
    /// <param name="aoCriar">Recebe a mensagem do aviso, o fornecedor e o texto do pedido para enviar.</param>
    public PedidoJanela(AreaInternaView area, FornecedorService servico, IReadOnlyList<Fornecedor> fornecedores, IReadOnlyList<Produto> produtos,
        Fornecedor? fornecedor, IReadOnlyList<Produto> itensIniciais, Func<Produto, bool> jaPedido, Func<string, Fornecedor, string, Task> aoCriar)
    {
        InitializeComponent();
        _area = area;
        _servico = servico;
        _fornecedores = fornecedores.OrderBy(f => f.Nome, StringComparer.Create(PtBr, true)).ToList();
        _produtos = produtos;
        _jaPedido = jaPedido;
        _aoCriar = aoCriar;

        EscolhaFornecedor.Items.Add(new ComboBoxItem { Content = "Selecione o fornecedor", Foreground = (Brush)FindResource("Placeholder") });
        foreach (var f in _fornecedores)
            EscolhaFornecedor.Items.Add(new ComboBoxItem { Content = f.Nome, Tag = f });
        EscolhaFornecedor.SelectedIndex = fornecedor is null ? 0 : _fornecedores.ToList().FindIndex(f => f.Id == fornecedor.Id) + 1;

        foreach (var p in itensIniciais)
            Adicionar(p, focar: false);

        Previsao.TextChanged += (_, _) =>
        {
            if (!_preenchendoPrevisao) DicaPrevisao.Text = "";
            AtualizarPrevia();
        };
        Observacao.TextChanged += (_, _) => AtualizarPrevia();
        Formulario.LimparAoDigitar(Previsao, PrevisaoErro);
        PreencherPrevisao();
        MostrarResultados();
        AtualizarPrevia();

        Loaded += (_, _) =>
        {
            if (EscolhaFornecedor.SelectedIndex <= 0) EscolhaFornecedor.Focus();
            else if (_itens.Count > 0) { _itens[0].Quantidade.Focus(); _itens[0].Quantidade.SelectAll(); }
            else Busca.Focus();
        };
    }

    private Fornecedor? Selecionado => (EscolhaFornecedor.SelectedItem as ComboBoxItem)?.Tag as Fornecedor;

    private void Fornecedor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || Previsao is null) return;
        FornecedorErro.Text = "";
        EscolhaFornecedor.Tag = null;
        PreencherPrevisao();
        MostrarResultados();
        AtualizarPrevia();
    }

    /// <summary>
    /// Previsão = hoje + o maior prazo de entrega entre os itens (prazo do produto ou, sem ele, o padrão do
    /// fornecedor), até a pessoa digitar outra data.
    /// </summary>
    private void PreencherPrevisao()
    {
        if (Previsao is null) return;
        var manual = Previsao.Text.Trim().Length > 0 && Previsao.Text != _previsaoAutomatica;
        if (manual) return;
        var f = Selecionado;
        var padrao = f?.Prazo ?? Models.Fornecedor.PrazoPadrao;
        var maior = _itens.Select(i => (i.Produto, Prazo: i.Produto.PrazoEntregaDias ?? padrao)).OrderByDescending(x => x.Prazo).FirstOrDefault();
        var prazo = maior.Produto is null ? padrao : maior.Prazo;
        _preenchendoPrevisao = true;
        Previsao.Text = _previsaoAutomatica = Formulario.Data(DateOnly.FromDateTime(DateTime.Today).AddDays(prazo));
        _preenchendoPrevisao = false;
        DicaPrevisao.Text = maior.Produto?.PrazoEntregaDias is not null
            ? $"Hoje + {Dias(prazo)} (maior prazo dos itens: {maior.Produto.Nome})."
            : f is null ? ""
            : f.PrazoEntregaDias is null
                ? $"Hoje + {Dias(prazo)} (prazo padrão; cadastre o prazo no produto ou no fornecedor para acertar)."
                : $"Hoje + {Dias(prazo)} (prazo padrão de {f.Nome}).";
    }

    private static string Dias(int n) => n == 1 ? "1 dia" : $"{n} dias";

    // ------------------------------------------------------------ Itens

    private void Adicionar(Produto p, bool focar = true)
    {
        var existente = _itens.FirstOrDefault(i => i.Produto.Id == p.Id);
        if (existente is not null)
        {
            if (focar) { existente.Quantidade.Focus(); existente.Quantidade.SelectAll(); }
            return;
        }

        var quantidade = new TextBox { Width = 110, MaxLength = 10, Text = Formulario.Quantidade(p.QuantidadeSugerida), HorizontalContentAlignment = HorizontalAlignment.Right };
        System.Windows.Automation.AutomationProperties.SetName(quantidade, $"Quantidade de {p.Nome}");
        var erro = new TextBlock { Style = (Style)FindResource("ErroCampo") };
        Formulario.LimparAoDigitar(quantidade, erro);
        quantidade.TextChanged += (_, _) => AtualizarPrevia();
        var item = new Item(p, quantidade, erro);
        _itens.Add(item);
        ItensErro.Text = "";

        var linha = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        var remover = new Button { Content = "×", Style = (Style)FindResource("LinkSuave"), FontSize = 22, FontWeight = FontWeights.Normal, Margin = new Thickness(12, -4, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Tirar do pedido" };
        System.Windows.Automation.AutomationProperties.SetName(remover, $"Tirar {p.Nome} do pedido");
        DockPanel.SetDock(remover, Dock.Right);
        linha.Children.Add(remover);

        var unidade = new TextBlock { Text = p.PorPeso ? "kg" : "un.", Width = 28, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Suave") };
        DockPanel.SetDock(unidade, Dock.Right);
        linha.Children.Add(unidade);
        var campo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        campo.Children.Add(quantidade);
        campo.Children.Add(erro);
        DockPanel.SetDock(campo, Dock.Right);
        linha.Children.Add(campo);

        var nome = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        nome.Children.Add(new TextBlock { Text = p.Nome, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        nome.Children.Add(Situacao(p));
        linha.Children.Add(nome);

        var bloco = new Border { BorderBrush = (Brush)FindResource("Linha"), BorderThickness = new Thickness(0, Itens.Children.Count == 0 ? 0 : 1, 0, 0), Child = linha };
        remover.Click += (_, _) =>
        {
            _itens.Remove(item);
            Itens.Children.Remove(bloco);
            if (Itens.Children.Count > 0 && Itens.Children[0] is Border primeiro) primeiro.BorderThickness = new Thickness(0);
            SemItens.Visibility = _itens.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PreencherPrevisao();
            MostrarResultados();
            AtualizarPrevia();
        };
        Itens.Children.Add(bloco);
        SemItens.Visibility = Visibility.Collapsed;
        PreencherPrevisao();
        if (focar)
        {
            MostrarResultados();
            AtualizarPrevia();
            Dispatcher.BeginInvoke(() => { quantidade.Focus(); quantidade.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    /// <summary>"Estoque: 3 unidades (mín. 8) · sem estoque" em vermelho, amarelo ou cinza.</summary>
    private TextBlock Situacao(Produto p)
    {
        var (texto, cor) = p.SemEstoque ? ("sem estoque", "Vermelho600")
            : p.EstoqueBaixo ? ("estoque baixo", "Vermelho600")
            : p.AvisoAntecipado ? ("chegando no mínimo", "Ambar700")
            : ("", "Suave");
        var caixa = (!p.PorPeso && p.UnidadesPorCaixa > 1 ? $" · caixa com {p.Numero(p.UnidadesPorCaixa)}" : "") +
                    (p.PrazoEntregaDias is { } prazo ? $" · entrega em {Dias(prazo)}" : "");
        return new TextBlock
        {
            Text = $"Estoque: {p.Formatar(p.Quantidade)} (mín. {p.Numero(p.EstoqueMinimo)}){caixa}" + (texto.Length > 0 ? " · " + texto : "") +
                   (_jaPedido(p) ? " · já tem pedido pendente" : ""),
            FontSize = 12.5,
            Foreground = (Brush)FindResource(cor)
        };
    }

    private void AdicionarOQueFalta_Click(object sender, RoutedEventArgs e)
    {
        if (Selecionado is not { } f) return;
        foreach (var p in ParaPedir(f)) Adicionar(p, focar: false);
        MostrarResultados();
        AtualizarPrevia();
    }

    private List<Produto> ParaPedir(Fornecedor f) =>
        _produtos.Where(p => f.Fornece(p) && p.PrecisaRepor && !_jaPedido(p) && _itens.All(i => i.Produto.Id != p.Id))
            .OrderBy(p => p.SemEstoque ? 0 : p.EstoqueBaixo ? 1 : 2).ThenBy(p => p.Nome, StringComparer.Create(PtBr, true)).ToList();

    // ------------------------------------------------------------ Busca de produtos

    private void Busca_TextChanged(object sender, TextChangedEventArgs e)
    {
        DicaBusca.Visibility = Busca.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        MostrarResultados();
    }

    /// <summary>Enter adiciona o primeiro resultado (o leitor de código de barras digita o código e manda Enter).</summary>
    private void Busca_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        if (Encontrados().FirstOrDefault() is { } p)
        {
            Busca.Clear();
            Adicionar(p);
        }
    }

    private List<Produto> Encontrados()
    {
        var termo = Busca.Text.Trim();
        var f = Selecionado;
        var porNome = StringComparer.Create(PtBr, true);
        if (termo.Length == 0)
            return f is null ? [] : ParaPedir(f).Take(6).ToList();
        return _produtos
            .Where(p => _itens.All(i => i.Produto.Id != p.Id) &&
                        (p.CodigoBarrasCaixa?.Trim() == termo ||
                         PtBr.CompareInfo.IndexOf(p.Nome, termo, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0 ||
                         PtBr.CompareInfo.IndexOf(p.Categoria, termo, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0))
            .OrderBy(p => p.CodigoBarrasCaixa?.Trim() == termo ? 0 : 1)
            .ThenBy(p => f is not null && f.Fornece(p) ? 0 : 1)   // do fornecedor primeiro, como no site
            .ThenBy(p => p.Nome, porNome)
            .Take(6)
            .ToList();
    }

    private void MostrarResultados()
    {
        Resultados.Children.Clear();
        var f = Selecionado;
        var faltam = f is null ? 0 : ParaPedir(f).Count;
        BotaoOQueFalta.Content = $"Adicionar tudo que falta ({faltam})";
        BotaoOQueFalta.Visibility = faltam > 0 ? Visibility.Visible : Visibility.Collapsed;

        var lista = Encontrados();
        TituloResultados.Text = Busca.Text.Trim().Length > 0
            ? (lista.Count == 0 ? "Nenhum produto encontrado." : "Clique para adicionar (Enter adiciona o primeiro)")
            : lista.Count > 0 ? $"Precisam de reposição em {f!.Nome}" : "";
        TituloResultados.Visibility = TituloResultados.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var p in lista)
        {
            var conteudo = new DockPanel();
            var mais = new ContentControl { Style = (Style)FindResource("Icone"), Tag = FindResource("IconeMais"), Foreground = (Brush)FindResource("Verde700"), Width = 16, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(mais, Dock.Left);
            conteudo.Children.Add(mais);
            var textos = new StackPanel();
            var nome = new WrapPanel();
            nome.Children.Add(new TextBlock { Text = p.Nome, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 8, 0) });
            if (f is not null && !f.Fornece(p))
                nome.Children.Add(new TextBlock { Text = "outro fornecedor", FontSize = 12, Foreground = (Brush)FindResource("Suave"), VerticalAlignment = VerticalAlignment.Center });
            textos.Children.Add(nome);
            textos.Children.Add(Situacao(p));
            conteudo.Children.Add(textos);

            var botao = new Button
            {
                Content = conteudo, Cursor = System.Windows.Input.Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Template = ItemResultado(), Margin = new Thickness(0, 0, 0, 4)
            };
            System.Windows.Automation.AutomationProperties.SetName(botao, $"Adicionar {p.Nome}");
            botao.Click += (_, _) => { Busca.Clear(); Adicionar(p); };
            Resultados.Children.Add(botao);
        }
    }

    private ControlTemplate ItemResultado()
    {
        var borda = new FrameworkElementFactory(typeof(Border), "Borda");
        borda.SetValue(Border.BackgroundProperty, Brushes.White);
        borda.SetValue(Border.BorderBrushProperty, FindResource("Linha"));
        borda.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        borda.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        borda.SetValue(Border.PaddingProperty, new Thickness(12, 8, 12, 8));
        borda.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var modelo = new ControlTemplate(typeof(Button)) { VisualTree = borda };
        var sobre = new Trigger { Property = IsMouseOverProperty, Value = true };
        sobre.Setters.Add(new Setter(Border.BorderBrushProperty, FindResource("Verde600"), "Borda"));
        sobre.Setters.Add(new Setter(Border.BackgroundProperty, FindResource("Verde50"), "Borda"));
        modelo.Triggers.Add(sobre);
        var foco = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        foco.Setters.Add(new Setter(Border.BorderBrushProperty, FindResource("Verde600"), "Borda"));
        modelo.Triggers.Add(foco);
        return modelo;
    }

    // ------------------------------------------------------------ Mensagem e gravação

    private string Mensagem(Fornecedor f) =>
        MensagemPedido.Montar(f,
            _itens.Select(i => new MensagemPedido.Linha(i.Produto.Nome,
                Formulario.LerQuantidade(i.Quantidade.Text, i.Produto.PorPeso) ?? 0, i.Produto.PorPeso, i.Produto.UnidadesPorCaixa)),
            Formulario.LerData(Previsao.Text), _servico.Usuario) +
        (Observacao.Text.Trim() is { Length: > 0 } obs ? "\n" + obs : "");

    private void AtualizarPrevia()
    {
        if (Previa is null) return;
        Previa.Text = Selecionado is { } f
            ? (_itens.Count == 0 ? "Adicione os produtos para ver a mensagem." : Mensagem(f))
            : "Escolha o fornecedor para ver a mensagem.";
        Previa.Foreground = (Brush)FindResource(Selecionado is not null && _itens.Count > 0 ? "Tinta" : "Suave");
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => _area.FecharJanela();

    private async void Salvar_Click(object sender, RoutedEventArgs e)
    {
        AvisoErro.Visibility = Visibility.Collapsed;
        var f = Selecionado;
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var previsao = Formulario.LerData(Previsao.Text);

        var erros = new List<Control>();
        FornecedorErro.Text = f is null ? "Escolha o fornecedor." : "";
        if (f is null) erros.Add(EscolhaFornecedor);
        ItensErro.Text = _itens.Count == 0 ? "Adicione pelo menos um produto." : "";
        if (_itens.Count == 0) erros.Add(Busca);
        foreach (var i in _itens)
        {
            var q = Formulario.LerQuantidade(i.Quantidade.Text, i.Produto.PorPeso);
            if (!Formulario.Validar(i.Quantidade, i.Erro, q > 0, i.Produto.PorPeso ? "Peso maior que 0, ex: 25,5." : "Número inteiro maior que 0."))
                erros.Add(i.Quantidade);
        }
        if (!Formulario.Validar(Previsao, PrevisaoErro, previsao is not null && previsao >= hoje,
                previsao is null ? "Use o formato dd/mm/aaaa." : "A previsão não pode ser no passado."))
            erros.Add(Previsao);

        if (erros.Count > 0)
        {
            erros[0].Focus();
            return;
        }

        var texto = Mensagem(f!);
        BotaoSalvar.IsEnabled = false;
        try
        {
            await _servico.CriarPedidoAsync(f!.Id, previsao, Observacao.Text.Trim(),
                _itens.Select(i => new ItemNovoPedido(i.Produto.Id, Formulario.LerQuantidade(i.Quantidade.Text, i.Produto.PorPeso)!.Value)).ToList());
            await _aoCriar($"Pedido para {f.Nome} criado ({Plural(_itens.Count, "item", "itens")}).", f, texto);
        }
        catch (ConexaoException ex)
        {
            AvisoErroTexto.Text = ex.Message;
            AvisoErro.Visibility = Visibility.Visible;
            BotaoSalvar.IsEnabled = true;
        }
    }

    private static string Plural(int n, string um, string varios) => $"{n} {(n == 1 ? um : varios)}";
}
