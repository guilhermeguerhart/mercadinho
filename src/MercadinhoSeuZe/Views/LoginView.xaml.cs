using System.Windows;
using System.Windows.Controls;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

public partial class LoginView : UserControl
{
    private readonly MainWindow _janela;
    private readonly AuthService? _auth;

    public LoginView(MainWindow janela, AuthService? auth)
    {
        InitializeComponent();
        _janela = janela;
        _auth = auth;

        if (_auth is null)
            MostrarErro("Sistema sem configuração do banco de dados. Veja o README (appsettings.json).");

        Loaded += (_, _) => CampoEmail.Focus();
    }

    private async void Entrar_Click(object sender, RoutedEventArgs e)
    {
        var email = CampoEmail.Text.Trim();
        var senha = CampoSenha.Password;

        if (email.Length == 0 || senha.Length == 0)
        {
            MostrarErro("Preencha o e-mail e a senha.");
            return;
        }
        if (_auth is null)
        {
            MostrarErro("Sistema sem configuração do banco de dados. Veja o README (appsettings.json).");
            return;
        }

        BotaoEntrar.IsEnabled = false;
        BotaoEntrar.Content = "Entrando...";
        Mensagem.Visibility = Visibility.Collapsed;

        try
        {
            var perfil = await _auth.EntrarAsync(email, senha);
            _janela.MostrarAreaDoUsuario(perfil);
        }
        catch (LoginException ex)
        {
            MostrarErro(ex.Message);
            CampoSenha.Clear();
            CampoSenha.Focus();
        }
        finally
        {
            BotaoEntrar.IsEnabled = true;
            BotaoEntrar.Content = "Entrar";
        }
    }

    private void Voltar_Click(object sender, RoutedEventArgs e) => _janela.MostrarInicio();

    private void MostrarErro(string texto)
    {
        Mensagem.Text = texto;
        Mensagem.Visibility = Visibility.Visible;
    }
}
