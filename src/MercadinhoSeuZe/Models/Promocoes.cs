using System.Globalization;
using System.Text.Json.Serialization;

namespace MercadinhoSeuZe.Models;

/// <summary>
/// Promoção de um produto (tabela promocoes, script 008): preço fixo ("de 8,90 por 6,99") ou porcentagem, de uma data
/// até outra (fim vazio = até encerrar). O caixa cobra o preço promocional sozinho no período.
/// </summary>
public sealed class Promocao
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("produto_id")] public long ProdutoId { get; set; }
    /// <summary>"preco" (valor = preço promocional em R$) ou "percentual" (valor = desconto em %).</summary>
    [JsonPropertyName("tipo")] public string Tipo { get; set; } = "preco";
    [JsonPropertyName("valor")] public decimal Valor { get; set; }
    [JsonPropertyName("inicio")] public DateOnly Inicio { get; set; }
    [JsonPropertyName("fim")] public DateOnly? Fim { get; set; }
    [JsonPropertyName("encerrada_em")] public DateTimeOffset? EncerradaEm { get; set; }
    [JsonPropertyName("criada_por")] public string? CriadaPor { get; set; }

    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Today);

    public bool PorPreco => Tipo == "preco";

    public bool Encerrada => EncerradaEm is not null || Fim < Hoje;
    public bool Agendada => !Encerrada && Inicio > Hoje;
    public bool Ativa => !Encerrada && !Agendada;

    /// <summary>"Ativa", "Agendada" ou "Encerrada".</summary>
    public string Situacao => Ativa ? "Ativa" : Agendada ? "Agendada" : "Encerrada";

    /// <summary>Preço com a promoção (por unidade ou por kg).</summary>
    public decimal PrecoCom(decimal preco) => PorPreco ? Math.Min(preco, Valor) : Promocoes.ComDesconto(preco, Valor);

    /// <summary>"R$ 6,99" ou "15%".</summary>
    public string Texto => PorPreco ? Valor.ToString("C", PtBr) : $"{Valor.ToString("0.##", PtBr)}%";

    /// <summary>"de 30/10 até 05/11", "a partir de 30/10".</summary>
    public string Periodo => Fim is { } f
        ? (f == Inicio ? $"só em {Inicio:dd/MM}" : $"de {Inicio:dd/MM} até {f:dd/MM}")
        : $"a partir de {Inicio:dd/MM}, sem data para acabar";
}

/// <summary>
/// Promoção de validade de uma categoria (tabela promocoes_validade, script 008): começa DiasAntes dias antes do
/// vencimento com o desconto mínimo e sobe por igual até o máximo um dia antes (no dia do vencimento continua o máximo).
/// Mesma conta da função desconto_validade do banco.
/// </summary>
public sealed class RegraValidade
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("categoria")] public string Categoria { get; set; } = "";
    [JsonPropertyName("dias_antes")] public int DiasAntes { get; set; } = 3;
    [JsonPropertyName("desconto_min")] public decimal DescontoMin { get; set; }
    [JsonPropertyName("desconto_max")] public decimal DescontoMax { get; set; }
    [JsonPropertyName("ativa")] public bool Ativa { get; set; } = true;

    /// <summary>Desconto (%) de um lote que vence em "validade", no dia "hoje". 0 = fora da promoção.</summary>
    public decimal Desconto(DateOnly? validade, DateOnly hoje) => Ativa ? Conta(validade, hoje) : 0;

    /// <summary>A conta da regra, ligada ou não (a prévia mostra os descontos mesmo da regra desligada).</summary>
    private decimal Conta(DateOnly? validade, DateOnly hoje)
    {
        if (validade is null) return 0;
        var dias = validade.Value.DayNumber - hoje.DayNumber;
        if (dias < 0 || dias > DiasAntes) return 0;
        if (dias <= 1 || DiasAntes == 1) return DescontoMax;
        return Math.Round(DescontoMin + (DescontoMax - DescontoMin) * (DiasAntes - dias) / (DiasAntes - 1), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Desconto de cada dia, do primeiro até o do vencimento (para a prévia da janela).</summary>
    public IEnumerable<(int Dias, decimal Desconto)> Escada()
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        for (var d = DiasAntes; d >= 0; d--) yield return (d, Conta(hoje.AddDays(d), hoje));
    }

    public static RegraValidade? Da(IEnumerable<RegraValidade> regras, string categoria) =>
        regras.FirstOrDefault(r => string.Equals(r.Categoria.Trim(), categoria.Trim(), StringComparison.CurrentCultureIgnoreCase));
}

/// <summary>Contas de promoção que as telas usam para mostrar (o caixa cobra pelo banco, com a mesma regra).</summary>
public static class Promocoes
{
    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Desconto de validade (%) que o lote tem hoje no caixa; 0 = sem.</summary>
    public static decimal DescontoValidade(Produto p, Lote l) =>
        p.SemPromocaoValidade || l.Esgotado || p.RegraValidade is not { } r ? 0 : r.Desconto(l.Validade, Hoje);

    /// <summary>Preço por unidade (ou kg) do lote no caixa hoje: o maior desconto entre a promoção do produto e a de validade.</summary>
    public static decimal PrecoDoLote(Produto p, Lote l)
    {
        var porProduto = p.PromocaoAtiva?.PrecoCom(p.Preco) ?? p.Preco;
        var porValidade = ComDesconto(p.Preco, DescontoValidade(p, l));
        return Math.Min(porProduto, porValidade);
    }

    /// <summary>
    /// Preço com um desconto em %, com o mesmo arredondamento do banco (preco_com_promocao): o desconto em R$ com 4 casas
    /// e o preço com 2, meio centavo para cima. Ex.: 15% de R$ 8,90 = R$ 7,57 (o Math.Round padrão daria 7,56).
    /// </summary>
    public static decimal ComDesconto(decimal preco, decimal porcentagem)
    {
        var desconto = Math.Round(preco * porcentagem / 100, 4, MidpointRounding.AwayFromZero);
        return Math.Round(preco - desconto, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Lotes com unidades que estão na promoção de validade hoje.</summary>
    public static IEnumerable<Lote> LotesEmPromocao(Produto p) =>
        p.Lotes.Where(l => !l.Esgotado && DescontoValidade(p, l) > 0);

    /// <summary>Liga em cada produto a promoção ativa e a regra de validade da categoria.</summary>
    public static void Aplicar(IEnumerable<Produto> produtos, IReadOnlyList<Promocao> promocoes, IReadOnlyList<RegraValidade> regras)
    {
        foreach (var p in produtos)
        {
            p.PromocaoAtiva = promocoes.Where(x => x.ProdutoId == p.Id && x.Ativa).OrderByDescending(x => x.Id).FirstOrDefault();
            p.RegraValidade = RegraValidade.Da(regras, p.Categoria);
        }
    }
}
