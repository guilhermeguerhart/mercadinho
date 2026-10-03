using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>Painel do gerente (igual a painel.html do site). Por enquanto usa dados de exemplo.</summary>
public partial class PainelView : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly AreaInternaView _area;
    private readonly Sessao _sessao;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };

    public PainelView(AreaInternaView area, Sessao sessao)
    {
        InitializeComponent();
        _area = area;
        _sessao = sessao;

        _timer.Tick += (_, _) => Atualizar();
        Loaded += (_, _) => { Atualizar(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Atualizar()
    {
        var agora = DateTime.Now;
        Saudacao.Text = $"Olá, {_sessao.Nome}";
        var data = agora.ToString("dddd, d 'de' MMMM", PtBr);
        DataTurno.Text = char.ToUpper(data[0]) + data[1..] + " — " + Turno(agora.Hour);

        var dados = DadosExemplo.Painel();
        var baixos = dados.EstoqueBaixo.ToList();

        KpiVendas.Text = Moeda(dados.Resumo.Total);
        KpiVendasSub.Text = Plural(dados.Resumo.QuantidadeVendas, "venda realizada", "vendas realizadas");
        KpiEstoque.Text = Plural(baixos.Count, "produto", "produtos");
        KpiPedidos.Text = Plural(dados.PedidosPendentes.Count, "pedido", "pedidos");
        KpiTicket.Text = Moeda(dados.Resumo.TicketMedio);
        AtalhoCaixa.Text = dados.Caixa.Aberto ? "Ir para o caixa" : "Abrir caixa";

        ListaAvisos.Children.Clear();
        foreach (var p in baixos)
            AdicionarAviso("IconeTriangulo", "Ambar50", "Ambar700",
                [Negrito(p.Nome), new Run($" está com estoque baixo ({Plural(p.Quantidade, "unidade", "unidades")}).")], "Estoque");
        foreach (var p in dados.PedidosPendentes)
            AdicionarAviso("IconeCaminhao", "Azul50", "Azul600",
                p.Atrasado
                    ? [new Run("Pedido para "), Negrito(p.Fornecedor), new Run($" está atrasado (previsto para {p.Previsao:dd/MM/yyyy}).")]
                    : [new Run("Pedido para "), Negrito(p.Fornecedor), new Run(" ainda não foi entregue.")],
                $"Pedido de {p.Data:dd/MM/yyyy}");
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
