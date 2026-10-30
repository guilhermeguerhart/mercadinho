using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Caixa (semana 5, como um caixa padrão de supermercado): abre o caixa com o troco inicial, monta a venda pelo
/// leitor, pela etiqueta da balança ou pelo nome ("6*" multiplica), mostra o último item em destaque e recebe o
/// pagamento em uma ou mais formas (dinheiro, Pix, cartão e vale pela maquininha). Só depois do pagamento o banco
/// dá baixa nos lotes. Cada item fica reservado no banco (outro caixa não vende o mesmo); lote vencido não é vendido.
/// Cancelar item, cancelar a venda e dar desconto precisam da senha do gerente.
/// </summary>
public partial class CaixaView : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");
    private static readonly Regex Multiplicacao = new(@"^(\d{1,4}(?:[.,]\d{1,3})?)\s*[*xX]\s*(.*)$");

    /// <summary>Uma linha da venda. Item = null: produto por quilo esperando o peso (ainda não reservado).</summary>
    private sealed class Linha(Produto produto)
    {
        public Produto Produto { get; } = produto;
        public ItemVenda? Item { get; set; }
        public TextBox? Campo { get; set; }
        public TextBlock? Erro { get; set; }
        public decimal Quantidade => Item?.Quantidade ?? 0;
        public decimal Subtotal => Item?.Subtotal ?? 0;
    }

    private readonly AreaInternaView _area;
    private readonly CaixaService? _caixaServico;
    private readonly EstoqueService? _estoque;
    private readonly bool _gerente;
    private readonly SemaphoreSlim _fila = new(1, 1);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly DispatcherTimer _relogio = new() { Interval = TimeSpan.FromSeconds(1) };

    private Caixa? _caixa;
    private long? _vendaId;
    private readonly List<Linha> _linhas = [];
    private Linha? _ultima;
    private readonly List<PagamentoVenda> _pagamentos = [];
    private decimal _desconto;
    private string? _cpf;
    private decimal? _multiplicador;
    private List<Produto> _produtos = [];
    private List<Venda> _vendasHoje = [];
    private List<Venda> _emEspera = [];
    private string? _forma;
    private bool _modoCaixa;
    private bool _carregou;
    private bool _recebendo;

    public CaixaView(AreaInternaView area, CaixaService? caixa, EstoqueService? estoque)
    {
        InitializeComponent();
        _area = area;
        _caixaServico = caixa;
        _estoque = estoque;
        _gerente = caixa?.Sessao.Perfil == Perfil.Gerente;

        MontarFormas();
        for (var i = 1; i <= 12; i++) Parcelas.Items.Add(new ComboBoxItem { Content = i == 1 ? "À vista" : $"{i}x", Tag = i });
        Parcelas.SelectedIndex = 0;
        Formulario.LimparAoDigitar(TrocoInicial, TrocoInicialErro);
        PreviewKeyDown += Atalhos;
        _timer.Tick += async (_, _) => await AtualizarEmSegundoPlanoAsync();
        _relogio.Tick += (_, _) => Relogio.Text = DateTime.Now.ToString("HH:mm");
        Loaded += async (_, _) =>
        {
            _timer.Start();
            if (PreferenciasLocais.Carregar().ModoCaixa) AplicarModo(true);
            if (!_carregou) await CarregarAsync();
        };
        Unloaded += (_, _) => { _timer.Stop(); _relogio.Stop(); };
        IniciarAtalhosDesenvolvimento();
    }

    // ------------------------------------------------------------ Dados

    private async Task CarregarAsync()
    {
        _carregou = true;
        AvisoErro.Visibility = Visibility.Collapsed;
        if (_caixaServico is null || _estoque is null)
        {
            MostrarErro("Sistema sem configuração do banco de dados. Veja o README.");
            return;
        }
        try
        {
            var caixa = _caixaServico.MeuCaixaAsync();
            var produtos = _estoque.ListarAsync();
            var vendas = _caixaServico.VendasDeHojeAsync();
            await Task.WhenAll(caixa, produtos, vendas);
            _caixa = caixa.Result;
            _produtos = produtos.Result;
            _vendasHoje = vendas.Result;
            await AplicarReservasAsync();

            LimparVenda();
            _emEspera = [];
            if (_caixa is not null)
            {
                _emEspera = await _caixaServico.VendasEmEsperaAsync(_caixa.Id);
                if (await _caixaServico.VendaAbertaAsync(_caixa.Id) is { } aberta)
                {
                    // Venda que ficou no meio (programa fechado) ou retomada da espera: volta com os itens reservados
                    _vendaId = aberta.Id;
                    _desconto = aberta.Desconto;
                    foreach (var item in aberta.Itens)
                    {
                        var produto = _produtos.FirstOrDefault(p => p.Id == item.ProdutoId) ?? new Produto
                        {
                            Id = item.ProdutoId ?? 0, Nome = item.NomeProduto, UnidadeMedida = item.UnidadeMedida, Preco = item.PrecoUnitario
                        };
                        _linhas.Add(new Linha(produto) { Item = item });
                    }
                    _ultima = _linhas.LastOrDefault();
                }
            }
        }
        catch (ConexaoException ex)
        {
            MostrarErro(ex.Message);
            return;
        }
        Mostrar();
    }

    /// <summary>Reservas de todos os caixas (vendas abertas e em espera), para mostrar o disponível de cada produto.</summary>
    private async Task AplicarReservasAsync()
    {
        var reservados = await _estoque!.ReservadosAsync();
        foreach (var p in _produtos) p.Reservado = reservados.GetValueOrDefault(p.Id);
    }

    /// <summary>A cada minuto: estoque, reservas e vendas do dia (sem mexer na venda que está sendo montada).</summary>
    private async Task AtualizarEmSegundoPlanoAsync()
    {
        if (_caixaServico is null || _estoque is null || !_fila.Wait(0)) return;
        try
        {
            var produtos = _estoque.ListarAsync();
            var vendas = _caixaServico.VendasDeHojeAsync();
            await Task.WhenAll(produtos, vendas);
            _produtos = produtos.Result;
            _vendasHoje = vendas.Result;
            await AplicarReservasAsync();
            if (_caixa is not null) _emEspera = await _caixaServico.VendasEmEsperaAsync(_caixa.Id);
            MostrarDia();
            MostrarPagamento();
            AtualizarResultados();
        }
        catch (ConexaoException)
        {
            // Sem internet por um momento: tenta de novo no próximo minuto
        }
        finally
        {
            _fila.Release();
        }
    }

    private void LimparVenda()
    {
        _linhas.Clear();
        _ultima = null;
        _vendaId = null;
        _pagamentos.Clear();
        _desconto = 0;
        _cpf = null;
        _multiplicador = null;
        _forma = null;
    }

    private void MostrarErro(string mensagem)
    {
        AvisoErroTexto.Text = mensagem;
        AvisoErro.Visibility = Visibility.Visible;
        Subtitulo.Text = "Não foi possível carregar o caixa.";
    }

    private async void TentarDeNovo_Click(object sender, RoutedEventArgs e) => await CarregarAsync();

    // ------------------------------------------------------------ Tela

    private void Mostrar()
    {
        var aberto = _caixa is not null;
        PainelAbrir.Visibility = aberto ? Visibility.Collapsed : Visibility.Visible;
        AreaVenda.Visibility = aberto ? Visibility.Visible : Visibility.Collapsed;
        Subtitulo.Text = aberto
            ? $"Caixa aberto por {_caixa!.OperadorNome} às {_caixa.Hora} · troco inicial {Moeda(_caixa.TrocoInicial)}"
            : "Registro de vendas do dia";
        SubtituloModoCaixa.Text = aberto ? $"{_caixa!.OperadorNome} · aberto às {_caixa.Hora}" : "Caixa fechado";
        if (aberto) MostrarVenda();
        MostrarDia();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (aberto) FocarBusca(); else { TrocoInicial.Focus(); TrocoInicial.SelectAll(); } });
    }

    private double FonteItem => _modoCaixa ? 18 : 15;

    private void MostrarVenda()
    {
        ItensVenda.Children.Clear();
        for (var i = 0; i < _linhas.Count; i++) ItensVenda.Children.Add(LinhaItem(_linhas[i], i + 1));
        SemItens.Visibility = _linhas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CabecalhoItens.Visibility = _linhas.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        LinhaCabecalho.Visibility = CabecalhoItens.Visibility;
        MostrarDestaque();
        MostrarPagamento();
        RolagemItens.ScrollToBottom();
    }

    /// <summary>Último item lido em letras grandes, como no visor do caixa.</summary>
    private void MostrarDestaque()
    {
        if (_ultima is null || !_linhas.Contains(_ultima))
        {
            DestaqueUltimo.Visibility = Visibility.Collapsed;
            return;
        }
        var p = _ultima.Produto;
        var n = _linhas.IndexOf(_ultima) + 1;
        DestaqueUltimo.Visibility = Visibility.Visible;
        DestaqueNome.Text = $"{n:00}  {p.Nome}";
        var preco = _ultima.Item?.PrecoUnitario ?? p.Preco;
        DestaqueConta.Text = _ultima.Item is null
            ? $"Digite o peso · {Moeda(preco)}/kg"
            : p.PorPeso ? $"{_ultima.Item.Numero} kg × {Moeda(preco)}/kg" : $"{_ultima.Item.Numero} × {Moeda(preco)}";
        DestaqueSubtotal.Text = _ultima.Item is null ? "—" : Moeda(_ultima.Subtotal);
    }

    private FrameworkElement LinhaItem(Linha linha, int numero)
    {
        var p = linha.Produto;
        var grade = new Grid { Margin = new Thickness(0, _modoCaixa ? 12 : 10, 0, _modoCaixa ? 12 : 10) };
        foreach (var largura in new[] { 36.0, -1, 150, 110, 110, 40 })
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = largura < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(largura) });

        grade.Children.Add(new TextBlock
        {
            Text = numero.ToString("00", PtBr), FontSize = FonteItem - 2, FontWeight = FontWeights.Bold,
            Style = Recurso<Style>("TextoSuave"), VerticalAlignment = VerticalAlignment.Center
        });

        var nome = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        nome.Children.Add(new TextBlock { Text = p.Nome, FontSize = FonteItem, FontWeight = FontWeights.SemiBold });
        if (p.Categoria.Length > 0)
        {
            var pilula = CategoriaVisual.Pilula(this, p.Categoria, 12);
            pilula.Margin = new Thickness(0, 4, 0, 0);
            nome.Children.Add(pilula);
        }
        Grid.SetColumn(nome, 1);
        grade.Children.Add(nome);

        // Quantidade ou peso: Enter (ou sair do campo) grava e reserva no banco
        var campo = new TextBox
        {
            Text = linha.Item is null ? "" : Formulario.Quantidade(linha.Quantidade),
            Width = _modoCaixa ? 100 : 84, MinHeight = _modoCaixa ? 46 : 38, FontSize = FonteItem, MaxLength = 9,
            HorizontalContentAlignment = HorizontalAlignment.Right, Padding = new Thickness(8, 0, 8, 0)
        };
        System.Windows.Automation.AutomationProperties.SetName(campo, p.PorPeso ? $"Peso de {p.Nome} em kg" : $"Quantidade de {p.Nome}");
        var erro = new TextBlock { Style = Recurso<Style>("ErroCampo"), TextAlignment = TextAlignment.Right, FontSize = 12, MaxWidth = 150 };
        linha.Campo = campo;
        linha.Erro = erro;
        campo.TextChanged += (_, _) => { campo.Tag = null; erro.Text = ""; };
        campo.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (await GravarQuantidadeAsync(linha)) FocarBusca();
        };
        campo.LostKeyboardFocus += async (_, e) =>
        {
            // Saiu para uma janela (autorização do gerente): quem grava é o Enter ou o Receber, não a perda de foco
            if (_area.JanelaAberta) return;
            await GravarQuantidadeAsync(linha);
        };
        var celulaQtd = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var qtd = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        qtd.Children.Add(campo);
        qtd.Children.Add(new TextBlock { Text = p.Sigla, FontSize = FonteItem - 2, Style = Recurso<Style>("TextoSuave"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Width = 26 });
        celulaQtd.Children.Add(qtd);
        celulaQtd.Children.Add(erro);
        Grid.SetColumn(celulaQtd, 2);
        grade.Children.Add(celulaQtd);

        var preco = new TextBlock
        {
            Text = Moeda(linha.Item?.PrecoUnitario ?? p.Preco) + (p.PorPeso ? "/kg" : ""), FontSize = FonteItem - 1,
            Style = Recurso<Style>("TextoSuave"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(preco, 3);
        grade.Children.Add(preco);

        var subtotal = new TextBlock
        {
            Text = linha.Item is null ? "—" : Moeda(linha.Subtotal), FontSize = FonteItem, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(subtotal, 4);
        grade.Children.Add(subtotal);

        var tirar = new Button
        {
            Content = "×", Style = Recurso<Style>("LinkSuave"), FontSize = 24, FontWeight = FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = _gerente ? "Cancelar item" : "Cancelar item (precisa do gerente)"
        };
        System.Windows.Automation.AutomationProperties.SetName(tirar, $"Cancelar {p.Nome}");
        tirar.Click += async (_, _) => await TirarAsync(linha);
        Grid.SetColumn(tirar, 5);
        grade.Children.Add(tirar);

        return new Border
        {
            BorderBrush = Recurso<Brush>("Linha"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grade,
            Background = linha == _ultima ? Recurso<Brush>("PapelClaro") : Brushes.Transparent
        };
    }

    private decimal ValorItens => _linhas.Sum(l => l.Subtotal);
    private decimal TotalVenda => Math.Max(0, ValorItens - _desconto);
    private decimal Pago => _pagamentos.Sum(p => p.Valor);
    private decimal FaltaPagar => Math.Max(0, TotalVenda - Pago);

    /// <summary>Total, desconto, CPF, pagamentos recebidos, o que falta e o troco.</summary>
    private void MostrarPagamento()
    {
        Total.Text = Moeda(TotalVenda);
        var n = _linhas.Count;
        QuantidadeItens.Text = n == 0 ? "Nenhum item" : n == 1 ? "1 item" : $"{n} itens";

        Detalhes.Children.Clear();
        if (_desconto > 0)
        {
            Detalhes.Children.Add(LinhaDetalhe("Itens", Moeda(ValorItens), null));
            Detalhes.Children.Add(LinhaDetalhe("Desconto", "− " + Moeda(_desconto), null, "Verde700"));
        }
        if (_cpf is not null) Detalhes.Children.Add(LinhaDetalhe("CPF/CNPJ na nota", Documento.Formatar(_cpf), null));
        foreach (var pg in _pagamentos)
        {
            var texto = pg.Forma == "dinheiro" && pg.Troco > 0 ? $"recebido {Moeda(pg.Recebido ?? pg.Valor)}, troco {Moeda(pg.Troco)}" : pg.Detalhe;
            var pagamento = pg;
            Detalhes.Children.Add(LinhaDetalhe(FormaPagamento.Nome(pg.Forma), Moeda(pg.Valor), texto, "Verde700", () => EstornarPagamento(pagamento)));
        }

        var falta = FaltaPagar;
        LinhaFalta.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
        RotuloFalta.Text = falta > 0 ? (_pagamentos.Count > 0 ? "Falta pagar" : "A pagar") : "Pago";
        Falta.Text = Moeda(falta);
        Falta.Foreground = Recurso<Brush>(falta > 0 ? (_pagamentos.Count > 0 ? "Vermelho600" : "Tinta") : "Verde700");
        DicaValor.Text = _forma == "dinheiro" ? $"vazio = {Moeda(falta)} (exato)" : $"vazio = {Moeda(falta)}";
        TextoFinalizar.Text = n > 0 && falta == 0 ? "Finalizar venda (F4)"
            : _forma is null ? "Receber (F4)"
            : $"Receber {(Formulario.LerPreco(Valor.Text) is { } v ? Moeda(v) : Moeda(falta))} (F4)";
        var troco = _forma == "dinheiro" && Formulario.LerPreco(Valor.Text) is { } recebido && recebido > falta ? recebido - falta : 0;
        Troco.Text = Moeda(troco);
        LinhaTroco.Visibility = _forma == "dinheiro" ? Visibility.Visible : Visibility.Collapsed;

        BotaoCancelarVenda.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
        BotaoDesconto.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
        BotaoEspera.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
        BotaoCpf.Content = _cpf is null ? "CPF na nota (F11)" : "Trocar CPF (F11)";
        BotaoVendasEspera.Visibility = _emEspera.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BotaoVendasEspera.Content = _emEspera.Count == 1 ? "1 venda em espera" : $"{_emEspera.Count} vendas em espera";
    }

    private FrameworkElement LinhaDetalhe(string rotulo, string valor, string? sub, string cor = "Tinta", Action? remover = null)
    {
        var linha = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        if (remover is not null)
        {
            var x = new Button { Content = "×", Style = Recurso<Style>("LinkSuave"), FontSize = 18, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Estornar este pagamento", VerticalAlignment = VerticalAlignment.Center };
            x.Click += (_, _) => remover();
            DockPanel.SetDock(x, Dock.Right);
            linha.Children.Add(x);
        }
        var v = new TextBlock { Text = valor, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Recurso<Brush>(cor), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(v, Dock.Right);
        linha.Children.Add(v);
        var textos = new StackPanel();
        textos.Children.Add(new TextBlock { Text = rotulo, FontSize = 14 });
        if (!string.IsNullOrEmpty(sub)) textos.Children.Add(new TextBlock { Text = sub, FontSize = 12, Style = Recurso<Style>("TextoSuave") });
        linha.Children.Add(textos);
        return linha;
    }

    private void MostrarDia()
    {
        var resumo = CaixaService.Resumo(_vendasHoje);
        ResumoTotal.Text = Moeda(resumo.Total);
        ResumoQuantidade.Text = resumo.QuantidadeVendas.ToString(PtBr);
        ResumoTicket.Text = Moeda(resumo.TicketMedio);

        ListaVendas.Children.Clear();
        if (_vendasHoje.Count == 0)
        {
            ListaVendas.Children.Add(new TextBlock { Text = "Nenhuma venda hoje.", FontSize = 14, Style = Recurso<Style>("TextoSuave") });
            return;
        }
        foreach (var v in _vendasHoje)
        {
            var linha = new DockPanel { Margin = new Thickness(0, 10, 0, 10), Opacity = v.Cancelada ? 0.65 : 1 };

            var fim = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            fim.Children.Add(new TextBlock
            {
                Text = v.ResumoFormas, FontSize = 13, Style = Recurso<Style>("TextoSuave"),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 16, 0)
            });
            var total = new TextBlock { Text = Moeda(v.Total), FontSize = 15, FontWeight = FontWeights.Bold };
            if (v.Cancelada) total.TextDecorations = TextDecorations.Strikethrough;
            fim.Children.Add(v.Cancelada ? total : new Border { Style = Recurso<Style>("EtiquetaPreco"), Child = total });
            if (v.Cancelada)
            {
                fim.Children.Add(new Border
                {
                    Background = Recurso<Brush>("Vermelho50"), CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = v.CanceladaPor is null ? null : $"Cancelada por {v.CanceladaPor}",
                    Child = new TextBlock { Text = "Cancelada", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Recurso<Brush>("Vermelho600") }
                });
            }
            else if (_gerente)
            {
                var cancelar = new Button
                {
                    Content = "Cancelar", Style = Recurso<Style>("BotaoLink"), Foreground = Recurso<Brush>("Vermelho600"),
                    Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
                };
                System.Windows.Automation.AutomationProperties.SetName(cancelar, $"Cancelar a venda número {v.Id}");
                var venda = v;
                cancelar.Click += (_, _) => ConfirmarCancelamentoPaga(venda);
                fim.Children.Add(cancelar);
            }
            DockPanel.SetDock(fim, Dock.Right);
            linha.Children.Add(fim);

            var texto = new StackPanel();
            var titulo = $"Venda nº {v.Id} · {v.Hora} · {v.OperadorNome}";
            if (v.Desconto > 0) titulo += $" · desconto {Moeda(v.Desconto)}";
            if (v.CpfNota is not null) titulo += " · CPF na nota";
            texto.Children.Add(new TextBlock { Text = titulo, FontSize = 14, FontWeight = FontWeights.SemiBold });
            texto.Children.Add(new TextBlock { Text = v.ResumoItens, FontSize = 12.5, Style = Recurso<Style>("TextoSuave"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
            linha.Children.Add(texto);

            if (ListaVendas.Children.Count > 0)
                ListaVendas.Children.Add(new Border { Height = 1, Background = Recurso<Brush>("Linha") });
            ListaVendas.Children.Add(linha);
        }
    }

    // ------------------------------------------------------------ Abrir o caixa

    private async void AbrirCaixa_Click(object sender, RoutedEventArgs e) => await AbrirCaixaAsync();

    private async void TrocoInicial_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await AbrirCaixaAsync();
    }

    private async Task AbrirCaixaAsync()
    {
        if (_caixaServico is null) return;
        var troco = TrocoInicial.Text.Trim().Length == 0 ? 0 : Formulario.LerPreco(TrocoInicial.Text);
        if (!Formulario.Validar(TrocoInicial, TrocoInicialErro, troco is not null, "Digite um valor válido, por exemplo 150,00."))
        {
            TrocoInicial.Focus();
            return;
        }
        BotaoAbrir.IsEnabled = false;
        try
        {
            await _caixaServico.AbrirAsync(troco!.Value);
            await CarregarAsync();
            _area.MostrarToast("Caixa aberto. Boas vendas!");
        }
        catch (ConexaoException ex)
        {
            Formulario.Validar(TrocoInicial, TrocoInicialErro, false, ex.Message);
        }
        finally
        {
            BotaoAbrir.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------ Busca e leitor

    private void Busca_TextChanged(object sender, TextChangedEventArgs e)
    {
        DicaBusca.Visibility = Busca.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        BuscaErro.Text = "";
        AtualizarResultados();
    }

    private IEnumerable<Produto> Encontrar(string texto)
    {
        var t = Normalizar(texto);
        var porNome = StringComparer.Create(PtBr, CompareOptions.IgnoreCase);
        return _produtos.Where(p => Normalizar(p.Nome).Contains(t) || Normalizar(p.Categoria).Contains(t))
            .OrderByDescending(p => Normalizar(p.Nome).StartsWith(t))
            .ThenBy(p => p.Nome, porNome);
    }

    private void AtualizarResultados()
    {
        Resultados.Children.Clear();
        var texto = Busca.Text.Trim();
        if (Multiplicacao.Match(texto) is { Success: true } m) texto = m.Groups[2].Value.Trim();
        if (texto.Length < 2 || texto.All(char.IsAsciiDigit))
        {
            CaixaResultados.Visibility = Visibility.Collapsed;
            return;
        }
        var achados = Encontrar(texto).Take(6).ToList();
        CaixaResultados.Visibility = Visibility.Visible;
        if (achados.Count == 0)
        {
            Resultados.Children.Add(new TextBlock { Text = "Nenhum produto encontrado.", FontSize = 14, Style = Recurso<Style>("TextoSuave"), Margin = new Thickness(14, 10, 14, 10) });
            return;
        }
        foreach (var p in achados)
        {
            var disponivel = p.DisponivelParaVenda;
            var situacao = disponivel > 0 ? $"Disponível: {p.Formatar(disponivel)}"
                : p.QuantidadeVencida > 0 && p.Vendavel == 0 ? "Só tem lote vencido: venda bloqueada"
                : p.Vendavel > 0 ? "Tudo reservado em vendas abertas" : "Sem estoque";
            var conteudo = new DockPanel();
            var preco = new TextBlock { Text = Moeda(p.Preco) + (p.PorPeso ? "/kg" : ""), FontSize = FonteItem, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(preco, Dock.Right);
            conteudo.Children.Add(preco);
            var nome = new TextBlock { FontSize = FonteItem, VerticalAlignment = VerticalAlignment.Center };
            nome.Inlines.Add(new System.Windows.Documents.Run(p.Nome) { FontWeight = FontWeights.SemiBold });
            nome.Inlines.Add(new System.Windows.Documents.Run($"  {p.Categoria} · {situacao}")
            {
                FontSize = 13, Foreground = Recurso<Brush>(disponivel > 0 ? "Suave" : "Vermelho600")
            });
            conteudo.Children.Add(nome);

            var botao = new Button
            {
                Content = conteudo, IsEnabled = disponivel > 0, Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(14, 10, 14, 10), Template = BotaoLinhaTemplate()
            };
            var produto = p;
            botao.Click += async (_, _) => await AdicionarAsync(new Leitura(produto));
            Resultados.Children.Add(botao);
        }
    }

    private async void Busca_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (Busca.Text.Length > 0 || _multiplicador is not null))
        {
            Busca.Text = "";
            DefinirMultiplicador(null);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Down && Resultados.Children.OfType<Button>().FirstOrDefault(b => b.IsEnabled) is { } primeiro)
        {
            primeiro.Focus();
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await LerBuscaAsync();
    }

    /// <summary>
    /// Enter na busca: código de barras (leitor) ou o primeiro produto disponível com o nome digitado.
    /// "6*" guarda a quantidade para a próxima leitura; "6*789..." já lê com a quantidade.
    /// </summary>
    private async Task LerBuscaAsync()
    {
        var texto = Busca.Text.Trim();
        if (texto.Length == 0) return;
        if (Multiplicacao.Match(texto) is { Success: true } m)
        {
            var q = decimal.Parse(m.Groups[1].Value.Replace('.', ','), NumberStyles.Number, PtBr);
            if (q <= 0)
            {
                ErroBusca("Quantidade inválida antes do *.");
                return;
            }
            DefinirMultiplicador(q);
            texto = m.Groups[2].Value.Trim();
            Busca.Text = "";
            if (texto.Length == 0) return;
        }
        if (texto.All(char.IsAsciiDigit))
        {
            // O leitor "digita" o código e manda Enter
            if (CodigoBarras.Ler(_produtos, texto) is { } leitura) await AdicionarAsync(leitura);
            else ErroBusca($"Nenhum produto com o código {texto}. Confira o cadastro no Estoque ou busque pelo nome.");
            return;
        }
        if (Encontrar(texto).FirstOrDefault(p => p.DisponivelParaVenda > 0) is { } achado) await AdicionarAsync(new Leitura(achado));
        else ErroBusca("Nenhum produto disponível com esse nome.");
    }

    private void DefinirMultiplicador(decimal? q)
    {
        _multiplicador = q;
        Multiplicador.Visibility = q is null ? Visibility.Collapsed : Visibility.Visible;
        MultiplicadorTexto.Text = q is { } v ? $"× {Formulario.Quantidade(v)}" : "";
    }

    private void ErroBusca(string mensagem)
    {
        BuscaErro.Text = mensagem;
        Busca.SelectAll();
        System.Media.SystemSounds.Beep.Play();
    }

    private void FocarBusca()
    {
        Busca.Focus();
        Busca.SelectAll();
    }

    // ------------------------------------------------------------ Itens

    /// <summary>Com pagamento já recebido, os itens não mudam (como no caixa de verdade).</summary>
    private bool ItensTravados(Action<string> erro)
    {
        if (_pagamentos.Count == 0) return false;
        erro("Já há pagamento recebido. Finalize a venda ou estorne os pagamentos (×) antes de mudar os itens.");
        return true;
    }

    /// <summary>Põe o produto na venda: + quantidade (1 ou a do "6*"), o peso da etiqueta, ou abre o campo do peso.</summary>
    private async Task AdicionarAsync(Leitura leitura)
    {
        var p = leitura.Produto;
        var mult = _multiplicador;
        Busca.Text = "";
        DefinirMultiplicador(null);
        if (ItensTravados(ErroBusca)) return;
        if (p.DisponivelParaVenda <= 0 && leitura.PesoEtiqueta is null && !_linhas.Any(l => l.Produto.Id == p.Id && p.PorPeso))
        {
            ErroBusca(p.Vendavel == 0 && p.QuantidadeVencida > 0
                ? $"{p.Nome}: só há lote vencido. A venda está bloqueada; retire o produto da prateleira."
                : p.Vendavel > 0 ? $"{p.Nome} está todo reservado em vendas abertas." : $"{p.Nome} está sem estoque.");
            return;
        }
        if (!p.PorPeso && mult is { } m && m % 1 != 0)
        {
            ErroBusca($"{p.Nome} é vendido por unidade: use um número inteiro antes do *.");
            return;
        }
        var linha = _linhas.FirstOrDefault(l => l.Produto.Id == p.Id);
        if (p.PorPeso && leitura.PesoEtiqueta is null && mult is null)
        {
            // Produto por quilo sem etiqueta: a pessoa digita o peso da balança
            if (linha is null)
            {
                linha = new Linha(p);
                _linhas.Add(linha);
            }
            _ultima = linha;
            MostrarVenda();
            linha.Campo?.Focus();
            linha.Campo?.SelectAll();
            return;
        }
        await ReservarAsync(p, linha, leitura.PesoEtiqueta ?? mult ?? 1, somar: true, leitura.ValorEtiqueta);
        FocarBusca();
    }

    /// <summary>Grava no banco a quantidade digitada no campo da linha. false = continua com erro.</summary>
    private async Task<bool> GravarQuantidadeAsync(Linha linha)
    {
        if (linha.Campo is null || !_linhas.Contains(linha)) return true;
        var p = linha.Produto;
        var texto = linha.Campo.Text.Trim();
        var q = Formulario.LerQuantidade(texto, p.PorPeso);
        if (linha.Item is not null && q == linha.Quantidade) return true;
        if (linha.Item is null && texto.Length == 0) return false;
        if (q is null or <= 0)
        {
            Formulario.Validar(linha.Campo, linha.Erro!, false, p.PorPeso ? "Digite o peso (ex.: 0,750)" : "Quantidade inválida");
            return false;
        }
        if (ItensTravados(m => Formulario.Validar(linha.Campo, linha.Erro!, false, m)))
        {
            linha.Campo.Text = Formulario.Quantidade(linha.Quantidade);
            return false;
        }
        long? autorizacao = null;
        if (linha.Item is not null && q < linha.Quantidade)
        {
            // Diminuir é cancelar parte do item: precisa do gerente
            var (ok, id) = await AutorizarAsync("cancelar_item",
                $"Diminuir {p.Nome} de {Formulario.Quantidade(linha.Quantidade)} para {Formulario.Quantidade(q.Value)} {p.Sigla}");
            if (!ok)
            {
                linha.Campo.Text = Formulario.Quantidade(linha.Quantidade);
                return false;
            }
            autorizacao = id;
        }
        return await ReservarAsync(p, linha, q.Value, somar: false, null, autorizacao);
    }

    private async Task<bool> ReservarAsync(Produto p, Linha? linha, decimal quantidade, bool somar, decimal? valorEtiqueta, long? autorizacao = null)
    {
        if (_caixaServico is null) return false;
        await _fila.WaitAsync();
        try
        {
            var antes = linha?.Quantidade ?? 0;
            var r = await _caixaServico.ReservarAsync(p.Id, quantidade, somar, valorEtiqueta, autorizacao);
            _vendaId = r.VendaId;
            if (linha is null)
            {
                linha = new Linha(p);
                _linhas.Add(linha);
            }
            linha.Item ??= new ItemVenda { ProdutoId = p.Id, NomeProduto = p.Nome, UnidadeMedida = p.UnidadeMedida, PrecoUnitario = p.Preco };
            linha.Item.Id = r.ItemId ?? 0;
            linha.Item.Quantidade = r.Quantidade;
            linha.Item.Subtotal = r.Subtotal;
            p.Reservado += r.Quantidade - antes;
            if (r.ItemId is null) _linhas.Remove(linha);
            else _ultima = linha;
            _desconto = Math.Min(_desconto, ValorItens);
            MostrarVenda();
            return true;
        }
        catch (ConexaoException ex)
        {
            if (linha?.Campo is { } campo && _linhas.Contains(linha) && !somar)
            {
                Formulario.Validar(campo, linha.Erro!, false, ex.Message);
                campo.Focus();
                campo.SelectAll();
            }
            else
            {
                ErroBusca(ex.Message);
            }
            return false;
        }
        finally
        {
            _fila.Release();
        }
    }

    /// <summary>Cancelar item: com o gerente (o gerente logado não precisa digitar a senha).</summary>
    private async Task TirarAsync(Linha linha)
    {
        if (linha.Item is null)
        {
            _linhas.Remove(linha);
            MostrarVenda();
            FocarBusca();
            return;
        }
        if (ItensTravados(ErroBusca)) return;
        var (ok, id) = await AutorizarAsync("cancelar_item",
            $"Cancelar o item {linha.Produto.Nome} ({Formulario.Quantidade(linha.Quantidade)} {linha.Produto.Sigla}, {Moeda(linha.Subtotal)}).");
        if (ok) await ReservarAsync(linha.Produto, linha, 0, somar: false, null, id);
        FocarBusca();
    }

    /// <summary>
    /// Pede a senha do gerente para uma ação do caixa. O gerente logado já está autorizado (o banco confere de novo).
    /// Devolve se pode seguir e o id da autorização.
    /// </summary>
    private async Task<(bool Ok, long? Id)> AutorizarAsync(string acao, string texto)
    {
        if (_gerente || _caixaServico is null) return (_caixaServico is not null, null);
        var janela = new AutorizacaoJanela(_area, _caixaServico, acao, _vendaId, texto);
        _area.AbrirJanela(janela);
        var id = await janela.Resultado;
        return (id is not null, id);
    }

    // ------------------------------------------------------------ Pagamento

    private void MontarFormas()
    {
        Formas.Children.Clear();
        foreach (var (id, nome, curto, tecla) in FormaPagamento.Todas)
        {
            var conteudo = new DockPanel();
            var atalho = new TextBlock { Text = tecla, FontSize = 11, Style = Recurso<Style>("TextoSuave"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(atalho, Dock.Right);
            conteudo.Children.Add(atalho);
            conteudo.Children.Add(new TextBlock { Text = curto, VerticalAlignment = VerticalAlignment.Center });
            var botao = new Button
            {
                Content = conteudo, Style = Recurso<Style>("Chip"), Uid = id, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Tag = _forma == id ? "ativo" : null, FontSize = _modoCaixa ? 17 : 14, MinHeight = _modoCaixa ? 54 : 40, ToolTip = $"{nome} ({tecla})"
            };
            System.Windows.Automation.AutomationProperties.SetName(botao, $"{nome} ({tecla})");
            botao.Click += (_, _) => EscolherForma(id);
            Formas.Children.Add(botao);
        }
    }

    private void EscolherForma(string? forma)
    {
        _forma = forma;
        foreach (var b in Formas.Children.OfType<Button>()) b.Tag = b.Uid == forma ? "ativo" : null;
        FormaErro.Text = "";
        ValorErro.Text = "";
        Valor.Tag = null;
        Valor.Text = "";
        PainelValor.Visibility = forma is null ? Visibility.Collapsed : Visibility.Visible;
        RotuloValor.Text = forma == "dinheiro" ? "Valor recebido (R$)" : "Valor (R$)";
        ColunaParcelas.Width = forma == "credito" ? new GridLength(120) : new GridLength(0);
        Parcelas.SelectedIndex = 0;
        MostrarPagamento();
        if (forma is not null) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Valor.Focus());
    }

    private void Valor_TextChanged(object sender, TextChangedEventArgs e)
    {
        DicaValor.Visibility = Valor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Valor.Tag = null;
        ValorErro.Text = "";
        MostrarPagamento();
    }

    private async void Valor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await ReceberAsync();
    }

    private async void Finalizar_Click(object sender, RoutedEventArgs e) => await ReceberAsync();

    /// <summary>
    /// F4 / botão: recebe o valor na forma escolhida (vazio = o que falta). Dinheiro pode passar do que falta (troco);
    /// cartão, vale e Pix vão para a maquininha. Quando não falta mais nada, finaliza a venda.
    /// </summary>
    private async Task ReceberAsync()
    {
        if (_caixaServico is null || _caixa is null || _recebendo) return;
        _recebendo = true;
        try
        {
            // Quantidade digitada e ainda não gravada (Enter não apertado)
            foreach (var l in _linhas.ToList())
                if (!await GravarQuantidadeAsync(l))
                {
                    if (l.Item is null && l.Campo is not null) Formulario.Validar(l.Campo, l.Erro!, false, "Digite o peso (ex.: 0,750)");
                    l.Campo?.Focus();
                    return;
                }
            if (_linhas.Count == 0 || _vendaId is null)
            {
                ErroBusca("Adicione pelo menos um produto à venda.");
                FocarBusca();
                return;
            }
            var falta = FaltaPagar;
            if (falta == 0)
            {
                await FinalizarVendaAsync();
                return;
            }
            if (_forma is null)
            {
                FormaErro.Text = "Escolha a forma de pagamento (F5 a F9).";
                return;
            }

            decimal valor = falta;
            if (Valor.Text.Trim().Length > 0)
            {
                var lido = Formulario.LerPreco(Valor.Text);
                var msg = lido is null or <= 0 ? "Digite um valor válido, por exemplo 50,00."
                    : _forma != "dinheiro" && lido > falta ? $"No {FormaPagamento.Curto(_forma).ToLower(PtBr)} o valor não pode passar do que falta ({Moeda(falta)})."
                    : "";
                if (!Formulario.Validar(Valor, ValorErro, msg.Length == 0, msg))
                {
                    Valor.Focus();
                    return;
                }
                valor = lido!.Value;
            }

            PagamentoVenda pagamento;
            if (_forma == "dinheiro")
            {
                var aplicado = Math.Min(valor, falta);
                pagamento = new PagamentoVenda { Forma = "dinheiro", Valor = aplicado, Recebido = valor, Troco = valor - aplicado };
            }
            else
            {
                var parcelas = _forma == "credito" ? (int)((ComboBoxItem)Parcelas.SelectedItem).Tag : 1;
                var janela = new MaquininhaJanela(_area, Maquininha.Atual, new Cobranca(_forma, valor, parcelas), $"VENDA{_vendaId}");
                _area.AbrirJanela(janela);
                var resposta = await janela.Resultado;
                if (resposta is null)
                {
                    FormaErro.Text = "Pagamento não aprovado ou cancelado. Escolha outra forma ou tente de novo.";
                    return;
                }
                pagamento = new PagamentoVenda
                {
                    Forma = _forma, Valor = valor, Bandeira = resposta.Bandeira, Nsu = resposta.Nsu, Autorizacao = resposta.Autorizacao,
                    Parcelas = _forma == "credito" ? parcelas : null, FinalCartao = resposta.FinalCartao
                };
            }
            _pagamentos.Add(pagamento);
            EscolherForma(null);
            if (FaltaPagar == 0) await FinalizarVendaAsync();
            else FormaErro.Text = "";
        }
        finally
        {
            _recebendo = false;
        }
    }

    /// <summary>Tudo pago: o banco confere os pagamentos e dá baixa nos lotes.</summary>
    private async Task FinalizarVendaAsync()
    {
        BotaoFinalizar.IsEnabled = false;
        await _fila.WaitAsync();
        try
        {
            var r = await _caixaServico!.FinalizarAsync(_vendaId!.Value, _pagamentos, _cpf);
            var formas = string.Join(" + ", _pagamentos.Select(p => FormaPagamento.Curto(p.Forma)).Distinct()).ToLower(PtBr);
            LimparVenda();
            EscolherForma(null);
            UltimaVendaTexto.Text = r.Troco > 0
                ? $"Venda nº {r.Id} registrada ({Moeda(r.Total)}, {formas}). Troco: {Moeda(r.Troco)}."
                : $"Venda nº {r.Id} registrada ({Moeda(r.Total)}, {formas}).";
            UltimaVenda.Visibility = Visibility.Visible;
            _area.MostrarToast(r.Troco > 0 ? $"Venda nº {r.Id} registrada. Troco: {Moeda(r.Troco)}." : $"Venda nº {r.Id} registrada ({Moeda(r.Total)}).");
            MostrarVenda();
        }
        catch (ConexaoException ex)
        {
            FormaErro.Text = ex.Message;
            return;
        }
        finally
        {
            _fila.Release();
            BotaoFinalizar.IsEnabled = true;
        }
        FocarBusca();
        await AtualizarEmSegundoPlanoAsync();
    }

    /// <summary>Tira um pagamento ainda não finalizado (cartão/Pix: estorno simulado na maquininha de teste).</summary>
    private void EstornarPagamento(PagamentoVenda pagamento)
    {
        if (pagamento.Forma == "dinheiro")
        {
            _pagamentos.Remove(pagamento);
            MostrarPagamento();
            return;
        }
        _area.AbrirJanela(new ConfirmacaoJanela(_area, "Estornar o pagamento?",
            $"{FormaPagamento.Nome(pagamento.Forma)} de {Moeda(pagamento.Valor)} ({pagamento.Detalhe}) volta para o cliente. " +
            "Na maquininha de teste o estorno é simulado.",
            () =>
            {
                _pagamentos.Remove(pagamento);
                _area.FecharJanela();
                MostrarPagamento();
                _area.MostrarToast("Pagamento estornado.");
                return Task.CompletedTask;
            }, "Estornar", "Voltar"));
    }

    // ------------------------------------------------------------ Funções do caixa

    private void Cpf_Click(object sender, RoutedEventArgs e) => PerguntarCpf();

    private void PerguntarCpf() =>
        _area.AbrirJanela(CaixaJanelas.Cpf(_area, _cpf, cpf => { _cpf = cpf; MostrarPagamento(); FocarBusca(); }));

    private void Desconto_Click(object sender, RoutedEventArgs e) => PerguntarDesconto();

    private void PerguntarDesconto()
    {
        if (_linhas.Count == 0 || _vendaId is not { } vendaId || ItensTravados(m => FormaErro.Text = m)) return;
        _area.AbrirJanela(CaixaJanelas.Desconto(_area, ValorItens, _desconto, async valor =>
        {
            var (ok, id) = await AutorizarAsync("desconto", $"Desconto de {Moeda(valor)} na venda de {Moeda(ValorItens)}.");
            if (!ok) return;
            try
            {
                await _caixaServico!.AplicarDescontoAsync(vendaId, valor, id);
                _desconto = valor;
                MostrarPagamento();
                _area.MostrarToast(valor > 0 ? $"Desconto de {Moeda(valor)} aplicado." : "Desconto retirado.");
            }
            catch (ConexaoException ex)
            {
                FormaErro.Text = ex.Message;
            }
        }));
    }

    private async void Espera_Click(object sender, RoutedEventArgs e) => await PorEmEsperaAsync();

    /// <summary>F12: com itens, põe a venda em espera; sem itens, mostra as vendas em espera para retomar.</summary>
    private async Task PorEmEsperaAsync()
    {
        if (_caixaServico is null || _caixa is null) return;
        if (_linhas.All(l => l.Item is null))
        {
            if (_emEspera.Count > 0) MostrarVendasEspera();
            return;
        }
        if (ItensTravados(m => FormaErro.Text = m)) return;
        try
        {
            var id = _vendaId!.Value;
            await _caixaServico.SuspenderAsync(id);
            LimparVenda();
            EscolherForma(null);
            _emEspera = await _caixaServico.VendasEmEsperaAsync(_caixa.Id);
            MostrarVenda();
            _area.MostrarToast($"Venda nº {id} em espera. Os itens continuam reservados.");
            FocarBusca();
        }
        catch (ConexaoException ex)
        {
            FormaErro.Text = ex.Message;
        }
    }

    private void VendasEspera_Click(object sender, RoutedEventArgs e) => MostrarVendasEspera();

    private void MostrarVendasEspera()
    {
        if (_caixaServico is null) return;
        _area.AbrirJanela(CaixaJanelas.Espera(_area, _emEspera, async v =>
        {
            await _caixaServico.RetomarAsync(v.Id);
            await CarregarAsync();
            _area.MostrarToast($"Venda nº {v.Id} retomada.");
        }));
    }

    private void Consulta_Click(object sender, RoutedEventArgs e) => _area.AbrirJanela(CaixaJanelas.ConsultaPreco(_area, _produtos));

    private async void CancelarVenda_Click(object sender, RoutedEventArgs e) => await CancelarVendaAsync();

    /// <summary>Cancelar a venda inteira: com itens, precisa do gerente. Pagamentos com cartão são estornados (simulado).</summary>
    private async Task CancelarVendaAsync()
    {
        var n = _linhas.Count(l => l.Item is not null);
        if (_linhas.Count == 0 || _caixaServico is null) return;
        var cartoes = _pagamentos.Where(p => p.Forma != "dinheiro").ToList();
        var texto = $"Cancelar a venda com {(n == 1 ? "1 item" : $"{n} itens")} ({Moeda(TotalVenda)}). Os itens voltam a ficar disponíveis; nada sai do estoque." +
                    (cartoes.Count > 0 ? $" Os {cartoes.Count} pagamento(s) na maquininha são estornados (simulado)." : "") +
                    (_pagamentos.Any(p => p.Forma == "dinheiro") ? " Devolva o dinheiro recebido." : "");
        long? autorizacao = null;
        if (n > 0)
        {
            var (ok, id) = await AutorizarAsync("cancelar_venda", texto);
            if (!ok) return;
            autorizacao = id;
        }
        try
        {
            if (n > 0 && _vendaId is { } vendaId) await _caixaServico.CancelarAsync(vendaId, autorizacao);
            foreach (var l in _linhas) l.Produto.Reservado -= l.Quantidade;
            LimparVenda();
            EscolherForma(null);
            MostrarVenda();
            _area.MostrarToast("Venda cancelada.");
            FocarBusca();
        }
        catch (ConexaoException ex)
        {
            FormaErro.Text = ex.Message;
        }
    }

    /// <summary>Só o gerente: a venda sai dos totais e os itens voltam para os mesmos lotes.</summary>
    private void ConfirmarCancelamentoPaga(Venda v)
    {
        var cartoes = v.Pagamentos.Where(p => p.Forma != "dinheiro").ToList();
        _area.AbrirJanela(new ConfirmacaoJanela(_area, $"Cancelar a venda nº {v.Id}?",
            $"A venda de {Moeda(v.Total)} ({v.ResumoFormas.ToLower(PtBr)}) sai dos totais do dia e os itens voltam para o estoque. " +
            (cartoes.Count > 0 ? $"Estorne na maquininha: {string.Join("; ", cartoes.Select(c => $"{FormaPagamento.Curto(c.Forma)} {Moeda(c.Valor)} {c.Detalhe}"))}. " : "") +
            (v.Pagamentos.Any(p => p.Forma == "dinheiro") || v.Pagamentos.Count == 0 ? "Devolva o dinheiro ao cliente." : ""),
            async () =>
            {
                await _caixaServico!.CancelarAsync(v.Id);
                _area.FecharJanela();
                _area.MostrarToast($"Venda nº {v.Id} cancelada.");
                await AtualizarEmSegundoPlanoAsync();
            }, "Cancelar venda", "Voltar"));
    }

    // ------------------------------------------------------------ Modo caixa e atalhos

    private void ChaveModo_Click(object sender, RoutedEventArgs e)
    {
        var ligar = ((CheckBox)sender).IsChecked == true;
        AplicarModo(ligar);
        var prefs = PreferenciasLocais.Carregar();
        prefs.ModoCaixa = ligar;
        prefs.Salvar();
        if (_caixa is not null) FocarBusca();
    }

    /// <summary>Modo caixa: tela inteira (sem o menu), letras e botões maiores, sem o resumo e a lista do dia.</summary>
    private void AplicarModo(bool ligado)
    {
        _modoCaixa = ligado;
        ChaveModoNormal.IsChecked = ligado;
        ChaveModoCaixa.IsChecked = ligado;
        _area.TelaCheia(ligado);

        var normal = ligado ? Visibility.Collapsed : Visibility.Visible;
        CabecalhoNormal.Visibility = normal;
        ResumoDia.Visibility = normal;
        CartaoVendas.Visibility = normal;
        BarraModoCaixa.Visibility = ligado ? Visibility.Visible : Visibility.Collapsed;
        LegendaAtalhos.Visibility = ligado ? Visibility.Visible : Visibility.Collapsed;
        if (ligado) { Relogio.Text = DateTime.Now.ToString("HH:mm"); _relogio.Start(); } else _relogio.Stop();

        AreaVenda.Margin = ligado ? new Thickness(20, 16, 20, 16) : new Thickness(0, 0, 0, 20);
        PainelAbrir.Margin = ligado ? new Thickness(20, 20, 20, 20) : new Thickness(0, 0, 0, 20);
        AvisoErro.Margin = ligado ? new Thickness(20, 16, 20, 0) : new Thickness(0, 0, 0, 20);
        LinhaVenda.Height = ligado ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        RolagemItens.MaxHeight = ligado ? double.PositiveInfinity : 420;
        CartaoItens.VerticalAlignment = ligado ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        CartaoPagamento.VerticalAlignment = ligado ? VerticalAlignment.Stretch : VerticalAlignment.Top;

        Busca.MinHeight = ligado ? 58 : 46;
        Busca.FontSize = ligado ? 20 : 15;
        DicaBusca.FontSize = ligado ? 20 : 15;
        IconeBusca.Width = ligado ? 24 : 20;
        DestaqueNome.FontSize = ligado ? 26 : 20;
        DestaqueSubtotal.FontSize = ligado ? 30 : 22;
        Total.FontSize = ligado ? 50 : 34;
        BotaoFinalizar.Height = ligado ? 64 : 52;
        BotaoFinalizar.FontSize = ligado ? 19 : 15;
        Valor.MinHeight = ligado ? 52 : 46;
        Valor.FontSize = ligado ? 19 : 15;
        Troco.FontSize = ligado ? 28 : 20;
        Falta.FontSize = ligado ? 26 : 20;

        MontarFormas();
        if (_caixa is not null) MostrarVenda();
        AtualizarResultados();
        AjustarColunas(ActualWidth);
    }

    private void Atalhos(object sender, KeyEventArgs e)
    {
        if (e.Handled || _caixa is null || AreaVenda.Visibility != Visibility.Visible) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.F2:
                FocarBusca();
                break;
            case Key.F3:
                _area.AbrirJanela(CaixaJanelas.ConsultaPreco(_area, _produtos));
                break;
            case Key.F4:
                _ = ReceberAsync();
                break;
            case Key.F5 or Key.F6 or Key.F7 or Key.F8:
                EscolherForma(FormaPagamento.Todas[e.Key - Key.F5].Id);
                break;
            case Key.F9:
                EscolherForma(shift ? "vale_refeicao" : "vale_alimentacao");
                break;
            case Key.F11:
                PerguntarCpf();
                break;
            case Key.F12:
                _ = PorEmEsperaAsync();
                break;
            case Key.D when ctrl:
                PerguntarDesconto();
                break;
            case Key.Escape when Busca.Text.Length == 0 && _multiplicador is null && _linhas.LastOrDefault() is { } ultima:
                _ = TirarAsync(ultima);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void AoRedimensionar(object sender, SizeChangedEventArgs e) => AjustarColunas(e.NewSize.Width);

    /// <summary>Janela estreita: o pagamento desce para baixo dos itens; o resumo vira uma coluna.</summary>
    private void AjustarColunas(double largura)
    {
        if (largura <= 0) return;
        ResumoDia.Columns = largura < 600 ? 1 : 3;
        var empilhar = largura < (_modoCaixa ? 860 : 800);
        ColunaPagamento.Width = empilhar ? new GridLength(0) : new GridLength(_modoCaixa ? 420 : 380);
        Grid.SetColumn(CartaoPagamento, empilhar ? 0 : 1);
        Grid.SetRow(CartaoPagamento, empilhar ? 1 : 0);
        CartaoItens.Margin = new Thickness(0, 0, empilhar ? 0 : 20, empilhar ? 16 : 0);
    }

    // ------------------------------------------------------------ Ajudantes

    private T Recurso<T>(string chave) => (T)FindResource(chave);

    private static string Moeda(decimal valor) => valor.ToString("C", PtBr);

    private static string Normalizar(string texto)
    {
        var decomposto = texto.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }

    /// <summary>Botão sem borda que destaca a linha ao passar o mouse (lista de resultados da busca).</summary>
    private ControlTemplate BotaoLinhaTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var borda = new FrameworkElementFactory(typeof(Border), "Fundo");
        borda.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        borda.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var conteudo = new FrameworkElementFactory(typeof(ContentPresenter));
        borda.AppendChild(conteudo);
        template.VisualTree = borda;
        var sobre = new Trigger { Property = IsMouseOverProperty, Value = true };
        sobre.Setters.Add(new Setter(Border.BackgroundProperty, Recurso<Brush>("Verde50"), "Fundo"));
        var foco = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        foco.Setters.Add(new Setter(Border.BackgroundProperty, Recurso<Brush>("EtiquetaClara"), "Fundo"));
        var desligado = new Trigger { Property = IsEnabledProperty, Value = false };
        desligado.Setters.Add(new Setter(OpacityProperty, 0.75));
        template.Triggers.Add(sobre);
        template.Triggers.Add(foco);
        template.Triggers.Add(desligado);
        return template;
    }

    partial void IniciarAtalhosDesenvolvimento();
}
