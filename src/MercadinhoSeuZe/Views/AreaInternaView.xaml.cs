using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

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
        new("estoque", "Estoque", "IconeCaixaProduto", true, [Perfil.Gerente]),
        new("fornecedores", "Fornecedores", "IconeCaminhao", true, [Perfil.Gerente]),
        new("previsao", "Previsão de compras", "IconeTendencia", true, [Perfil.Gerente]),
        new("caixa", "Caixa", "IconeCaixaRegistradora", true, [Perfil.Gerente, Perfil.Operador]),
        new("funcionarios", "Funcionários", "IconeEquipe", true, [Perfil.Gerente]),
        new("fechamento", "Fechamento do dia", "IconeFechamento", true, [Perfil.Gerente]),
    ];

    private readonly MainWindow _janela;
    private readonly Sessao _sessao;
    private readonly SupabaseApi? _api;
    private readonly DispatcherTimer _timerToast = new() { Interval = TimeSpan.FromSeconds(2.8) };
    private bool _compacta;
    private bool _telaCheia;
    private bool _simulacaoLigada;
    private string _telaAtual = "";

    public AreaInternaView(MainWindow janela, Sessao sessao, SupabaseApi? api)
    {
        InitializeComponent();
        _janela = janela;
        _sessao = sessao;
        _api = api;
        _timerToast.Tick += (_, _) => { Toast.Visibility = Visibility.Collapsed; _timerToast.Stop(); };
        Unloaded += (_, _) => _timerToast.Stop();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape && Sobreposicao.Visibility == Visibility.Visible)
            {
                FecharJanela();
                e.Handled = true;
            }
        };

        Avatar.Text = sessao.Nome[..1].ToUpperInvariant();
        NomeUsuario.Text = sessao.Nome;
        PerfilUsuario.Text = sessao.Perfil == Perfil.Gerente ? "Gerente" : "Operador";

        // Tela inicial de cada perfil: gerente no Painel, operador no Caixa
        Abrir(sessao.Perfil == Perfil.Gerente ? "painel" : "caixa");
        _ = VerSimulacaoAsync();
    }

    // ------------------------------------------------------------ Modo simulação (script 018, só no desenvolvimento)

    private sealed record EstadoSimulacao([property: System.Text.Json.Serialization.JsonPropertyName("ligada")] bool Ligada);

    /// <summary>Mostra a faixa "Modo simulação" quando a loja está vendendo sozinha. Sem o script 018: nada aparece.</summary>
    private async Task VerSimulacaoAsync()
    {
        if (_api is null) return;
        try
        {
            var estado = (await _api.ListarAsync<EstadoSimulacao>("simulacao?select=ligada&id=eq.1", _sessao.Token)).FirstOrDefault();
            _simulacaoLigada = estado?.Ligada == true;
        }
        catch
        {
            _simulacaoLigada = false;
        }
        BotaoSimularAgora.Visibility = _sessao.Perfil == Perfil.Gerente ? Visibility.Visible : Visibility.Collapsed;
        AtualizarFaixaSimulacao();
    }

    private void AtualizarFaixaSimulacao() =>
        FaixaSimulacao.Visibility = _simulacaoLigada && !_telaCheia ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Gerente: faz a loja andar até agora (sem esperar o agendamento de 30 em 30 minutos).</summary>
    private async void SimularAgora_Click(object sender, RoutedEventArgs e)
    {
        if (_api is null) return;
        BotaoSimularAgora.IsEnabled = false;
        try
        {
            var resposta = await _api.ChamarAsync<string>("simular", new { }, _sessao.Token);
            MostrarToast(resposta ?? "Simulação atualizada.");
            if (_telaAtual == "painel") Abrir("painel");
        }
        catch (Exception ex)
        {
            MostrarToast("Não deu para simular agora: " + ex.Message);
        }
        finally
        {
            BotaoSimularAgora.IsEnabled = true;
        }
    }

    /// <summary>Abre a tela do menu e devolve ela (ou null se não estiver liberada).</summary>
    public UserControl? Abrir(string id)
    {
        var item = Menu.First(m => m.Id == id);
        if (!item.Pronto || !item.Perfis.Contains(_sessao.Perfil))
        {
            EmBreve(item.Nome);
            return null;
        }

        TelaCheia(false);
        UserControl tela = id switch
        {
            "painel" => new PainelView(this, _sessao, Estoque(), Fornecedores(), Caixa()),
            "estoque" => new EstoqueView(this, Estoque(), Fornecedores()),
            "fornecedores" => new FornecedoresView(this, Fornecedores(), Estoque(), Mensagens()),
            "funcionarios" => new FuncionariosView(this, Funcionarios()),
            "fechamento" => new FechamentoView(this, Fechamento(), Caixa()),
            "previsao" => new PrevisaoView(this, Previsao(), Fornecedores(), Estoque()),
            _ => new CaixaView(this, Caixa(), Estoque()),
        };
        Pagina.Content = tela;
        _telaAtual = id;
        MontarMenu(id);
        Rolagem.ScrollToTop();
        if (_compacta) MostrarMenuCompacto(false);
        return tela;
    }

    // Um serviço de cada para todas as telas: o que já se descobriu do banco (scripts rodados ou não) vale para
    // a próxima tela, sem repetir as consultas de teste a cada troca no menu.
    private EstoqueService? _estoque;
    private FornecedorService? _fornecedores;
    private CaixaService? _caixa;
    private FuncionarioService? _funcionarios;
    private FechamentoService? _fechamento;
    private MensagensService? _mensagens;
    private PrevisaoService? _previsao;

    private EstoqueService? Estoque() => _api is null ? null : _estoque ??= new EstoqueService(_api, _sessao);

    private FornecedorService? Fornecedores() => _api is null ? null : _fornecedores ??= new FornecedorService(_api, _sessao);

    private CaixaService? Caixa() => _api is null ? null : _caixa ??= new CaixaService(_api, _sessao);

    private FuncionarioService? Funcionarios() => _api is null ? null : _funcionarios ??= new FuncionarioService(_api, _sessao);

    private FechamentoService? Fechamento() => _api is null ? null : _fechamento ??= new FechamentoService(_api, _sessao);

    private PrevisaoService? Previsao() => _api is null ? null : _previsao ??= new PrevisaoService(_api, _sessao);

    public MensagensService? Mensagens() => _api is null ? null : _mensagens ??= new MensagensService(_api, _sessao);

    private void MeuPerfil_Click(object sender, RoutedEventArgs e)
    {
        if (_compacta) MostrarMenuCompacto(false);
        AbrirJanela(new MeuPerfilJanela(this, _sessao, Funcionarios()));
    }

    /// <summary>Tela inteira (modo caixa): sem o menu e sem margens; a página ocupa a altura da janela, sem rolagem.</summary>
    public void TelaCheia(bool ligada)
    {
        if (ligada == _telaCheia) return;
        _telaCheia = ligada;
        AplicarLayout();
        Pagina.Height = ligada ? Rolagem.ActualHeight : double.NaN;
    }

    private void Rolagem_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_telaCheia) Pagina.Height = e.NewSize.Height;
    }

    /// <summary>Mostra uma janela (cadastro, confirmação) por cima de tudo.</summary>
    public void AbrirJanela(FrameworkElement conteudo)
    {
        conteudo.MaxWidth = Math.Max(300, ActualWidth - 32);
        JanelaAtual.Content = conteudo;
        Sobreposicao.Visibility = Visibility.Visible;
    }

    /// <summary>Tem uma janela (cadastro, confirmação, autorização) aberta por cima da tela.</summary>
    public bool JanelaAberta => Sobreposicao.Visibility == Visibility.Visible;

    public void FecharJanela()
    {
        Sobreposicao.Visibility = Visibility.Collapsed;
        JanelaAtual.Content = null;
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
        AplicarLayout();
    }

    private void AplicarLayout()
    {
        var compacta = _compacta;
        ColunaMenu.Width = compacta || _telaCheia ? new GridLength(0) : new GridLength(264);
        BarraCompacta.Visibility = compacta && !_telaCheia ? Visibility.Visible : Visibility.Collapsed;
        MarcaMenu.Visibility = compacta ? Visibility.Collapsed : Visibility.Visible;
        ToldoMenu.Visibility = compacta ? Visibility.Collapsed : Visibility.Visible;
        MenuLateral.Padding = compacta ? new Thickness(14, 20, 14, 20) : new Thickness(14, 0, 14, 20);
        Grid.SetRow(MenuLateral, compacta ? 1 : 0);
        Grid.SetRowSpan(MenuLateral, compacta ? 1 : 2);
        AreaConteudo.Margin = _telaCheia ? new Thickness(0)
            : compacta ? new Thickness(16, 24, 16, 40) : new Thickness(40, 32, 40, 48);
        Rolagem.VerticalScrollBarVisibility = _telaCheia ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        AtualizarFaixaSimulacao();
        MostrarMenuCompacto(false);
    }

    private void MostrarMenuCompacto(bool aberto)
    {
        MenuLateral.Visibility = !_telaCheia && (!_compacta || aberto) ? Visibility.Visible : Visibility.Collapsed;
        FundoMenu.Visibility = _compacta && aberto && !_telaCheia ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AlternarMenu_Click(object sender, RoutedEventArgs e) =>
        MostrarMenuCompacto(MenuLateral.Visibility != Visibility.Visible);

    private void FundoMenu_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => MostrarMenuCompacto(false);
}
