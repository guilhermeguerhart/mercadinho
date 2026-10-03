using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Estrutura comum das telas internas (igual a js/app.js do site): menu lateral com as telas
/// liberadas para o perfil, usuário logado, botão Sair e aviso rápido (toast).
/// </summary>
public partial class AreaInternaView : UserControl
{
    private sealed record ItemMenu(string Id, string Nome, string Icone, bool Pronto, Perfil[] Perfis);

    // Pronto = false: tela das próximas semanas (mostra "em breve").
    private static readonly ItemMenu[] Menu =
    [
        new("painel", "Painel", "IconePainel", true, [Perfil.Gerente]),
        new("estoque", "Estoque", "IconeCaixaProduto", false, [Perfil.Gerente]),
        new("fornecedores", "Fornecedores", "IconeCaminhao", false, [Perfil.Gerente]),
        new("caixa", "Caixa", "IconeCaixaRegistradora", true, [Perfil.Gerente, Perfil.Operador]),
        new("fechamento", "Fechamento do dia", "IconeFechamento", false, [Perfil.Gerente, Perfil.Operador]),
    ];

    private readonly MainWindow _janela;
    private readonly Sessao _sessao;
    private readonly DispatcherTimer _timerToast = new() { Interval = TimeSpan.FromSeconds(2.8) };
    private bool _compacta;

    public AreaInternaView(MainWindow janela, Sessao sessao)
    {
        InitializeComponent();
        _janela = janela;
        _sessao = sessao;
        _timerToast.Tick += (_, _) => { Toast.Visibility = Visibility.Collapsed; _timerToast.Stop(); };
        Unloaded += (_, _) => _timerToast.Stop();

        Avatar.Text = sessao.Nome[..1].ToUpperInvariant();
        NomeUsuario.Text = sessao.Nome;
        PerfilUsuario.Text = sessao.Perfil == Perfil.Gerente ? "Gerente" : "Operador";

        // Tela inicial de cada perfil: gerente no Painel, operador no Caixa
        Abrir(sessao.Perfil == Perfil.Gerente ? "painel" : "caixa");
    }

    public void Abrir(string id)
    {
        var item = Menu.First(m => m.Id == id);
        if (!item.Pronto || !item.Perfis.Contains(_sessao.Perfil))
        {
            EmBreve(item.Nome);
            return;
        }

        Pagina.Content = id switch
        {
            "painel" => new PainelView(this, _sessao),
            _ => new CaixaView(),
        };
        MontarMenu(id);
        Rolagem.ScrollToTop();
        if (_compacta) MostrarMenuCompacto(false);
    }

    public void EmBreve(string nomeTela) => MostrarToast($"A tela “{nomeTela}” será desenvolvida na próxima etapa.");

    public void MostrarToast(string texto)
    {
        ToastTexto.Text = texto;
        Toast.Visibility = Visibility.Visible;
        _timerToast.Stop();
        _timerToast.Start();
    }

    private void MontarMenu(string ativo)
    {
        ItensMenu.Children.Clear();
        foreach (var item in Menu.Where(m => m.Perfis.Contains(_sessao.Perfil)))
        {
            var linha = new DockPanel();
            var icone = new ContentControl
            {
                Style = (Style)FindResource("Icone"),
                Tag = FindResource(item.Icone),
                Width = 20,
                Margin = new Thickness(0, 0, 12, 0)
            };
            icone.SetBinding(ForegroundProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1)
            });
            linha.Children.Add(icone);

            if (!item.Pronto)
            {
                var embreve = new Border
                {
                    Background = (Brush)FindResource("Fundo"),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(7, 1, 7, 1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "em breve", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Suave") }
                };
                DockPanel.SetDock(embreve, Dock.Right);
                linha.Children.Add(embreve);
            }
            linha.Children.Add(new TextBlock { Text = item.Nome, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) });

            var botao = new Button
            {
                Style = (Style)FindResource("ItemMenu"),
                Content = linha,
                Tag = item.Id == ativo ? "ativo" : null
            };
            System.Windows.Automation.AutomationProperties.SetName(botao, item.Nome);
            botao.Click += (_, _) => Abrir(item.Id);
            ItensMenu.Children.Add(botao);
        }
    }

    private void Sair_Click(object sender, RoutedEventArgs e) => _janela.MostrarInicio();

    // ------------------------------------------------------------ Janela estreita (como no celular do site)

    private void AoRedimensionar(object sender, SizeChangedEventArgs e)
    {
        var compacta = e.NewSize.Width < 900;
        if (compacta == _compacta) return;
        _compacta = compacta;

        ColunaMenu.Width = compacta ? new GridLength(0) : new GridLength(264);
        BarraCompacta.Visibility = compacta ? Visibility.Visible : Visibility.Collapsed;
        MarcaMenu.Visibility = compacta ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(MenuLateral, compacta ? 1 : 0);
        Grid.SetRowSpan(MenuLateral, compacta ? 1 : 2);
        AreaConteudo.Margin = compacta ? new Thickness(16, 24, 16, 40) : new Thickness(40, 32, 40, 48);
        MostrarMenuCompacto(false);
    }

    private void MostrarMenuCompacto(bool aberto)
    {
        MenuLateral.Visibility = !_compacta || aberto ? Visibility.Visible : Visibility.Collapsed;
        FundoMenu.Visibility = _compacta && aberto ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AlternarMenu_Click(object sender, RoutedEventArgs e) =>
        MostrarMenuCompacto(MenuLateral.Visibility != Visibility.Visible);

    private void FundoMenu_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => MostrarMenuCompacto(false);
}
