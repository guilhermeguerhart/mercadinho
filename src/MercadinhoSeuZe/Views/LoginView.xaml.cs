using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

public partial class LoginView : UserControl
{
    private readonly MainWindow _janela;
    private readonly AuthService? _auth;
    private readonly PreferenciasLocais _preferencias;
    private readonly DispatcherTimer _timerBloqueio = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _fimBloqueio;

    public LoginView(MainWindow janela, AuthService? auth, PreferenciasLocais preferencias)
    {
        InitializeComponent();
        _janela = janela;
        _auth = auth;
        _preferencias = preferencias;
        _timerBloqueio.Tick += (_, _) => AtualizarBloqueio();
        IniciarAtalhosDesenvolvimento();

        Loaded += (_, _) =>
        {
            if (_auth is null)
                MostrarAviso("Sistema sem configuração do banco de dados. Veja o README (appsettings.json).", TipoAviso.Erro);

            // Preenche o usuário lembrado
            if (!string.IsNullOrEmpty(_preferencias.UltimoUsuario))
            {
                CampoUsuario.Text = _preferencias.UltimoUsuario;
                Lembrar.IsChecked = true;
                CampoSenha.Focus();
            }
            else
            {
                CampoUsuario.Focus();
            }
        };
        Unloaded += (_, _) => _timerBloqueio.Stop();
    }

    private string Senha => CampoSenha.Visibility == Visibility.Visible ? CampoSenha.Password : CampoSenhaVisivel.Text;

    // ------------------------------------------------------------ Login

    private async void Entrar_Click(object sender, RoutedEventArgs e)
    {
        if (!BotaoEntrar.IsEnabled || EtapaLogin.Visibility != Visibility.Visible) return;
        Aviso.Visibility = Visibility.Collapsed;

        var usuario = CampoUsuario.Text.Trim();
        var usuarioOk = MarcarErro(CampoUsuario, ErroUsuario, usuario.Length > 0 ? "" : "Informe seu usuário.");
        var senhaOk = MarcarErro(SenhaAtiva(), ErroSenha, Senha.Length > 0 ? "" : "Informe sua senha.");
        if (!usuarioOk) { CampoUsuario.Focus(); return; }
        if (!senhaOk) { SenhaAtiva().Focus(); return; }

        if (_auth is null)
        {
            MostrarAviso("Sistema sem configuração do banco de dados. Veja o README (appsettings.json).", TipoAviso.Erro);
            return;
        }

        DefinirCarregando(true);
        var resultado = await _auth.EntrarAsync(usuario, Senha);
        DefinirCarregando(false);

        if (resultado.Ok)
        {
            _preferencias.UltimoUsuario = Lembrar.IsChecked == true ? usuario : null;
            _preferencias.Salvar();
            _janela.MostrarAreaDoUsuario(resultado.Sessao!);
            return;
        }

        LimparSenha();

        if (resultado.Bloqueado is { } tempo)
        {
            IniciarBloqueio(tempo);
            return;
        }

        var texto = resultado.Erro!;
        if (resultado.TentativasRestantes is { } restantes and <= 3)
            texto += $" Restam {restantes} {(restantes == 1 ? "tentativa" : "tentativas")} antes do bloqueio temporário.";
        MostrarAviso(texto, TipoAviso.Erro);
        SenhaAtiva().Focus();
    }

    private void DefinirCarregando(bool carregando)
    {
        BotaoEntrar.IsEnabled = !carregando;
        BotaoEntrarTexto.Text = carregando ? "Entrando" : "Entrar";
        Carregando.Visibility = carregando ? Visibility.Visible : Visibility.Collapsed;
        var giro = carregando
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.7)) { RepeatBehavior = RepeatBehavior.Forever }
            : null;
        GiroCarregando.BeginAnimation(RotateTransform.AngleProperty, giro);
    }

    // ------------------------------------------------------------ Bloqueio por tentativas

    private void IniciarBloqueio(TimeSpan tempo)
    {
        _fimBloqueio = DateTime.UtcNow + tempo;
        BotaoEntrar.IsEnabled = false;
        AtualizarBloqueio();
        _timerBloqueio.Start();
    }

    private void AtualizarBloqueio()
    {
        var restante = _fimBloqueio - DateTime.UtcNow;
        if (restante <= TimeSpan.Zero)
        {
            EncerrarBloqueio();
            return;
        }
        MostrarAviso("Muitas tentativas sem sucesso. Por segurança, o acesso foi bloqueado. " +
                     $"Tente novamente em {(int)restante.TotalMinutes}:{restante.Seconds:00} ou use “Esqueci minha senha”.",
                     TipoAviso.Atencao);
    }

    private void EncerrarBloqueio()
    {
        _timerBloqueio.Stop();
        BotaoEntrar.IsEnabled = true;
        Aviso.Visibility = Visibility.Collapsed;
    }

    // ------------------------------------------------------------ Campos

    private void CampoUsuario_TextChanged(object sender, TextChangedEventArgs e)
    {
        DicaUsuario.Visibility = CampoUsuario.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        MarcarErro(CampoUsuario, ErroUsuario, "");
        // Ao trocar de usuário, encerra o aviso de bloqueio do anterior
        if (_timerBloqueio.IsEnabled) EncerrarBloqueio();
        else Aviso.Visibility = Visibility.Collapsed;
    }

    private void CampoSenha_PasswordChanged(object sender, RoutedEventArgs e) => AoMudarSenha(CampoSenha.Password);

    private void CampoSenhaVisivel_TextChanged(object sender, TextChangedEventArgs e) => AoMudarSenha(CampoSenhaVisivel.Text);

    private void AoMudarSenha(string senha)
    {
        DicaSenha.Visibility = senha.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        MarcarErro(SenhaAtiva(), ErroSenha, "");
        if (!_timerBloqueio.IsEnabled) Aviso.Visibility = Visibility.Collapsed;
    }

    private void CampoSenha_Teclado(object sender, RoutedEventArgs e) =>
        AvisoCapsLock.Visibility = Keyboard.IsKeyToggled(Key.CapsLock) ? Visibility.Visible : Visibility.Collapsed;

    private void CampoSenha_PerdeuFoco(object sender, RoutedEventArgs e) => AvisoCapsLock.Visibility = Visibility.Collapsed;

    private void MostrarSenha_Click(object sender, RoutedEventArgs e)
    {
        var mostrar = CampoSenha.Visibility == Visibility.Visible;
        if (mostrar)
        {
            CampoSenhaVisivel.Text = CampoSenha.Password;
            CampoSenha.Visibility = Visibility.Collapsed;
            CampoSenhaVisivel.Visibility = Visibility.Visible;
            CampoSenhaVisivel.CaretIndex = CampoSenhaVisivel.Text.Length;
        }
        else
        {
            CampoSenha.Password = CampoSenhaVisivel.Text;
            CampoSenhaVisivel.Visibility = Visibility.Collapsed;
            CampoSenha.Visibility = Visibility.Visible;
        }
        BotaoMostrar.Content = mostrar ? "Ocultar" : "Mostrar";
        BotaoMostrar.ToolTip = mostrar ? "Ocultar senha" : "Mostrar senha";
        SenhaAtiva().Focus();
    }

    private Control SenhaAtiva() => CampoSenha.Visibility == Visibility.Visible ? CampoSenha : CampoSenhaVisivel;

    private void LimparSenha()
    {
        CampoSenha.Clear();
        CampoSenhaVisivel.Clear();
    }

    private static bool MarcarErro(Control campo, TextBlock erro, string mensagem)
    {
        erro.Text = mensagem;
        campo.Tag = mensagem.Length > 0 ? "invalido" : null;
        return mensagem.Length == 0;
    }

    private enum TipoAviso { Erro, Atencao }

    private void MostrarAviso(string texto, TipoAviso tipo)
    {
        var (fundo, borda, cor) = tipo == TipoAviso.Erro
            ? ("Vermelho50", "#F5C6C0", "Vermelho600")
            : ("Ambar50", "#F0DCA8", "Ambar700");
        Aviso.Background = (Brush)FindResource(fundo);
        Aviso.BorderBrush = (Brush)new BrushConverter().ConvertFromString(borda)!;
        AvisoTexto.Foreground = AvisoIcone.Foreground = (Brush)FindResource(cor);
        AvisoTexto.Text = texto;
        Aviso.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------ Recuperar senha

    private void EsqueciSenha_Click(object sender, RoutedEventArgs e)
    {
        EtapaLogin.Visibility = Visibility.Collapsed;
        EtapaRecuperar.Visibility = Visibility.Visible;
        AvisoRecuperar.Visibility = Visibility.Collapsed;
        CampoUsuarioRecuperar.Text = CampoUsuario.Text.Trim();
        CampoUsuarioRecuperar.Focus();
    }

    private void VoltarLogin_Click(object sender, RoutedEventArgs e)
    {
        EtapaRecuperar.Visibility = Visibility.Collapsed;
        EtapaLogin.Visibility = Visibility.Visible;
        CampoUsuario.Focus();
    }

    private async void Recuperar_Click(object sender, RoutedEventArgs e)
    {
        var usuario = CampoUsuarioRecuperar.Text.Trim();
        if (!MarcarErro(CampoUsuarioRecuperar, ErroUsuarioRecuperar, usuario.Length > 0 ? "" : "Informe seu usuário."))
        {
            CampoUsuarioRecuperar.Focus();
            return;
        }

        BotaoRecuperar.IsEnabled = false;
        if (_auth is not null) await _auth.SolicitarRecuperacaoAsync(usuario);
        BotaoRecuperar.IsEnabled = true;

        AvisoRecuperarTexto.Text = "Se o usuário existir, o gerente vai receber o pedido para redefinir a sua senha.";
        AvisoRecuperar.Visibility = Visibility.Visible;
    }

    /// <summary>Atalhos que só existem na versão de desenvolvimento (ver LoginView.Desenvolvimento.cs).</summary>
    partial void IniciarAtalhosDesenvolvimento();

    // ------------------------------------------------------------ Navegação e layout

    private void Voltar_Click(object sender, RoutedEventArgs e) => _janela.MostrarInicio();

    /// <summary>Mesmo comportamento responsivo do site: abaixo de 960px some o painel da marca.</summary>
    private void AoRedimensionar(object sender, SizeChangedEventArgs e)
    {
        var estreita = e.NewSize.Width < 960;
        PainelMarca.Visibility = estreita ? Visibility.Collapsed : Visibility.Visible;
        ColunaMarca.Width = estreita ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        MarcaCompacta.Visibility = estreita ? Visibility.Visible : Visibility.Collapsed;
        ColunaFormulario.Width = Math.Min(420, Math.Max(280, (estreita ? e.NewSize.Width : e.NewSize.Width / 2) - 48));

        // Telas baixas: tira o que é secundário
        MiniResumo.Visibility = e.NewSize.Height < 720 ? Visibility.Collapsed : Visibility.Visible;
        Perfis.Visibility = e.NewSize.Height < 720 ? Visibility.Collapsed : Visibility.Visible;
    }
}
