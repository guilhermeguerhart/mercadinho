using System.Windows;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Views;

namespace MercadinhoSeuZe;

public partial class MainWindow : Window
{
    private readonly SupabaseApi? _api;
    private readonly AuthService? _auth;
    private readonly PreferenciasLocais _preferencias = PreferenciasLocais.Carregar();

    public MainWindow()
    {
        InitializeComponent();

        var config = Configuracao.Carregar();
        if (config is not null)
        {
            _api = new SupabaseApi(config);
            _auth = new AuthService(_api, _preferencias);
        }

        MostrarInicio();
    }

    public void MostrarInicio() => Tela.Content = new LandingView(this, _api);

    public void MostrarLogin() => Tela.Content = new LoginView(this, _auth, _preferencias);

    /// <summary>
    /// Tela de cada perfil após o login. Por enquanto só a tela inicial e o login estão prontos,
    /// então os dois perfis vão para "em desenvolvimento" (como no site de referência).
    /// A partir da semana 2: gerente vai para o Painel e operador para o Caixa.
    /// </summary>
    public void MostrarAreaDoUsuario(Sessao sessao) => Tela.Content = new EmDesenvolvimentoView(this, sessao);
}
