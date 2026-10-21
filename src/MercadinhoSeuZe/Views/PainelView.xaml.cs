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
/// Painel do gerente (igual a painel.html do site). Os avisos de estoque e os pedidos pendentes vêm do banco
/// (semanas 3 e 4); vendas e caixa ainda são dados de exemplo até a semana 5.
/// </summary>
public partial class PainelView : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly AreaInternaView _area;
    private readonly Sessao _sessao;
    private readonly EstoqueService? _estoque;
    private readonly FornecedorService? _fornecedores;
    private bool _atualizando;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };

    public PainelView(AreaInternaView area, Sessao sessao, EstoqueService? estoque, FornecedorService? fornecedores)
    {
        InitializeComponent();
        _area = area;
        _sessao = sessao;
        _estoque = estoque;
        _fornecedores = fornecedores;

        _timer.Tick += async (_, _) => await AtualizarAsync();
        Loaded += async (_, _) => { _timer.Start(); await AtualizarAsync(); };
        Unloaded += (_, _) => _timer.Stop();
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

        var dados = DadosExemplo.Painel();

        // Estoque real do banco
        List<Produto>? produtos = null;
        string? erroEstoque = null;
        if (_estoque is null) erroEstoque = "Sistema sem configuração do banco de dados.";
        else
        {
            try { produtos = await _estoque.ListarAsync(); }
            catch (ConexaoException ex) { erroEstoque = ex.Message; }
        }
        // Pedidos pendentes do banco (semana 4)
        List<Pedido>? pedidos = null;
        Dictionary<long, string> nomesFornecedores = [];
        string? erroPedidos = null;
        if (_fornecedores is not null)
        {
            try
            {
                var lista = _fornecedores.ListarAsync();
                var pendentes = _fornecedores.ListarPendentesAsync();
                await Task.WhenAll(lista, pendentes);
                nomesFornecedores = lista.Result.ToDictionary(f => f.Id, f => f.Nome);
                if (produtos is not null) Fornecedor.AplicarPrazos(produtos, lista.Result);
                pedidos = pendentes.Result;
            }
            catch (ConexaoException ex) { erroPedidos = ex.Message; }
        }

        var porNome = StringComparer.Create(PtBr, CompareOptions.IgnoreCase);
        var semEstoque = produtos?.Where(p => p.SemEstoque).OrderBy(p => p.Nome, porNome).ToList() ?? [];
        var baixos = produtos?.Where(p => p.EstoqueBaixo).OrderBy(p => p.Quantidade).ThenBy(p => p.Nome, porNome).ToList() ?? [];
        var antecipados = produtos?.Where(p => p.AvisoAntecipado).OrderBy(p => p.Quantidade).ThenBy(p => p.Nome, porNome).ToList() ?? [];
        var validade = produtos?.Where(p => p.Vencido || p.ValidadeProxima).OrderBy(p => p.ProximaValidade).ToList() ?? [];

        KpiVendas.Text = Moeda(dados.Resumo.Total);
        KpiVendasSub.Text = Plural(dados.Resumo.QuantidadeVendas, "venda realizada", "vendas realizadas");
        KpiEstoque.Text = produtos is null ? "—" : Plural(baixos.Count + semEstoque.Count, "produto", "produtos");
        KpiEstoqueSub.Text = produtos is null ? "Não foi possível ler o estoque"
            : antecipados.Count > 0 ? $"Abaixo do mínimo ou zerados · +{antecipados.Count} perto do mínimo"
            : "Abaixo do mínimo ou zerados";
        KpiPedidos.Text = pedidos is null ? "—" : Plural(pedidos.Count, "pedido", "pedidos");
        KpiPedidosSub.Text = pedidos is null ? "Não foi possível ler os pedidos"
            : pedidos.Count(p => p.Atrasado) is > 0 and var atrasados ? $"Aguardando entrega · {atrasados} com atraso"
            : "Aguardando entrega do fornecedor";
        KpiTicket.Text = Moeda(dados.Resumo.TicketMedio);
        AtalhoCaixa.Text = dados.Caixa.Aberto ? "Ir para o caixa" : "Abrir caixa";

        ListaAvisos.Children.Clear();
        if (erroEstoque is not null)
            AdicionarAviso("IconeAlerta", "Vermelho50", "Vermelho600", [new Run("Não foi possível ler o estoque: " + erroEstoque)], "Estoque");

        // Avisos de estoque (do mais urgente ao menos urgente), no máximo 8 para não esconder o resto
        var avisosEstoque = new List<(string Icone, string Fundo, string Cor, Inline[] Texto)>();
        foreach (var p in semEstoque)
            avisosEstoque.Add(("IconeAlerta", "Vermelho50", "Vermelho600", [Negrito(p.Nome), new Run(" está sem estoque.")]));
        foreach (var p in baixos)
            avisosEstoque.Add(("IconeTriangulo", "Ambar50", "Ambar700",
                [Negrito(p.Nome), new Run($" está com estoque baixo ({p.Formatar(p.Quantidade)}, mínimo {p.Formatar(p.EstoqueMinimo)}).")]));
        foreach (var p in validade)
            avisosEstoque.Add(p.Situacao!.Nivel == NivelValidade.Vencido
                ? ("IconeRelogio", "Vermelho50", "Vermelho600", [Negrito(p.Nome), new Run($": tem lote vencido ({p.Situacao.Texto.ToLower(PtBr)}).")])
                : ("IconeRelogio", "Ambar50", "Ambar700", [Negrito(p.Nome), new Run($": lote {p.Situacao.Texto.ToLower(PtBr)}.")]));
        foreach (var p in antecipados)
            avisosEstoque.Add(("IconeTriangulo", "Ambar50", "Ambar700",
                [Negrito(p.Nome), new Run($" está chegando no mínimo ({p.Formatar(p.Quantidade)}). Hora de pedir.")]));
        foreach (var a in avisosEstoque.Take(8))
            AdicionarAviso(a.Icone, a.Fundo, a.Cor, a.Texto, "Estoque");
        if (avisosEstoque.Count > 8)
            AdicionarAviso("IconeCaixaProduto", "Verde50", "Verde700",
                [new Run($"E mais {avisosEstoque.Count - 8} avisos de estoque. Abra o Estoque para ver todos.")], "Estoque");

        if (erroPedidos is not null)
            AdicionarAviso("IconeAlerta", "Vermelho50", "Vermelho600", [new Run("Não foi possível ler os pedidos: " + erroPedidos)], "Fornecedores");
        foreach (var p in pedidos?.OrderByDescending(p => p.Atrasado).ThenBy(p => p.Previsao) ?? Enumerable.Empty<Pedido>())
        {
            var fornecedor = nomesFornecedores.GetValueOrDefault(p.FornecedorId, "fornecedor");
            AdicionarAviso("IconeCaminhao", p.Atrasado ? "Vermelho50" : "Azul50", p.Atrasado ? "Vermelho600" : "Azul600",
                p.Atrasado
                    ? [new Run("Pedido para "), Negrito(fornecedor), new Run($" está atrasado (previsto para {p.Previsao:dd/MM/yyyy}).")]
                    : [new Run("Pedido para "), Negrito(fornecedor), new Run(p.Previsao is { } prev ? $" chega até {prev:dd/MM/yyyy}." : " ainda não foi entregue.")],
                $"Pedido de {p.Data:dd/MM/yyyy}");
        }
        if (dados.Caixa.Aberto)
            AdicionarAviso("IconeCaixaRegistradora", "Verde50", "Verde700",
                [new Run("Caixa aberto por "), Negrito(dados.Caixa.AbertoPor ?? ""), new Run($" às {dados.Caixa.AbertoAs}.")], "Hoje");
        if (ListaAvisos.Children.Count == 0)
            ListaAvisos.Children.Add(new TextBlock { Text = "Nenhum aviso no momento.", FontSize = 14, Style = (Style)FindResource("TextoSuave") });

        Atualizado.Text = $"Atualizado às {agora:HH:mm}";
    }

    private void AdicionarAviso(string icone, string fundo, string cor, Inline[] texto, string quando)
    {
        var linha = new DockPanel { Margin = new Thickness(0, ListaAvisos.Children.Count == 0 ? 0 : 12, 0, 12) };
        var caixaIcone = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = (Brush)FindResource(fundo),
            Child = new ContentControl { Style = (Style)FindResource("Icone"), Tag = FindResource(icone), Foreground = (Brush)FindResource(cor), Width = 16 }
        };
        linha.Children.Add(caixaIcone);

        var textoBloco = new TextBlock { FontSize = 14 };
        textoBloco.Inlines.AddRange(texto);
        var conteudo = new StackPanel();
        conteudo.Children.Add(textoBloco);
        conteudo.Children.Add(new TextBlock { Text = quando, FontSize = 12, Style = (Style)FindResource("TextoSuave") });
        linha.Children.Add(conteudo);

        if (ListaAvisos.Children.Count > 0)
            ListaAvisos.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Linha") });
        ListaAvisos.Children.Add(linha);
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
