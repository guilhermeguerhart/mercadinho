using System.Globalization;
using System.Text.Json.Serialization;

namespace MercadinhoSeuZe.Models;

/// <summary>Fornecedor (tabela fornecedores): contato e as categorias do estoque que ele entrega.</summary>
public sealed class Fornecedor
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("nome")] public string Nome { get; set; } = "";
    [JsonPropertyName("telefone")] public string Telefone { get; set; } = "";
    /// <summary>Número do WhatsApp quando é outro; null = o mesmo telefone.</summary>
    [JsonPropertyName("whatsapp")] public string? Whatsapp { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    /// <summary>Pessoa com quem falar (vendedor).</summary>
    [JsonPropertyName("contato")] public string? Contato { get; set; }
    /// <summary>O que entrega: categorias do estoque separadas por vírgula.</summary>
    [JsonPropertyName("categorias")] public string Categorias { get; set; } = "";
    /// <summary>Prazo padrão de entrega; o produto pode ter o seu (Produto.PrazoEntregaDias).</summary>
    [JsonPropertyName("prazo_entrega_dias")] public int? PrazoEntregaDias { get; set; }
    /// <summary>"whatsapp" ou "email": como os pedidos são enviados (o outro continua disponível).</summary>
    [JsonPropertyName("envio_preferido")] public string EnvioPreferido { get; set; } = "whatsapp";
    /// <summary>Mensagem de pedido; null = modelo padrão (MensagemPedido.ModeloPadrao).</summary>
    [JsonPropertyName("modelo_mensagem")] public string? ModeloMensagem { get; set; }
    [JsonPropertyName("observacao")] public string? Observacao { get; set; }
    [JsonPropertyName("ativo")] public bool Ativo { get; set; } = true;
    /// <summary>Dias da semana em que entrega: "1,3" = segunda e quarta (0 = domingo); null = qualquer dia.</summary>
    [JsonPropertyName("dias_entrega")] public string? DiasEntrega { get; set; }
    /// <summary>Valor mínimo do pedido em R$; null = sem mínimo.</summary>
    [JsonPropertyName("pedido_minimo")] public decimal? PedidoMinimo { get; set; }

    public static readonly string[] NomesDias = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];

    public IReadOnlyList<DayOfWeek> ListaDiasEntrega =>
        (DiasEntrega ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(d => (DayOfWeek)int.Parse(d, CultureInfo.InvariantCulture)).ToList();

    /// <summary>"seg, qua" ou "qualquer dia".</summary>
    public string TextoDiasEntrega => ListaDiasEntrega.Count == 0 ? "qualquer dia" : string.Join(", ", ListaDiasEntrega.Select(d => NomesDias[(int)d]));

    /// <summary>Primeiro dia de entrega do fornecedor a partir da data (a própria data se ele entrega qualquer dia).</summary>
    public DateOnly ProximaEntrega(DateOnly aPartirDe)
    {
        var dias = ListaDiasEntrega;
        if (dias.Count == 0) return aPartirDe;
        var d = aPartirDe;
        while (!dias.Contains(d.DayOfWeek)) d = d.AddDays(1);
        return d;
    }

    /// <summary>Entrega de um pedido feito hoje com este prazo: hoje + prazo, no próximo dia de entrega.</summary>
    public DateOnly PrevisaoParaPrazo(int prazo) => ProximaEntrega(DateOnly.FromDateTime(DateTime.Today).AddDays(prazo));

    /// <summary>
    /// Hoje é o último dia para pedir e ainda pegar a próxima entrega? (o caminhão vem em dias certos; amanhã já
    /// cairia na entrega seguinte). Só vale para quem tem dias de entrega cadastrados.
    /// </summary>
    public DateOnly? UltimoDiaHoje()
    {
        if (ListaDiasEntrega.Count == 0) return null;
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var entregaHoje = PrevisaoParaPrazo(Prazo);
        var entregaAmanha = ProximaEntrega(hoje.AddDays(1 + Prazo));
        return entregaAmanha > entregaHoje ? entregaHoje : null;
    }

    /// <summary>Prazo usado na previsão do pedido quando não há um cadastrado.</summary>
    public const int PrazoPadrao = 3;

    public int Prazo => PrazoEntregaDias ?? PrazoPadrao;

    /// <summary>Prazo de entrega do produto com este fornecedor: o do produto ou, sem ele, o do fornecedor.</summary>
    public int PrazoDe(Produto p) => p.PrazoEntregaDias ?? Prazo;

    public bool PrefereEmail => EnvioPreferido == "email" && !string.IsNullOrWhiteSpace(Email);

    /// <summary>
    /// Preenche Produto.PrazoDoFornecedor (para o ponto de pedido): o fornecedor ligado ao produto ou, sem ele,
    /// o primeiro (A-Z) ativo que entrega a categoria.
    /// </summary>
    public static void AplicarPrazos(IEnumerable<Produto> produtos, IReadOnlyList<Fornecedor> fornecedores)
    {
        var ativos = fornecedores.Where(f => f.Ativo).OrderBy(f => f.Nome, StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), true)).ToList();
        foreach (var p in produtos)
        {
            var f = fornecedores.FirstOrDefault(x => x.Id == p.FornecedorId) ?? ativos.FirstOrDefault(x => p.FornecedorId is null && x.Fornece(p));
            p.PrazoDoFornecedor = f?.Prazo ?? PrazoPadrao;
        }
    }

    public IReadOnlyList<string> ListaCategorias =>
        Categorias.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Número para o link do WhatsApp (wa.me), com o 55 do Brasil.</summary>
    public string NumeroWhatsapp
    {
        get
        {
            var d = SoDigitos(Whatsapp ?? Telefone);
            return d.Length is 10 or 11 ? "55" + d : d;
        }
    }

    public string Modelo => string.IsNullOrWhiteSpace(ModeloMensagem) ? MensagemPedido.ModeloPadrao : ModeloMensagem;

    /// <summary>
    /// O produto é deste fornecedor: ligado a ele no cadastro ou, sem fornecedor ligado, da categoria que ele
    /// entrega (mesma regra do site de referência).
    /// </summary>
    public bool Fornece(Produto p) =>
        p.FornecedorId == Id || p.Vinculos.Any(v => v.FornecedorId == Id) ||
        (p.FornecedorId is null && ListaCategorias.Any(c => MesmoTexto(c, p.Categoria)));

    public static string SoDigitos(string texto) => new(texto.Where(char.IsDigit).ToArray());

    /// <summary>"(11) 4002-1122" ou "(11) 98888-7777", igual à máscara do site.</summary>
    public static string MascaraTelefone(string texto)
    {
        var n = SoDigitos(texto);
        if (n.Length > 11) n = n[..11];
        if (n.Length <= 2) return n.Length > 0 ? "(" + n : "";
        if (n.Length <= 6) return $"({n[..2]}) {n[2..]}";
        if (n.Length <= 10) return $"({n[..2]}) {n[2..6]}-{n[6..]}";
        return $"({n[..2]}) {n[2..7]}-{n[7..]}";
    }

    public static bool MesmoTexto(string a, string b) =>
        string.Compare(a.Trim(), b.Trim(), CultureInfo.GetCultureInfo("pt-BR"), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;
}

/// <summary>Um produto dentro do pedido. Nome e unidade ficam guardados para o histórico.</summary>
public sealed class ItemPedido
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("produto_id")] public long? ProdutoId { get; set; }
    [JsonPropertyName("nome_produto")] public string NomeProduto { get; set; } = "";
    [JsonPropertyName("unidade_medida")] public string UnidadeMedida { get; set; } = "un";
    [JsonPropertyName("quantidade")] public decimal Quantidade { get; set; }
    [JsonPropertyName("quantidade_recebida")] public decimal? QuantidadeRecebida { get; set; }
    [JsonPropertyName("preco_custo")] public decimal? PrecoCusto { get; set; }
    /// <summary>Por que chegou diferente: faltou, avariado, vencido, errado ou outro.</summary>
    [JsonPropertyName("motivo_diferenca")] public string? MotivoDiferenca { get; set; }

    public static readonly (string Id, string Nome)[] Motivos =
        [("faltou", "Faltou"), ("avariado", "Avariado"), ("vencido", "Vencido"), ("errado", "Produto errado"), ("outro", "Outro motivo")];

    public string? NomeMotivo => Motivos.FirstOrDefault(m => m.Id == MotivoDiferenca).Nome;

    public bool PorPeso => UnidadeMedida == "kg";

    /// <summary>"20 un." ou "12,5 kg".</summary>
    public string QuantidadeCurta(decimal q) =>
        q.ToString(PorPeso ? "0.###" : "0", CultureInfo.GetCultureInfo("pt-BR")) + (PorPeso ? " kg" : " un.");
}

/// <summary>Pedido de reposição para um fornecedor (tabela pedidos, com os itens).</summary>
public sealed class Pedido
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("fornecedor_id")] public long FornecedorId { get; set; }
    [JsonPropertyName("data_pedido")] public DateOnly Data { get; set; }
    [JsonPropertyName("previsao_entrega")] public DateOnly? Previsao { get; set; }
    /// <summary>"pendente", "entregue" ou "cancelado".</summary>
    [JsonPropertyName("situacao")] public string Situacao { get; set; } = "pendente";
    [JsonPropertyName("entregue_em")] public DateOnly? EntregueEm { get; set; }
    [JsonPropertyName("observacao")] public string? Observacao { get; set; }
    [JsonPropertyName("itens")] public List<ItemPedido> Itens { get; set; } = [];
    [JsonPropertyName("numero_nota")] public string? NumeroNota { get; set; }
    /// <summary>Pedido criado com o que faltou deste outro pedido.</summary>
    [JsonPropertyName("pedido_origem_id")] public long? PedidoOrigemId { get; set; }

    /// <summary>Soma de quantidade × custo; null se algum item está sem custo.</summary>
    public decimal? Total => Itens.Count > 0 && Itens.All(i => i.PrecoCusto is not null)
        ? Itens.Sum(i => (i.QuantidadeRecebida ?? i.Quantidade) * i.PrecoCusto!.Value)
        : null;

    public bool Pendente => Situacao == "pendente";
    public bool Entregue => Situacao == "entregue";
    public bool Cancelado => Situacao == "cancelado";

    /// <summary>Pendente com a previsão de entrega já passada (igual a pedidoAtrasado do site).</summary>
    public bool Atrasado => Pendente && Previsao < DateOnly.FromDateTime(DateTime.Today);

    /// <summary>"20 un. Café 500g, 12,5 kg Alcatra".</summary>
    public string DescreverItens() => string.Join(", ", Itens.Select(i => $"{i.QuantidadeCurta(i.Quantidade)} {i.NomeProduto}"));
}

/// <summary>Situação do fornecedor na lista (igual a statusFornecedor do site).</summary>
public enum SituacaoFornecedor { SemPedidos, EmDia, Pendente, Atrasado }

/// <summary>Monta a mensagem de pedido a partir do modelo do fornecedor.</summary>
public static class MensagemPedido
{
    public const string ModeloPadrao =
        "Olá{, contato}! Aqui é {usuario}, do Mercado.\n" +
        "Gostaria de fazer um pedido:\n" +
        "{itens}\n" +
        "Entrega prevista: {previsao}.\n" +
        "Pode confirmar, por favor? Obrigado!";

    /// <summary>Texto que explica os campos do modelo, para a janela de cadastro.</summary>
    public const string Campos = "{contato} vira o nome do contato (\"{, contato}\" põe a vírgula só se houver contato), " +
                                 "{fornecedor} o nome do fornecedor, {itens} a lista de produtos, {previsao} a data de entrega " +
                                 "e {usuario} o seu nome.";

    public sealed record Linha(string Nome, decimal Quantidade, bool PorPeso, decimal? PorCaixa);

    public static string Montar(Fornecedor f, IEnumerable<Linha> itens, DateOnly? previsao, string usuario)
    {
        var ptBr = CultureInfo.GetCultureInfo("pt-BR");
        var lista = string.Join("\n", itens.Select(i =>
        {
            var q = i.Quantidade.ToString(i.PorPeso ? "0.###" : "0", ptBr) + (i.PorPeso ? " kg" : " un.");
            // Por unidade, quando fecha caixas inteiras, diz quantas caixas são
            var caixas = !i.PorPeso && i.PorCaixa is > 1 && i.Quantidade % i.PorCaixa.Value == 0
                ? $" ({i.Quantidade / i.PorCaixa.Value:0} {(i.Quantidade / i.PorCaixa.Value == 1 ? "caixa" : "caixas")})"
                : "";
            return $"- {q} {i.Nome}{caixas}";
        }));
        var contato = f.Contato?.Trim() ?? "";
        return f.Modelo
            .Replace("{, contato}", contato.Length > 0 ? ", " + contato : "")
            .Replace("{contato}", contato)
            .Replace("{fornecedor}", f.Nome)
            .Replace("{itens}", lista)
            .Replace("{previsao}", previsao?.ToString("dd/MM/yyyy", ptBr) ?? "a combinar")
            .Replace("{usuario}", usuario);
    }

    /// <summary>Mensagem do contato rápido (sem pedido).</summary>
    public static string Saudacao(Fornecedor f, string usuario) =>
        $"Olá{(string.IsNullOrWhiteSpace(f.Contato) ? "" : ", " + f.Contato.Trim())}! Aqui é {usuario}, do Mercado.";
}

/// <summary>Produto também comprado deste fornecedor (tabela produto_fornecedores).</summary>
public sealed class ProdutoFornecedor
{
    [JsonPropertyName("fornecedor_id")] public long FornecedorId { get; set; }
    [JsonPropertyName("ultimo_custo")] public decimal? UltimoCusto { get; set; }
    [JsonPropertyName("ultima_compra")] public DateOnly? UltimaCompra { get; set; }
}
