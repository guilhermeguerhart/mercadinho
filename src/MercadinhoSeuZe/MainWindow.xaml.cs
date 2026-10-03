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

    /// <summary>Área interna: gerente cai no Painel e operador no Caixa (semana 2, modo de teste).</summary>
    public void MostrarAreaDoUsuario(Sessao sessao) => Tela.Content = new AreaInternaView(this, sessao);
}
