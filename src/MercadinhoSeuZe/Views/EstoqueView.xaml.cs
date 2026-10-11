using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Estoque do gerente (igual a estoque.html do site): produtos com lotes, quantidade restante e validade,
/// filtros, ordem e indicadores. Lê e grava no Supabase (database/003_semana3_estoque.sql).
/// </summary>
public partial class EstoqueView : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly AreaInternaView _area;
    private readonly EstoqueService? _servico;
    private readonly HashSet<long> _abertos = [];   // produtos com os lotes à mostra
    private List<Produto> _produtos = [];
    private string _filtro = "todos";
    private string _ordem = "nome";
    private bool _compacta;
    private bool _carregou;

    public EstoqueView(AreaInternaView area, EstoqueService? servico)
    {
        InitializeComponent();
        _area = area;
        _servico = servico;
        AjustarColunas(CabecalhoTabela);
        IniciarAtalhosDesenvolvimento();
        Loaded += async (_, _) => { if (!_carregou) await CarregarAsync(); };
    }

    // ------------------------------------------------------------ Dados

    private async Task CarregarAsync()
    {
        _carregou = true;
        AvisoErro.Visibility = Visibility.Collapsed;
        if (_servico is null)
        {
            MostrarErro("Sistema sem configuração do banco de dados. Veja o README.");
            return;
        }

        try
        {
            _produtos = await _servico.ListarAsync();
        }
        catch (ConexaoException ex)
        {
            MostrarErro(ex.Message);
            return;
        }
        Mostrar();
        Leitor.Focus();
    }

    private void MostrarErro(string texto)
    {
        AvisoErroTexto.Text = texto;
        AvisoErro.Visibility = Visibility.Visible;
        Resumo.Text = "Não foi possível carregar o estoque.";
    }

    private async void TentarDeNovo_Click(object sender, RoutedEventArgs e) => await CarregarAsync();

    /// <summary>Chamado pelas janelas de cadastro depois de gravar.</summary>
    public async Task RecarregarAsync(string mensagem)
    {
        _area.FecharJanela();
        _area.MostrarToast(mensagem);
        await CarregarAsync();
    }

    // ------------------------------------------------------------ Lista

    private void Mostrar()
    {
        var nBaixo = _produtos.Count(p => p.EstoqueBaixo);
        var nSem = _produtos.Count(p => p.SemEstoque);
        var nValidade = _produtos.Count(p => p.ValidadeProxima);
        var nAviso = _produtos.Count(p => p.AvisoAntecipado);

        KpiTotal.Text = _produtos.Count.ToString(PtBr);
        var unidades = _produtos.Where(p => !p.PorPeso).Sum(p => p.Quantidade);
        var quilos = _produtos.Where(p => p.PorPeso).Sum(p => p.Quantidade);
        KpiTotalSub.Text = (_produtos.Any(p => p.PorPeso)
            ? $"{Produto.FormatarQuantidade(unidades, false)} e {Produto.FormatarQuantidade(quilos, true)}"
            : Produto.FormatarQuantidade(unidades, false)) + " em estoque";
        KpiBaixo.Text = nBaixo.ToString(PtBr);
        KpiBaixoSub.Text = nAviso > 0 ? $"Abaixo do mínimo · +{nAviso} perto do mínimo" : "Abaixo do mínimo cadastrado";
        ContAviso.Text = nAviso.ToString(PtBr);
        KpiSem.Text = nSem.ToString(PtBr);
        KpiValidade.Text = nValidade.ToString(PtBr);
        ContTodos.Text = _produtos.Count.ToString(PtBr);
        ContBaixo.Text = nBaixo.ToString(PtBr);
        ContSem.Text = nSem.ToString(PtBr);
        ContValidade.Text = nValidade.ToString(PtBr);
        Resumo.Text = $"{Plural(_produtos.Count, "produto cadastrado", "produtos cadastrados")} · {nBaixo} com estoque baixo · {nAviso} para repor em breve · " +
                      $"{nSem} sem estoque · {nValidade} com validade próxima";

        var termo = Busca.Text.Trim();
        IEnumerable<Produto> lista = _produtos
            .Where(p => termo.Length == 0 || Contem(p.Nome, termo) || Contem(p.Categoria, termo))
            .Where(p => _filtro switch
            {
                "baixo" => p.EstoqueBaixo,
                "aviso" => p.AvisoAntecipado,
                "sem" => p.SemEstoque,
                "validade" => p.ValidadeProxima,
                _ => true
            });
        var porNome = StringComparer.Create(PtBr, CompareOptions.IgnoreCase);
        lista = _ordem switch
        {
            "validade" => lista.OrderBy(p => p.ProximaValidade ?? DateOnly.MaxValue).ThenBy(p => p.Nome, porNome),
            "quantidade" => lista.OrderBy(p => p.Quantidade).ThenBy(p => p.Nome, porNome),
            _ => lista.OrderBy(p => p.Nome, porNome)
        };

        Lista.Children.Clear();
        foreach (var p in lista)
            Lista.Children.Add(LinhaProduto(p));

        Vazio.Visibility = Lista.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Vazio.Text = _produtos.Count == 0 ? "Nenhum produto cadastrado ainda. Clique em “Novo produto” para começar."
            : termo.Length > 0 ? $"Nenhum produto encontrado para “{termo}”."
            : "Nenhum produto neste filtro.";
    }

    private FrameworkElement LinhaProduto(Produto p)
    {
        var aberto = _abertos.Contains(p.Id);
        var alerta = p.SemEstoque || p.EstoqueBaixo;
        var grade = new Grid { MinHeight = 56 };
        AjustarColunas(grade);

        // Seta para abrir os lotes
        var seta = new Button
        {
            Style = Recurso<Style>("LinkSuave"),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = aberto ? "Ocultar lotes" : "Ver lotes",
            Content = new ContentControl
            {
                Style = Recurso<Style>("Icone"),
                Tag = Recurso<Geometry>(aberto ? "IconeSetaBaixo" : "IconeSetaDireita"),
                Foreground = Recurso<Brush>("Suave"),
                Width = 18
            }
        };
        // BotaoLink desenha só texto; aqui o conteúdo é o ícone
        seta.Template = BotaoSoConteudo();
        System.Windows.Automation.AutomationProperties.SetName(seta, $"Ver lotes de {p.Nome}");
        seta.Click += (_, _) => AlternarLotes(p.Id);
        grade.Children.Add(seta);

        // Produto (+ etiqueta de validade)
        var nome = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 12, 8) };
        nome.Children.Add(new TextBlock { Text = p.Nome, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        if (p.PorPeso)
            nome.Children.Add(new Border
            {
                Background = Recurso<Brush>("Fundo"), BorderBrush = Recurso<Brush>("Linha"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center, ToolTip = "Vendido por quilo: estoque, mínimo e lotes em kg",
                Child = new TextBlock { Text = "por kg", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Recurso<Brush>("Suave") }
            });
        if (p.Situacao is { Alerta: true } sv)
            nome.Children.Add(Etiqueta(sv));
        if (_compacta)
            nome.Children.Add(new TextBlock { Text = $"{p.Categoria} · {p.Preco.ToString("C", PtBr)}{(p.PorPeso ? "/kg" : "")}", FontSize = 13, Foreground = Recurso<Brush>("Suave"), Margin = new Thickness(0, 2, 0, 0) });
        Colocar(grade, nome, 1);

        Colocar(grade, Celula(p.Categoria, suave: true), 2);

        // Quantidade (vermelha com bolinha quando baixa ou zerada)
        var qtd = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        if (alerta)
        {
            qtd.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = Recurso<Brush>("Vermelho600"), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            qtd.ToolTip = p.SemEstoque ? "Sem estoque" : $"Abaixo do mínimo ({p.Formatar(p.EstoqueMinimo)})";
        }
        if (p.AvisoAntecipado)
            qtd.Children.Add(SinalAviso(p));
        qtd.Children.Add(new TextBlock
        {
            Text = p.PorPeso ? p.Numero(p.Quantidade) + " kg" : p.Numero(p.Quantidade),
            FontWeight = alerta ? FontWeights.Bold : FontWeights.Normal,
            Foreground = alerta ? Recurso<Brush>("Vermelho600") : Recurso<Brush>("Tinta")
        });
        Colocar(grade, qtd, 3);

        Colocar(grade, Celula(p.ProximaValidade is { } v ? v.ToString("dd/MM/yyyy", PtBr) : "—", suave: p.ProximaValidade is null, margem: 20), 4);
        var preco = Celula(p.Preco.ToString("C", PtBr) + (p.PorPeso ? "/kg" : ""));
        preco.HorizontalAlignment = HorizontalAlignment.Right;
        Colocar(grade, preco, 5);

        // Ações
        var acoes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        if (!_compacta) acoes.Children.Add(Link(aberto ? "Ocultar" : "Lotes", () => AlternarLotes(p.Id)));
        acoes.Children.Add(Link("Editar", () => AbrirProduto(p)));
        acoes.Children.Add(Link("Excluir", () => ConfirmarExclusaoProduto(p), perigo: true));
        Colocar(grade, acoes, 6);

        var bloco = new StackPanel();
        bloco.Children.Add(grade);
        if (aberto) bloco.Children.Add(PainelLotes(p));

        return new Border
        {
            BorderBrush = Recurso<Brush>("Linha"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = aberto ? Recurso<Brush>("Fundo") : Brushes.Transparent,
            Child = bloco
        };
    }

    /// <summary>Lotes do produto, do que vence primeiro para o último.</summary>
    private FrameworkElement PainelLotes(Produto p)
    {
        var painel = new StackPanel { Margin = new Thickness(_compacta ? 8 : 36, 0, 8, 16) };

        var resumo = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        resumo.Children.Add(Detalhe("Em estoque", p.Formatar(p.Quantidade)));
        resumo.Children.Add(Detalhe("Já vendido", p.Formatar(p.Vendido)));
        resumo.Children.Add(Detalhe("Estoque mínimo", p.Formatar(p.EstoqueMinimo)));
        resumo.Children.Add(Detalhe("Aviso antecipado em", p.Formatar(p.LimiteAviso)));
        var porCaixa = $"{p.Numero(p.UnidadesPorCaixa)} {p.Sigla}";
        resumo.Children.Add(Detalhe("Caixa", p.CodigoBarrasCaixa is null ? $"{porCaixa} (sem código)" : $"{p.CodigoBarrasCaixa} · {porCaixa}"));
        painel.Children.Add(resumo);

        var lotes = p.Lotes
            .OrderBy(l => l.Esgotado)
            .ThenBy(l => l.Validade ?? DateOnly.MaxValue)
            .ThenBy(l => l.DataCompra)
            .ToList();

        if (lotes.Count == 0)
            painel.Children.Add(new TextBlock { Text = "Nenhum lote cadastrado. Adicione um lote quando a mercadoria chegar.", FontSize = 14, Foreground = Recurso<Brush>("Suave"), Margin = new Thickness(0, 0, 0, 10) });

        foreach (var l in lotes)
        {
            var linha = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var caixa = new Border
            {
                Background = Brushes.White,
                BorderBrush = Recurso<Brush>("Linha"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 10, 12, 10),
                Opacity = l.Esgotado ? 0.65 : 1,
                Child = linha
            };

            var acoes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            acoes.Children.Add(Link("Editar", () => AbrirLote(p, l)));
            acoes.Children.Add(Link("Excluir", () => ConfirmarExclusaoLote(p, l), perigo: true));
            DockPanel.SetDock(acoes, Dock.Right);
            linha.Children.Add(acoes);

            var info = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(BarraLote(p, l));
            info.Children.Add(Detalhe("Vendido", p.PorPeso ? p.Formatar(l.Vendido) : p.Numero(l.Vendido), menor: true));
            info.Children.Add(Detalhe("Compra", l.DataCompra.ToString("dd/MM/yyyy", PtBr), menor: true));
            info.Children.Add(Detalhe("Validade", l.Validade?.ToString("dd/MM/yyyy", PtBr) ?? "Sem validade", menor: true));
            if (!l.Esgotado && l.Situacao is { } sv)
            {
                var etiqueta = Etiqueta(sv, mostrarOk: true);
                etiqueta.Margin = new Thickness(0, 0, 0, 0);
                etiqueta.VerticalAlignment = VerticalAlignment.Center;
                info.Children.Add(etiqueta);
            }
            linha.Children.Add(info);
            painel.Children.Add(caixa);
        }

        var novo = new Button { Style = Recurso<Style>("BotaoFantasma"), Height = 38, FontSize = 14, Padding = new Thickness(14, 0, 14, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        var conteudo = new StackPanel { Orientation = Orientation.Horizontal };
        conteudo.Children.Add(new ContentControl { Style = Recurso<Style>("Icone"), Tag = Recurso<Geometry>("IconeMais"), Foreground = Recurso<Brush>("Verde700"), Width = 16, Margin = new Thickness(0, 0, 6, 0) });
        conteudo.Children.Add(new TextBlock { Text = "Adicionar lote" });
        novo.Content = conteudo;
        novo.Click += (_, _) => AbrirLote(p, null);
        painel.Children.Add(novo);
        return painel;
    }

    /// <summary>Sinal amarelo ao lado da quantidade; o texto explica o aviso ao passar o mouse.</summary>
    private Border SinalAviso(Produto p)
    {
        var sinal = new Border
        {
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new ContentControl { Style = Recurso<Style>("Icone"), Tag = Recurso<Geometry>("IconeTriangulo"), Foreground = Recurso<Brush>("Ambar700"), Width = 17 },
            ToolTip = new ToolTip
            {
                MaxWidth = 320,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = $"Aviso antecipado: restam {p.Formatar(p.Quantidade)} de {p.Nome}. " +
                           $"O mínimo é {p.Formatar(p.EstoqueMinimo)} e o aviso começa em {p.Formatar(p.LimiteAviso)}" +
                           (p.EstoqueAviso is null ? " (automático: mínimo + 50%)" : "") +
                           ". Faça o pedido agora para a mercadoria chegar antes de faltar, principalmente se o fornecedor demora para entregar."
                }
            }
        };
        ToolTipService.SetInitialShowDelay(sinal, 150);
        ToolTipService.SetShowDuration(sinal, 30000);
        System.Windows.Automation.AutomationProperties.SetName(sinal, "Aviso antecipado de estoque");
        return sinal;
    }

    /// <summary>"7/10" com uma barra do quanto ainda resta no lote.</summary>
    private StackPanel BarraLote(Produto p, Lote l)
    {
        var bloco = new StackPanel { Margin = new Thickness(0, 0, 24, 0), Width = 120 };
        bloco.Children.Add(new TextBlock { Text = "Restante", FontSize = 12, Foreground = Recurso<Brush>("Suave") });
        bloco.Children.Add(new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Text = (l.Esgotado ? "Esgotado " : "") + $"{p.Numero(l.QuantidadeRestante)}/{p.Numero(l.QuantidadeInicial)}" + (p.PorPeso ? " kg" : "")
        });
        var proporcao = l.QuantidadeInicial > 0 ? (double)(l.QuantidadeRestante / l.QuantidadeInicial) : 0;
        var barra = new Grid { Height = 6, Margin = new Thickness(0, 4, 0, 0) };
        barra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(proporcao, GridUnitType.Star) });
        barra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - proporcao, GridUnitType.Star) });
        var fundo = new Border { Background = Recurso<Brush>("Linha"), CornerRadius = new CornerRadius(3) };
        Grid.SetColumnSpan(fundo, 2);
        barra.Children.Add(fundo);
        barra.Children.Add(new Border { Background = Recurso<Brush>("Verde600"), CornerRadius = new CornerRadius(3) });
        bloco.Children.Add(barra);
        bloco.ToolTip = $"{p.Formatar(l.Vendido)} de {p.Formatar(l.QuantidadeInicial)} já saíram deste lote";
        return bloco;
    }

    private void AlternarLotes(long id)
    {
        if (!_abertos.Remove(id)) _abertos.Add(id);
        Mostrar();
    }

    // ------------------------------------------------------------ Leitor de código de barras

    /// <summary>O leitor digita o código e aperta Enter: abre a entrada de lote do produto daquela caixa.</summary>
    private void Leitor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        var codigo = Leitor.Text.Trim();
        Leitor.Clear();
        if (codigo.Length > 0) ProcessarCodigo(codigo);
    }

    /// <summary>
    /// Caixa conhecida abre a entrada de lote; desconhecida abre o cadastro com o código preenchido.
    /// Devolve a janela aberta (ou null).
    /// </summary>
    private UserControl? ProcessarCodigo(string codigo)
    {
        if (_servico is null) { _area.MostrarToast("Sistema sem configuração do banco de dados."); return null; }
        if (!_carregou || AvisoErro.Visibility == Visibility.Visible) { _area.MostrarToast("O estoque ainda não foi carregado."); return null; }

        var produto = _produtos.FirstOrDefault(p => p.CodigoBarrasCaixa?.Trim() == codigo);
        UserControl janela;
        if (produto is not null)
        {
            janela = new LoteJanela(this, _area, _servico, produto, null, pelaCaixa: true);
        }
        else
        {
            _area.MostrarToast($"O código {codigo} ainda não é de nenhum produto. Cadastre o produto desta caixa.");
            janela = new ProdutoJanela(this, _area, _servico, null, _produtos, codigo);
        }
        _area.AbrirJanela(janela);
        return janela;
    }

    private void Leitor_TextChanged(object sender, TextChangedEventArgs e) =>
        DicaLeitor.Visibility = Leitor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ------------------------------------------------------------ Filtros, busca e ordem

    private void Filtro_Click(object sender, RoutedEventArgs e)
    {
        _filtro = ((FrameworkElement)sender).Uid;
        foreach (Button chip in Filtros.Children)
            chip.Tag = chip.Uid == _filtro ? "ativo" : null;
        Mostrar();
    }

    private void Ordem_Click(object sender, RoutedEventArgs e)
    {
        _ordem = ((FrameworkElement)sender).Uid;
        foreach (var chip in Ordens.Children.OfType<Button>())
            chip.Tag = chip.Uid == _ordem ? "ativo" : null;
        Mostrar();
    }

    private void Busca_TextChanged(object sender, TextChangedEventArgs e)
    {
        DicaBusca.Visibility = Busca.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_carregou) Mostrar();
    }

    // ------------------------------------------------------------ Cadastro, edição e exclusão

    /// <summary>Atalho "Novo produto" do painel.</summary>
    public void NovoProduto() => AbrirProduto(null);

    private void NovoProduto_Click(object sender, RoutedEventArgs e) => AbrirProduto(null);

    private void AbrirProduto(Produto? produto)
    {
        if (_servico is null) { _area.MostrarToast("Sistema sem configuração do banco de dados."); return; }
        _area.AbrirJanela(new ProdutoJanela(this, _area, _servico, produto, _produtos));
    }

    private void AbrirLote(Produto produto, Lote? lote) =>
        _area.AbrirJanela(new LoteJanela(this, _area, _servico!, produto, lote));

    private void ConfirmarExclusaoProduto(Produto p) =>
        _area.AbrirJanela(new ConfirmacaoJanela(_area, "Excluir produto?",
            $"“{p.Nome}” e {Plural(p.Lotes.Count, "lote", "lotes")} serão removidos do estoque. Esta ação não pode ser desfeita.",
            async () =>
            {
                await _servico!.ExcluirProdutoAsync(p.Id);
                _abertos.Remove(p.Id);
                await RecarregarAsync($"“{p.Nome}” foi excluído.");
            }));

    private void ConfirmarExclusaoLote(Produto p, Lote l) =>
        _area.AbrirJanela(new ConfirmacaoJanela(_area, "Excluir lote?",
            $"O lote de {p.Nome} comprado em {l.DataCompra:dd/MM/yyyy} ({p.Formatar(l.QuantidadeRestante)} restantes) " +
            "será removido. Esta ação não pode ser desfeita.",
            async () =>
            {
                await _servico!.ExcluirLoteAsync(l.Id);
                await RecarregarAsync("Lote excluído.");
            }));

    // ------------------------------------------------------------ Peças da tabela

    private void AjustarColunas(Grid grade)
    {
        grade.ColumnDefinitions.Clear();
        GridLength[] larguras = _compacta
            ? [new(32), new(1, GridUnitType.Star), new(0), new(56), new(0), new(0), new(130)]
            : [new(36), new(2, GridUnitType.Star), new(1.1, GridUnitType.Star), new(70), new(1.2, GridUnitType.Star), new(100), new(200)];
        foreach (var l in larguras)
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = l });
    }

    private void AoRedimensionar(object sender, SizeChangedEventArgs e)
    {
        Kpis.Columns = e.NewSize.Width < 520 ? 1 : e.NewSize.Width < 900 ? 2 : 4;
        var compacta = e.NewSize.Width < 760;
        if (compacta == _compacta) return;
        _compacta = compacta;
        DockPanel.SetDock(TextoLeitor, compacta ? Dock.Top : Dock.Left);
        TextoLeitor.Margin = new Thickness(0, 0, 20, compacta ? 12 : 0);
        CaixaLeitor.Width = compacta ? double.NaN : 360;
        CaixaLeitor.HorizontalAlignment = compacta ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        AjustarColunas(CabecalhoTabela);
        foreach (TextBlock t in CabecalhoTabela.Children)
            t.Visibility = _compacta && Grid.GetColumn(t) is 2 or 4 or 5 ? Visibility.Collapsed : Visibility.Visible;
        if (_carregou) Mostrar();
    }

    private static void Colocar(Grid grade, UIElement elemento, int coluna)
    {
        Grid.SetColumn(elemento, coluna);
        grade.Children.Add(elemento);
    }

    private TextBlock Celula(string texto, bool suave = false, double margem = 0) => new()
    {
        Text = texto,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = Recurso<Brush>(suave ? "Suave" : "Tinta"),
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        Margin = new Thickness(margem, 0, 8, 0)
    };

    /// <summary>Etiqueta de validade (igual a .badge-warn / .badge-danger do site).</summary>
    private Border Etiqueta(SituacaoValidade sv, bool mostrarOk = false)
    {
        var (fundo, cor) = sv.Nivel switch
        {
            NivelValidade.Vencido => ("Vermelho50", "Vermelho600"),
            NivelValidade.Atencao => ("Ambar50", "Ambar700"),
            _ => ("Verde50", "Verde700")
        };
        return new Border
        {
            Background = Recurso<Brush>(fundo),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = sv.Alerta || mostrarOk ? Visibility.Visible : Visibility.Collapsed,
            Child = new TextBlock { Text = sv.Texto, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Recurso<Brush>(cor) }
        };
    }

    private StackPanel Detalhe(string rotulo, string valor, bool menor = false)
    {
        var bloco = new StackPanel { Margin = new Thickness(0, 0, menor ? 24 : 32, menor ? 0 : 4) };
        bloco.Children.Add(new TextBlock { Text = rotulo, FontSize = 12, Foreground = Recurso<Brush>("Suave") });
        bloco.Children.Add(new TextBlock { Text = valor, FontSize = menor ? 14 : 15, FontWeight = FontWeights.SemiBold });
        return bloco;
    }

    private Button Link(string texto, Action acao, bool perigo = false)
    {
        var botao = new Button
        {
            Content = texto,
            Style = Recurso<Style>("BotaoLink"),
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        if (perigo) botao.Foreground = Recurso<Brush>("Vermelho600");
        botao.Click += (_, _) => acao();
        return botao;
    }

    private static ControlTemplate BotaoSoConteudo()
    {
        var borda = new FrameworkElementFactory(typeof(Border));
        borda.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        borda.SetValue(Border.PaddingProperty, new Thickness(4));
        borda.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        return new ControlTemplate(typeof(Button)) { VisualTree = borda };
    }

    /// <summary>Alt+2 (leitor simulado) em EstoqueView.Desenvolvimento.cs; só existe na versão de desenvolvimento.</summary>
    partial void IniciarAtalhosDesenvolvimento();

    private T Recurso<T>(string chave) => (T)FindResource(chave);

    private static bool Contem(string texto, string termo) =>
        PtBr.CompareInfo.IndexOf(texto, termo, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private static string Plural(int n, string um, string varios) => $"{n} {(n == 1 ? um : varios)}";
}
