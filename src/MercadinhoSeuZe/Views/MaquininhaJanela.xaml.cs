using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Janela da cobrança na maquininha: mostra o visor (valor e o que fazer), o Pix com QR e "copia e cola", e o
/// resultado. Resultado: a resposta aprovada, ou null se foi cancelada ou recusada (o caixa volta para escolher a forma).
/// </summary>
public partial class MaquininhaJanela : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly AreaInternaView _area;
    private readonly IMaquininha _maquininha;
    private readonly Cobranca _cobranca;
    private readonly CancellationTokenSource _cancelar = new();
    private readonly TaskCompletionSource<RespostaMaquininha?> _resultado = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private RespostaMaquininha? _resposta;

    public Task<RespostaMaquininha?> Resultado => _resultado.Task;

    public MaquininhaJanela(AreaInternaView area, IMaquininha maquininha, Cobranca cobranca, string identificador)
    {
        InitializeComponent();
        _area = area;
        _maquininha = maquininha;
        _cobranca = cobranca;

        NomeMaquininha.Text = maquininha.Nome;
        Titulo.Text = cobranca.Forma == "pix" ? "Pagamento com Pix" : "Pagamento na maquininha";
        FormaTexto.Text = FormaPagamento.Nome(cobranca.Forma).ToUpper(PtBr) + (cobranca.Parcelas > 1 ? $" · {cobranca.Parcelas}x" : "");
        Valor.Text = cobranca.Valor.ToString("C", PtBr);
        PainelTeste.Visibility = maquininha is MaquininhaEmulada ? Visibility.Visible : Visibility.Collapsed;
        if (cobranca.Forma == "pix")
        {
            var codigo = PixCopiaECola.Gerar(cobranca.Valor, identificador);
            CopiaECola.Text = codigo;
            Qr.Source = QrDeTeste(codigo);
            PainelPix.Visibility = Visibility.Visible;
            BotaoAprovar.Content = "Simular Pix recebido";
        }

        // Fechar a janela (Esc) no meio da cobrança é o mesmo que cancelar
        Unloaded += (_, _) =>
        {
            _cancelar.Cancel();
            _resultado.TrySetResult(_resposta is { Aprovada: true } ? _resposta : null);
        };
        Loaded += async (_, _) => { BotaoAprovar.Focus(); await CobrarAsync(); };
    }

    private async Task CobrarAsync()
    {
        var etapa = new Progress<string>(t => Visor.Text = t);
        try
        {
            _resposta = await _maquininha.CobrarAsync(_cobranca, etapa, _cancelar.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        PainelTeste.Visibility = Visibility.Collapsed;
        Visor.Text = _resposta.Aprovada ? "APROVADO" : "NÃO APROVADO";
        AvisoResultado.Visibility = Visibility.Visible;
        AvisoResultado.Background = (Brush)FindResource(_resposta.Aprovada ? "Verde50" : "Vermelho50");
        AvisoResultado.BorderBrush = (Brush)FindResource(_resposta.Aprovada ? "Verde700" : "Vermelho600");
        AvisoResultadoTexto.Foreground = (Brush)FindResource(_resposta.Aprovada ? "Verde700" : "Vermelho600");
        AvisoResultadoTexto.Text = _resposta.Aprovada
            ? $"Aprovado · {_resposta.Bandeira}{(_resposta.FinalCartao is null ? "" : " •••• " + _resposta.FinalCartao)} · NSU {_resposta.Nsu} · autorização {_resposta.Autorizacao}"
            : _resposta.Mensagem + " Escolha outra forma ou tente de novo.";
        BotaoCancelar.Visibility = Visibility.Collapsed;
        BotaoOk.Content = _resposta.Aprovada ? "Continuar" : "Voltar";
        BotaoOk.Visibility = Visibility.Visible;
        BotaoOk.Focus();
        if (_resposta.Aprovada)
        {
            // Aprovado: segue sozinho depois de um instante, como o caixa de verdade
            await Task.Delay(900);
            Fechar();
        }
    }

    private void Fechar()
    {
        _resultado.TrySetResult(_resposta is { Aprovada: true } ? _resposta : null);
        _area.FecharJanela();
    }

    private void Aprovar_Click(object sender, RoutedEventArgs e) => (_maquininha as MaquininhaEmulada)?.Simular(MaquininhaEmulada.Resultado.Aprovar);

    private void Recusar_Click(object sender, RoutedEventArgs e) => (_maquininha as MaquininhaEmulada)?.Simular(MaquininhaEmulada.Resultado.Recusar);

    private void SemComunicacao_Click(object sender, RoutedEventArgs e) => (_maquininha as MaquininhaEmulada)?.Simular(MaquininhaEmulada.Resultado.SemComunicacao);

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        _cancelar.Cancel();
        Fechar();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Fechar();

    private void Copiar_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(CopiaECola.Text); } catch (System.Runtime.InteropServices.COMException) { }
    }

    /// <summary>
    /// Desenho com cara de QR code (os três quadrados de canto e módulos tirados do código), só para o teste:
    /// não é lido por aplicativo de banco. O QR de verdade vem da maquininha ou do banco quando houver integração.
    /// </summary>
    private static BitmapSource QrDeTeste(string texto)
    {
        const int n = 29;
        var bits = SHA256.HashData(Encoding.UTF8.GetBytes(texto));
        var pixels = new byte[n * n];
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var i = y * n + x;
                var escuro = (bits[(i / 8) % bits.Length] >> (i % 8) & 1) == 1 ^ ((x * 7 + y * 13) % 5 == 0);
                pixels[i] = escuro ? (byte)0 : (byte)255;
            }
        void Canto(int cx, int cy)
        {
            for (var y = -1; y <= 7; y++)
                for (var x = -1; x <= 7; x++)
                {
                    var px = cx + x; var py = cy + y;
                    if (px < 0 || py < 0 || px >= n || py >= n) continue;
                    var borda = x is 0 or 6 || y is 0 or 6;
                    var miolo = x is >= 2 and <= 4 && y is >= 2 and <= 4;
                    var dentro = x is >= 0 and <= 6 && y is >= 0 and <= 6;
                    pixels[py * n + px] = dentro && (borda || miolo) ? (byte)0 : (byte)255;
                }
        }
        Canto(0, 0);
        Canto(n - 7, 0);
        Canto(0, n - 7);
        return BitmapSource.Create(n, n, 96, 96, PixelFormats.Gray8, null, pixels, n);
    }
}
