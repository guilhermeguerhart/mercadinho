using System.Windows;
using System.Windows.Controls;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// O gerente digita usuário e senha no caixa do operador. O banco confere (autorizar_gerente) e devolve uma
/// autorização que vale uma vez, para aquela venda. Resultado: o id da autorização, ou null se voltou.
/// </summary>
public partial class AutorizacaoJanela : UserControl
{
    private readonly AreaInternaView _area;
    private readonly CaixaService _servico;
    private readonly string _acao;
    private readonly long? _vendaId;
    private readonly TaskCompletionSource<long?> _resultado = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<long?> Resultado => _resultado.Task;

    public AutorizacaoJanela(AreaInternaView area, CaixaService servico, string acao, long? vendaId, string texto)
    {
        InitializeComponent();
        _area = area;
        _servico = servico;
        _acao = acao;
        _vendaId = vendaId;
        Texto.Text = texto;
        Unloaded += (_, _) => _resultado.TrySetResult(null);
        Loaded += (_, _) => Usuario.Focus();
    }

    private void Voltar_Click(object sender, RoutedEventArgs e) => _area.FecharJanela();

    private async void Autorizar_Click(object sender, RoutedEventArgs e)
    {
        AvisoErro.Visibility = Visibility.Collapsed;
        if (Usuario.Text.Trim().Length == 0 || Senha.Password.Length == 0)
        {
            AvisoErroTexto.Text = "Digite o usuário e a senha do gerente.";
            AvisoErro.Visibility = Visibility.Visible;
            (Usuario.Text.Trim().Length == 0 ? (Control)Usuario : Senha).Focus();
            return;
        }
        BotaoAutorizar.IsEnabled = false;
        try
        {
            var id = await _servico.AutorizarAsync(Usuario.Text.Trim(), Senha.Password, _acao, _vendaId);
            if (id is null)
            {
                AvisoErroTexto.Text = "Usuário ou senha do gerente inválidos.";
                AvisoErro.Visibility = Visibility.Visible;
                Senha.Clear();
                Senha.Focus();
                return;
            }
            _resultado.TrySetResult(id);
            _area.FecharJanela();
        }
        catch (ConexaoException ex)
        {
            AvisoErroTexto.Text = ex.Message;
            AvisoErro.Visibility = Visibility.Visible;
        }
        finally
        {
            BotaoAutorizar.IsEnabled = true;
        }
    }
}
