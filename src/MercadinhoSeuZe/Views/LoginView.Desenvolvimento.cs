#if DESENVOLVIMENTO
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// SÓ PARA DESENVOLVIMENTO: Alt+1 na tela de login abre, no canto inferior esquerdo, a lista dos
/// usuários de teste (USUARIOS_TESTE.md). Clicar num usuário preenche usuário e senha.
/// Só é compilado em Debug ou com "publicar.ps1 -Desenvolvimento"; o .exe normal (publicar.ps1) não tem o atalho.
/// </summary>
public partial class LoginView
{
    private static readonly (string Usuario, string Senha, string Descricao)[] UsuariosTeste =
    [
        ("marcos.gerente", "Gerente#4821", "Gerente"),
        ("julia.gerente", "Gerente#7350", "Gerente"),
        ("ana.caixa", "Caixa#1964", "Operador"),
        ("pedro.caixa", "Caixa#5307", "Operador"),
        ("carla.caixa", "Caixa#8142", "Operador desativado"),
    ];

    private Border? _painelTeste;

    partial void IniciarAtalhosDesenvolvimento()
    {
        PreviewKeyDown += (_, e) =>
        {
            var tecla = e.Key == Key.System ? e.SystemKey : e.Key;
            if (Keyboard.Modifiers == ModifierKeys.Alt && tecla is Key.D1 or Key.NumPad1)
            {
                AlternarPainelTeste();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _painelTeste?.Visibility == Visibility.Visible)
            {
                _painelTeste.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        };
    }

    private void AlternarPainelTeste()
    {
        _painelTeste ??= CriarPainelTeste();
        _painelTeste.Visibility = _painelTeste.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private Border CriarPainelTeste()
    {
        var lista = new StackPanel();
        lista.Children.Add(new TextBlock
        {
            Text = "Usuários de teste (Alt+1)",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Suave"),
            Margin = new Thickness(4, 0, 4, 6)
        });

        foreach (var (usuario, senha, descricao) in UsuariosTeste)
        {
            var botao = new Button
            {
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 6, 8, 6),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Content = new DockPanel
                {
                    Children =
                    {
                        new TextBlock { Text = descricao, FontSize = 12, Foreground = (Brush)FindResource("Suave"),
                                        Margin = new Thickness(16, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right },
                        new TextBlock { Text = usuario, FontSize = 14, FontWeight = FontWeights.SemiBold }
                    }
                }
            };
            DockPanel.SetDock(((DockPanel)botao.Content).Children[0], Dock.Right);
            System.Windows.Automation.AutomationProperties.SetName(botao, usuario);
            botao.Click += (_, _) => PreencherUsuarioTeste(usuario, senha);
            lista.Children.Add(botao);
        }

        var painel = new Border
        {
            Child = lista,
            Width = 290,
            Padding = new Thickness(10),
            Margin = new Thickness(16),
            CornerRadius = new CornerRadius(12),
            Background = Brushes.White,
            BorderBrush = (Brush)FindResource("Linha"),
            BorderThickness = new Thickness(1),
            Effect = (System.Windows.Media.Effects.Effect)FindResource("SombraJanela"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumnSpan(painel, 2);
        Panel.SetZIndex(painel, 10);
        Raiz.Children.Add(painel);
        return painel;
    }

    private void PreencherUsuarioTeste(string usuario, string senha)
    {
        if (EtapaRecuperar.Visibility == Visibility.Visible) VoltarLogin_Click(this, new RoutedEventArgs());

        CampoUsuario.Text = usuario;
        if (CampoSenha.Visibility == Visibility.Visible) CampoSenha.Password = senha;
        else CampoSenhaVisivel.Text = senha;

        _painelTeste!.Visibility = Visibility.Collapsed;
        BotaoEntrar.Focus();
    }
}
#endif
