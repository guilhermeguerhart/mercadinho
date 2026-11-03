using System.Text.Json.Serialization;

namespace MercadinhoSeuZe.Models;

/// <summary>
/// Resumo de um dia (função resumo_dia, script 009): o que a tela Fechamento do dia, o e-mail e o celular mostram.
/// </summary>
public sealed class ResumoDoDia
{
    [JsonPropertyName("dia")] public DateTime Dia { get; set; }
    [JsonPropertyName("total")] public decimal Total { get; set; }
    [JsonPropertyName("vendas")] public int Vendas { get; set; }
    [JsonPropertyName("ticket_medio")] public decimal TicketMedio { get; set; }
    [JsonPropertyName("descontos")] public decimal Descontos { get; set; }
    [JsonPropertyName("promocoes")] public decimal Promocoes { get; set; }
    [JsonPropertyName("itens_vendidos")] public int ItensVendidos { get; set; }
    [JsonPropertyName("canceladas")] public int Canceladas { get; set; }
    [JsonPropertyName("canceladas_valor")] public decimal CanceladasValor { get; set; }
    /// <summary>Custo do que saiu dos lotes com preço de custo e quanto foi vendido desses itens (para a margem).</summary>
    [JsonPropertyName("custo")] public decimal Custo { get; set; }
    [JsonPropertyName("vendido_com_custo")] public decimal VendidoComCusto { get; set; }
    [JsonPropertyName("formas")] public Dictionary<string, decimal> Formas { get; set; } = [];
    [JsonPropertyName("caixas")] public List<CaixaDoDia> Caixas { get; set; } = [];
    [JsonPropertyName("diferenca_caixas")] public decimal DiferencaCaixas { get; set; }
    [JsonPropertyName("mais_vendidos")] public List<ProdutoVendido> MaisVendidos { get; set; } = [];
    [JsonPropertyName("por_hora")] public List<VendasNaHora> PorHora { get; set; } = [];
    [JsonPropertyName("itens_cancelados")] public List<ItemCanceladoDia> ItensCancelados { get; set; } = [];
    [JsonPropertyName("semana_anterior")] public Comparacao? SemanaAnterior { get; set; }
    [JsonPropertyName("vendas_abertas")] public int VendasAbertas { get; set; }
    /// <summary>null = o dia ainda não foi fechado.</summary>
    [JsonPropertyName("fechamento")] public FechamentoDia? Fechamento { get; set; }

    public bool Fechado => Fechamento is { Automatico: false };
    public bool TodosCaixasFechados => Caixas.All(c => c.FechadoEm is not null);
    public decimal Lucro => VendidoComCusto - Custo;
    /// <summary>Margem sobre o que tem custo cadastrado (null = nenhum lote com custo).</summary>
    public decimal? Margem => VendidoComCusto > 0 ? Lucro / VendidoComCusto : null;
}

public sealed class CaixaDoDia
{
    [JsonPropertyName("caixa_id")] public long CaixaId { get; set; }
    [JsonPropertyName("operador_nome")] public string OperadorNome { get; set; } = "";
    [JsonPropertyName("aberto_em")] public DateTimeOffset AbertoEm { get; set; }
    [JsonPropertyName("fechado_em")] public DateTimeOffset? FechadoEm { get; set; }
    [JsonPropertyName("troco_inicial")] public decimal TrocoInicial { get; set; }
    [JsonPropertyName("vendas")] public int Vendas { get; set; }
    [JsonPropertyName("total")] public decimal Total { get; set; }
    /// <summary>Dinheiro que deveria estar na gaveta (no fechamento, o que foi conferido).</summary>
    [JsonPropertyName("esperado")] public decimal Esperado { get; set; }
    [JsonPropertyName("contado")] public decimal? Contado { get; set; }
    [JsonPropertyName("diferenca")] public decimal? Diferenca { get; set; }
    [JsonPropertyName("obs")] public string? Obs { get; set; }
    [JsonPropertyName("fechado_por")] public string? FechadoPor { get; set; }
    [JsonPropertyName("sangrias")] public decimal Sangrias { get; set; }
    [JsonPropertyName("suprimentos")] public decimal Suprimentos { get; set; }

    public string HoraAbertura => AbertoEm.ToLocalTime().ToString("HH:mm");
    public string? HoraFechamento => FechadoEm?.ToLocalTime().ToString("HH:mm");
}

public sealed class ProdutoVendido
{
    [JsonPropertyName("nome")] public string Nome { get; set; } = "";
    [JsonPropertyName("unidade")] public string Unidade { get; set; } = "un";
    [JsonPropertyName("quantidade")] public decimal Quantidade { get; set; }
    [JsonPropertyName("valor")] public decimal Valor { get; set; }

    public string QuantidadeTexto => Unidade == "kg" ? $"{Quantidade:0.###} kg" : $"{Quantidade:0} un.";
}

public sealed class VendasNaHora
{
    [JsonPropertyName("hora")] public int Hora { get; set; }
    [JsonPropertyName("vendas")] public int Vendas { get; set; }
    [JsonPropertyName("total")] public decimal Total { get; set; }
}

public sealed class ItemCanceladoDia
{
    [JsonPropertyName("nome")] public string Nome { get; set; } = "";
    [JsonPropertyName("quantidade")] public decimal Quantidade { get; set; }
    [JsonPropertyName("unidade")] public string Unidade { get; set; } = "un";
    [JsonPropertyName("valor")] public decimal Valor { get; set; }
    [JsonPropertyName("operador_nome")] public string OperadorNome { get; set; } = "";
    [JsonPropertyName("autorizado_por")] public string? AutorizadoPor { get; set; }
    [JsonPropertyName("criado_em")] public DateTimeOffset CriadoEm { get; set; }
}

public sealed class Comparacao
{
    [JsonPropertyName("dia")] public DateTime? Dia { get; set; }
    [JsonPropertyName("inicio")] public DateTime? Inicio { get; set; }
    [JsonPropertyName("fim")] public DateTime? Fim { get; set; }
    [JsonPropertyName("total")] public decimal Total { get; set; }
    [JsonPropertyName("vendas")] public int Vendas { get; set; }
}

public sealed class FechamentoDia
{
    [JsonPropertyName("fechado_em")] public DateTimeOffset FechadoEm { get; set; }
    [JsonPropertyName("fechado_por")] public string FechadoPor { get; set; } = "";
    [JsonPropertyName("automatico")] public bool Automatico { get; set; }
    [JsonPropertyName("obs")] public string? Obs { get; set; }
    [JsonPropertyName("email_enviado_em")] public DateTimeOffset? EmailEnviadoEm { get; set; }
    [JsonPropertyName("email_para")] public string? EmailPara { get; set; }
    [JsonPropertyName("email_erro")] public string? EmailErro { get; set; }
}

/// <summary>Configuração do mercado (tabela config_mercado, uma linha só).</summary>
public sealed class ConfigMercado
{
    [JsonPropertyName("emails_resumo")] public string? EmailsResumo { get; set; }
    [JsonPropertyName("email_pedidos")] public string? EmailPedidos { get; set; }
    [JsonPropertyName("resumo_automatico")] public bool ResumoAutomatico { get; set; } = true;
}

/// <summary>Vendas de um período (função analise_vendas): por dia, semana ou mês.</summary>
public sealed class AnaliseVendas
{
    [JsonPropertyName("inicio")] public DateTime Inicio { get; set; }
    [JsonPropertyName("fim")] public DateTime Fim { get; set; }
    [JsonPropertyName("agrupar")] public string Agrupar { get; set; } = "dia";
    [JsonPropertyName("serie")] public List<PontoSerie> Serie { get; set; } = [];
    [JsonPropertyName("total")] public decimal Total { get; set; }
    [JsonPropertyName("vendas")] public int Vendas { get; set; }
    [JsonPropertyName("anterior")] public Comparacao Anterior { get; set; } = new();
    [JsonPropertyName("categorias")] public List<ValorCategoria> Categorias { get; set; } = [];
    [JsonPropertyName("formas")] public Dictionary<string, decimal> Formas { get; set; } = [];
    [JsonPropertyName("dias_semana")] public List<VendasDiaSemana> DiasSemana { get; set; } = [];
    [JsonPropertyName("mais_vendidos")] public List<ProdutoVendido> MaisVendidos { get; set; } = [];
    [JsonPropertyName("custo")] public decimal Custo { get; set; }
    [JsonPropertyName("vendido_com_custo")] public decimal VendidoComCusto { get; set; }
    [JsonPropertyName("descontos")] public decimal Descontos { get; set; }
    [JsonPropertyName("promocoes")] public decimal Promocoes { get; set; }
    [JsonPropertyName("perdas")] public Perdas Perdas { get; set; } = new();
    [JsonPropertyName("cancelamentos")] public Cancelamentos Cancelamentos { get; set; } = new();

    public decimal TicketMedio => Vendas > 0 ? Total / Vendas : 0;
    public decimal? Margem => VendidoComCusto > 0 ? (VendidoComCusto - Custo) / VendidoComCusto : null;
    /// <summary>Variação sobre o período anterior do mesmo tamanho (null = antes não vendeu nada).</summary>
    public decimal? Variacao => Anterior.Total > 0 ? (Total - Anterior.Total) / Anterior.Total : null;
}

public sealed class PontoSerie
{
    [JsonPropertyName("periodo")] public DateTime Periodo { get; set; }
    [JsonPropertyName("total")] public decimal Total { get; set; }
    [JsonPropertyName("vendas")] public int Vendas { get; set; }
    [JsonPropertyName("descontos")] public decimal Descontos { get; set; }
}

public sealed class ValorCategoria
{
    [JsonPropertyName("categoria")] public string Categoria { get; set; } = "";
    [JsonPropertyName("valor")] public decimal Valor { get; set; }
}

public sealed class VendasDiaSemana
{
    /// <summary>0 = domingo ... 6 = sábado.</summary>
    [JsonPropertyName("dia_semana")] public int DiaSemana { get; set; }
    [JsonPropertyName("total")] public decimal Total { get; set; }
    [JsonPropertyName("vendas")] public int Vendas { get; set; }
    /// <summary>Quantos dias desse dia da semana tiveram venda (para a média).</summary>
    [JsonPropertyName("dias")] public int Dias { get; set; }

    public decimal Media => Dias > 0 ? Total / Dias : 0;
}

public sealed class Perdas
{
    [JsonPropertyName("lotes")] public int Lotes { get; set; }
    [JsonPropertyName("valor")] public decimal Valor { get; set; }
}

public sealed class Cancelamentos
{
    [JsonPropertyName("vendas")] public int Vendas { get; set; }
    [JsonPropertyName("itens")] public int Itens { get; set; }
    [JsonPropertyName("valor_itens")] public decimal ValorItens { get; set; }
}
