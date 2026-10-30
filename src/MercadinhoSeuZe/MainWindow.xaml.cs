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
    private readonly System.Windows.Threading.DispatcherTimer _renovacao = new() { Interval = TimeSpan.FromMinutes(1) };
    private Sessao? _sessao;

    public MainWindow()
    {
        InitializeComponent();

        var config = Configuracao.Carregar();
        if (config is not null)
        {
            _api = new SupabaseApi(config);
            _auth = new AuthService(_api, _preferencias);
        }

        _renovacao.Tick += async (_, _) => await RenovarSessaoAsync();
        MostrarInicio();
    }

    public void MostrarInicio()
    {
        PararSessao();
        Tela.Content = new LandingView(this, _api);
    }

    /// <summary>A cada minuto: faltando 10 minutos para o token vencer, pede um novo (o turno do caixa passa de 1 hora).</summary>
    private async Task RenovarSessaoAsync()
    {
        if (_sessao is not { } sessao || _auth is null || sessao.ExpiraEm - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(10)) return;
        _renovacao.Stop();
        try { await _auth.RenovarAsync(sessao); }
        finally { if (ReferenceEquals(_sessao, sessao)) _renovacao.Start(); }
    }

    private void PararSessao()
    {
        _sessao = null;
        _renovacao.Stop();
    }

    public void MostrarLogin()
    {
        PararSessao();
        Tela.Content = new LoginView(this, _auth, _preferencias);
    }

    /// <summary>Área interna: gerente cai no Painel e operador no Caixa.</summary>
    public void MostrarAreaDoUsuario(Sessao sessao)
    {
        _sessao = sessao;
        _renovacao.Start();
        Tela.Content = new AreaInternaView(this, sessao, _api);
    }
}
