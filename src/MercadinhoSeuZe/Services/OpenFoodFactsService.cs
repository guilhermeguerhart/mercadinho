using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MercadinhoSeuZe.Services;

/// <summary>Produto encontrado num banco aberto pelo código de barras (só sugestão para o cadastro).</summary>
public sealed record ProdutoEncontrado(string Nome, string? Categoria, string Fonte, string CodigoConsultado);

/// <summary>
/// Procura o código de barras no Open Food Facts (alimentos e bebidas) e, se não achar, no Open Beauty Facts
/// (higiene) e no Open Products Facts (outros). São bancos abertos e gratuitos, sem chave.
/// O código da caixa (14 dígitos) é convertido no código da unidade (13 dígitos), que é o que esses bancos guardam.
/// Qualquer falha (sem internet, demora, não encontrado) devolve null e o cadastro segue normal.
/// </summary>
public sealed class OpenFoodFactsService
{
    private static readonly (string Site, string Fonte, string? CategoriaPadrao)[] Bancos =
    [
        ("https://world.openfoodfacts.org", "Open Food Facts", null),
        ("https://world.openbeautyfacts.org", "Open Beauty Facts", "Higiene"),
        ("https://world.openproductsfacts.org", "Open Products Facts", null),
    ];

    private readonly HttpClient _http;

    public OpenFoodFactsService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        // Esses sites pedem que cada programa se identifique
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MercadoSistemaGestao/0.1 (github.com/guilhermeguerhart/mercadinho)");
    }

    public async Task<ProdutoEncontrado?> ProcurarAsync(string codigo)
    {
        foreach (var candidato in CodigosParaConsultar(codigo))
            foreach (var (site, fonte, categoriaPadrao) in Bancos)
            {
                Resposta? resposta;
                try
                {
                    resposta = await _http.GetFromJsonAsync<Resposta>(
                        $"{site}/api/v2/product/{candidato}.json?lc=pt&fields=product_name,product_name_pt,brands,quantity,categories_tags");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
                {
                    continue;
                }
                if (resposta is not { Status: 1, Produto: { } p }) continue;

                var nome = MontarNome(p);
                if (nome.Length == 0) continue;
                return new ProdutoEncontrado(nome, Categoria(p.Categorias) ?? CategoriaPeloNome(nome) ?? categoriaPadrao, fonte, candidato);
            }
        return null;
    }

    /// <summary>
    /// O próprio código e, se for código de caixa (DUN-14 / GTIN-14), o código da unidade que vem dentro dele:
    /// tira o primeiro dígito (indicador da embalagem) e recalcula o dígito verificador.
    /// </summary>
    public static IEnumerable<string> CodigosParaConsultar(string codigo)
    {
        codigo = codigo.Trim();
        if (codigo.Length == 0 || !codigo.All(char.IsAsciiDigit)) yield break;
        yield return codigo;
        if (codigo.Length == 14)
        {
            var base12 = codigo.Substring(1, 12);
            var unidade = base12 + DigitoVerificador(base12);
            if (unidade != codigo) yield return unidade;
        }
    }

    /// <summary>Dígito verificador GS1 (EAN/GTIN).</summary>
    public static char DigitoVerificador(string semDigito)
    {
        var soma = 0;
        for (var i = 0; i < semDigito.Length; i++)
        {
            // Da direita para a esquerda, o primeiro peso é 3
            var peso = (semDigito.Length - i) % 2 == 1 ? 3 : 1;
            soma += (semDigito[i] - '0') * peso;
        }
        return (char)('0' + (10 - soma % 10) % 10);
    }

    /// <summary>"Leite Condensado Integral Moça 395 g": nome + marca (se não estiver no nome) + quantidade.</summary>
    private static string MontarNome(ProdutoOff p)
    {
        var nome = (p.NomePt is { Length: > 0 } pt ? pt : p.Nome ?? "").Trim();
        if (nome.Length == 0) return "";
        nome = char.ToUpper(nome[0]) + nome[1..];
        var marca = p.Marcas?.Split(',')[0].Trim();
        if (marca is { Length: > 0 } && !nome.Contains(marca, StringComparison.CurrentCultureIgnoreCase))
            nome += " " + marca;
        var quantidade = p.Quantidade?.Trim();
        if (quantidade is { Length: > 0 } && !nome.Contains(quantidade, StringComparison.CurrentCultureIgnoreCase))
            nome += " " + quantidade;
        return nome.Length > 80 ? nome[..80].Trim() : nome;
    }

    /// <summary>Converte as categorias do Open Food Facts (em inglês) nas categorias do mercado.</summary>
    private static string? Categoria(string[]? tags)
    {
        if (tags is null) return null;
        bool Tem(params string[] partes) => tags.Any(t => partes.Any(t.Contains));
        if (Tem("beverages", "waters", "juices", "sodas", "beers", "wines")) return "Bebidas";
        if (Tem("sausages", "hams", "cold-cuts", "salamis")) return "Frios";
        if (Tem("dairies", "milks", "cheeses", "yogurts", "butters")) return "Laticínios";
        if (Tem("frozen")) return "Congelados";
        if (Tem("meats", "poultry", "fishes")) return "Açougue";
        if (Tem("breads", "cakes")) return "Padaria";
        if (Tem("rices", "legumes", "beans", "cereal-grains", "lentils")) return "Grãos";
        if (Tem("fresh-fruits", "fresh-vegetables")) return "Hortifrúti";
        if (Tem("pet-food", "dog-food", "cat-food")) return "Pet";
        if (Tem("en:")) return "Mercearia";
        return null;
    }

    /// <summary>Quando o banco não traz categoria, tenta pelo nome (ex.: "Refrigerante ..." vira Bebidas).</summary>
    private static string? CategoriaPeloNome(string nome)
    {
        var n = nome.ToLowerInvariant();
        (string Categoria, string[] Palavras)[] regras =
        [
            ("Bebidas", ["refrigerante", "suco", "água", "agua", "cerveja", "vinho", "energético", "chá "]),
            ("Laticínios", ["leite", "iogurte", "queijo", "manteiga", "requeijão", "creme de leite"]),
            ("Frios", ["presunto", "salsicha", "mortadela", "salame", "peito de peru"]),
            ("Grãos", ["arroz", "feijão", "lentilha", "grão-de-bico", "milho de pipoca"]),
            ("Limpeza", ["sabão", "detergente", "desinfetante", "amaciante", "água sanitária", "limpador"]),
            ("Higiene", ["shampoo", "sabonete", "creme dental", "desodorante", "papel higiênico", "condicionador"]),
            ("Padaria", ["pão", "bisnaguinha", "bolo"]),
            ("Mercearia", ["açúcar", "acucar", "café", "óleo", "azeite", "macarrão", "farinha", "biscoito", "molho", "sal "]),
        ];
        foreach (var (categoria, palavras) in regras)
            if (palavras.Any(n.Contains))
                return categoria;
        return null;
    }

    private sealed record Resposta(
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("product")] ProdutoOff? Produto);

    private sealed record ProdutoOff(
        [property: JsonPropertyName("product_name")] string? Nome,
        [property: JsonPropertyName("product_name_pt")] string? NomePt,
        [property: JsonPropertyName("brands")] string? Marcas,
        [property: JsonPropertyName("quantity")] string? Quantidade,
        [property: JsonPropertyName("categories_tags")] string[]? Categorias);
}
