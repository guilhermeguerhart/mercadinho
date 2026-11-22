using System.Globalization;
using System.Text;

namespace MercadinhoSeuZe.Services;

/// <summary>O que o caixa pede para a maquininha cobrar.</summary>
public sealed record Cobranca(string Forma, decimal Valor, int Parcelas = 1);

/// <summary>Resposta da maquininha. Aprovada = false traz o motivo em Mensagem.</summary>
public sealed record RespostaMaquininha(bool Aprovada, string Mensagem, string? Bandeira = null, string? Nsu = null,
    string? Autorizacao = null, string? FinalCartao = null);

/// <summary>
/// Encaixe da maquininha de cartão (TEF/POS). O caixa só conversa com esta interface: hoje roda a
/// MaquininhaEmulada; para ligar uma maquininha de verdade (Stone, Cielo, PagBank, Mercado Pago, SiTef/PayGo),
/// basta escrever outra classe que implemente CobrarAsync e trocar em Maquininha.Atual.
/// </summary>
public interface IMaquininha
{
    /// <summary>Nome mostrado na janela (ex.: "Maquininha de teste").</summary>
    string Nome { get; }

    /// <summary>Envia o valor para a maquininha e espera o cliente pagar. etapa recebe os textos do visor.</summary>
    Task<RespostaMaquininha> CobrarAsync(Cobranca cobranca, IProgress<string> etapa, CancellationToken cancelar);
}

public static class Maquininha
{
    /// <summary>"ton" (Stone Ton: o operador digita o valor na maquininha e confirma no caixa) ou "teste" (emulada).</summary>
    public const string Ton = "ton", Teste = "teste";

    /// <summary>
    /// A maquininha deste computador, escolhida pelo gerente no Caixa (fica em PreferenciasLocais). Sem escolha:
    /// a de teste na versão de desenvolvimento e a Ton no .exe do mercado.
    /// </summary>
    public static IMaquininha Atual { get; set; } = Criar(PreferenciasLocais.Carregar().Maquininha ?? Padrao);

#if DESENVOLVIMENTO
    public const string Padrao = Teste;
#else
    public const string Padrao = Ton;
#endif

    public static IMaquininha Criar(string tipo) => tipo == Ton ? new MaquininhaTon() : new MaquininhaEmulada();

    public static string Tipo => Atual is MaquininhaTon ? Ton : Teste;

    /// <summary>Troca a maquininha deste computador e guarda a escolha.</summary>
    public static void Escolher(string tipo)
    {
        Atual = Criar(tipo);
        var p = PreferenciasLocais.Carregar();
        p.Maquininha = tipo;
        p.Salvar();
    }
}

/// <summary>
/// Stone Ton (semana 8): a Ton não recebe o valor de um programa de computador (a integração da Stone, Connect 2.0 e
/// TEF, é para contas Stone empresa com maquininha homologada). Então o caixa mostra o valor e a forma, o operador
/// digita na Ton e, quando ela aprova, confirma na janela. A cobrança fica ligada à nota (número da venda): para achar na
/// maquininha, valor e hora da nota (pedido do Guilherme: nada de anotar o final do cartão). Pix: a Ton gera o QR na tela dela. Para ficar automático no futuro, basta outra classe com CobrarAsync (o caixa não muda).
/// </summary>
public sealed class MaquininhaTon : IMaquininha
{
    private TaskCompletionSource<bool>? _confirmacao;

    public string Nome => "Stone Ton · digite o valor na maquininha e confirme aqui";

    /// <summary>Botões da janela: o que a Ton mostrou.</summary>
    public void Confirmar(bool aprovado) => _confirmacao?.TrySetResult(aprovado);

    public async Task<RespostaMaquininha> CobrarAsync(Cobranca cobranca, IProgress<string> etapa, CancellationToken cancelar)
    {
        _confirmacao = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registro = cancelar.Register(() => _confirmacao.TrySetCanceled());
        var forma = cobranca.Forma switch
        {
            "debito" => "no débito",
            "credito" => cobranca.Parcelas > 1 ? $"no crédito em {cobranca.Parcelas}x" : "no crédito à vista",
            "pix" => "no Pix (a Ton mostra o QR)",
            "vale_alimentacao" or "vale_refeicao" => "no vale (voucher)",
            _ => ""
        };
        etapa.Report($"Digite {cobranca.Valor.ToString("C", CultureInfo.GetCultureInfo("pt-BR"))} {forma} na Ton");
        return await _confirmacao.Task
            ? new RespostaMaquininha(true, "Aprovado na maquininha", cobranca.Forma == "pix" ? "Pix" : "Maquininha")
            : new RespostaMaquininha(false, "A maquininha não aprovou.");
    }
}

/// <summary>
/// Maquininha de teste: a janela mostra botões para o operador escolher o que "acontece" (aprovar, recusar, sem
/// comunicação). Gera bandeira, NSU, autorização e final do cartão fictícios. Nada sai do computador.
/// </summary>
public sealed class MaquininhaEmulada : IMaquininha
{
    public enum Resultado { Aprovar, Recusar, SemComunicacao }

    private static readonly string[] Bandeiras = ["Visa", "Mastercard", "Elo", "Hipercard", "American Express"];
    private static readonly string[] Vales = ["Alelo", "Pluxee", "Ticket", "VR"];
    private readonly Random _sorteio = new();
    private TaskCompletionSource<Resultado>? _escolha;

    public string Nome => "Maquininha de teste (emulada)";

    /// <summary>Botões de teste da janela.</summary>
    public void Simular(Resultado resultado) => _escolha?.TrySetResult(resultado);

    public async Task<RespostaMaquininha> CobrarAsync(Cobranca cobranca, IProgress<string> etapa, CancellationToken cancelar)
    {
        _escolha = new TaskCompletionSource<Resultado>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registro = cancelar.Register(() => _escolha.TrySetCanceled());
        etapa.Report("Conectando…");
        await Task.Delay(500, cancelar);
        etapa.Report(cobranca.Forma == "pix" ? "Aguardando o cliente pagar o Pix" : "Aproxime, insira ou passe o cartão");

        var resultado = await _escolha.Task;
        etapa.Report("Processando…");
        await Task.Delay(1200, cancelar);
        if (resultado == Resultado.SemComunicacao)
            return new RespostaMaquininha(false, "Sem comunicação com a maquininha. Confira o cabo/Wi-Fi e tente de novo.");
        if (resultado == Resultado.Recusar)
            return new RespostaMaquininha(false, cobranca.Forma == "pix" ? "Pix não recebido (expirou)." : "Transação negada: saldo insuficiente.");

        var vale = cobranca.Forma.StartsWith("vale", StringComparison.Ordinal);
        return new RespostaMaquininha(true, "Aprovado",
            cobranca.Forma == "pix" ? "Pix" : vale ? Vales[_sorteio.Next(Vales.Length)] : Bandeiras[_sorteio.Next(Bandeiras.Length)],
            _sorteio.Next(100000, 999999).ToString(CultureInfo.InvariantCulture) + _sorteio.Next(100, 999).ToString(CultureInfo.InvariantCulture),
            _sorteio.Next(100000, 999999).ToString(CultureInfo.InvariantCulture),
            cobranca.Forma == "pix" ? null : _sorteio.Next(0, 10000).ToString("0000", CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Pix "copia e cola" (BR Code do Banco Central: campos EMV com CRC16 no fim). Com chave e nome de teste enquanto
/// o pagamento é emulado; com a integração real, o QR dinâmico vem do banco ou da maquininha.
/// </summary>
public static class PixCopiaECola
{
    public const string ChaveTeste = "pix-de-teste@mercado.local";

    public static string Gerar(decimal valor, string identificador, string chave = ChaveTeste, string nome = "MERCADO", string cidade = "SAO PAULO")
    {
        static string Campo(string id, string valor) => id + valor.Length.ToString("00", CultureInfo.InvariantCulture) + valor;
        var conta = Campo("00", "br.gov.bcb.pix") + Campo("01", chave);
        var txid = new string(identificador.Where(char.IsAsciiLetterOrDigit).Take(25).ToArray());
        var texto = Campo("00", "01") + Campo("26", conta) + Campo("52", "0000") + Campo("53", "986") +
                    Campo("54", valor.ToString("0.00", CultureInfo.InvariantCulture)) + Campo("58", "BR") +
                    Campo("59", nome) + Campo("60", cidade) + Campo("62", Campo("05", txid.Length > 0 ? txid : "***")) + "6304";
        return texto + Crc16(texto);
    }

    private static string Crc16(string texto)
    {
        ushort crc = 0xFFFF;
        foreach (var b in Encoding.UTF8.GetBytes(texto))
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc.ToString("X4", CultureInfo.InvariantCulture);
    }
}
