using System.Windows;
using System.Windows.Controls;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Mostra a mensagem do pedido (dá para mudar) e abre o WhatsApp ou o e-mail do fornecedor já com ela escrita.
/// Abre sozinha depois de criar o pedido e pelo "Reenviar" do histórico.
/// </summary>
public partial class EnvioPedidoJanela : UserControl
{
    private readonly AreaInternaView _area;
    private readonly Fornecedor _fornecedor;

    public EnvioPedidoJanela(AreaInternaView area, Fornecedor fornecedor, string mensagem, string titulo)
    {
        InitializeComponent();
        _area = area;
        _fornecedor = fornecedor;

        Titulo.Text = titulo;
        Subtitulo.Text = $"Para {fornecedor.Nome} · WhatsApp {fornecedor.Whatsapp ?? fornecedor.Telefone}" +
                         (string.IsNullOrWhiteSpace(fornecedor.Email) ? "" : $" · {fornecedor.Email}");
        Mensagem.Text = mensagem;
        if (string.IsNullOrWhiteSpace(fornecedor.Email))
        {
            BotoesEnvio.Children.Remove(BotaoEmail);
            BotoesEnvio.Columns = 1;
        }
        else if (fornecedor.PrefereEmail)
        {
            // Fornecedor que recebe pedidos por e-mail: o e-mail vira o botão principal (primeiro, verde)
            BotoesEnvio.Children.Clear();
            BotaoEmail.Style = (Style)FindResource("BotaoPrimario");
            BotaoWhatsapp.Style = (Style)FindResource("BotaoFantasma");
            BotaoEmail.Margin = new Thickness(0);
            BotaoWhatsapp.Margin = new Thickness(10, 0, 0, 0);
            ((ContentControl)((StackPanel)BotaoEmail.Content).Children[0]).Foreground = System.Windows.Media.Brushes.White;
            ((ContentControl)((StackPanel)BotaoWhatsapp.Content).Children[0]).Foreground = (System.Windows.Media.Brush)FindResource("Verde700");
            BotoesEnvio.Children.Add(BotaoEmail);
            BotoesEnvio.Children.Add(BotaoWhatsapp);
        }
        Loaded += (_, _) => (fornecedor.PrefereEmail ? BotaoEmail : BotaoWhatsapp).Focus();
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => _area.FecharJanela();

    private void Whatsapp_Click(object sender, RoutedEventArgs e)
    {
        if (ContatoRapido.AbrirWhatsapp(_fornecedor, Mensagem.Text.Trim()))
        {
            _area.FecharJanela();
            _area.MostrarToast($"WhatsApp aberto com o pedido para {_fornecedor.Nome}. Confira e aperte enviar.");
        }
        else _area.MostrarToast("Não foi possível abrir o WhatsApp. Use “Copiar” e cole a mensagem.");
    }

    private void Email_Click(object sender, RoutedEventArgs e)
    {
        if (ContatoRapido.AbrirEmail(_fornecedor, $"Pedido do Mercado para {_fornecedor.Nome}", Mensagem.Text.Trim()))
        {
            _area.FecharJanela();
            _area.MostrarToast($"E-mail aberto com o pedido para {_fornecedor.Nome}. Confira e envie.");
        }
        else _area.MostrarToast("Não foi possível abrir o e-mail. Use “Copiar” e cole a mensagem.");
    }

    private void Copiar_Click(object sender, RoutedEventArgs e) =>
        _area.MostrarToast(ContatoRapido.Copiar(Mensagem.Text.Trim()) ? "Mensagem copiada." : "Não foi possível copiar. Tente de novo.");
}
