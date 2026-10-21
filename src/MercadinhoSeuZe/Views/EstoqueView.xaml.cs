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
    private readonly FornecedorService? _fornecedores;
    private readonly OpenFoodFactsService _bancoAberto = new();
    private bool _procurandoCodigo;
    private readonly HashSet<long> _abertos = [];   // produtos com os lotes à mostra
    private List<Produto> _produtos = [];
    private List<Fornecedor>? _listaFornecedores;   // null = tabela de fornecedores ainda não existe
    private List<Pedido> _pendentes = [];
    private string _filtro = "todos";
    private string? _categoria;          // null = todas
    private string _tipo = "todos";      // todos, un (por unidade) ou kg (por quilo)
    private string _ordem = "nome";
    private bool _compacta;
    private bool _carregou;

    public EstoqueView(AreaInternaView area, EstoqueService? servico, FornecedorService? fornecedores)
    {
        InitializeComponent();
        _area = area;
        _servico = servico;
        _fornecedores = fornecedores;
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
        await CarregarFornecedoresAsync();
        Mostrar();
        Leitor.Focus();
    }

    /// <summary>
    /// Fornecedores (campo do cadastro) e pedidos pendentes (aviso na entrada de lote). Sem o script 004 rodado,
    /// o estoque funciona como antes, só sem essas partes.
    /// </summary>
    private async Task CarregarFornecedoresAsync()
    {
        if (_fornecedores is null || _servico is not { TemFornecedores: true }) { _listaFornecedores = null; return; }
        try
        {
            var lista = _fornecedores.ListarAsync();
            var pendentes = _fornecedores.ListarPendentesAsync();
            await Task.WhenAll(lista, pendentes);
            _listaFornecedores = lista.Result;
            _pendentes = pendentes.Result;
            Fornecedor.AplicarPrazos(_produtos, _listaFornecedores);
        }
        catch (ConexaoException)
        {
            _listaFornecedores = null;
            _pendentes = [];
        }
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
        var nVencido = _produtos.Count(p => p.Vencido);
        var nAviso = _produtos.Count(p => p.AvisoAntecipado);

        KpiTotal.Text = _produtos.Count.ToString(PtBr);
        var unidades = _produtos.Where(p => !p.PorPeso).Sum(p => p.Quantidade);
        var quilos = _produtos.Where(p => p.PorPeso).Sum(p => p.Quantidade);
        KpiTotalSub.Text = (_produtos.Any(p => p.PorPeso)
            ? $"{Produto.FormatarQuantidade(unidades, false)} e {Produto.FormatarQuantidade(quilos, true)}"
            : Produto.FormatarQuantidade(unidades, false)) + " em estoque";
        KpiBaixo.Text = nBaixo.ToString(PtBr);
        KpiBaixoSub.Text = nAviso > 0 ? $"Abaixo do mínimo · +{nAviso} perto do mínimo" : "Abaixo do mínimo cadastrado";
        KpiSem.Text = nSem.ToString(PtBr);
        KpiValidade.Text = nValidade.ToString(PtBr);
        KpiVencido.Text = nVencido.ToString(PtBr);
        Resumo.Text = $"{Plural(_produtos.Count, "produto cadastrado", "produtos cadastrados")} · {nBaixo} com estoque baixo · {nAviso} para repor em breve · " +
                      $"{nSem} sem estoque · {nValidade} com validade próxima · {nVencido} vencidos";

        var termo = Busca.Text.Trim();
        IEnumerable<Produto> lista = _produtos
            .Where(p => PassaBusca(p, termo) && PassaSituacao(p, _filtro) && PassaCategoria(p, _categoria) && PassaTipo(p, _tipo))
            .ToList();
        MostrarFiltrosAtivos(termo, ((List<Produto>)lista).Count);
        var porNome = StringComparer.Create(PtBr, CompareOptions.IgnoreCase);
        lista = _ordem switch
        {
            "validade" => lista.OrderBy(p => p.ProximaValidade ?? DateOnly.MaxValue).ThenBy(p => p.Nome, porNome),
            "quantidade" => lista.OrderBy(p => p.Quantidade).ThenBy(p => p.Nome, porNome),
            "valor" => lista.OrderByDescending(p => p.Quantidade * p.Preco).ThenBy(p => p.Nome, porNome),
            "preco" => lista.OrderBy(p => p.Preco).ThenBy(p => p.Nome, porNome),
            _ => lista.OrderBy(p => p.Nome, porNome)
        };

        Lista.Children.Clear();
        foreach (var p in lista)
            Lista.Children.Add(LinhaProduto(p));

        Vazio.Visibility = Lista.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Vazio.Text = _produtos.Count == 0 ? "Nenhum produto cadastrado ainda. Clique em “Novo produto” para começar."
            : termo.Length > 0 ? $"Nenhum produto encontrado para “{termo}”."
            : "Nenhum produto com esses filtros.";
    }

    // ------------------------------------------------------------ Filtros dinâmicos

    private static bool PassaBusca(Produto p, string termo) =>
        termo.Length == 0 || Contem(p.Nome, termo) || Contem(p.Categoria, termo) || (p.CodigoBarrasCaixa?.Contains(termo) ?? false);

    private static bool PassaSituacao(Produto p, string filtro) => filtro switch
    {
        "baixo" => p.EstoqueBaixo,
        "aviso" => p.AvisoAntecipado,
        "sem" => p.SemEstoque,
        "validade" => p.ValidadeProxima,
        "vencido" => p.Vencido,
        "semcodigo" => string.IsNullOrWhiteSpace(p.CodigoBarrasCaixa),
        _ => true
    };

    private static bool PassaCategoria(Produto p, string? categoria) =>
        categoria is null || string.Equals(p.Categoria.Trim(), categoria, StringComparison.CurrentCultureIgnoreCase);

    private static bool PassaTipo(Produto p, string tipo) => tipo switch
    {
        "un" => !p.PorPeso,
        "kg" => p.PorPeso,
        _ => true
    };

    private FrameworkElement LinhaProduto(Produto p)
    {
        var aberto = _abertos.Contains(p.Id);
        var alerta = p.SemEstoque || p.EstoqueBaixo;
        var grade = new Grid { MinHeight = 54 };
        AjustarColunas(grade);

        // Uma seta só: abre e fecha o drop-down (lotes, validade, editar e excluir)
        var seta = new Button
        {
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = aberto ? "Fechar detalhes" : "Ver lotes, validade e opções",
            Template = BotaoSoConteudo(),
            Content = new ContentControl
            {
                Style = Recurso<Style>("Icone"),
                Tag = Recurso<Geometry>(aberto ? "IconeSetaBaixo" : "IconeSetaDireita"),
                Foreground = Recurso<Brush>(aberto ? "Verde700" : "Suave"),
                Width = 18
            }
        };
        System.Windows.Automation.AutomationProperties.SetName(seta, aberto ? $"Fechar detalhes de {p.Nome}" : $"Abrir detalhes de {p.Nome}");
        seta.Click += (_, _) => AlternarLotes(p.Id);
        seta.HorizontalAlignment = HorizontalAlignment.Right;
        Colocar(grade, seta, 5);

        // Antes da seta: alerta de validade (vermelho = vencido, amarelo = vence em até 30 dias)
        if (SinalValidade(p.Situacao) is { } sinalValidade)
        {
            sinalValidade.HorizontalAlignment = HorizontalAlignment.Center;
            Colocar(grade, sinalValidade, 4);
        }

        // Produto
        var nome = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 8, 12, 8) };
        nome.Children.Add(new TextBlock { Text = p.Nome, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        if (p.PorPeso)
            nome.Children.Add(new Border
            {
                Background = Recurso<Brush>("Fundo"), BorderBrush = Recurso<Brush>("Linha"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center, ToolTip = "Vendido por quilo: estoque, mínimo e lotes em kg",
                Child = new TextBlock { Text = "por kg", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Recurso<Brush>("Suave") }
            });
        if (_compacta)
        {
            var categoria = CategoriaVisual.Pilula(this, p.Categoria, 12);
            categoria.Margin = new Thickness(0, 2, 8, 0);
            nome.Children.Add(categoria);
            nome.Children.Add(new TextBlock { Text = $"{p.Preco.ToString("C", PtBr)}{(p.PorPeso ? "/kg" : "")}", FontSize = 13, Foreground = Recurso<Brush>("Suave"), Margin = new Thickness(0, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        Colocar(grade, nome, 0);

        Colocar(grade, CategoriaVisual.Pilula(this, p.Categoria), 1);

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
        Colocar(grade, qtd, 2);

        var preco = Celula(p.Preco.ToString("C", PtBr) + (p.PorPeso ? "/kg" : ""));
        preco.HorizontalAlignment = HorizontalAlignment.Right;
        Colocar(grade, preco, 3);

        // Clicar em qualquer parte da linha também abre e fecha
        var linha = new Border { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Child = grade };
        linha.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not System.Windows.Documents.Run) AlternarLotes(p.Id); };

        var bloco = new StackPanel();
        bloco.Children.Add(linha);
        if (aberto) bloco.Children.Add(PainelLotes(p));

        return new Border
        {
            BorderBrush = Recurso<Brush>("Linha"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = aberto ? Recurso<Brush>("Fundo") : Brushes.Transparent,
            Child = bloco
        };
    }

    /// <summary>Drop-down do produto: resumo, próxima validade, editar/excluir e os lotes.</summary>
    private FrameworkElement PainelLotes(Produto p)
    {
        var painel = new StackPanel { Margin = new Thickness(_compacta ? 4 : 12, 0, 8, 16) };

        var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var acoes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        acoes.Children.Add(BotaoPequeno("Editar produto", "IconeFerramenta", "Verde700", () => AbrirProduto(p)));
        acoes.Children.Add(BotaoPequeno("Excluir", "IconeLixeira", "Vermelho600", () => ConfirmarExclusaoProduto(p)));
        DockPanel.SetDock(acoes, Dock.Right);
        topo.Children.Add(acoes);

        var resumo = new WrapPanel();
        resumo.Children.Add(Detalhe("Em estoque", p.Formatar(p.Quantidade)));
        resumo.Children.Add(Detalhe("Já vendido", p.Formatar(p.Vendido)));
        resumo.Children.Add(DetalheValidade("Próxima validade", p.ProximaValidade, p.Situacao));
        resumo.Children.Add(Detalhe("Estoque mínimo", p.Formatar(p.EstoqueMinimo)));
        resumo.Children.Add(Detalhe("Aviso antecipado em", p.Formatar(p.LimiteAviso) + (p.PontoDePedido is null ? "" : " (calculado)")));
        if (_listaFornecedores is not null)
        {
            var fornecedor = _listaFornecedores.FirstOrDefault(f => f.Id == p.FornecedorId);
            var daCategoria = fornecedor is null ? _listaFornecedores.Where(f => f.Ativo && f.Fornece(p)).Select(f => f.Nome).ToList() : [];
            resumo.Children.Add(Detalhe("Fornecedor", fornecedor?.Nome
                ?? (daCategoria.Count > 0 ? string.Join(", ", daCategoria) + " (pela categoria)" : "nenhum")));
            resumo.Children.Add(Detalhe("Prazo de entrega", p.PrazoEntregaDias is { } prazo
                ? Plural(prazo, "dia", "dias") : "padrão do fornecedor"));
        }
        var porCaixa = $"{p.Numero(p.UnidadesPorCaixa)} {p.Sigla}";
        resumo.Children.Add(Detalhe("Caixa", p.CodigoBarrasCaixa is null ? $"{porCaixa} (sem código)" : $"{p.CodigoBarrasCaixa} · {porCaixa}"));
        topo.Children.Add(resumo);
        painel.Children.Add(topo);

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

            var acoesLote = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            acoesLote.Children.Add(Link("Editar", () => AbrirLote(p, l)));
            acoesLote.Children.Add(Link("Excluir", () => ConfirmarExclusaoLote(p, l), perigo: true));
            DockPanel.SetDock(acoesLote, Dock.Right);
            linha.Children.Add(acoesLote);

            var info = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(BarraLote(p, l));
            info.Children.Add(Detalhe("Vendido", p.PorPeso ? p.Formatar(l.Vendido) : p.Numero(l.Vendido), menor: true));
            info.Children.Add(Detalhe("Compra", l.DataCompra.ToString("dd/MM/yyyy", PtBr), menor: true));
            info.Children.Add(DetalheValidade("Validade", l.Validade, l.Esgotado ? null : l.Situacao, menor: true));
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

    /// <summary>
    /// Data de validade com o símbolo de alerta ao lado: vermelho se já venceu, amarelo se vence em até 30 dias.
    /// Passar o mouse mostra há quantos dias venceu ou em quantos dias vence.
    /// </summary>
    private StackPanel DetalheValidade(string rotulo, DateOnly? validade, SituacaoValidade? situacao, bool menor = false)
    {
        var bloco = new StackPanel { Margin = new Thickness(0, 0, menor ? 24 : 32, menor ? 0 : 4) };
        bloco.Children.Add(new TextBlock { Text = rotulo, FontSize = 12, Foreground = Recurso<Brush>("Suave") });
        var valor = new StackPanel { Orientation = Orientation.Horizontal };
        valor.Children.Add(new TextBlock
        {
            Text = validade?.ToString("dd/MM/yyyy", PtBr) ?? "Sem validade",
            FontSize = menor ? 14 : 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (SinalValidade(situacao) is { } sinal)
        {
            sinal.Margin = new Thickness(6, 0, 0, 0);
            valor.Children.Add(sinal);
        }
        bloco.Children.Add(valor);
        return bloco;
    }

    /// <summary>Triângulo de alerta de validade; null quando não há alerta. O texto ao passar o mouse diz os dias.</summary>
    private Border? SinalValidade(SituacaoValidade? situacao)
    {
        if (situacao is not { Alerta: true }) return null;
        var vencido = situacao.Nivel == NivelValidade.Vencido;
        var dias = Math.Abs(situacao.Dias);
        var explicacao = vencido
            ? $"Vencido: passou da validade há {dias} {(dias == 1 ? "dia" : "dias")}. Tire da venda."
            : situacao.Dias == 0 ? "Vence hoje."
            : $"Validade próxima: vence em {dias} {(dias == 1 ? "dia" : "dias")}.";
        var sinal = new Border
        {
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = explicacao,
            Child = new ContentControl
            {
                Style = Recurso<Style>("Icone"),
                Tag = Recurso<Geometry>("IconeTriangulo"),
                Foreground = Recurso<Brush>(vencido ? "Vermelho600" : "Ambar700"),
                Width = 17
            }
        };
        ToolTipService.SetInitialShowDelay(sinal, 150);
        System.Windows.Automation.AutomationProperties.SetName(sinal, explicacao);
        return sinal;
    }

    private Button BotaoPequeno(string texto, string icone, string cor, Action acao)
    {
        var conteudo = new StackPanel { Orientation = Orientation.Horizontal };
        conteudo.Children.Add(new ContentControl { Style = Recurso<Style>("Icone"), Tag = Recurso<Geometry>(icone), Foreground = Recurso<Brush>(cor), Width = 15, Margin = new Thickness(0, 0, 6, 0) });
        conteudo.Children.Add(new TextBlock { Text = texto });
        var botao = new Button
        {
            Style = Recurso<Style>("BotaoFantasma"), Height = 34, FontSize = 14, Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(8, 0, 0, 0), Foreground = Recurso<Brush>(cor), Content = conteudo
        };
        botao.Click += (_, _) => acao();
        return botao;
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
                           (p.PontoDePedido is not null
                               ? $" (pedir antes de acabar: sai {p.Formatar(Math.Round(p.ConsumoDiario!.Value, 1))} por dia e a entrega leva {p.PrazoReposicao} dias)"
                               : p.EstoqueAviso is null ? " (automático: mínimo + 50%)" : "") +
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
        if (codigo.Length > 0) _ = LerCodigoAsync(codigo);
    }

    /// <summary>
    /// Código desconhecido: antes de abrir o cadastro, procura o produto no Open Food Facts para já vir com
    /// nome e categoria. Sem internet ou sem resultado, abre o cadastro como antes.
    /// </summary>
    private async Task LerCodigoAsync(string codigo)
    {
        if (_procurandoCodigo) return;
        var conhecido = _produtos.Any(p => p.CodigoBarrasCaixa?.Trim() == codigo);
        if (conhecido || _servico is null || !_carregou || AvisoErro.Visibility == Visibility.Visible)
        {
            ProcessarCodigo(codigo);
            return;
        }

        _procurandoCodigo = true;
        Leitor.IsEnabled = false;
        DicaLeitor.Text = "Procurando o produto pelo código…";
        try
        {
            var encontrado = await _bancoAberto.ProcurarAsync(codigo);
            ProcessarCodigo(codigo, encontrado);
        }
        finally
        {
            _procurandoCodigo = false;
            Leitor.IsEnabled = true;
            DicaLeitor.Text = "Código de barras da caixa";
        }
    }

    /// <summary>
    /// Caixa conhecida abre a entrada de lote; desconhecida abre o cadastro com o código preenchido.
    /// Devolve a janela aberta (ou null).
    /// </summary>
    private UserControl? ProcessarCodigo(string codigo, ProdutoEncontrado? encontrado = null)
    {
        if (_servico is null) { _area.MostrarToast("Sistema sem configuração do banco de dados."); return null; }
        if (!_carregou || AvisoErro.Visibility == Visibility.Visible) { _area.MostrarToast("O estoque ainda não foi carregado."); return null; }

        var produto = _produtos.FirstOrDefault(p => p.CodigoBarrasCaixa?.Trim() == codigo);
        UserControl janela;
        if (produto is not null)
        {
            var pedido = _pendentes.FirstOrDefault(o => o.Itens.Any(i => i.ProdutoId == produto.Id));
            var fornecedorPedido = pedido is null ? null : _listaFornecedores?.FirstOrDefault(f => f.Id == pedido.FornecedorId);
            janela = new LoteJanela(this, _area, _servico, produto, null, pelaCaixa: true, pedido, fornecedorPedido?.Nome,
                pedido is null || _fornecedores is null ? null : () => _area.AbrirJanela(
                    new EntregaJanela(_area, _fornecedores, pedido, fornecedorPedido?.Nome ?? "", _produtos, RecarregarAsync)));
        }
        else
        {
            _area.MostrarToast(encontrado is null
                ? $"O código {codigo} ainda não é de nenhum produto. Cadastre o produto desta caixa."
                : $"Produto novo: {encontrado.Nome}. Confira o cadastro.");
            janela = new ProdutoJanela(this, _area, _servico, null, _produtos, codigo, encontrado, _listaFornecedores);
        }
        _area.AbrirJanela(janela);
        return janela;
    }

    private void Leitor_TextChanged(object sender, TextChangedEventArgs e) =>
        DicaLeitor.Visibility = Leitor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ------------------------------------------------------------ Filtros, busca e ordem

    /// <summary>Indicadores do topo: clicar filtra pela situação.</summary>
    private void Filtro_Click(object sender, RoutedEventArgs e)
    {
        _filtro = ((FrameworkElement)sender).Uid;
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
        _area.AbrirJanela(new ProdutoJanela(this, _area, _servico, produto, _produtos, fornecedores: _listaFornecedores));
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
            ? [new(1, GridUnitType.Star), new(0), new(96), new(0), new(28), new(32)]
            : [new(2.2, GridUnitType.Star), new(1.3, GridUnitType.Star), new(110), new(130), new(36), new(40)];
        foreach (var l in larguras)
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = l });
    }

    private void AoRedimensionar(object sender, SizeChangedEventArgs e)
    {
        // Cinco quadrados numa fileira: diminuem junto com a janela (no máximo 140 px)
        var lado = Math.Max(96, Math.Min(140, (e.NewSize.Width - 5 * 12) / 5));
        foreach (var kpi in Kpis.Children.OfType<Button>())
            kpi.Width = kpi.Height = lado;
        var compacta = e.NewSize.Width < 760;
        if (compacta == _compacta) return;
        _compacta = compacta;
        DockPanel.SetDock(TextoLeitor, compacta ? Dock.Top : Dock.Left);
        TextoLeitor.Margin = new Thickness(0, 0, 20, compacta ? 12 : 0);
        CaixaLeitor.Width = compacta ? double.NaN : 360;
        CaixaLeitor.HorizontalAlignment = compacta ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        AjustarColunas(CabecalhoTabela);
        foreach (TextBlock t in CabecalhoTabela.Children)
            t.Visibility = _compacta && Grid.GetColumn(t) is 1 or 3 ? Visibility.Collapsed : Visibility.Visible;
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
