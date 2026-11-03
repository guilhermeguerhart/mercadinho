using System.Windows;
using System.Windows.Controls;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Mostra a mensagem do pedido (dá para mudar) e abre o WhatsApp ou o e-mail do fornecedor já com ela escrita.
/// Abre sozinha depois de criar o pedido e pelo "Reenviar" do histórico. Semana 6: a mensagem começa com "Pedido nº 42"
/// e, com a Edge Function enviar-email publicada, o e-mail sai direto pela caixa de pedidos do mercado (a resposta do
/// fornecedor volta para a janela de mensagens); sem ela, abre o programa de e-mail como antes.
/// </summary>
public partial class EnvioPedidoJanela : UserControl
{
    private readonly AreaInternaView _area;
    private readonly Fornecedor _fornecedor;
    private readonly long? _pedidoId;
    private readonly MensagensService? _mensagens;

    public EnvioPedidoJanela(AreaInternaView area, Fornecedor fornecedor, string mensagem, string titulo, long? pedidoId = null, MensagensService? mensagens = null)
    {
        InitializeComponent();
        _area = area;
        _fornecedor = fornecedor;
        _pedidoId = pedidoId;
        _mensagens = mensagens is { TemMensagens: true } ? mensagens : null;
        if (pedidoId is { } numero) mensagem = MensagemPedido.ComNumero(numero, mensagem);

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

    private async void Email_Click(object sender, RoutedEventArgs e)
    {
        var assunto = _pedidoId is { } numero ? MensagemPedido.Assunto(numero, _fornecedor.Nome) : $"Pedido do Mercado para {_fornecedor.Nome}";
        if (_mensagens is not null)
        {
            // Envio pela caixa de pedidos do mercado (Edge Function); a conversa fica na janela de mensagens
            BotaoEmail.IsEnabled = false;
            try
            {
                var para = await _mensagens.EnviarEmailAsync("pedido", _fornecedor.Id, _pedidoId, assunto, Mensagem.Text.Trim());
                _area.FecharJanela();
                _area.MostrarToast($"Pedido enviado por e-mail para {para}. A resposta aparece nas mensagens do fornecedor.");
                return;
            }
            catch (FuncaoInexistenteException)
            {
                // Função ainda não publicada: segue abrindo o programa de e-mail do computador
            }
            catch (ConexaoException ex)
            {
                _area.MostrarToast(ex.Message);
                return;
            }
            finally
            {
                BotaoEmail.IsEnabled = true;
            }
        }
        if (ContatoRapido.AbrirEmail(_fornecedor, assunto, Mensagem.Text.Trim()))
        {
            _area.FecharJanela();
            _area.MostrarToast($"E-mail aberto com o pedido para {_fornecedor.Nome}. Confira e envie.");
        }
        else _area.MostrarToast("Não foi possível abrir o e-mail. Use “Copiar” e cole a mensagem.");
    }

    private void Copiar_Click(object sender, RoutedEventArgs e) =>
        _area.MostrarToast(ContatoRapido.Copiar(Mensagem.Text.Trim()) ? "Mensagem copiada." : "Não foi possível copiar. Tente de novo.");
}
