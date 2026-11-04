using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Painel do gerente (igual a painel.html do site). Estoque (semana 3), pedidos (semana 4) e vendas e caixas do dia
/// (semana 5) vêm do banco.
/// </summary>
public partial class PainelView : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly AreaInternaView _area;
    private readonly Sessao _sessao;
    private readonly EstoqueService? _estoque;
    private readonly FornecedorService? _fornecedores;
    private readonly CaixaService? _caixa;
    private readonly MensagensService? _mensagens;
    private bool _atualizando;
    private ResumoDia? _resumo;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };

    public PainelView(AreaInternaView area, Sessao sessao, EstoqueService? estoque, FornecedorService? fornecedores, CaixaService? caixa)
    {
        InitializeComponent();
        _area = area;
        _sessao = sessao;
        _estoque = estoque;
        _fornecedores = fornecedores;
        _caixa = caixa;
        _mensagens = area.Mensagens();

        ChaveOcultar.IsChecked = ValoresVenda.Ocultos;
        _timer.Tick += async (_, _) => await AtualizarAsync();
        Loaded += async (_, _) => { _timer.Start(); ValoresVenda.Mudou += MostrarVendas; await AtualizarAsync(); };
        Unloaded += (_, _) => { _timer.Stop(); ValoresVenda.Mudou -= MostrarVendas; };
    }

    private async Task AtualizarAsync()
    {
        if (_atualizando) return;
        _atualizando = true;
        try { await AtualizarDadosAsync(); }
        finally { _atualizando = false; }
    }

    private async Task AtualizarDadosAsync()
    {
        var agora = DateTime.Now;
        Saudacao.Text = $"Olá, {_sessao.Nome}";
        var data = agora.ToString("dddd, d 'de' MMMM", PtBr);
        DataTurno.Text = char.ToUpper(data[0]) + data[1..] + " — " + Turno(agora.Hour);

        // As três leituras (vendas e caixas, estoque, fornecedores) saem ao mesmo tempo: o Painel espera só pela mais lenta
        ResumoDia? resumo = null;
        List<Caixa> caixas = [];
        List<Caixa> fechados = [];
        string? erroVendas = _caixa is null ? "Sistema sem configuração do banco de dados." : null;
        List<Produto>? produtos = null;
        string? erroEstoque = _estoque is null ? "Sistema sem configuração do banco de dados." : null;
        List<Pedido>? pedidos = null;
        Dictionary<long, string> nomesFornecedores = [];
        List<Fornecedor> fornecedores = [];
        string? erroPedidos = null;

        // Vendas pagas e caixas abertos hoje (semana 5)
        async Task LerVendasAsync()
        {
            if (_caixa is null) return;
            try
            {
                var vendas = _caixa.VendasDeHojeAsync();
                var abertos = _caixa.CaixasDeHojeAsync();
                await Task.WhenAll(vendas, abertos);
                resumo = CaixaService.Resumo(vendas.Result);
                caixas = abertos.Result.Where(c => c.FechadoEm is null).ToList();
                fechados = abertos.Result.Where(c => c.FechadoEm is not null).ToList();
            }
            catch (ConexaoException ex) { erroVendas = ex.Message; }
        }

        // Estoque real do banco
        async Task LerEstoqueAsync()
        {
            if (_estoque is null) return;
            try { produtos = (await _estoque.ListarComPromocoesAsync()).Produtos; }
            catch (ConexaoException ex) { erroEstoque = ex.Message; }
        }

        // Pedidos pendentes do banco (semana 4)
        async Task LerPedidosAsync()
        {
            if (_fornecedores is null) return;
            try
            {
                var lista = _fornecedores.ListarAsync();
                var pendentes = _fornecedores.ListarPendentesAsync();
                await Task.WhenAll(lista, pendentes);
                fornecedores = lista.Result;
                nomesFornecedores = lista.Result.ToDictionary(f => f.Id, f => f.Nome);
                pedidos = pendentes.Result;
            }
            catch (ConexaoException ex) { erroPedidos = ex.Message; }
        }

        // Respostas dos fornecedores que chegaram por e-mail (semana 6, script 009)
        List<NaoLidas> naoLidas = [];
        async Task LerMensagensAsync()
        {
            if (_mensagens is null) return;
            try { naoLidas = await _mensagens.NaoLidasAsync(); }
            catch (ConexaoException) { }   // sem internet agora: tenta de novo na próxima atualização
        }

        await Task.WhenAll(LerVendasAsync(), LerEstoqueAsync(), LerPedidosAsync(), LerMensagensAsync());
        if (produtos is not null && pedidos is not null) Fornecedor.AplicarPrazos(produtos, fornecedores);

        var porNome = StringComparer.Create(PtBr, CompareOptions.IgnoreCase);
        var semEstoque = produtos?.Where(p => p.SemEstoque).OrderBy(p => p.Nome, porNome).ToList() ?? [];
        var baixos = produtos?.Where(p => p.EstoqueBaixo).OrderBy(p => p.Quantidade).ThenBy(p => p.Nome, porNome).ToList() ?? [];
        var antecipados = produtos?.Where(p => p.AvisoAntecipado).OrderBy(p => p.Quantidade).ThenBy(p => p.Nome, porNome).ToList() ?? [];
        var validade = produtos?.Where(p => p.Vencido || p.ValidadeProxima).OrderBy(p => p.ProximaValidade).ToList() ?? [];

        _resumo = resumo;
        MostrarVendas();
        KpiEstoque.Text = produtos is null ? "—" : Plural(baixos.Count + semEstoque.Count, "produto", "produtos");
        KpiEstoqueSub.Text = produtos is null ? "Não foi possível ler o estoque"
            : antecipados.Count > 0 ? $"Abaixo do mínimo ou zerados · +{antecipados.Count} perto do mínimo"
            : "Abaixo do mínimo ou zerados";
        KpiPedidos.Text = pedidos is null ? "—" : Plural(pedidos.Count, "pedido", "pedidos");
        KpiPedidosSub.Text = pedidos is null ? "Não foi possível ler os pedidos"
            : pedidos.Count(p => p.Atrasado) is > 0 and var atrasados ? $"Aguardando entrega · {atrasados} com atraso"
            : "Aguardando entrega do fornecedor";
        AtalhoCaixa.Text = caixas.Any(c => c.OperadorId == _sessao.Id) ? "Ir para o caixa" : "Abrir caixa";

        // Avisos separados por tópico (pedido do Guilherme): o cabeçalho mostra só quantos são; as linhas são montadas
        // quando o tópico é aberto (cada tópico guarda uma função que cria os avisos na hora).
        var topicos = new List<Topico>();

        // Estoque, do mais urgente ao menos urgente
        topicos.Add(new Topico("estoque", "Estoque", "IconeCaixaProduto",
            semEstoque.Count + baixos.Count + validade.Count + antecipados.Count + (erroEstoque is null ? 0 : 1),
            erroEstoque is not null || semEstoque.Count > 0 || validade.Any(p => p.Vencido) ? Gravidade.Urgente
                : baixos.Count + validade.Count + antecipados.Count > 0 ? Gravidade.Atencao : Gravidade.Normal,
            () =>
            {
                var lista = new List<Aviso>();
                if (erroEstoque is not null)
                    lista.Add(new("IconeAlerta", "Vermelho50", "Vermelho600", [new Run("Não foi possível ler o estoque: " + erroEstoque)], "Estoque"));
                foreach (var p in semEstoque)
                    lista.Add(new("IconeAlerta", "Vermelho50", "Vermelho600", [Negrito(p.Nome), new Run(" está sem estoque.")], "Estoque"));
                foreach (var p in baixos)
                    lista.Add(new("IconeTriangulo", "Ambar50", "Ambar700",
                        [Negrito(p.Nome), new Run($" está com estoque baixo ({p.Formatar(p.Quantidade)}, mínimo {p.Formatar(p.EstoqueMinimo)}).")], "Estoque"));
                foreach (var p in validade)
                    lista.Add(p.Situacao!.Nivel == NivelValidade.Vencido
                        ? new("IconeRelogio", "Vermelho50", "Vermelho600", [Negrito(p.Nome), new Run($": tem lote vencido ({p.Situacao.Texto.ToLower(PtBr)}).")], "Validade")
                        : new("IconeRelogio", "Ambar50", "Ambar700", [Negrito(p.Nome), new Run($": lote {p.Situacao.Texto.ToLower(PtBr)}.")], "Validade"));
                foreach (var p in antecipados)
                    lista.Add(new("IconeTriangulo", "Ambar50", "Ambar700",
                        [Negrito(p.Nome), new Run($" está chegando no mínimo ({p.Formatar(p.Quantidade)}). Hora de pedir.")], "Estoque"));
                return lista;
            }));

        // Promoção de validade (script 008): lembrar de etiquetar a prateleira com o preço do dia
        var lotesPromocao = produtos?.Sum(p => Promocoes.LotesEmPromocao(p).Count()) ?? 0;
        topicos.Add(new Topico("promocoes", "Promoções", "IconeCesta", lotesPromocao > 0 ? 1 : 0, Gravidade.Normal, () =>
            [new("IconeCesta", "Verde50", "Verde700",
                [Negrito(Plural(lotesPromocao, "lote", "lotes")), new Run(" em promoção de validade hoje. Confira as etiquetas da prateleira (Estoque > Promoções).")],
                "Promoções")]));

        // Fornecedor com dias certos de entrega: hoje é o último dia para pedir e ainda pegar a próxima entrega
        var ultimoDia = new List<(Fornecedor F, DateOnly Chega, int Faltam)>();
        if (produtos is not null && pedidos is not null)
            foreach (var f in fornecedores.Where(f => f.Ativo))
            {
                if (f.UltimoDiaHoje() is not { } chega) continue;
                var faltam = produtos.Count(p => f.Fornece(p) && p.PrecisaRepor && !pedidos.Any(o => o.Itens.Any(i => i.ProdutoId == p.Id)));
                if (faltam > 0) ultimoDia.Add((f, chega, faltam));
            }
        var pendentes = pedidos?.OrderByDescending(p => p.Atrasado).ThenBy(p => p.Previsao).ToList() ?? [];
        topicos.Add(new Topico("pedidos", "Pedidos e fornecedores", "IconeCaminhao", ultimoDia.Count + pendentes.Count + (erroPedidos is null ? 0 : 1),
            erroPedidos is not null || pendentes.Any(p => p.Atrasado) ? Gravidade.Urgente : ultimoDia.Count > 0 ? Gravidade.Atencao : Gravidade.Normal,
            () =>
            {
                var lista = new List<Aviso>();
                if (erroPedidos is not null)
                    lista.Add(new("IconeAlerta", "Vermelho50", "Vermelho600", [new Run("Não foi possível ler os pedidos: " + erroPedidos)], "Fornecedores"));
                foreach (var (f, chega, faltam) in ultimoDia)
                    lista.Add(new("IconeRelogio", "Ambar50", "Ambar700",
                        [new Run("Hoje é o último dia para pedir para "), Negrito(f.Nome),
                         new Run($" e pegar a entrega de {Fornecedor.NomesDias[(int)chega.DayOfWeek]}, {chega:dd/MM}: {Plural(faltam, "produto precisa", "produtos precisam")} de reposição.")],
                        "Fornecedores"));
                foreach (var p in pendentes)
                {
                    var fornecedor = nomesFornecedores.GetValueOrDefault(p.FornecedorId, "fornecedor");
                    lista.Add(new("IconeCaminhao", p.Atrasado ? "Vermelho50" : "Azul50", p.Atrasado ? "Vermelho600" : "Azul600",
                        p.Atrasado
                            ? [new Run("Pedido para "), Negrito(fornecedor), new Run($" está atrasado (previsto para {p.Previsao:dd/MM/yyyy}).")]
                            : [new Run("Pedido para "), Negrito(fornecedor), new Run(p.Previsao is { } prev ? $" chega até {prev:dd/MM/yyyy}." : " ainda não foi entregue.")],
                        $"Pedido de {p.Data:dd/MM/yyyy}"));
                }
                return lista;
            }));

        // Mensagens novas dos fornecedores
        topicos.Add(new Topico("mensagens", "Mensagens", "IconeEmail", naoLidas.Count, Gravidade.Normal, () =>
            naoLidas.OrderByDescending(n => n.UltimaEm).Select(n => new Aviso("IconeEmail", "Azul50", "Azul600",
                [Negrito(n.FornecedorNome), new Run(n.PedidoId is { } pid ? $" respondeu sobre o pedido nº {pid}" : " mandou mensagem"),
                 new Run(n.Quantidade > 1 ? $" ({n.Quantidade} mensagens novas)." : ".")],
                $"Chegou às {n.UltimaEm.ToLocalTime():HH:mm}" + (n.UltimaEm.ToLocalTime().Date == DateTime.Today ? "" : $" de {n.UltimaEm.ToLocalTime():dd/MM}"),
                () => (_area.Abrir("fornecedores") as FornecedoresView)?.MensagensDe(n.FornecedorId, n.PedidoId))).ToList()));

        // Caixas abertos hoje e fechamentos com sobra ou falta na gaveta
        var comDiferenca = fechados.Where(c => c.FechadoDiferenca is not null and not 0).ToList();
        topicos.Add(new Topico("caixa", "Caixa", "IconeCaixaRegistradora", caixas.Count + comDiferenca.Count + (erroVendas is null ? 0 : 1),
            erroVendas is not null || comDiferenca.Any(c => c.FechadoDiferenca < 0) ? Gravidade.Urgente : comDiferenca.Count > 0 ? Gravidade.Atencao : Gravidade.Normal,
            () =>
            {
                var lista = new List<Aviso>();
                if (erroVendas is not null)
                    lista.Add(new("IconeAlerta", "Vermelho50", "Vermelho600", [new Run("Não foi possível ler as vendas: " + erroVendas)], "Caixa"));
                foreach (var c in caixas)
                    lista.Add(new("IconeCaixaRegistradora", "Verde50", "Verde700", [new Run("Caixa aberto por "), Negrito(c.OperadorNome), new Run($" às {c.Hora}.")], "Hoje"));
                foreach (var c in comDiferenca)
                {
                    var d = c.FechadoDiferenca!.Value;
                    lista.Add(new("IconeCaixaRegistradora", d < 0 ? "Vermelho50" : "Ambar50", d < 0 ? "Vermelho600" : "Ambar700",
                        [new Run("Caixa de "), Negrito(c.OperadorNome), new Run($" fechou às {c.HoraFechamento} com {(d < 0 ? "falta" : "sobra")} de {Moeda(Math.Abs(d))}" +
                            (c.FechadoObs is null ? "." : $": “{c.FechadoObs}”."))], "Hoje"));
                }
                return lista;
            }));

        _topicos = topicos.Where(t => t.Quantidade > 0).ToList();
        MostrarAvisos();

        Atualizado.Text = $"Atualizado às {agora:HH:mm}";
    }

    /// <summary>Vendas e ticket médio do dia; com "Ocultar vendas" ligado, aparecem como R$ •••••.</summary>
    private void MostrarVendas()
    {
        ChaveOcultar.IsChecked = ValoresVenda.Ocultos;
        KpiVendas.Text = _resumo is null ? "—" : ValoresVenda.Moeda(_resumo.Total);
        KpiVendasSub.Text = _resumo is null ? "Não foi possível ler as vendas"
            : ValoresVenda.Ocultos ? "Valores ocultos" : Plural(_resumo.QuantidadeVendas, "venda realizada", "vendas realizadas");
        KpiTicket.Text = _resumo is null ? "—" : ValoresVenda.Moeda(_resumo.TicketMedio);
    }

    private void ChaveOcultar_Click(object sender, RoutedEventArgs e) => ValoresVenda.Definir(ChaveOcultar.IsChecked == true);

    // ------------------------------------------------------------ Avisos por tópico

    private enum Gravidade { Normal, Atencao, Urgente }

    private sealed record Aviso(string Icone, string Fundo, string Cor, Inline[] Texto, string Quando, Action? Abrir = null);

    /// <summary>Um tópico dos avisos: o cabeçalho usa só o número e a gravidade; Montar cria as linhas ao abrir.</summary>
    private sealed record Topico(string Id, string Nome, string Icone, int Quantidade, Gravidade Gravidade, Func<List<Aviso>> Montar);

    private List<Topico> _topicos = [];
    private readonly HashSet<string> _topicosAbertos = [];   // continuam abertos na atualização de 30 em 30 segundos
    private readonly Dictionary<string, Paginacao> _paginasAvisos = [];

    private void MostrarAvisos()
    {
        ListaAvisos.Children.Clear();
        if (_topicos.Count == 0)
        {
            ListaAvisos.Children.Add(new TextBlock { Text = "Nenhum aviso no momento.", FontSize = 14, Style = (Style)FindResource("TextoSuave") });
            return;
        }
        foreach (var t in _topicos)
        {
            if (ListaAvisos.Children.Count > 0)
                ListaAvisos.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Linha") });
            ListaAvisos.Children.Add(CabecalhoTopico(t));
            if (_topicosAbertos.Contains(t.Id)) ListaAvisos.Children.Add(CorpoTopico(t));
        }
    }

    /// <summary>Linha clicável: ícone, nome, quantos avisos (vermelho = urgente, âmbar = atenção) e a seta.</summary>
    private Button CabecalhoTopico(Topico t)
    {
        var aberto = _topicosAbertos.Contains(t.Id);
        var (fundo, cor) = t.Gravidade switch
        {
            Gravidade.Urgente => ("Vermelho50", "Vermelho600"),
            Gravidade.Atencao => ("Ambar50", "Ambar700"),
            _ => ("Verde50", "Verde700")
        };
        var linha = new DockPanel { Margin = new Thickness(0, 10, 0, 10), Background = Brushes.Transparent };
        linha.Children.Add(new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 12, 0), Background = (Brush)FindResource(fundo),
            Child = new ContentControl { Style = (Style)FindResource("Icone"), Tag = FindResource(t.Icone), Foreground = (Brush)FindResource(cor), Width = 16 }
        });
        var seta = new ContentControl
        {
            Style = (Style)FindResource("Icone"), Tag = FindResource("IconeSetaBaixo"), Foreground = (Brush)FindResource("Suave"), Width = 18,
            Margin = new Thickness(10, 0, 0, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(aberto ? 180 : 0)
        };
        DockPanel.SetDock(seta, Dock.Right);
        linha.Children.Add(seta);
        var numero = new Border
        {
            Background = (Brush)FindResource(fundo), CornerRadius = new CornerRadius(10), MinWidth = 26, Height = 22, Padding = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = t.Quantidade.ToString(PtBr), FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource(cor),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        DockPanel.SetDock(numero, Dock.Right);
        linha.Children.Add(numero);
        linha.Children.Add(new TextBlock { Text = t.Nome, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        var botao = new Button
        {
            Content = linha, Cursor = System.Windows.Input.Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }
        };
        System.Windows.Automation.AutomationProperties.SetName(botao, $"{t.Nome}: {Plural(t.Quantidade, "aviso", "avisos")}" + (aberto ? " (aberto)" : ""));
        botao.Click += (_, _) =>
        {
            if (!_topicosAbertos.Remove(t.Id)) _topicosAbertos.Add(t.Id);
            MostrarAvisos();
        };
        return botao;
    }

    /// <summary>Os avisos do tópico aberto, em páginas de 5.</summary>
    private StackPanel CorpoTopico(Topico t)
    {
        var corpo = new StackPanel { Margin = new Thickness(44, 0, 0, 10) };
        if (!_paginasAvisos.TryGetValue(t.Id, out var paginas))
        {
            paginas = new Paginacao("avisos", [5, 10, 25], 5);
            paginas.Mudou += MostrarAvisos;
            _paginasAvisos[t.Id] = paginas;
        }
        foreach (var a in paginas.Recortar(t.Montar()))
        {
            if (corpo.Children.Count > 0)
                corpo.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Linha") });
            corpo.Children.Add(LinhaAviso(a));
        }
        corpo.Children.Add(paginas.Barra(compacta: true));
        return corpo;
    }

    private DockPanel LinhaAviso(Aviso a)
    {
        var linha = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
        linha.Children.Add(new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = (Brush)FindResource(a.Fundo),
            Child = new ContentControl { Style = (Style)FindResource("Icone"), Tag = FindResource(a.Icone), Foreground = (Brush)FindResource(a.Cor), Width = 14 }
        });
        if (a.Abrir is not null)
        {
            var botao = new Button { Content = "Abrir", Style = (Style)FindResource("BotaoLink"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            botao.Click += (_, _) => a.Abrir();
            DockPanel.SetDock(botao, Dock.Right);
            linha.Children.Add(botao);
        }

        var textoBloco = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap };
        textoBloco.Inlines.AddRange(a.Texto);
        var conteudo = new StackPanel();
        conteudo.Children.Add(textoBloco);
        conteudo.Children.Add(new TextBlock { Text = a.Quando, FontSize = 12, Style = (Style)FindResource("TextoSuave") });
        linha.Children.Add(conteudo);
        return linha;
    }

    private void Atalho_Click(object sender, RoutedEventArgs e)
    {
        var destino = (string)((Button)sender).Tag;
        if (destino == "caixa") _area.Abrir("caixa");
        else if (destino == "estoque") _area.Abrir("estoque");
        else if (destino == "Estoque") (_area.Abrir("estoque") as EstoqueView)?.NovoProduto();
        else if (destino == "Fornecedores") (_area.Abrir("fornecedores") as FornecedoresView)?.NovoPedido();
        else _area.EmBreve(destino);
    }

    /// <summary>Mesmo comportamento responsivo do site.</summary>
    private void AoRedimensionar(object sender, SizeChangedEventArgs e)
    {
        Kpis.Columns = e.NewSize.Width < 520 ? 1 : e.NewSize.Width < 900 ? 2 : 4;
        var empilhar = e.NewSize.Width < 760;
        // Largura disponível para o cartão de atalhos: inteira se empilhado, senão 1/2,2 da linha
        var larguraAtalhos = empilhar ? e.NewSize.Width : (e.NewSize.Width - 20) / 2.2;
        Atalhos.Columns = larguraAtalhos < 460 ? 1 : 2;
        ColunaAvisos.Width = empilhar ? new GridLength(0) : new GridLength(1.2, GridUnitType.Star);
        Grid.SetColumn(CartaoAvisos, empilhar ? 0 : 1);
        Grid.SetRow(CartaoAvisos, empilhar ? 1 : 0);
        CartaoAtalhos.Margin = new Thickness(0, 0, empilhar ? 0 : 20, 20);
    }

    private static string Turno(int hora) => hora < 12 ? "turno da manhã" : hora < 18 ? "turno da tarde" : "turno da noite";

    private static string Moeda(decimal valor) => valor.ToString("C", PtBr);

    private static string Plural(int n, string um, string varios) => $"{n} {(n == 1 ? um : varios)}";

    private static Run Negrito(string texto) => new(texto) { FontWeight = FontWeights.SemiBold };
}
