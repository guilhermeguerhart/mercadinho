using System.Net.Mail;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

public partial class LandingView : UserControl
{
    private readonly MainWindow _janela;
    private readonly SupabaseApi? _api;

    public LandingView(MainWindow janela, SupabaseApi? api)
    {
        InitializeComponent();
        _janela = janela;
        _api = api;

        LinhasPrevia.ItemsSource = new Dictionary<string, string>
        {
            ["Arroz 5kg"] = "42 un.",
            ["Feijão 1kg"] = "8 un.",
            ["Óleo de soja"] = "30 un.",
        };

        // O erro de cada campo some quando a pessoa volta a digitar nele
        ContatoNome.TextChanged += (_, _) => Validar(ContatoNome, ContatoNomeErro, true, "");
        ContatoEmail.TextChanged += (_, _) => Validar(ContatoEmail, ContatoEmailErro, true, "");
        ContatoMensagem.TextChanged += (_, _) => Validar(ContatoMensagem, ContatoMensagemErro, true, "");

        // Esc fecha a janela de contato
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && JanelaContato.Visibility == Visibility.Visible)
                JanelaContato.Visibility = Visibility.Collapsed;
        };
    }

    private void AcessarSistema_Click(object sender, RoutedEventArgs e) => _janela.MostrarLogin();

    /// <summary>Mesmo comportamento responsivo do site: abaixo de 900px a prévia some e os cartões empilham.</summary>
    private void AoRedimensionar(object sender, SizeChangedEventArgs e)
    {
        var estreita = e.NewSize.Width < 900;
        Previa.Visibility = estreita ? Visibility.Collapsed : Visibility.Visible;
        ColunaPrevia.Width = estreita ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        HeroTexto.Margin = estreita ? new Thickness(0) : new Thickness(0, 0, 56, 0);
        Modulos.Columns = estreita ? 1 : 3;
        Titulo.FontSize = e.NewSize.Width < 680 ? 28 : 44;
        Titulo.LineHeight = e.NewSize.Width < 680 ? 32 : 50;
    }

    // ------------------------------------------------------------ Contato

    private void AbrirContato_Click(object sender, RoutedEventArgs e)
    {
        AvisoContato.Visibility = Visibility.Collapsed;
        JanelaContato.Visibility = Visibility.Visible;
        ContatoNome.Focus();
    }

    private void FecharContato_Click(object sender, RoutedEventArgs e) => JanelaContato.Visibility = Visibility.Collapsed;

    private void FundoContato_Click(object sender, MouseButtonEventArgs e) => JanelaContato.Visibility = Visibility.Collapsed;

    private async void EnviarContato_Click(object sender, RoutedEventArgs e)
    {
        AvisoContato.Visibility = Visibility.Collapsed;

        var nome = ContatoNome.Text.Trim();
        var email = ContatoEmail.Text.Trim();
        var mensagem = ContatoMensagem.Text.Trim();

        var nomeOk = Validar(ContatoNome, ContatoNomeErro, nome.Length > 0, "Informe seu nome.");
        var emailOk = Validar(ContatoEmail, ContatoEmailErro, EmailValido(email), "Informe um e-mail válido.");
        var mensagemOk = Validar(ContatoMensagem, ContatoMensagemErro, mensagem.Length > 0, "Escreva sua mensagem.");
        if (!nomeOk) { ContatoNome.Focus(); return; }
        if (!emailOk) { ContatoEmail.Focus(); return; }
        if (!mensagemOk) { ContatoMensagem.Focus(); return; }

        if (_api is null)
        {
            MostrarAvisoContato("Sistema sem configuração do banco de dados. Veja o README.", sucesso: false);
            return;
        }

        BotaoEnviarContato.IsEnabled = false;
        try
        {
            await _api.InserirAsync("contatos", new { nome, email, mensagem });
            ContatoNome.Clear();
            ContatoEmail.Clear();
            ContatoMensagem.Clear();
            MostrarAvisoContato("Mensagem enviada! Nossa equipe vai retornar em breve.", sucesso: true);
        }
        catch (ConexaoException ex)
        {
            MostrarAvisoContato(ex.Message, sucesso: false);
        }
        finally
        {
            BotaoEnviarContato.IsEnabled = true;
        }
    }

    private void MostrarAvisoContato(string texto, bool sucesso)
    {
        AvisoContatoTexto.Text = texto;
        AvisoContatoTexto.Foreground = (System.Windows.Media.Brush)FindResource(sucesso ? "Verde700" : "Vermelho600");
        AvisoContato.Background = (System.Windows.Media.Brush)FindResource(sucesso ? "Verde50" : "Vermelho50");
        AvisoContato.Visibility = Visibility.Visible;
    }

    private static bool Validar(Control campo, TextBlock erro, bool valido, string mensagem)
    {
        erro.Text = valido ? "" : mensagem;
        campo.Tag = valido ? null : "invalido";
        return valido;
    }

    private static bool EmailValido(string email) =>
        MailAddress.TryCreate(email, out var endereco) && endereco.Address == email && email.Contains('.');
}
