using System.Globalization;
using System.Text.Json.Serialization;

namespace MercadinhoSeuZe.Models;

/// <summary>Caixa aberto por um funcionário num dia (tabela caixas), com o troco inicial.</summary>
public sealed class Caixa
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("operador_id")] public Guid OperadorId { get; set; }
    [JsonPropertyName("operador_nome")] public string OperadorNome { get; set; } = "";
    [JsonPropertyName("aberto_em")] public DateTimeOffset AbertoEm { get; set; }
    [JsonPropertyName("troco_inicial")] public decimal TrocoInicial { get; set; }
    [JsonPropertyName("fechado_em")] public DateTimeOffset? FechadoEm { get; set; }
    /// <summary>Fechamento (script 008): dinheiro contado na gaveta, o que deveria ter e a diferença (+ sobra, − falta).</summary>
    [JsonPropertyName("fechado_contado")] public decimal? FechadoContado { get; set; }
    [JsonPropertyName("fechado_esperado")] public decimal? FechadoEsperado { get; set; }
    [JsonPropertyName("fechado_diferenca")] public decimal? FechadoDiferenca { get; set; }
    [JsonPropertyName("fechado_obs")] public string? FechadoObs { get; set; }
    [JsonPropertyName("fechado_por")] public string? FechadoPor { get; set; }

    public bool Fechado => FechadoEm is not null;

    public string Hora => AbertoEm.ToLocalTime().ToString("HH:mm");

    public string HoraFechamento => FechadoEm?.ToLocalTime().ToString("HH:mm") ?? "";
}

/// <summary>Resposta de resumo_caixa, registrar_movimento e fechar_caixa (script 008).</summary>
public sealed class ResumoCaixa
{
    [JsonPropertyName("caixa_id")] public long CaixaId { get; set; }
    [JsonPropertyName("operador_nome")] public string OperadorNome { get; set; } = "";
    [JsonPropertyName("aberto_em")] public DateTimeOffset AbertoEm { get; set; }
    [JsonPropertyName("fechado_em")] public DateTimeOffset? FechadoEm { get; set; }
    [JsonPropertyName("troco_inicial")] public decimal TrocoInicial { get; set; }
    [JsonPropertyName("vendas")] public int Vendas { get; set; }
    [JsonPropertyName("total_vendas")] public decimal TotalVendas { get; set; }
    [JsonPropertyName("descontos")] public decimal Descontos { get; set; }
    [JsonPropertyName("promocoes")] public decimal Promocoes { get; set; }
    [JsonPropertyName("canceladas")] public int Canceladas { get; set; }
    [JsonPropertyName("em_espera")] public int EmEspera { get; set; }
    /// <summary>Total recebido por forma de pagamento ("dinheiro" → 350,00...).</summary>
    [JsonPropertyName("formas")] public Dictionary<string, decimal> Formas { get; set; } = [];
    [JsonPropertyName("suprimentos")] public decimal Suprimentos { get; set; }
    [JsonPropertyName("sangrias")] public decimal Sangrias { get; set; }
    [JsonPropertyName("movimentos")] public List<MovimentoCaixa> Movimentos { get; set; } = [];
    /// <summary>Dinheiro que deveria estar na gaveta agora.</summary>
    [JsonPropertyName("esperado")] public decimal Esperado { get; set; }
    [JsonPropertyName("contado")] public decimal? Contado { get; set; }
    [JsonPropertyName("diferenca")] public decimal? Diferenca { get; set; }
    [JsonPropertyName("obs")] public string? Obs { get; set; }
    [JsonPropertyName("fechado_por")] public string? FechadoPor { get; set; }

    public decimal Dinheiro => Formas.GetValueOrDefault("dinheiro");
}

/// <summary>Sangria (dinheiro tirado da gaveta para o cofre) ou suprimento (troco colocado na gaveta).</summary>
public sealed class MovimentoCaixa
{
    [JsonPropertyName("tipo")] public string Tipo { get; set; } = "sangria";
    [JsonPropertyName("valor")] public decimal Valor { get; set; }
    [JsonPropertyName("motivo")] public string? Motivo { get; set; }
    [JsonPropertyName("autorizado_por")] public string? AutorizadoPor { get; set; }
    [JsonPropertyName("criado_em")] public DateTimeOffset CriadoEm { get; set; }

    public bool Sangria => Tipo == "sangria";
    public string Hora => CriadoEm.ToLocalTime().ToString("HH:mm");
}

/// <summary>Venda (tabela vendas): aberta enquanto o cliente está no caixa, depois paga ou cancelada.</summary>
public sealed class Venda
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("caixa_id")] public long CaixaId { get; set; }
    [JsonPropertyName("operador_id")] public Guid OperadorId { get; set; }
    [JsonPropertyName("operador_nome")] public string OperadorNome { get; set; } = "";
    [JsonPropertyName("situacao")] public string Situacao { get; set; } = "aberta";
    [JsonPropertyName("forma_pagamento")] public string? FormaPagamento { get; set; }
    [JsonPropertyName("total")] public decimal Total { get; set; }
    [JsonPropertyName("recebido")] public decimal? Recebido { get; set; }
    [JsonPropertyName("troco")] public decimal? Troco { get; set; }
    [JsonPropertyName("criada_em")] public DateTimeOffset CriadaEm { get; set; }
    [JsonPropertyName("paga_em")] public DateTimeOffset? PagaEm { get; set; }
    [JsonPropertyName("cancelada_por")] public string? CanceladaPor { get; set; }
    /// <summary>CPF (11 dígitos) ou CNPJ (14) para a nota; null = sem.</summary>
    [JsonPropertyName("cpf_nota")] public string? CpfNota { get; set; }
    [JsonPropertyName("desconto")] public decimal Desconto { get; set; }
    [JsonPropertyName("desconto_autorizado_por")] public string? DescontoAutorizadoPor { get; set; }
    [JsonPropertyName("venda_itens")] public List<ItemVenda> Itens { get; set; } = [];
    [JsonPropertyName("venda_pagamentos")] public List<PagamentoVenda> Pagamentos { get; set; } = [];

    public bool Paga => Situacao == "paga";
    public bool Cancelada => Situacao == "cancelada";

    /// <summary>Hora do pagamento (ou da criação, se não foi paga).</summary>
    public string Hora => (PagaEm ?? CriadaEm).ToLocalTime().ToString("HH:mm");

    /// <summary>"2 un. Café 500g, 0,75 kg Alcatra".</summary>
    public string ResumoItens => string.Join(", ", Itens.Select(i => $"{i.Numero} {i.Sigla} {i.NomeProduto}"));

    /// <summary>"Dinheiro + Débito" (ou a forma única).</summary>
    public string ResumoFormas => Pagamentos.Count > 0
        ? string.Join(" + ", Pagamentos.Select(p => Models.FormaPagamento.Curto(p.Forma)).Distinct())
        : Models.FormaPagamento.Nome(FormaPagamento);
}

/// <summary>
/// Uma parte do pagamento (tabela venda_pagamentos). Cartão, vale e Pix guardam o que a maquininha devolveu.
/// Antes de finalizar, fica só na tela do caixa.
/// </summary>
public sealed class PagamentoVenda
{
    [JsonPropertyName("forma")] public string Forma { get; set; } = "dinheiro";
    /// <summary>Quanto desta forma foi para a venda.</summary>
    [JsonPropertyName("valor")] public decimal Valor { get; set; }
    /// <summary>Dinheiro entregue pelo cliente (só dinheiro).</summary>
    [JsonPropertyName("recebido")] public decimal? Recebido { get; set; }
    [JsonPropertyName("troco")] public decimal Troco { get; set; }
    [JsonPropertyName("bandeira")] public string? Bandeira { get; set; }
    [JsonPropertyName("nsu")] public string? Nsu { get; set; }
    [JsonPropertyName("autorizacao")] public string? Autorizacao { get; set; }
    [JsonPropertyName("parcelas")] public int? Parcelas { get; set; }
    [JsonPropertyName("final_cartao")] public string? FinalCartao { get; set; }

    /// <summary>"Visa •••• 1234 · 3x · NSU 123456".</summary>
    [JsonIgnore] public string Detalhe
    {
        get
        {
            var partes = new List<string>();
            if (Bandeira is not null) partes.Add(Bandeira + (FinalCartao is null ? "" : $" •••• {FinalCartao}"));
            if (Parcelas is > 1) partes.Add($"{Parcelas}x");
            if (Nsu is not null) partes.Add($"NSU {Nsu}");
            return string.Join(" · ", partes);
        }
    }
}

/// <summary>Produto dentro de uma venda (tabela venda_itens), com o preço da hora da venda.</summary>
public sealed class ItemVenda
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("produto_id")] public long? ProdutoId { get; set; }
    [JsonPropertyName("nome_produto")] public string NomeProduto { get; set; } = "";
    [JsonPropertyName("unidade_medida")] public string UnidadeMedida { get; set; } = "un";
    [JsonPropertyName("quantidade")] public decimal Quantidade { get; set; }
    [JsonPropertyName("preco_unitario")] public decimal PrecoUnitario { get; set; }
    [JsonPropertyName("subtotal")] public decimal Subtotal { get; set; }
    /// <summary>Subtotal sem a promoção (script 008); null em vendas antigas.</summary>
    [JsonPropertyName("subtotal_normal")] public decimal? SubtotalNormal { get; set; }
    [JsonPropertyName("desconto_promocao")] public decimal DescontoPromocao { get; set; }
    /// <summary>"Promoção", "Validade −30%" ou os dois; null sem promoção.</summary>
    [JsonPropertyName("promocao")] public string? Promocao { get; set; }

    public bool EmPromocao => DescontoPromocao > 0;

    public bool PorPeso => UnidadeMedida == "kg";
    public string Numero => Quantidade.ToString(PorPeso ? "0.000" : "0", PtBr);
    public string Sigla => PorPeso ? "kg" : "un.";
}

/// <summary>Formas de pagamento do caixa, na ordem dos botões e dos atalhos (F5 a F9, Shift+F9).</summary>
public static class FormaPagamento
{
    public static readonly (string Id, string Nome, string Curto, string Tecla)[] Todas =
    [
        ("dinheiro", "Dinheiro", "Dinheiro", "F5"),
        ("pix", "Pix", "Pix", "F6"),
        ("debito", "Cartão de débito", "Débito", "F7"),
        ("credito", "Cartão de crédito", "Crédito", "F8"),
        ("vale_alimentacao", "Vale-alimentação", "Vale-alim.", "F9"),
        ("vale_refeicao", "Vale-refeição", "Vale-ref.", "Shift+F9"),
    ];

    public static string Nome(string? id) => id == "varias" ? "Várias formas" : Todas.FirstOrDefault(f => f.Id == id).Nome ?? id ?? "";

    public static string Curto(string? id) => Todas.FirstOrDefault(f => f.Id == id).Curto ?? Nome(id);

    /// <summary>Passa pela maquininha (cartão, vale e Pix); dinheiro não.</summary>
    public static bool UsaMaquininha(string id) => id != "dinheiro";
}

/// <summary>Resposta de reservar_item: a venda aberta e como o item ficou (item_id null = saiu da venda).</summary>
public sealed record ResultadoReserva(
    [property: JsonPropertyName("venda_id")] long VendaId,
    [property: JsonPropertyName("item_id")] long? ItemId,
    [property: JsonPropertyName("quantidade")] decimal Quantidade,
    [property: JsonPropertyName("subtotal")] decimal Subtotal)
{
    /// <summary>Subtotal sem promoção (script 008; null antes dele).</summary>
    [JsonPropertyName("normal")] public decimal? Normal { get; init; }
    [JsonPropertyName("promocao")] public string? Promocao { get; init; }
}

/// <summary>Resposta de finalizar_venda.</summary>
public sealed record ResultadoVenda(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("total")] decimal Total,
    [property: JsonPropertyName("troco")] decimal Troco);

/// <summary>O que o leitor leu no caixa: um produto (com o valor da etiqueta da balança, se for o caso).</summary>
public sealed record Leitura(Produto Produto, decimal? ValorEtiqueta = null)
{
    /// <summary>Peso da etiqueta: valor impresso ÷ preço por kg (3 casas).</summary>
    public decimal? PesoEtiqueta => ValorEtiqueta is { } v && Produto.Preco > 0 ? Math.Round(v / Produto.Preco, 3, MidpointRounding.AwayFromZero) : null;
}

/// <summary>Códigos de barras que o caixa entende.</summary>
public static class CodigoBarras
{
    /// <summary>
    /// Acha o produto pelo código lido: o da caixa (DUN-14) igual ao cadastrado, ou o da unidade (EAN-13), que é o
    /// DUN-14 sem o primeiro dígito e com outro dígito verificador (mesma regra do site). Etiqueta da balança
    /// (EAN-13 que começa com 2) vem com o valor impresso.
    /// </summary>
    public static Leitura? Ler(IEnumerable<Produto> produtos, string codigo)
    {
        codigo = codigo.Trim();
        var lista = produtos as IReadOnlyCollection<Produto> ?? produtos.ToList();
        if (lista.FirstOrDefault(p => p.CodigoBarrasCaixa?.Trim() == codigo) is { } exato) return new(exato);
        if (codigo.Length == 13 && codigo.All(char.IsAsciiDigit))
        {
            if (lista.FirstOrDefault(p => p.CodigoBarrasCaixa?.Trim() is { Length: 14 } dun && dun.All(char.IsAsciiDigit)
                                          && dun[1..13] == codigo[..12]) is { } unidade)
                return new(unidade);
            if (Etiqueta(lista, codigo) is { } etiqueta) return etiqueta;
        }
        return null;
    }

    /// <summary>
    /// Etiqueta da balança com preço (padrão das balanças brasileiras): "2" + código do produto + valor em centavos +
    /// dígito verificador. Aceita código de 4 dígitos ("2 CCCC 0 VVVVVV D") e de 5 ("2 CCCCC 0 VVVVV D").
    /// </summary>
    public static Leitura? Etiqueta(IEnumerable<Produto> produtos, string codigo)
    {
        if (codigo.Length != 13 || codigo[0] != '2' || !codigo.All(char.IsAsciiDigit) || !DigitoOk(codigo)) return null;
        var valor = int.Parse(codigo[6..12], CultureInfo.InvariantCulture) / 100m;
        if (valor <= 0) return null;
        var porPeso = produtos.Where(p => p.CodigoBalanca is not null).ToList();
        var quatro = int.Parse(codigo[1..5], CultureInfo.InvariantCulture);
        var cinco = int.Parse(codigo[1..6], CultureInfo.InvariantCulture);
        // Com "0" depois de 4 dígitos, o código mais provável é o de 4
        var produto = codigo[5] == '0'
            ? porPeso.FirstOrDefault(p => p.CodigoBalanca == quatro) ?? porPeso.FirstOrDefault(p => p.CodigoBalanca == cinco)
            : porPeso.FirstOrDefault(p => p.CodigoBalanca == cinco);
        return produto is null ? null : new Leitura(produto, valor);
    }

    /// <summary>Confere o dígito verificador do EAN-13.</summary>
    public static bool DigitoOk(string ean13)
    {
        var soma = 0;
        for (var i = 0; i < 12; i++) soma += (ean13[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - soma % 10) % 10 == ean13[12] - '0';
    }

    /// <summary>Monta o EAN-13 com o dígito verificador a partir dos 12 primeiros dígitos.</summary>
    public static string ComDigito(string doze)
    {
        var soma = 0;
        for (var i = 0; i < 12; i++) soma += (doze[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return doze + (10 - soma % 10) % 10;
    }
}

/// <summary>CPF e CNPJ da nota: só dígitos, com os dígitos verificadores conferidos.</summary>
public static class Documento
{
    public static string Digitos(string texto) => new(texto.Where(char.IsAsciiDigit).ToArray());

    public static bool Valido(string texto)
    {
        var d = Digitos(texto);
        if (d.Length == 11) return d.Distinct().Count() > 1 && Dv(d, 9, 10) == d[9] - '0' && Dv(d, 10, 11) == d[10] - '0';
        if (d.Length == 14) return d.Distinct().Count() > 1 && DvCnpj(d, 12) == d[12] - '0' && DvCnpj(d, 13) == d[13] - '0';
        return false;
    }

    /// <summary>"123.456.789-09" ou "12.345.678/0001-90".</summary>
    public static string Formatar(string digitos) => digitos.Length == 11
        ? $"{digitos[..3]}.{digitos[3..6]}.{digitos[6..9]}-{digitos[9..]}"
        : digitos.Length == 14 ? $"{digitos[..2]}.{digitos[2..5]}.{digitos[5..8]}/{digitos[8..12]}-{digitos[12..]}" : digitos;

    private static int Dv(string d, int n, int peso)
    {
        var soma = 0;
        for (var i = 0; i < n; i++) soma += (d[i] - '0') * (peso - i);
        var resto = soma * 10 % 11;
        return resto == 10 ? 0 : resto;
    }

    private static int DvCnpj(string d, int n)
    {
        int[] pesos = n == 12 ? [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2] : [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        var soma = 0;
        for (var i = 0; i < n; i++) soma += (d[i] - '0') * pesos[i];
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
