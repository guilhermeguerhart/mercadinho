using System.Text.Json.Serialization;

namespace MercadinhoSeuZe.Models;

// Tela "Previsão de compras" (semana 7, scripts 016 e 019). Tudo vem pronto das funções do banco:
// previsao_tela, pedido_sugerido, previsao_produto e previsao_alertas.

/// <summary>Topo da tela e aba "Teste da previsão" (função previsao_tela).</summary>
public sealed class PrevisaoTela
{
    [JsonPropertyName("hoje")] public DateOnly Hoje { get; set; }
    [JsonPropertyName("geral")] public PrevisaoGeral? Geral { get; set; }
    [JsonPropertyName("perdidas_7d")] public VendasPerdidas? Perdidas { get; set; }
    [JsonPropertyName("anos")] public List<AnoTestado> Anos { get; set; } = [];
    [JsonPropertyName("piores")] public List<ErroProduto> Piores { get; set; } = [];
    [JsonPropertyName("melhores")] public List<ErroProduto> Melhores { get; set; } = [];
}

public sealed class PrevisaoGeral
{
    [JsonPropertyName("calculado_em")] public DateTimeOffset CalculadoEm { get; set; }
    [JsonPropertyName("treino_ini")] public DateOnly? TreinoIni { get; set; }
    [JsonPropertyName("treino_fim")] public DateOnly? TreinoFim { get; set; }
    [JsonPropertyName("crescimento_ano")] public decimal? CrescimentoAno { get; set; }
    [JsonPropertyName("teste_erro_semana")] public decimal? ErroSemana { get; set; }
    [JsonPropertyName("teste_erro_ingenuo")] public decimal? ErroIngenuo { get; set; }
    [JsonPropertyName("teste_erro_media")] public decimal? ErroMedia { get; set; }
    [JsonPropertyName("teste_vies")] public decimal? Vies { get; set; }
    [JsonPropertyName("teste_ini")] public DateOnly? TesteIni { get; set; }
    [JsonPropertyName("teste_fim")] public DateOnly? TesteFim { get; set; }
}

public sealed class VendasPerdidas
{
    [JsonPropertyName("clientes")] public int Clientes { get; set; }
    [JsonPropertyName("produtos")] public int Produtos { get; set; }
}

public sealed class AnoTestado
{
    [JsonPropertyName("ano")] public int Ano { get; set; }
    [JsonPropertyName("erro")] public decimal? Erro { get; set; }
    [JsonPropertyName("erro_ingenuo")] public decimal? ErroIngenuo { get; set; }
    [JsonPropertyName("erro_media")] public decimal? ErroMedia { get; set; }
    [JsonPropertyName("vies")] public decimal? Vies { get; set; }
}

public sealed class ErroProduto
{
    [JsonPropertyName("produto_id")] public long ProdutoId { get; set; }
    [JsonPropertyName("produto")] public string Produto { get; set; } = "";
    [JsonPropertyName("erro")] public decimal Erro { get; set; }
    [JsonPropertyName("erro_ingenuo")] public decimal? ErroIngenuo { get; set; }
    [JsonPropertyName("vendido")] public decimal? Vendido { get; set; }
}

/// <summary>Pedido sugerido de um fornecedor (função pedido_sugerido).</summary>
public sealed class PedidoSugerido
{
    [JsonPropertyName("fornecedor_id")] public long FornecedorId { get; set; }
    [JsonPropertyName("fornecedor")] public string Fornecedor { get; set; } = "";
    [JsonPropertyName("envio_preferido")] public string? EnvioPreferido { get; set; }
    [JsonPropertyName("pedido_minimo")] public decimal? PedidoMinimo { get; set; }
    [JsonPropertyName("data_pedido")] public DateOnly DataPedido { get; set; }
    [JsonPropertyName("pedir_hoje")] public bool PedirHoje { get; set; }
    [JsonPropertyName("chegada")] public DateOnly Chegada { get; set; }
    [JsonPropertyName("proxima_entrega")] public DateOnly ProximaEntrega { get; set; }
    [JsonPropertyName("tem_pendente")] public bool TemPendente { get; set; }
    [JsonPropertyName("total")] public decimal Total { get; set; }
    [JsonPropertyName("itens")] public List<ItemSugerido> Itens { get; set; } = [];
}

public sealed class ItemSugerido
{
    [JsonPropertyName("produto_id")] public long ProdutoId { get; set; }
    [JsonPropertyName("nome")] public string Nome { get; set; } = "";
    [JsonPropertyName("categoria")] public string Categoria { get; set; } = "";
    [JsonPropertyName("unidade")] public string Unidade { get; set; } = "un";
    [JsonPropertyName("caixa")] public decimal Caixa { get; set; } = 1;
    [JsonPropertyName("estoque")] public decimal Estoque { get; set; }
    [JsonPropertyName("pendente")] public decimal Pendente { get; set; }
    /// <summary>Quanto deve vender de hoje até a entrega seguinte (ou até a validade).</summary>
    [JsonPropertyName("previsto")] public decimal Previsto { get; set; }
    [JsonPropertyName("margem")] public decimal Margem { get; set; }
    [JsonPropertyName("ate")] public DateOnly Ate { get; set; }
    [JsonPropertyName("eventos")] public string? Eventos { get; set; }
    [JsonPropertyName("sugerido")] public decimal Sugerido { get; set; }
    [JsonPropertyName("custo")] public decimal? Custo { get; set; }

    [JsonIgnore] public bool PorPeso => Unidade == "kg";
}

/// <summary>Um produto: semanas passadas, próximas semanas e o que o modelo aprendeu (função previsao_produto).</summary>
public sealed class PrevisaoProduto
{
    [JsonPropertyName("produto_id")] public long ProdutoId { get; set; }
    [JsonPropertyName("nome")] public string Nome { get; set; } = "";
    [JsonPropertyName("categoria")] public string Categoria { get; set; } = "";
    [JsonPropertyName("unidade")] public string Unidade { get; set; } = "un";
    [JsonPropertyName("situacao")] public string Situacao { get; set; } = "sem_vendas";
    [JsonPropertyName("media_dia")] public decimal? MediaDia { get; set; }
    [JsonPropertyName("crescimento_ano")] public decimal? CrescimentoAno { get; set; }
    [JsonPropertyName("parte_datas")] public decimal? ParteDatas { get; set; }
    [JsonPropertyName("idx_semana")] public List<decimal>? IdxSemana { get; set; }
    [JsonPropertyName("idx_mes")] public List<decimal>? IdxMes { get; set; }
    [JsonPropertyName("erro_teste")] public decimal? ErroTeste { get; set; }
    [JsonPropertyName("erro_ingenuo")] public decimal? ErroIngenuo { get; set; }
    [JsonPropertyName("passado")] public List<SemanaPassada> Passado { get; set; } = [];
    [JsonPropertyName("futuro")] public List<SemanaFutura> Futuro { get; set; } = [];
    [JsonPropertyName("datas")] public List<DataDoProduto> Datas { get; set; } = [];

    [JsonIgnore] public bool PorPeso => Unidade == "kg";
}

public sealed class SemanaPassada
{
    [JsonPropertyName("semana")] public DateOnly Semana { get; set; }
    [JsonPropertyName("vendido")] public decimal Vendido { get; set; }
    [JsonPropertyName("previsto")] public decimal Previsto { get; set; }
    [JsonPropertyName("tendencia")] public decimal Tendencia { get; set; }
    [JsonPropertyName("datas")] public decimal Datas { get; set; }
}

public sealed class SemanaFutura
{
    [JsonPropertyName("semana")] public DateOnly Semana { get; set; }
    [JsonPropertyName("previsto")] public decimal Previsto { get; set; }
    [JsonPropertyName("minimo")] public decimal Minimo { get; set; }
    [JsonPropertyName("maximo")] public decimal Maximo { get; set; }
    [JsonPropertyName("eventos")] public string? Eventos { get; set; }
}

public sealed class DataDoProduto
{
    [JsonPropertyName("evento")] public string Evento { get; set; } = "";
    [JsonPropertyName("a_mais")] public decimal AMais { get; set; }
}

/// <summary>Aviso da previsão (função previsao_alertas): falta, parado, queda, alta ou data especial chegando.</summary>
public sealed class AlertaPrevisao
{
    [JsonPropertyName("tipo")] public string Tipo { get; set; } = "";
    [JsonPropertyName("produto_id")] public long? ProdutoId { get; set; }
    [JsonPropertyName("produto")] public string Produto { get; set; } = "";
    [JsonPropertyName("texto")] public string Texto { get; set; } = "";
    [JsonPropertyName("dia")] public DateOnly? Dia { get; set; }
    [JsonPropertyName("pedir_ate")] public DateOnly? PedirAte { get; set; }
}

// ------------------------------------------------------------ Laboratório (só na versão de desenvolvimento, script 020)

/// <summary>O teste do último ano aberto por dia, semana ou mês (função previsao_laboratorio).</summary>
public sealed class LaboratorioPrevisao
{
    [JsonPropertyName("teste_ini")] public DateOnly? TesteIni { get; set; }
    [JsonPropertyName("teste_fim")] public DateOnly? TesteFim { get; set; }
    [JsonPropertyName("escopo")] public string Escopo { get; set; } = "todos";
    [JsonPropertyName("agrupar")] public string Agrupar { get; set; } = "semana";
    [JsonPropertyName("faixa_z")] public decimal FaixaZ { get; set; }
    [JsonPropertyName("nome")] public string? Nome { get; set; }
    /// <summary>"un", "kg", "vendas" (movimento da loja) ou "mistura" (vários produtos somados).</summary>
    [JsonPropertyName("unidade")] public string? Unidade { get; set; }
    [JsonPropertyName("resumo")] public LabResumo? Resumo { get; set; }
    [JsonPropertyName("serie")] public List<LabPeriodo> Serie { get; set; } = [];
    [JsonPropertyName("dia_semana")] public List<LabDiaSemana> DiaSemana { get; set; } = [];
    [JsonPropertyName("meses")] public List<LabMes> Meses { get; set; } = [];
    [JsonPropertyName("categorias")] public List<LabCategoria> Categorias { get; set; } = [];
    [JsonPropertyName("produtos")] public List<LabProduto> Produtos { get; set; } = [];
    [JsonPropertyName("eventos")] public List<LabEvento> Eventos { get; set; } = [];
    [JsonPropertyName("anos")] public List<LabAno> Anos { get; set; } = [];
    [JsonPropertyName("categorias_lista")] public List<string> CategoriasLista { get; set; } = [];
}

public sealed class LabResumo
{
    [JsonPropertyName("vendido")] public decimal? Vendido { get; set; }
    [JsonPropertyName("previsto")] public decimal? Previsto { get; set; }
    [JsonPropertyName("ingenuo")] public decimal? Ingenuo { get; set; }
    [JsonPropertyName("erro")] public decimal? Erro { get; set; }
    [JsonPropertyName("erro_ingenuo")] public decimal? ErroIngenuo { get; set; }
    [JsonPropertyName("erro_media")] public decimal? ErroMedia { get; set; }
    [JsonPropertyName("vies")] public decimal? Vies { get; set; }
    [JsonPropertyName("dentro_faixa")] public decimal? DentroFaixa { get; set; }
    [JsonPropertyName("erro_total")] public decimal? ErroTotal { get; set; }
    [JsonPropertyName("erro_total_ingenuo")] public decimal? ErroTotalIngenuo { get; set; }
    [JsonPropertyName("periodos")] public int Periodos { get; set; }
    [JsonPropertyName("periodos_ganhou")] public int PeriodosGanhou { get; set; }
    [JsonPropertyName("produtos")] public int Produtos { get; set; }
}

public sealed class LabPeriodo
{
    [JsonPropertyName("periodo")] public DateOnly Periodo { get; set; }
    [JsonPropertyName("ini")] public DateOnly? Ini { get; set; }
    [JsonPropertyName("fim")] public DateOnly? Fim { get; set; }
    [JsonPropertyName("abertos")] public int? Abertos { get; set; }
    [JsonPropertyName("vendido")] public decimal Vendido { get; set; }
    [JsonPropertyName("previsto")] public decimal Previsto { get; set; }
    [JsonPropertyName("minimo")] public decimal Minimo { get; set; }
    [JsonPropertyName("maximo")] public decimal Maximo { get; set; }
    [JsonPropertyName("ingenuo")] public decimal Ingenuo { get; set; }
    [JsonPropertyName("media60")] public decimal Media60 { get; set; }
    [JsonPropertyName("erro")] public decimal? Erro { get; set; }
    [JsonPropertyName("erro_ingenuo")] public decimal? ErroIngenuo { get; set; }
    [JsonPropertyName("eventos")] public string? Eventos { get; set; }
    [JsonPropertyName("anomalias")] public string? Anomalias { get; set; }

    [JsonIgnore] public bool ForaDaFaixa => Previsto > 0 && (Vendido < Minimo || Vendido > Maximo);
}

public sealed class LabDiaSemana
{
    [JsonPropertyName("dow")] public int Dow { get; set; }
    [JsonPropertyName("vendido")] public decimal Vendido { get; set; }
    [JsonPropertyName("previsto")] public decimal Previsto { get; set; }
    [JsonPropertyName("erro")] public decimal? Erro { get; set; }
    [JsonPropertyName("erro_ingenuo")] public decimal? ErroIngenuo { get; set; }
    [JsonPropertyName("vies")] public decimal? Vies { get; set; }
}

public sealed class LabMes
{
    [JsonPropertyName("mes")] public DateOnly Mes { get; set; }
    [JsonPropertyName("vendido")] public decimal Vendido { get; set; }
    [JsonPropertyName("previsto")] public decimal Previsto { get; set; }
    [JsonPropertyName("erro")] public decimal? Erro { get; set; }
    [JsonPropertyName("erro_ingenuo")] public decimal? ErroIngenuo { get; set; }
    [JsonPropertyName("erro_media")] public decimal? ErroMedia { get; set; }
    [JsonPropertyName("vies")] public decimal? Vies { get; set; }
}

public sealed class LabCategoria
{
    [JsonPropertyName("categoria")] public string Categoria { get; set; } = "";
    [JsonPropertyName("produtos")] public int Produtos { get; set; }
    [JsonPropertyName("vendido")] public decimal Vendido { get; set; }
    [JsonPropertyName("erro")] public decimal? Erro { get; set; }
    [JsonPropertyName("erro_ingenuo")] public decimal? ErroIngenuo { get; set; }
    [JsonPropertyName("vies")] public decimal? Vies { get; set; }
}

public sealed class LabProduto
{
    [JsonPropertyName("produto_id")] public long ProdutoId { get; set; }
    [JsonPropertyName("produto")] public string Produto { get; set; } = "";
    [JsonPropertyName("categoria")] public string Categoria { get; set; } = "";
    [JsonPropertyName("unidade")] public string Unidade { get; set; } = "un";
    [JsonPropertyName("vendido")] public decimal Vendido { get; set; }
    [JsonPropertyName("previsto")] public decimal Previsto { get; set; }
    [JsonPropertyName("erro")] public decimal? Erro { get; set; }
    [JsonPropertyName("erro_ingenuo")] public decimal? ErroIngenuo { get; set; }
    [JsonPropertyName("erro_media")] public decimal? ErroMedia { get; set; }
    [JsonPropertyName("vies")] public decimal? Vies { get; set; }
}

public sealed class LabEvento
{
    [JsonPropertyName("dia")] public DateOnly Dia { get; set; }
    [JsonPropertyName("evento")] public string Evento { get; set; } = "";
    [JsonPropertyName("vendido")] public decimal Vendido { get; set; }
    [JsonPropertyName("previsto")] public decimal Previsto { get; set; }
    [JsonPropertyName("ingenuo")] public decimal Ingenuo { get; set; }
}

public sealed class LabAno
{
    [JsonPropertyName("ano")] public int Ano { get; set; }
    [JsonPropertyName("erro")] public decimal? Erro { get; set; }
    [JsonPropertyName("erro_normal")] public decimal? ErroNormal { get; set; }
    [JsonPropertyName("erro_ingenuo")] public decimal? ErroIngenuo { get; set; }
    [JsonPropertyName("erro_media")] public decimal? ErroMedia { get; set; }
    [JsonPropertyName("mov_erro")] public decimal? MovErro { get; set; }
    [JsonPropertyName("vies")] public decimal? Vies { get; set; }
    [JsonPropertyName("dentro_faixa")] public decimal? DentroFaixa { get; set; }
    [JsonPropertyName("semanas_com_anomalia")] public int? SemanasComAnomalia { get; set; }
}
