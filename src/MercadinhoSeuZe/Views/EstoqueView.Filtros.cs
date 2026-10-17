using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Janela de filtros do estoque (situação, categoria, tipo e ordem) e as etiquetas dos filtros em uso.
/// Na janela as escolhas só valem ao clicar em "Mostrar N produtos"; o número já mostra quantos vão aparecer.
/// </summary>
public partial class EstoqueView
{
    private sealed record OpcaoFiltro(string Id, string Nome, string Icone, string Fundo, string Cor);

    private static readonly OpcaoFiltro[] Situacoes =
    [
        new("todos", "Todos", "IconeCaixaProduto", "Verde50", "Verde700"),
        new("baixo", "Estoque baixo", "IconeRelogio", "Ambar50", "Ambar700"),
        new("aviso", "Repor em breve", "IconeTriangulo", "Ambar50", "Ambar700"),
        new("sem", "Sem estoque", "IconeAlerta", "Vermelho50", "Vermelho600"),
        new("validade", "Validade próxima", "IconeTriangulo", "Ambar50", "Ambar700"),
        new("vencido", "Vencidos", "IconeTriangulo", "Vermelho50", "Vermelho600"),
        new("semcodigo", "Sem código de barras", "IconeCodigoBarras", "Fundo", "Suave"),
    ];

    private static readonly OpcaoFiltro[] Tipos =
    [
        new("todos", "Todos", "IconeCaixaProduto", "Verde50", "Verde700"),
        new("un", "Por unidade", "IconeCaixaProduto", "Azul50", "Azul600"),
        new("kg", "Por quilo (kg)", "IconeBalanca", "Vermelho50", "Vermelho600"),
    ];

    private static readonly (string Id, string Nome)[] Ordens =
    [
        ("nome", "Nome (A–Z)"), ("validade", "Validade mais próxima"), ("quantidade", "Menor quantidade"),
        ("valor", "Maior valor em estoque"), ("preco", "Menor preço"),
    ];

    // ------------------------------------------------------------ Filtros em uso, na tela

    private void MostrarFiltrosAtivos(string termo, int mostrando)
    {
        Mostrando.Text = mostrando == _produtos.Count
            ? $"Mostrando todos os {_produtos.Count} produtos"
            : $"Mostrando {mostrando} de {_produtos.Count} produtos";

        while (FiltrosAtivos.Children.Count > 1) FiltrosAtivos.Children.RemoveAt(1);
        var ativos = 0;
        if (_filtro != "todos" && Situacoes.FirstOrDefault(s => s.Id == _filtro) is { } sit)
        {
            ativos++;
            FiltrosAtivos.Children.Add(EtiquetaAtiva(sit.Nome, sit.Icone, sit.Fundo, sit.Cor, () => _filtro = "todos"));
        }
        if (_categoria is not null)
        {
            ativos++;
            var v = CategoriaVisual.De(_categoria);
            FiltrosAtivos.Children.Add(EtiquetaAtiva(_categoria, v.Icone, v.Fundo, v.Cor, () => _categoria = null));
        }
        if (_tipo != "todos" && Tipos.FirstOrDefault(t => t.Id == _tipo) is { } tipo)
        {
            ativos++;
            FiltrosAtivos.Children.Add(EtiquetaAtiva(tipo.Nome, tipo.Icone, tipo.Fundo, tipo.Cor, () => _tipo = "todos"));
        }
        if (_ordem != "nome")
            FiltrosAtivos.Children.Add(EtiquetaAtiva("Ordem: " + Ordens.First(o => o.Id == _ordem).Nome, "IconeSetaBaixo", "Fundo", "Suave", () => _ordem = "nome"));

        ContadorFiltrosTexto.Text = ativos.ToString(PtBr);
        ContadorFiltros.Visibility = ativos > 0 ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(BotaoFiltros, ativos > 0 ? $"Abrir filtros ({ativos} em uso)" : "Abrir filtros");
        LimparFiltros.Visibility = ativos > 0 || _ordem != "nome" || termo.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Etiqueta de um filtro em uso; o × tira só esse filtro.</summary>
    private Border EtiquetaAtiva(string texto, string icone, string fundo, string cor, Action tirar)
    {
        var linha = new StackPanel { Orientation = Orientation.Horizontal };
        linha.Children.Add(new ContentControl { Style = Recurso<Style>("Icone"), Tag = Recurso<Geometry>(icone), Foreground = Recurso<Brush>(cor), Width = 15, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        linha.Children.Add(new TextBlock { Text = texto, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Recurso<Brush>(cor), VerticalAlignment = VerticalAlignment.Center });
        var fechar = new Button { Content = "×", Style = Recurso<Style>("LinkSuave"), FontSize = 16, Margin = new Thickness(8, -2, 0, 0), ToolTip = "Tirar este filtro" };
        System.Windows.Automation.AutomationProperties.SetName(fechar, $"Tirar o filtro {texto}");
        fechar.Click += (_, _) => { tirar(); Mostrar(); };
        linha.Children.Add(fechar);
        return new Border
        {
            Background = Recurso<Brush>(fundo),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 4, 8, 4),
            Margin = new Thickness(0, 0, 8, 8),
            Child = linha
        };
    }

    private void LimparFiltros_Click(object sender, RoutedEventArgs e)
    {
        _filtro = "todos";
        _categoria = null;
        _tipo = "todos";
        _ordem = "nome";
        Busca.Clear();   // dispara Mostrar()
        Mostrar();
    }

    // ------------------------------------------------------------ Janela de filtros

    private void AbrirFiltros_Click(object sender, RoutedEventArgs e) => _area.AbrirJanela(CriarJanelaFiltros());

    private FrameworkElement CriarJanelaFiltros()
    {
        // Escolhas da janela; só passam a valer em "Mostrar N produtos"
        var filtro = _filtro;
        var categoria = _categoria;
        var tipo = _tipo;
        var ordem = _ordem;
        var termo = Busca.Text.Trim();

        var corpo = new StackPanel();
        var mostrar = new Button { Style = Recurso<Style>("BotaoPrimario"), IsDefault = true };
        var raiz = new UserControl { Width = 680 };

        void Montar()
        {
            corpo.Children.Clear();

            // Situação
            var baseSituacao = _produtos.Where(p => PassaBusca(p, termo) && PassaCategoria(p, categoria) && PassaTipo(p, tipo)).ToList();
            corpo.Children.Add(TituloSecao("Situação"));
            var situacoes = new WrapPanel();
            foreach (var s in Situacoes)
            {
                var n = baseSituacao.Count(p => PassaSituacao(p, s.Id));
                if (n > 0 || s.Id == "todos" || s.Id == filtro)
                    situacoes.Children.Add(OpcaoJanela(s.Nome, n, s.Icone, s.Fundo, s.Cor, filtro == s.Id, () => filtro = s.Id));
            }
            corpo.Children.Add(situacoes);

            // Categoria (cores e ícones por categoria)
            var porNome = StringComparer.Create(PtBr, CompareOptions.IgnoreCase);
            var categorias = _produtos.Select(p => p.Categoria.Trim()).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(c => c, porNome).ToList();
            if (categorias.Count > 1)
            {
                var baseCategoria = _produtos.Where(p => PassaBusca(p, termo) && PassaSituacao(p, filtro) && PassaTipo(p, tipo)).ToList();
                corpo.Children.Add(TituloSecao("Categoria"));
                var lista = new WrapPanel();
                lista.Children.Add(OpcaoJanela("Todas", baseCategoria.Count, "IconeCaixaProduto", "Verde50", "Verde700", categoria is null, () => categoria = null));
                foreach (var c in categorias)
                {
                    var n = baseCategoria.Count(p => PassaCategoria(p, c));
                    var marcada = string.Equals(categoria, c, StringComparison.CurrentCultureIgnoreCase);
                    var v = CategoriaVisual.De(c);
                    if (n > 0 || marcada)
                        lista.Children.Add(OpcaoJanela(c, n, v.Icone, v.Fundo, v.Cor, marcada, () => categoria = c));
                }
                corpo.Children.Add(lista);
            }

            // Tipo (só se existirem os dois)
            if (_produtos.Any(p => p.PorPeso) && _produtos.Any(p => !p.PorPeso))
            {
                var baseTipo = _produtos.Where(p => PassaBusca(p, termo) && PassaSituacao(p, filtro) && PassaCategoria(p, categoria)).ToList();
                corpo.Children.Add(TituloSecao("Tipo"));
                var tipos = new WrapPanel();
                foreach (var t in Tipos)
                {
                    var n = baseTipo.Count(p => PassaTipo(p, t.Id));
                    if (n > 0 || t.Id == "todos" || t.Id == tipo)
                        tipos.Children.Add(OpcaoJanela(t.Nome, n, t.Icone, t.Fundo, t.Cor, tipo == t.Id, () => tipo = t.Id));
                }
                corpo.Children.Add(tipos);
            }

            // Ordem
            corpo.Children.Add(TituloSecao("Ordenar por"));
            var ordens = new WrapPanel();
            foreach (var (id, nome) in Ordens)
                ordens.Children.Add(OpcaoJanela(nome, null, null, null, null, ordem == id, () => ordem = id));
            corpo.Children.Add(ordens);

            var total = _produtos.Count(p => PassaBusca(p, termo) && PassaSituacao(p, filtro) && PassaCategoria(p, categoria) && PassaTipo(p, tipo));
            mostrar.Content = total == 1 ? "Mostrar 1 produto" : $"Mostrar {total} produtos";
        }

        Button OpcaoJanela(string nome, int? n, string? icone, string? fundo, string? cor, bool marcada, Action escolher)
        {
            var linha = new StackPanel { Orientation = Orientation.Horizontal };
            if (icone is not null)
                linha.Children.Add(new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(-6, 0, 8, 0),
                    Background = Recurso<Brush>(fundo!),
                    Child = new ContentControl { Style = Recurso<Style>("Icone"), Tag = Recurso<Geometry>(icone), Foreground = Recurso<Brush>(cor!), Width = 15 }
                });
            linha.Children.Add(new TextBlock { Text = nome, VerticalAlignment = VerticalAlignment.Center });
            if (n is not null)
                linha.Children.Add(new TextBlock { Text = n.Value.ToString(PtBr), FontWeight = FontWeights.Bold, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            if (marcada)
                linha.Children.Add(new TextBlock { Text = "✓", FontWeight = FontWeights.Bold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var botao = new Button { Style = Recurso<Style>("Chip"), Content = linha, Tag = marcada ? "ativo" : null, MinHeight = 38 };
            System.Windows.Automation.AutomationProperties.SetName(botao, $"{nome}{(n is null ? "" : $", {n} produtos")}{(marcada ? ", selecionado" : "")}");
            botao.Click += (_, _) => { escolher(); Montar(); };
            return botao;
        }

        // Cabeçalho
        var cabecalho = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var fechar = new Button { Content = "×", Style = Recurso<Style>("LinkSuave"), FontSize = 24, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Top, ToolTip = "Fechar" };
        System.Windows.Automation.AutomationProperties.SetName(fechar, "Fechar");
        fechar.Click += (_, _) => _area.FecharJanela();
        DockPanel.SetDock(fechar, Dock.Right);
        cabecalho.Children.Add(fechar);
        var titulos = new StackPanel();
        var titulo = new StackPanel { Orientation = Orientation.Horizontal };
        titulo.Children.Add(new ContentControl { Style = Recurso<Style>("Icone"), Tag = Recurso<Geometry>("IconeFiltro"), Foreground = Recurso<Brush>("Verde700"), Width = 22, Margin = new Thickness(0, 0, 10, 0) });
        titulo.Children.Add(new TextBlock { Text = "Filtros", FontSize = 22, FontWeight = FontWeights.Bold });
        titulos.Children.Add(titulo);
        titulos.Children.Add(new TextBlock
        {
            Text = termo.Length > 0 ? $"Os números já contam a busca “{termo}”." : "Os números mostram quantos produtos aparecem em cada opção.",
            FontSize = 13, Foreground = Recurso<Brush>("Suave")
        });
        cabecalho.Children.Add(titulos);

        // Rodapé
        var rodape = new DockPanel { Margin = new Thickness(0, 22, 0, 0) };
        var limpar = new Button { Content = "Limpar filtros", Style = Recurso<Style>("BotaoLink"), VerticalAlignment = VerticalAlignment.Center };
        limpar.Click += (_, _) => { filtro = "todos"; categoria = null; tipo = "todos"; ordem = "nome"; Montar(); };
        rodape.Children.Add(limpar);
        var botoes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelar = new Button { Content = "Cancelar", Style = Recurso<Style>("BotaoFantasma"), Margin = new Thickness(0, 0, 10, 0) };
        cancelar.Click += (_, _) => _area.FecharJanela();
        botoes.Children.Add(cancelar);
        mostrar.Click += (_, _) =>
        {
            _filtro = filtro;
            _categoria = categoria;
            _tipo = tipo;
            _ordem = ordem;
            _area.FecharJanela();
            Mostrar();
        };
        botoes.Children.Add(mostrar);
        rodape.Children.Add(botoes);

        var tudo = new StackPanel();
        tudo.Children.Add(cabecalho);
        tudo.Children.Add(corpo);
        tudo.Children.Add(rodape);
        Montar();

        raiz.Content = new Border
        {
            Style = Recurso<Style>("CartaoBorda"),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24, 22, 24, 20),
            Effect = (System.Windows.Media.Effects.Effect)FindResource("SombraJanela"),
            Child = tudo
        };
        raiz.Loaded += (_, _) => mostrar.Focus();
        return raiz;
    }

    private TextBlock TituloSecao(string texto) => new()
    {
        Text = texto, FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 16, 0, 8)
    };
}
