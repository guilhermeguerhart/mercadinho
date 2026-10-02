using System.Windows;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Views;

namespace MercadinhoSeuZe;

public partial class MainWindow : Window
{
    private readonly AuthService? _auth;

    public MainWindow()
    {
        InitializeComponent();

        var config = Configuracao.Carregar();
        if (config is not null)
            _auth = new AuthService(config);

        MostrarInicio();
    }

    public void MostrarInicio() => Tela.Content = new LandingView(this);

    public void MostrarLogin() => Tela.Content = new LoginView(this, _auth);

    public void MostrarAreaDoUsuario(Perfil perfil) => Tela.Content = new AreaUsuarioView(this, perfil);
}
