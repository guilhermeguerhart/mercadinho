using System.Windows;
using System.Windows.Controls;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>Pergunta antes de excluir (ou cancelar, desativar). A ação só roda depois de clicar no botão vermelho.</summary>
public partial class ConfirmacaoJanela : UserControl
{
    private readonly AreaInternaView _area;
    private readonly Func<Task> _acao;

    public ConfirmacaoJanela(AreaInternaView area, string titulo, string texto, Func<Task> acao,
        string textoConfirmar = "Excluir", string textoCancelar = "Cancelar")
    {
        InitializeComponent();
        BotaoConfirmar.Content = textoConfirmar;
        BotaoCancelar.Content = textoCancelar;
        _area = area;
        _acao = acao;
        Titulo.Text = titulo;
        Texto.Text = texto;
        // Como no site, o foco começa em "Cancelar" para um Enter sem querer não excluir
        Loaded += (_, _) => BotaoCancelar.Focus();
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => _area.FecharJanela();

    private async void Confirmar_Click(object sender, RoutedEventArgs e)
    {
        BotaoConfirmar.IsEnabled = false;
        AvisoErro.Visibility = Visibility.Collapsed;
        try
        {
            await _acao();
        }
        catch (ConexaoException ex)
        {
            AvisoErroTexto.Text = ex.Message;
            AvisoErro.Visibility = Visibility.Visible;
            BotaoConfirmar.IsEnabled = true;
        }
    }
}
