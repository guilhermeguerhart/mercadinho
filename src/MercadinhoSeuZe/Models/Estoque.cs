using System.Globalization;
using System.Text.Json.Serialization;

namespace MercadinhoSeuZe.Models;

/// <summary>Uma compra de um produto: quantidade comprada, quanto ainda resta e a validade.</summary>
public sealed class Lote
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("produto_id")] public long ProdutoId { get; set; }
    [JsonPropertyName("quantidade_inicial")] public decimal QuantidadeInicial { get; set; }
    [JsonPropertyName("quantidade_restante")] public decimal QuantidadeRestante { get; set; }
    [JsonPropertyName("data_compra")] public DateOnly DataCompra { get; set; }
    [JsonPropertyName("validade")] public DateOnly? Validade { get; set; }

    public bool Esgotado => QuantidadeRestante == 0;

    /// <summary>Unidades (ou kg) deste lote que já saíram (vendidas ou baixadas).</summary>
    public decimal Vendido => QuantidadeInicial - QuantidadeRestante;

    public SituacaoValidade? Situacao => SituacaoValidade.De(Validade);
}

/// <summary>Produto do estoque (tabela produtos) com os lotes dele.</summary>
public sealed class Produto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("nome")] public string Nome { get; set; } = "";
    [JsonPropertyName("categoria")] public string Categoria { get; set; } = "";
    [JsonPropertyName("preco")] public decimal Preco { get; set; }
    [JsonPropertyName("estoque_minimo")] public decimal EstoqueMinimo { get; set; }
    [JsonPropertyName("codigo_barras_caixa")] public string? CodigoBarrasCaixa { get; set; }
    /// <summary>Unidades por caixa (ou kg por caixa nos produtos por quilo).</summary>
    [JsonPropertyName("unidades_por_caixa")] public decimal UnidadesPorCaixa { get; set; } = 1;
    /// <summary>Quantidade para o aviso antecipado; null = mínimo + 50%.</summary>
    [JsonPropertyName("estoque_aviso")] public decimal? EstoqueAviso { get; set; }
    /// <summary>"un" (por unidade) ou "kg" (por quilo, ex.: carnes do açougue).</summary>
    [JsonPropertyName("unidade_medida")] public string UnidadeMedida { get; set; } = "un";
    /// <summary>Validade padrão: dias depois da compra. Preenche a validade dos lotes novos.</summary>
    [JsonPropertyName("dias_validade")] public int? DiasValidade { get; set; }
    /// <summary>Fornecedor principal (semana 4); null = o que entrega a categoria.</summary>
    [JsonPropertyName("fornecedor_id")] public long? FornecedorId { get; set; }
    /// <summary>Dias que o fornecedor leva para entregar este produto; null = prazo padrão do fornecedor.</summary>
    [JsonPropertyName("prazo_entrega_dias")] public int? PrazoEntregaDias { get; set; }
    /// <summary>"Pedir antes de acabar": o aviso antecipado vira o ponto de pedido calculado (PontoDePedido).</summary>
    [JsonPropertyName("pedir_antes")] public bool PedirAntes { get; set; }

    /// <summary>
    /// Prazo padrão do fornecedor deste produto (não vem do banco; as telas preenchem com
    /// Fornecedor.AplicarPrazos). Usado quando o produto não tem prazo próprio.
    /// </summary>
    [JsonIgnore] public int PrazoDoFornecedor { get; set; } = Fornecedor.PrazoPadrao;

    /// <summary>Dias que leva para chegar: o prazo do produto ou o do fornecedor.</summary>
    public int PrazoReposicao => PrazoEntregaDias ?? PrazoDoFornecedor;

    /// <summary>
    /// Dias de validade para preencher um lote novo: a validade padrão cadastrada ou, se não tiver,
    /// o mesmo prazo do último lote com validade (compra até validade).
    /// </summary>
    public int? DiasValidadeSugerida => DiasValidade ?? Lotes
        .Where(l => l.Validade is not null && l.Validade > l.DataCompra)
        .OrderByDescending(l => l.DataCompra).ThenByDescending(l => l.Id)
        .Select(l => (int?)(l.Validade!.Value.DayNumber - l.DataCompra.DayNumber))
        .FirstOrDefault();
    [JsonPropertyName("lotes")] public List<Lote> Lotes { get; set; } = [];

    public bool PorPeso => UnidadeMedida == "kg";

    public decimal Quantidade => Lotes.Sum(l => l.QuantidadeRestante);

    /// <summary>"12 unidades", "1 unidade" ou "12,35 kg".</summary>
    public string Formatar(decimal quantidade) => FormatarQuantidade(quantidade, PorPeso);

    /// <summary>Só o número: "12" ou "12,35".</summary>
    public string Numero(decimal quantidade) => quantidade.ToString(PorPeso ? "0.###" : "0", PtBr);

    /// <summary>Sufixo curto da unidade: "un." ou "kg".</summary>
    public string Sigla => PorPeso ? "kg" : "un.";

    public static string FormatarQuantidade(decimal quantidade, bool porPeso) => porPeso
        ? quantidade.ToString("0.###", PtBr) + " kg"
        : $"{quantidade.ToString("0", PtBr)} {(quantidade == 1 ? "unidade" : "unidades")}";

    private static readonly CultureInfo PtBr = new("pt-BR");

    public bool SemEstoque => Quantidade == 0;

    /// <summary>Tem alguma unidade, mas menos que o mínimo cadastrado.</summary>
    public bool EstoqueBaixo => !SemEstoque && Quantidade < EstoqueMinimo;

    /// <summary>Com esta quantidade ou menos (e ainda acima do mínimo) aparece o aviso amarelo antecipado.</summary>
    public decimal LimiteAviso => PontoDePedido ?? EstoqueAviso ?? AvisoPadrao(EstoqueMinimo, PorPeso);

    /// <summary>Quantos dias para trás entram na conta do que sai por dia.</summary>
    public const int DiasConsumo = 60;

    /// <summary>
    /// Quanto sai por dia, em média: o que já saiu dos lotes comprados nos últimos 60 dias dividido pelos dias
    /// desde a compra mais antiga deles. null enquanto nada saiu (as vendas dão baixa a partir da semana 5).
    /// </summary>
    public decimal? ConsumoDiario
    {
        get
        {
            var hoje = DateOnly.FromDateTime(DateTime.Today);
            var recentes = Lotes.Where(l => l.DataCompra.DayNumber >= hoje.DayNumber - DiasConsumo).ToList();
            var saiu = recentes.Sum(l => l.Vendido);
            if (saiu <= 0) return null;
            var dias = Math.Max(1, hoje.DayNumber - recentes.Min(l => l.DataCompra).DayNumber);
            return saiu / dias;
        }
    }

    /// <summary>
    /// Ponto de pedido do "Pedir antes de acabar": mínimo + o que sai por dia × prazo de entrega (arredondado
    /// para cima). null quando o switch está desligado ou ainda não há saídas para calcular.
    /// </summary>
    public decimal? PontoDePedido
    {
        get
        {
            if (!PedirAntes || ConsumoDiario is not { } porDia) return null;
            var ponto = EstoqueMinimo + porDia * PrazoReposicao;
            return PorPeso ? Math.Ceiling(ponto * 10) / 10 : Math.Ceiling(ponto);
        }
    }

    /// <summary>Mínimo + 50% (arredondado para cima: inteiro por unidade, 0,1 kg por quilo).</summary>
    public static decimal AvisoPadrao(decimal minimo, bool porPeso) => porPeso
        ? Math.Ceiling(minimo * 15m) / 10m
        : Math.Ceiling(minimo * 1.5m);

    /// <summary>Ainda não está baixo, mas está chegando no mínimo: hora de pedir, principalmente se a entrega demora.</summary>
    public bool AvisoAntecipado => !SemEstoque && !EstoqueBaixo && Quantidade <= LimiteAviso && LimiteAviso > EstoqueMinimo;

    public decimal Vendido => Lotes.Sum(l => l.Vendido);

    /// <summary>Precisa repor: sem estoque, abaixo do mínimo ou no aviso antecipado.</summary>
    public bool PrecisaRepor => SemEstoque || EstoqueBaixo || AvisoAntecipado;

    /// <summary>
    /// Quanto pedir (regra do site: o dobro do mínimo menos o que tem, e pelo menos o mínimo). Por unidade,
    /// arredonda para caixas inteiras.
    /// </summary>
    public decimal QuantidadeSugerida
    {
        get
        {
            var q = Math.Max(Math.Max(EstoqueMinimo * 2 - Quantidade, EstoqueMinimo), PorPeso ? 1 : UnidadesPorCaixa);
            if (PorPeso) return Math.Ceiling(q * 10) / 10;
            return UnidadesPorCaixa > 1 ? Math.Ceiling(q / UnidadesPorCaixa) * UnidadesPorCaixa : Math.Ceiling(q);
        }
    }

    /// <summary>Validade mais próxima entre os lotes que ainda têm unidades.</summary>
    public DateOnly? ProximaValidade => Lotes.Where(l => !l.Esgotado && l.Validade is not null).Min(l => l.Validade);

    public SituacaoValidade? Situacao => SituacaoValidade.De(ProximaValidade);

    /// <summary>Tem lote com unidades e validade que já passou (antes de hoje).</summary>
    public bool Vencido => Situacao?.Nivel == NivelValidade.Vencido;

    /// <summary>Ainda não venceu, mas vence em até 30 dias (com unidades restantes).</summary>
    public bool ValidadeProxima => Situacao?.Nivel == NivelValidade.Atencao;
}

public enum NivelValidade { Ok, Atencao, Vencido }

/// <summary>Situação da validade, com os mesmos textos e o prazo de 30 dias do site de referência.</summary>
public sealed record SituacaoValidade(NivelValidade Nivel, string Texto, int Dias)
{
    public const int DiasAlerta = 30;

    public bool Alerta => Nivel != NivelValidade.Ok;

    public static SituacaoValidade? De(DateOnly? validade)
    {
        if (validade is null) return null;
        var d = validade.Value.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber;
        if (d < 0) return new(NivelValidade.Vencido, d == -1 ? "Venceu ontem" : $"Venceu há {-d} dias", d);
        if (d == 0) return new(NivelValidade.Atencao, "Vence hoje", d);
        if (d <= DiasAlerta) return new(NivelValidade.Atencao, d == 1 ? "Vence amanhã" : $"Vence em {d} dias", d);
        return new(NivelValidade.Ok, $"Vence em {d} dias", d);
    }
}
