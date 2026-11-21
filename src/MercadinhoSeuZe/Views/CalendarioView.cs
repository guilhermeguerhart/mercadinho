using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Calendário (semana 8, pedido do Guilherme em 20/11): junta a Previsão de compras e o Fechamento do dia. No topo, o mês
/// em grade com o vendido até hoje e a previsão dos outros dias; o caminhão marca os dias de compra (fornecedor com produtos
/// para pedir naquele dia, ou último dia para pedir para uma data festiva). Clicar num dia mostra embaixo o resumo e o
/// fechamento (até hoje) ou a previsão e as compras do dia (futuro). Mais abaixo, recolhidas, as análises de vendas e todos
/// os dados da previsão (a tela antiga inteira). Os números vêm de calendario_mes (script 023) e do pedido sugerido.
/// </summary>
public sealed class CalendarioView : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");
    private static readonly string[] NomesSemana = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];

    private readonly AreaInternaView _area;
    private readonly PrevisaoService? _previsao;
    private readonly Func<FechamentoView> _novoFechamentoDia;
    private readonly Func<FechamentoView> _novasAnalises;
    private readonly Func<PrevisaoView> _novaPrevisao;

    private DateOnly _mes;
    private DateOnly _selecionado;
    private CalendarioMes? _dados;
    private FechamentoView? _fechamento;

    private readonly TextBlock _subtitulo = new() { Text = "Carregando…", FontSize = 14 };
    private readonly CheckBox _ocultar = new() { Content = "Ocultar vendas", VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _nomeMes = new() { FontSize = 18, FontWeight = FontWeights.Bold, MinWidth = 210, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _botaoHoje = new() { Content = "Voltar para hoje", Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _erro = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 20) };
    private readonly TextBlock _erroTexto = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly UniformGrid _kpis = new() { Rows = 1 };
    private readonly Grid _grade = new();
    private readonly WrapPanel _legenda = new() { Margin = new Thickness(0, 14, 0, 0) };
    private readonly TextBlock _tituloDia = new() { FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock _subtituloDia = new() { FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
    private readonly ContentControl _detalheDia = new() { Focusable = false };
    private readonly Expander _expAnalises = new();
    private readonly Expander _expPrevisao = new();

    public CalendarioView(AreaInternaView area, PrevisaoService? previsao, FechamentoService? fechamento, CaixaService? caixa,
        FornecedorService? fornecedores, EstoqueService? estoque)
    {
        _area = area;
        _previsao = previsao;
        _novoFechamentoDia = () => new FechamentoView(area, fechamento, caixa, "dia", _selecionado.ToDateTime(TimeOnly.MinValue));
        _novasAnalises = () => new FechamentoView(area, fechamento, caixa, "analises");
        _novaPrevisao = () => new PrevisaoView(area, previsao, fornecedores, estoque, embutida: true);
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        _mes = new DateOnly(hoje.Year, hoje.Month, 1);
        _selecionado = hoje;
        Content = Montar();
        Loaded += async (_, _) =>
        {
            ValoresVenda.Mudou += Redesenhar;
            _area.SugeridosMudaram += Redesenhar;
            await CarregarAsync();
        };
        Unloaded += (_, _) => { ValoresVenda.Mudou -= Redesenhar; _area.SugeridosMudaram -= Redesenhar; };
    }

    // ------------------------------------------------------------ Montagem fixa

    private StackPanel Montar()
    {
        var raiz = new StackPanel();

        // Cabeçalho
        var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 24) };
        _ocultar.Style = Estilo("Interruptor");
        _ocultar.IsChecked = ValoresVenda.Ocultos;
        _ocultar.ToolTip = "Esconde os valores de venda da tela (vale para todas as telas deste computador)";
        _ocultar.Click += (_, _) => ValoresVenda.Definir(_ocultar.IsChecked == true);
        DockPanel.SetDock(_ocultar, Dock.Right);
        cab.Children.Add(_ocultar);
        var titulos = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        titulos.Children.Add(new TextBlock { Text = "Calendário", FontSize = 28, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        _subtitulo.Style = Estilo("TextoSuave");
        titulos.Children.Add(_subtitulo);
        cab.Children.Add(titulos);
        raiz.Children.Add(cab);

        // Erro ao ler o banco
        _erro.Style = Estilo("Aviso");
        _erro.Background = Pincel("Vermelho50");
        _erro.BorderBrush = Pincel("Vermelho600");
        _erroTexto.Foreground = Pincel("Vermelho600");
        var linhaErro = new DockPanel();
        var tentar = new Button { Content = "Tentar de novo", Style = Estilo("BotaoLink"), Foreground = Pincel("Vermelho600"), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        tentar.Click += async (_, _) => await CarregarAsync();
        DockPanel.SetDock(tentar, Dock.Right);
        linhaErro.Children.Add(tentar);
        linhaErro.Children.Add(_erroTexto);
        _erro.Child = linhaErro;
        raiz.Children.Add(_erro);

        // Mês: ‹ novembro de 2026 › Voltar para hoje
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        var anterior = new Button { Content = "‹", Style = Estilo("BotaoFantasma"), Width = 40, Height = 40, Padding = new Thickness(0), FontSize = 20, ToolTip = "Mês anterior" };
        anterior.Click += async (_, _) => { _mes = _mes.AddMonths(-1); await CarregarAsync(); };
        var proximo = new Button { Content = "›", Style = Estilo("BotaoFantasma"), Width = 40, Height = 40, Padding = new Thickness(0), FontSize = 20, ToolTip = "Próximo mês" };
        proximo.Click += async (_, _) => { _mes = _mes.AddMonths(1); await CarregarAsync(); };
        System.Windows.Automation.AutomationProperties.SetName(anterior, "Mês anterior");
        System.Windows.Automation.AutomationProperties.SetName(proximo, "Próximo mês");
        _botaoHoje.Style = Estilo("BotaoLink");
        _botaoHoje.Click += async (_, _) =>
        {
            var hoje = DateOnly.FromDateTime(DateTime.Today);
            _mes = new DateOnly(hoje.Year, hoje.Month, 1);
            _selecionado = hoje;
            await CarregarAsync();
        };
        nav.Children.Add(anterior);
        nav.Children.Add(new Border { Margin = new Thickness(10, 0, 10, 0), Child = _nomeMes });
        nav.Children.Add(proximo);
        nav.Children.Add(_botaoHoje);
        raiz.Children.Add(nav);

        // Indicadores do mês
        raiz.Children.Add(new Border { Style = Estilo("FaixaKpis"), Child = _kpis });

        // Grade do mês
        var cartao = new Border { Style = Estilo("CartaoBorda"), Padding = new Thickness(20, 18, 20, 18), Margin = new Thickness(0, 0, 0, 24) };
        var corpo = new StackPanel();
        corpo.Children.Add(_grade);
        corpo.Children.Add(_legenda);
        cartao.Child = corpo;
        raiz.Children.Add(cartao);

        // Dia escolhido
        raiz.Children.Add(_tituloDia);
        _subtituloDia.Style = Estilo("TextoSuave");
        raiz.Children.Add(_subtituloDia);
        raiz.Children.Add(_detalheDia);

        // Áreas recolhidas (menos à vista): análises e os dados completos da previsão
        raiz.Children.Add(new Border { Height = 1, Background = Pincel("Linha"), Margin = new Thickness(0, 28, 0, 8) });
        raiz.Children.Add(Recolhida(_expAnalises, "Análises de vendas", "Vendas por dia, semana e mês, formas de pagamento, categorias e produtos", _novasAnalises));
        raiz.Children.Add(Recolhida(_expPrevisao, "Dados completos da previsão", "Pedido sugerido de cada fornecedor, previsão por produto, teste da previsão e alertas", _novaPrevisao));
        return raiz;
    }

    /// <summary>Área que abre e fecha; o conteúdo só é criado (e lido do banco) na primeira vez que abre.</summary>
    private Expander Recolhida(Expander e, string titulo, string dica, Func<UserControl> criar)
    {
        var cabecalho = new StackPanel { Margin = new Thickness(6, 6, 0, 6) };
        cabecalho.Children.Add(new TextBlock { Text = titulo, FontSize = 16, FontWeight = FontWeights.SemiBold });
        cabecalho.Children.Add(new TextBlock { Text = dica, FontSize = 13, Style = Estilo("TextoSuave") });
        e.Header = cabecalho;
        e.Margin = new Thickness(0, 4, 0, 4);
        e.Foreground = Pincel("Tinta");
        e.Expanded += (_, _) => { e.Content ??= new Border { Margin = new Thickness(0, 16, 0, 8), Child = criar() }; };
        return e;
    }

    /// <summary>"Ver conta" dos Fornecedores: abre os dados completos da previsão.</summary>
    public void AbrirPrevisaoCompleta()
    {
        _expPrevisao.IsExpanded = true;
        Dispatcher.BeginInvoke(() => _expPrevisao.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ------------------------------------------------------------ Dados

    private async Task CarregarAsync()
    {
        _erro.Visibility = Visibility.Collapsed;
        _nomeMes.Text = Capitalizar(_mes.ToString("MMMM 'de' yyyy", PtBr));
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        _botaoHoje.Visibility = _mes.Year == hoje.Year && _mes.Month == hoje.Month && _selecionado == hoje ? Visibility.Collapsed : Visibility.Visible;
        if (_previsao is null) { MostrarErro("Sistema sem configuração do banco de dados. Veja o README."); return; }
        try
        {
            var dados = _previsao.CalendarioAsync(_mes);
            var sugeridos = _area.SugeridosAsync();
            await Task.WhenAll(dados, sugeridos);
            _dados = dados.Result;
        }
        catch (TabelaInexistenteException)
        {
            MostrarErro("O calendário precisa do script database/023_semana8_calendario.sql rodado no Supabase (depois do 022). Depois, saia e entre de novo.");
            return;
        }
        catch (ConexaoException ex)
        {
            MostrarErro(ex.Message);
            return;
        }
        // Mudou de mês: escolhe hoje (se for este mês) ou o dia 1
        if (_selecionado < _dados.Inicio || _selecionado > _dados.Fim)
            _selecionado = hoje >= _dados.Inicio && hoje <= _dados.Fim ? hoje : _dados.Inicio;
        Redesenhar();
    }

    private void MostrarErro(string texto)
    {
        _erroTexto.Text = texto;
        _erro.Visibility = Visibility.Visible;
        _subtitulo.Text = "Não foi possível carregar o calendário.";
    }

    private void Redesenhar()
    {
        _ocultar.IsChecked = ValoresVenda.Ocultos;
        if (_dados is null) return;
        MostrarKpis();
        MostrarGrade();
        MostrarDia();
    }

    // ------------------------------------------------------------ Indicadores

    private void MostrarKpis()
    {
        var d = _dados!;
        var hoje = d.Hoje;
        var vendido = d.Dias.Sum(x => x.Vendido ?? 0);
        var previsto = d.Dias.Where(x => x.Dia > hoje || (x.Dia == hoje && x.Vendido is null)).Sum(x => x.Previsto ?? 0);
        var diasSemPrevisao = d.Dias.Count(x => x.Dia > hoje && x.Previsto is null && !x.LojaFechada);
        var anoPassado = d.Dias.Where(x => x.Dia <= hoje).Sum(x => x.AnoPassado ?? 0);
        var passou = d.Fim < hoje;
        var futuro = d.Inicio > hoje;

        _kpis.Children.Clear();
        _kpis.Children.Add(Kpi("IconeCarteira", "Verde700", futuro ? "—" : MoedaCurta(vendido), passou ? "Vendido no mês" : "Vendido até hoje",
            futuro ? "Mês que ainda não começou" : $"{Plural(d.Dias.Sum(x => x.Vendas ?? 0), "venda", "vendas")} pagas"));
        if (!passou)
        {
            _kpis.Children.Add(Kpi("IconeTendencia", "Azul600", previsto > 0 ? MoedaCurta(previsto) : "—", "Previsto até o fim do mês",
                previsto <= 0 ? "Sem previsão para estes dias ainda"
                : diasSemPrevisao > 0 ? $"A previsão cobre até {d.PrevisaoAte:dd/MM}; {Plural(diasSemPrevisao, "dia fica", "dias ficam")} sem conta" : "Pela previsão de cada produto × preço de hoje"));
            _kpis.Children.Add(Kpi("IconeFechamento", "Verde700", MoedaCurta(vendido + previsto), "Mês inteiro (estimado)",
                ValoresVenda.Ocultos ? "Vendido até hoje + previsto" : $"{ValoresVenda.Moeda(vendido)} vendido + {ValoresVenda.Moeda(previsto)} previsto"));
        }
        if (anoPassado > 0 && !futuro)
        {
            var variacao = (vendido - anoPassado) / anoPassado;
            _kpis.Children.Add(Kpi("IconeRelogio", variacao >= 0 ? "Verde700" : "Ambar700",
                ValoresVenda.Ocultos ? "•••" : (variacao >= 0 ? "+" : "−") + Math.Abs(variacao).ToString("P0", PtBr), "Contra o ano passado",
                $"Mesmos dias da semana, 52 semanas antes: {ValoresVenda.Moeda(anoPassado)}"));
        }
        var compras = DiasDeCompra().Count(x => x.Key >= hoje && x.Key <= d.Fim);
        _kpis.Children.Add(Kpi("IconeCaminhao", compras > 0 ? "Ambar700" : "Verde700", compras.ToString(PtBr), "Dias de compra",
            compras > 0 ? "Daqui até o fim do mês, com produtos para pedir" : "Nenhum até o fim do mês"));
        _kpis.Columns = _kpis.Children.Count;

        var fechados = d.Dias.Count(x => x.Fechado);
        var abertos = d.Dias.Count(x => x.Dia < hoje && !x.Fechado && (x.Vendas ?? 0) > 0);
        _subtitulo.Text = futuro ? "Previsão de vendas e dias de compra do mês."
            : $"{Plural(fechados, "dia fechado", "dias fechados")}" + (abertos > 0 ? $" · {Plural(abertos, "dia com venda ainda sem fechar", "dias com venda ainda sem fechar")}" : "") +
              " · clique num dia para ver o resumo, o fechamento ou a previsão.";
    }

    // ------------------------------------------------------------ Grade do mês

    /// <summary>
    /// Dias de compra com produtos: o dia de pedir de cada fornecedor com itens no pedido sugerido (sem pedido pendente) e o
    /// último dia para pedir para uma data festiva. Chave = dia; valor = linhas do que comprar.
    /// </summary>
    private Dictionary<DateOnly, List<(string Titulo, string Detalhe, PedidoSugerido? Sugerido)>> DiasDeCompra()
    {
        var res = new Dictionary<DateOnly, List<(string, string, PedidoSugerido?)>>();
        void Por(DateOnly dia, string t, string det, PedidoSugerido? s)
        {
            if (!res.TryGetValue(dia, out var l)) res[dia] = l = [];
            l.Add((t, det, s));
        }
        foreach (var s in _area.Sugeridos ?? [])
            if (s.Itens.Count > 0 && !s.TemPendente)
                Por(s.DataPedido, s.Fornecedor, $"{Plural(s.Itens.Count, "produto", "produtos")}" + (s.Total > 0 ? $", ≈ {s.Total.ToString("C0", PtBr)}" : "") +
                    $": {string.Join(", ", s.Itens.Take(4).Select(i => i.Nome))}{(s.Itens.Count > 4 ? "…" : "")}", s);
        foreach (var f in _dados?.Datas ?? [])
            if (f.Fator >= 1.2m && f.PedirAte >= (_dados?.Hoje ?? f.PedirAte))   // só as datas que mexem de verdade no movimento
                Por(f.PedirAte, $"Último dia para pedir para {f.Nome} ({f.Dia:dd/MM})",
                    string.IsNullOrWhiteSpace(f.Produtos) ? "Movimento maior que o normal" : $"Vende a mais: {f.Produtos}", null);
        return res;
    }

    private void MostrarGrade()
    {
        var d = _dados!;
        var compras = DiasDeCompra();
        var rotina = new Dictionary<DateOnly, List<string>>();
        foreach (var f in d.Fornecedores)
            foreach (var dia in f.Dias ?? [])
            {
                if (!rotina.TryGetValue(dia, out var l)) rotina[dia] = l = [];
                l.Add(f.Fornecedor);
            }

        _grade.Children.Clear();
        _grade.ColumnDefinitions.Clear();
        _grade.RowDefinitions.Clear();
        for (var c = 0; c < 7; c++) _grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var primeiro = (int)d.Inicio.DayOfWeek;
        var semanas = (int)Math.Ceiling((primeiro + d.Dias.Count) / 7.0);
        _grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var r = 0; r < semanas; r++) _grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c < 7; c++)
        {
            var t = new TextBlock { Text = NomesSemana[c], FontSize = 13, FontWeight = FontWeights.SemiBold, Style = Estilo("TextoSuave"), Margin = new Thickness(8, 0, 0, 8) };
            Grid.SetColumn(t, c);
            _grade.Children.Add(t);
        }
        var maior = d.Dias.Max(x => Math.Max(x.Vendido ?? 0, x.Previsto ?? 0));
        foreach (var dia in d.Dias)
        {
            var i = primeiro + dia.Dia.Day - 1;
            var celula = Celula(dia, compras.GetValueOrDefault(dia.Dia), rotina.GetValueOrDefault(dia.Dia), maior);
            Grid.SetColumn(celula, i % 7);
            Grid.SetRow(celula, i / 7 + 1);
            _grade.Children.Add(celula);
        }

        _legenda.Children.Clear();
        _legenda.Children.Add(Legenda(IconeCompra(true), "dia de compra (produtos para pedir)"));
        _legenda.Children.Add(Legenda(IconeCompra(false), "dia de pedido de rotina de algum fornecedor"));
        _legenda.Children.Add(Legenda(new TextBlock { Text = "R$", FontWeight = FontWeights.Bold, FontSize = 12 }, "vendido"));
        _legenda.Children.Add(Legenda(new TextBlock { Text = "≈ R$", FontSize = 12, Foreground = Pincel("Azul600") }, "previsto"));
        _legenda.Children.Add(Legenda(new ContentControl { Style = Estilo("Icone"), Tag = FindResource("IconeCheck"), Foreground = Pincel("Verde700"), Width = 12 }, "dia fechado"));
    }

    private Button Celula(DiaCalendario dia, List<(string Titulo, string Detalhe, PedidoSugerido? Sugerido)>? compras, List<string>? rotina, decimal maior)
    {
        var hoje = _dados!.Hoje;
        var ehHoje = dia.Dia == hoje;
        var passado = dia.Dia < hoje;
        var escolhido = dia.Dia == _selecionado;

        var corpo = new Grid { MinHeight = 84 };
        corpo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        corpo.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        corpo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Número do dia (hoje com fundo azul-celeste) e os sinais à direita
        var topo = new DockPanel();
        var sinais = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        if (dia.Fechado)
            sinais.Children.Add(new ContentControl { Style = Estilo("Icone"), Tag = FindResource("IconeCheck"), Foreground = Pincel("Verde700"), Width = 13, Margin = new Thickness(0, 2, 4, 0), ToolTip = "Dia fechado" });
        if (compras is { Count: > 0 })
        {
            var ic = IconeCompra(true);
            ic.ToolTip = string.Join("\n", compras.Select(c => $"{c.Titulo}: {c.Detalhe}"));
            sinais.Children.Add(ic);
        }
        else if (rotina is { Count: > 0 } && dia.Dia >= hoje)
        {
            var ic = IconeCompra(false);
            ic.ToolTip = "Dia de pedido de " + string.Join(", ", rotina);
            sinais.Children.Add(ic);
        }
        DockPanel.SetDock(sinais, Dock.Right);
        topo.Children.Add(sinais);
        var numero = new Border
        {
            Background = ehHoje ? Pincel("Etiqueta") : Brushes.Transparent, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 0, 6, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = dia.Dia.Day.ToString(PtBr), FontSize = 15, FontWeight = ehHoje ? FontWeights.ExtraBold : FontWeights.SemiBold, Foreground = Pincel(passado || ehHoje ? "Tinta" : "Suave") }
        };
        topo.Children.Add(numero);
        corpo.Children.Add(topo);

        // Data especial
        if (dia.Evento is { } evento)
        {
            var ev = new TextBlock
            {
                Text = dia.LojaFechada ? $"{evento} (fechado)" : evento, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Pincel("Ambar700"),
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 4, 2, 0), ToolTip = evento, VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(ev, 1);
            corpo.Children.Add(ev);
        }

        // Valor: vendido (até hoje) ou previsto (≈), com uma barrinha do tamanho do dia
        var valor = dia.Vendido is { } v && (passado || ehHoje && v > 0) ? (Texto: ValoresVenda.Moeda(v), Previsto: false, Num: v)
            : dia.Previsto is { } p && !passado ? (Texto: "≈ " + ValoresVenda.Moeda(p), Previsto: true, Num: p)
            : (Texto: passado ? (dia.LojaFechada ? "" : ValoresVenda.Moeda(0)) : "", Previsto: false, Num: 0m);
        var baixo = new StackPanel { Margin = new Thickness(6, 4, 6, 0) };
        if (valor.Texto.Length > 0)
        {
            baixo.Children.Add(new TextBlock
            {
                Text = valor.Texto, FontSize = 13, FontWeight = valor.Previsto ? FontWeights.Normal : FontWeights.SemiBold,
                Foreground = Pincel(valor.Previsto ? "Azul600" : "Tinta"), TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (maior > 0 && valor.Num > 0 && !ValoresVenda.Ocultos)
                baixo.Children.Add(new Border
                {
                    Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
                    Width = Math.Max(4, (double)(valor.Num / maior) * 110), Background = Pincel(valor.Previsto ? "Azul50" : "Etiqueta")
                });
        }
        Grid.SetRow(baixo, 2);
        corpo.Children.Add(baixo);

        var dica = $"{Capitalizar(dia.Dia.ToString("dddd, d 'de' MMMM", PtBr))}" +
                   (dia.Vendido is { } vv && !ValoresVenda.Ocultos && (passado || ehHoje) ? $"\nVendido: {vv.ToString("C", PtBr)} ({Plural(dia.Vendas ?? 0, "venda", "vendas")})" : "") +
                   (dia.Previsto is { } pp && !ValoresVenda.Ocultos && !passado ? $"\nPrevisto: ≈ {pp.ToString("C0", PtBr)} (entre {dia.PrevistoMin?.ToString("C0", PtBr)} e {dia.PrevistoMax?.ToString("C0", PtBr)})" : "");
        var borda = new Border
        {
            Background = escolhido ? Pincel("Verde50") : passado || ehHoje ? Brushes.White : Pincel("Fundo"),
            BorderBrush = Pincel(escolhido ? "Verde700" : "Linha"), BorderThickness = new Thickness(escolhido ? 2 : 1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(escolhido ? 3 : 4), Margin = new Thickness(3), Child = corpo
        };
        var botao = new Button
        {
            Content = borda, Cursor = Cursors.Hand, ToolTip = dica, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }
        };
        System.Windows.Automation.AutomationProperties.SetName(botao, dica.Replace('\n', ' '));
        botao.Click += (_, _) =>
        {
            _selecionado = dia.Dia;
            _botaoHoje.Visibility = Visibility.Visible;
            MostrarGrade();
            MostrarDia();
        };
        return botao;
    }

    private FrameworkElement IconeCompra(bool comProdutos) => new Border
    {
        Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 0, 0),
        Background = comProdutos ? Pincel("Ambar50") : Brushes.Transparent,
        BorderBrush = comProdutos ? Pincel("Ambar700") : Pincel("Linha"), BorderThickness = new Thickness(comProdutos ? 1.2 : 1),
        Child = new ContentControl { Style = Estilo("Icone"), Tag = FindResource("IconeCaminhao"), Foreground = Pincel(comProdutos ? "Ambar700" : "Suave"), Width = 12 }
    };

    private StackPanel Legenda(FrameworkElement sinal, string texto)
    {
        var l = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 22, 4) };
        sinal.VerticalAlignment = VerticalAlignment.Center;
        sinal.Margin = new Thickness(0, 0, 6, 0);
        l.Children.Add(sinal);
        l.Children.Add(new TextBlock { Text = texto, FontSize = 13, Style = Estilo("TextoSuave"), VerticalAlignment = VerticalAlignment.Center });
        return l;
    }

    // ------------------------------------------------------------ Dia escolhido

    private void MostrarDia()
    {
        var d = _dados!;
        var dia = d.Dias.FirstOrDefault(x => x.Dia == _selecionado);
        if (dia is null) return;
        var hoje = d.Hoje;
        _tituloDia.Text = Capitalizar(_selecionado.ToString("dddd, d 'de' MMMM", PtBr)) + (_selecionado == hoje ? " (hoje)" : "");
        var compras = DiasDeCompra().GetValueOrDefault(_selecionado) ?? [];

        if (_selecionado <= hoje)
        {
            _subtituloDia.Text = (dia.Evento is { } ev ? $"{ev} · " : "") + "Resumo de todos os caixas e o fechamento do dia." +
                                 (_selecionado == hoje && compras.Count > 0 ? " Hoje também é dia de compra (veja abaixo)." : "");
            var painel = new StackPanel();
            if (_selecionado == hoje && compras.Count > 0) painel.Children.Add(CartaoCompras(compras));
            if (_fechamento is null) _fechamento = _novoFechamentoDia();
            else _ = _fechamento.MostrarDiaAsync(_selecionado.ToDateTime(TimeOnly.MinValue));
            if (_fechamento.Parent is Panel antigo) antigo.Children.Remove(_fechamento);
            painel.Children.Add(_fechamento);
            _detalheDia.Content = painel;
            return;
        }

        // Dia que ainda não chegou: previsão e compras
        _subtituloDia.Text = (dia.Evento is { } evento ? $"{evento} · " : "") + "Previsão de vendas e o que pedir neste dia.";
        var futuro = new StackPanel();
        var previsao = new StackPanel();
        if (dia.Previsto is { } p)
        {
            previsao.Children.Add(new TextBlock { Text = "≈ " + ValoresVenda.Moeda(p), FontSize = 28, FontWeight = FontWeights.Bold, Foreground = Pincel("Azul600") });
            if (!ValoresVenda.Ocultos)
                previsao.Children.Add(Suave($"Em 8 de cada 10 dias assim a venda fica entre {dia.PrevistoMin?.ToString("C0", PtBr)} e {dia.PrevistoMax?.ToString("C0", PtBr)}."));
            if (dia.AnoPassado is { } ap && !ValoresVenda.Ocultos)
                previsao.Children.Add(Suave($"Mesmo dia da semana no ano passado: {ap.ToString("C", PtBr)}."));
        }
        else previsao.Children.Add(Suave(dia.LojaFechada ? "Loja fechada neste dia (calendário de datas)." :
            d.PrevisaoAte is { } ate ? $"A previsão só vai até {ate:dd/MM}. Ela é recalculada toda madrugada e anda junto com os dias." : "Ainda não há previsão calculada."));
        futuro.Children.Add(Cartao("Previsão de vendas", previsao));
        futuro.Children.Add(compras.Count > 0 ? CartaoCompras(compras) : Cartao("Compras", Suave(RotinaDoDia(_selecionado))));
        _detalheDia.Content = futuro;
    }

    private string RotinaDoDia(DateOnly dia)
    {
        var nomes = _dados!.Fornecedores.Where(f => f.Dias?.Contains(dia) == true).Select(f => f.Fornecedor).ToList();
        return nomes.Count == 0
            ? "Nenhum fornecedor tem pedido marcado para este dia."
            : $"Dia de pedido de rotina de {string.Join(", ", nomes)}. A lista do que pedir sai no próprio dia (a previsão usa o estoque daquele dia).";
    }

    /// <summary>O que comprar no dia: um bloco por fornecedor (com "Montar pedido") ou por data festiva.</summary>
    private Border CartaoCompras(List<(string Titulo, string Detalhe, PedidoSugerido? Sugerido)> compras)
    {
        var lista = new StackPanel();
        foreach (var (titulo, detalhe, s) in compras)
        {
            var linha = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            if (s is not null)
            {
                var montar = new Button { Content = "Montar pedido", Style = Estilo("BotaoPrimario"), Height = 34, Padding = new Thickness(14, 0, 14, 0), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
                montar.Click += (_, _) => (_area.Abrir("fornecedores") as FornecedoresView)?.MontarPedidoSugerido(s.FornecedorId);
                DockPanel.SetDock(montar, Dock.Right);
                linha.Children.Add(montar);
            }
            var ic = IconeCompra(true);
            ic.VerticalAlignment = VerticalAlignment.Top;
            ic.Margin = new Thickness(0, 2, 12, 0);
            DockPanel.SetDock(ic, Dock.Left);
            linha.Children.Add(ic);
            var textos = new StackPanel();
            textos.Children.Add(new TextBlock { Text = titulo, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            textos.Children.Add(Suave(detalhe + (s is not null ? $" · chega {s.Chegada:dd/MM} e precisa durar até {s.ProximaEntrega:dd/MM}" : "")));
            linha.Children.Add(textos);
            if (lista.Children.Count > 0) lista.Children.Add(new Border { Height = 1, Background = Pincel("Linha") });
            lista.Children.Add(linha);
        }
        return Cartao("Dia de compra", lista);
    }

    // ------------------------------------------------------------ Peças

    private Button Kpi(string icone, string cor, string valor, string rotulo, string dica)
    {
        var conteudo = new StackPanel();
        conteudo.Children.Add(new Border { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Left,
            Child = new ContentControl { Style = Estilo("Icone"), Tag = FindResource(icone), Foreground = Pincel(cor), Width = 16 } });
        conteudo.Children.Add(new TextBlock { Text = valor, Style = Estilo("KpiValor"), TextTrimming = TextTrimming.CharacterEllipsis });
        conteudo.Children.Add(new TextBlock { Text = rotulo, Style = Estilo("KpiRotulo") });
        conteudo.Children.Add(new TextBlock { Text = dica, Style = Estilo("KpiSub"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        return new Button { Style = Estilo("Kpi"), Content = conteudo, Cursor = Cursors.Arrow, Focusable = false, ToolTip = dica };
    }

    private Border Cartao(string titulo, UIElement conteudo)
    {
        var corpo = new StackPanel();
        corpo.Children.Add(new TextBlock { Text = titulo, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });
        corpo.Children.Add(conteudo);
        return new Border { Style = Estilo("CartaoBorda"), Padding = new Thickness(24, 20, 24, 20), Margin = new Thickness(0, 0, 0, 16), Child = corpo };
    }

    private TextBlock Suave(string texto) => new() { Text = texto, FontSize = 13.5, Style = Estilo("TextoSuave"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };

    private Style Estilo(string chave) => (Style)FindResource(chave);

    private Brush Pincel(string chave) => (Brush)FindResource(chave);

    /// <summary>"R$ 38,4 mil" nos quadrados de cima (o valor inteiro não cabe); com "Ocultar vendas", R$ •••••.</summary>
    private static string MoedaCurta(decimal v) =>
        ValoresVenda.Ocultos || Math.Abs(v) < 10000 ? ValoresVenda.Moeda(v) : $"R$ {(v / 1000m).ToString("0.0", PtBr)} mil";

    private static string Capitalizar(string s) => s.Length == 0 ? s : char.ToUpper(s[0], PtBr) + s[1..];

    private static string Plural(int n, string um, string varios) => $"{n.ToString(PtBr)} {(n == 1 ? um : varios)}";
}
