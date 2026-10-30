using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Funcionários (só o gerente): lista com função, situação e último acesso; cadastrar, editar, redefinir a senha
/// e remover (quem já vendeu fica desativado, para o histórico continuar no nome dele).
/// </summary>
public partial class FuncionariosView : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");
    private static readonly (string Id, string Nome)[] Filtros =
        [("todos", "Todos"), ("gerente", "Gerentes"), ("operador", "Operadores"), ("desativados", "Desativados")];

    private readonly AreaInternaView _area;
    private readonly FuncionarioService? _servico;
    private List<Funcionario> _todos = [];
    private string _filtro = "todos";
    private bool _compacta;

    public FuncionariosView(AreaInternaView area, FuncionarioService? servico)
    {
        InitializeComponent();
        _area = area;
        _servico = servico;
        Loaded += async (_, _) => await CarregarAsync();
    }

    private async Task CarregarAsync()
    {
        AvisoErro.Visibility = Visibility.Collapsed;
        if (_servico is null)
        {
            MostrarErro("Sistema sem configuração do banco de dados. Veja o README.");
            return;
        }
        try
        {
            _todos = await _servico.ListarAsync();
        }
        catch (ConexaoException ex)
        {
            MostrarErro(ex.Message);
            return;
        }
        Mostrar();
    }

    /// <summary>Depois de gravar numa janela: fecha, recarrega e avisa.</summary>
    public async Task RecarregarAsync(string mensagem)
    {
        _area.FecharJanela();
        await CarregarAsync();
        _area.MostrarToast(mensagem);
    }

    private void MostrarErro(string mensagem)
    {
        AvisoErroTexto.Text = mensagem;
        AvisoErro.Visibility = Visibility.Visible;
        Resumo.Text = "Não foi possível carregar os funcionários.";
    }

    private async void TentarDeNovo_Click(object sender, RoutedEventArgs e) => await CarregarAsync();

    private void Mostrar()
    {
        var ativos = _todos.Where(f => f.Ativo).ToList();
        Resumo.Text = $"{Plural(ativos.Count, "funcionário ativo", "funcionários ativos")} · " +
                      $"{Plural(ativos.Count(f => f.Gerente), "gerente", "gerentes")} · {Plural(ativos.Count(f => !f.Gerente), "operador", "operadores")}";

        var pedidos = _todos.Where(f => f.PediuSenha is not null && f.Ativo).ToList();
        AvisoSenhas.Visibility = pedidos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AvisoSenhasTexto.Text = pedidos.Count == 1
            ? $"{pedidos[0].Nome} pediu uma senha nova na tela de login. Use “Redefinir senha” na linha dele."
            : $"{string.Join(", ", pedidos.Select(f => f.Nome))} pediram uma senha nova na tela de login. Use “Redefinir senha” na linha de cada um.";

        Chips.Children.Clear();
        foreach (var (id, nome) in Filtros)
        {
            var n = _todos.Count(f => Passa(f, id, ""));
            if (id == "desativados" && n == 0 && _filtro != id) continue;
            var chip = new Button { Content = $"{nome} ({n})", Style = (Style)FindResource("Chip"), Tag = _filtro == id ? "ativo" : null };
            chip.Click += (_, _) => { _filtro = id; Mostrar(); };
            Chips.Children.Add(chip);
        }

        var busca = Busca.Text.Trim();
        var lista = _todos.Where(f => Passa(f, _filtro, busca)).ToList();
        Lista.Children.Clear();
        if (lista.Count == 0)
            Lista.Children.Add(new TextBlock { Text = "Nenhum funcionário encontrado.", FontSize = 14, Style = (Style)FindResource("TextoSuave"), Margin = new Thickness(0, 16, 0, 0) });
        foreach (var f in lista) Lista.Children.Add(Linha(f));
    }

    private bool Passa(Funcionario f, string filtro, string busca)
    {
        var ok = filtro switch
        {
            "gerente" => f.Ativo && f.Gerente,
            "operador" => f.Ativo && !f.Gerente,
            "desativados" => !f.Ativo,
            _ => true
        };
        return ok && (busca.Length == 0 || f.Nome.Contains(busca, StringComparison.CurrentCultureIgnoreCase)
                      || f.Usuario.Contains(busca, StringComparison.OrdinalIgnoreCase));
    }

    private FrameworkElement Linha(Funcionario f)
    {
        var eu = _servico?.Sessao.Id == f.Id;
        var grade = new Grid { Margin = new Thickness(0, 12, 0, 12), Opacity = f.Ativo ? 1 : 0.7 };
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (!_compacta)
        {
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        }
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = _compacta ? GridLength.Auto : new GridLength(290) });

        // Crachá: inicial, nome e usuário
        var cracha = new DockPanel();
        cracha.Children.Add(new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 12, 0),
            Background = (Brush)FindResource(f.Ativo ? "Etiqueta" : "Linha"), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = f.Nome[..1].ToUpperInvariant(), FontWeight = FontWeights.ExtraBold, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        var nomes = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nome = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold };
        nome.Inlines.Add(f.Nome);
        if (eu) nome.Inlines.Add(new System.Windows.Documents.Run(" (você)") { FontWeight = FontWeights.Normal, Foreground = (Brush)FindResource("Suave") });
        nomes.Children.Add(nome);
        var sub = f.Usuario + (f.Ativo ? "" : " · desativado") + (f.PediuSenha is not null && f.Ativo ? " · pediu senha nova" : "");
        if (_compacta) sub += $" · {f.NomeFuncao} · {UltimoAcesso(f)}";
        nomes.Children.Add(new TextBlock
        {
            Text = sub, FontSize = 12.5,
            Foreground = (Brush)FindResource(f.PediuSenha is not null && f.Ativo ? "Ambar700" : "Suave")
        });
        cracha.Children.Add(nomes);
        grade.Children.Add(cracha);

        if (!_compacta)
        {
            var funcao = new Border
            {
                Style = (Style)FindResource("Pilula"), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
                Background = (Brush)FindResource(f.Gerente ? "EtiquetaClara" : "Verde50"),
                Child = new TextBlock { Text = f.NomeFuncao, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource(f.Gerente ? "Ambar700" : "Verde700") }
            };
            Grid.SetColumn(funcao, 1);
            grade.Children.Add(funcao);

            var acesso = new TextBlock { Text = UltimoAcesso(f), FontSize = 13, Style = (Style)FindResource("TextoSuave"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(acesso, 2);
            grade.Children.Add(acesso);
        }

        var acoes = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        acoes.Children.Add(Acao("Editar", "Verde700", () => Abrir(new FuncionarioJanela(_area, this, _servico!, f, FuncionarioJanela.Modo.Editar, _todos))));
        acoes.Children.Add(Acao("Redefinir senha", f.PediuSenha is not null ? "Ambar700" : "Verde700",
            () => Abrir(new FuncionarioJanela(_area, this, _servico!, f, FuncionarioJanela.Modo.Senha, _todos))));
        if (!eu)
            acoes.Children.Add(f.Ativo
                ? Acao("Remover", "Vermelho600", () => ConfirmarRemocao(f))
                : Acao("Reativar", "Verde700", async () =>
                {
                    try
                    {
                        await _servico!.EditarAsync(f.Id, f.Nome, f.Perfil, true);
                        await RecarregarAsync($"{f.Nome} pode entrar de novo no sistema.");
                    }
                    catch (ConexaoException ex) { _area.MostrarToast(ex.Message); }
                }));
        Grid.SetColumn(acoes, grade.ColumnDefinitions.Count - 1);
        grade.Children.Add(acoes);

        return new Border { BorderBrush = (Brush)FindResource("Linha"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grade };
    }

    private static string UltimoAcesso(Funcionario f) => f.UltimoAcesso is { } a
        ? (a.ToLocalTime().Date == DateTime.Today ? "hoje às " + a.ToLocalTime().ToString("HH:mm") : a.ToLocalTime().ToString("dd/MM/yyyy HH:mm", PtBr))
        : "nunca entrou";

    private Button Acao(string texto, string cor, Action acao)
    {
        var botao = new Button
        {
            Content = texto, Style = (Style)FindResource("BotaoLink"), Foreground = (Brush)FindResource(cor),
            FontSize = 13.5, Margin = new Thickness(14, 2, 0, 2)
        };
        botao.Click += (_, _) => acao();
        return botao;
    }

    private void Abrir(FrameworkElement janela) => _area.AbrirJanela(janela);

    private void Novo_Click(object sender, RoutedEventArgs e)
    {
        if (_servico is null) return;
        Abrir(new FuncionarioJanela(_area, this, _servico, null, FuncionarioJanela.Modo.Novo, _todos));
    }

    private void ConfirmarRemocao(Funcionario f)
    {
        var texto = f.TemVendas
            ? $"{f.Nome} já abriu caixa, então fica desativado: não consegue mais entrar, mas as vendas continuam no nome dele. Dá para reativar depois."
            : $"{f.Nome} nunca vendeu, então o usuário {f.Usuario} é apagado de vez.";
        _area.AbrirJanela(new ConfirmacaoJanela(_area, $"Remover {f.Nome}?", texto, async () =>
        {
            var resultado = await _servico!.RemoverAsync(f.Id);
            await RecarregarAsync(resultado == "desativado" ? $"{f.Nome} foi desativado." : $"{f.Nome} foi removido.");
        }, f.TemVendas ? "Desativar" : "Remover"));
    }

    private void Busca_TextChanged(object sender, TextChangedEventArgs e)
    {
        DicaBusca.Visibility = Busca.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Mostrar();
    }

    private void AoRedimensionar(object sender, SizeChangedEventArgs e)
    {
        var compacta = e.NewSize.Width < 760;
        if (compacta == _compacta) return;
        _compacta = compacta;
        CabecalhoTabela.Visibility = compacta ? Visibility.Collapsed : Visibility.Visible;
        Mostrar();
    }

    private static string Plural(int n, string um, string varios) => $"{n} {(n == 1 ? um : varios)}";
}
