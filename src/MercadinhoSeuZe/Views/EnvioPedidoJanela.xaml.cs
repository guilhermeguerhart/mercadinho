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
/// Pedido do Guilherme (25/11): ao criar ou alterar um pedido (enviarSozinho), o e-mail sai na hora, sem clique, e a
/// janela mostra a situação do envio com o WhatsApp ao lado. O "Reenviar" não manda sozinho (para não chegar repetido),
/// nem e-mails de teste (.example).
/// </summary>
public partial class EnvioPedidoJanela : UserControl
{
    private readonly AreaInternaView _area;
    private readonly Fornecedor _fornecedor;
    private readonly long? _pedidoId;
    private readonly MensagensService? _mensagens;
    private readonly bool _enviaSozinho;
    private bool _enviando;

    public EnvioPedidoJanela(AreaInternaView area, Fornecedor fornecedor, string mensagem, string titulo, long? pedidoId = null,
        MensagensService? mensagens = null, bool enviarSozinho = false)
    {
        InitializeComponent();
        _area = area;
        _fornecedor = fornecedor;
        _pedidoId = pedidoId;
        _mensagens = mensagens is { TemMensagens: true } ? mensagens : null;
        _enviaSozinho = enviarSozinho && _mensagens is not null && PodeEnviarSozinho(fornecedor.Email);
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
        else if (_enviaSozinho)
        {
            // O e-mail já vai sozinho: o WhatsApp fica como botão principal e o e-mail serve para mandar de novo
            BotaoEmailTexto.Text = "Enviar de novo por e-mail";
            Dica.Text = "O e-mail sai sozinho pela caixa do mercado. Para mandar também pelo WhatsApp, use o botão abaixo " +
                        "(dá para mudar o texto antes).";
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
        Loaded += async (_, _) =>
        {
            (fornecedor.PrefereEmail && !_enviaSozinho ? BotaoEmail : BotaoWhatsapp).Focus();
            if (_enviaSozinho) await EnviarSozinhoAsync();
        };
    }

    /// <summary>E-mail que recebe o pedido sozinho: precisa parecer um e-mail de verdade (os de teste terminam em .example).</summary>
    public static bool PodeEnviarSozinho(string? email)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        return System.Text.RegularExpressions.Regex.IsMatch(e, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")
               && !e.EndsWith(".example") && !e.EndsWith("@example.com") && !e.EndsWith(".local");
    }

    private void MostrarSituacao(string texto, string fundo, string borda)
    {
        SituacaoEmailTexto.Text = texto;
        SituacaoEmail.Background = (System.Windows.Media.Brush)FindResource(fundo);
        SituacaoEmail.BorderBrush = (System.Windows.Media.Brush)FindResource(borda);
        SituacaoEmail.Visibility = Visibility.Visible;
    }

    private async Task EnviarSozinhoAsync()
    {
        _enviando = true;
        BotaoEmail.IsEnabled = false;
        MostrarSituacao($"Enviando o pedido por e-mail para {_fornecedor.Email}…", "Azul50", "Azul600");
        try
        {
            var para = await _mensagens!.EnviarEmailAsync("pedido", _fornecedor.Id, _pedidoId, Assunto(), Mensagem.Text.Trim());
            MostrarSituacao($"E-mail enviado para {(string.IsNullOrWhiteSpace(para) ? _fornecedor.Email : para)} às {DateTime.Now:HH:mm}. " +
                            "A resposta aparece nas mensagens do fornecedor.", "Verde50", "Verde700");
        }
        catch (FuncaoInexistenteException)
        {
            BotaoEmailTexto.Text = "Enviar por e-mail";
            MostrarSituacao("O envio automático por e-mail ainda não está ligado no Supabase. Use os botões abaixo.", "Ambar50", "Ambar700");
        }
        catch (ConexaoException ex)
        {
            BotaoEmailTexto.Text = "Tentar de novo por e-mail";
            MostrarSituacao($"O e-mail não saiu: {ex.Message}", "Vermelho50", "Vermelho600");
            if (!IsLoaded) _area.MostrarToast($"O e-mail do pedido para {_fornecedor.Nome} não saiu: {ex.Message} Use “Reenviar” no histórico.");
        }
        finally
        {
            _enviando = false;
            BotaoEmail.IsEnabled = true;
        }
    }

    private string Assunto() =>
        _pedidoId is { } numero ? MensagemPedido.Assunto(numero, _fornecedor.Nome) : $"Pedido do Mercado para {_fornecedor.Nome}";

    private void Fechar_Click(object sender, RoutedEventArgs e) => _area.FecharJanela();

    private void Whatsapp_Click(object sender, RoutedEventArgs e)
    {
        if (ContatoRapido.AbrirWhatsapp(_fornecedor, Mensagem.Text.Trim()))
        {
            _area.FecharJanela();
            _area.MostrarToast($"WhatsApp aberto com o pedido para {_fornecedor.Nome}. Confira e aperte enviar." +
                               (_enviando ? " O e-mail continua sendo enviado." : ""));
        }
        else _area.MostrarToast("Não foi possível abrir o WhatsApp. Use “Copiar” e cole a mensagem.");
    }

    private async void Email_Click(object sender, RoutedEventArgs e)
    {
        var assunto = Assunto();
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
