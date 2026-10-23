using System.Globalization;
using System.Text;
using System.Xml.Linq;
using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

/// <summary>Um produto da nota fiscal (tag det/prod da NF-e).</summary>
public sealed record ItemNota(string Nome, string? Ean, string? EanTributavel, decimal QuantidadeComercial, string UnidadeComercial,
    decimal ValorUnitarioComercial, decimal QuantidadeTributavel, string UnidadeTributavel, decimal ValorUnitarioTributavel,
    DateOnly? Validade);

/// <summary>Número, emitente e produtos de uma NF-e lida do arquivo XML.</summary>
public sealed record NotaLida(string? Numero, string? Emitente, IReadOnlyList<ItemNota> Itens);

/// <summary>Quanto chegou de um item do pedido segundo a nota, já na unidade do estoque (un. ou kg).</summary>
public sealed record ItemNotaNoPedido(decimal Quantidade, decimal? CustoUnitario, DateOnly? Validade);

/// <summary>
/// Lê o XML da NF-e (o arquivo que o fornecedor manda por e-mail) e liga os produtos da nota aos itens do pedido.
/// Só lê o arquivo: não consulta a SEFAZ nem valida a assinatura.
/// </summary>
public static class NotaFiscalXml
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";
    private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

    /// <summary>Unidades de embalagem: a quantidade vem em caixas/fardos e vira unidades pelo "unidades por caixa".</summary>
    private static readonly string[] Embalagens = ["CX", "CXA", "CAIXA", "FD", "FARDO", "PCT", "PC", "PACOTE", "DZ", "DUZIA", "EMB", "DP", "DISPLAY"];

    /// <exception cref="FormatException">O arquivo não é uma NF-e.</exception>
    public static NotaLida Ler(string arquivo)
    {
        XDocument xml;
        try { xml = XDocument.Load(arquivo); }
        catch (System.Xml.XmlException) { throw new FormatException("O arquivo não é um XML válido."); }

        var inf = xml.Descendants(Nfe + "infNFe").FirstOrDefault()
                  ?? throw new FormatException("O arquivo não é o XML de uma NF-e (nota fiscal eletrônica).");
        var numero = inf.Element(Nfe + "ide")?.Element(Nfe + "nNF")?.Value;
        var emitente = inf.Element(Nfe + "emit")?.Element(Nfe + "xNome")?.Value;

        var itens = new List<ItemNota>();
        foreach (var prod in inf.Elements(Nfe + "det").Select(d => d.Element(Nfe + "prod")).OfType<XElement>())
        {
            string? Texto(string nome) => prod.Element(Nfe + nome)?.Value.Trim();
            decimal Numero(string nome) => decimal.TryParse(Texto(nome), NumberStyles.Number, Invariante, out var v) ? v : 0;
            string? Codigo(string nome) => Texto(nome) is { Length: > 0 } c && c.All(char.IsDigit) ? c : null;   // "SEM GTIN" vira null

            // Validade do lote (grupo rastro, quando o fornecedor informa)
            DateOnly? validade = prod.Elements(Nfe + "rastro").Select(r => r.Element(Nfe + "dVal")?.Value)
                .Select(v => DateOnly.TryParseExact(v, "yyyy-MM-dd", Invariante, DateTimeStyles.None, out var d) ? d : (DateOnly?)null)
                .Where(d => d is not null).Min();

            itens.Add(new ItemNota(Texto("xProd") ?? "", Codigo("cEAN"), Codigo("cEANTrib"),
                Numero("qCom"), (Texto("uCom") ?? "").ToUpperInvariant(), Numero("vUnCom"),
                Numero("qTrib"), (Texto("uTrib") ?? "").ToUpperInvariant(), Numero("vUnTrib"), validade));
        }
        if (itens.Count == 0) throw new FormatException("A nota não tem produtos.");
        return new NotaLida(numero, emitente, itens);
    }

    /// <summary>
    /// Liga os produtos do pedido às linhas da nota: primeiro pelo código de barras da caixa; depois pelo nome,
    /// juntando primeiro os pares mais parecidos de todo o pedido (assim "Papel toalha" não pega a linha do
    /// "Papel higiênico"). Cada linha da nota vai para um produto só. Devolve o que chegou de cada produto e as
    /// linhas da nota que não foram usadas.
    /// </summary>
    public static (Dictionary<long, ItemNotaNoPedido> Achados, List<ItemNota> Sobraram) Ligar(NotaLida nota, IReadOnlyList<Produto> produtos)
    {
        var linhasDe = produtos.ToDictionary(p => p.Id, _ => new List<ItemNota>());
        var usadas = new HashSet<ItemNota>();

        // 1. Código de barras da caixa (cEAN ou cEANTrib)
        foreach (var p in produtos)
        {
            var codigo = p.CodigoBarrasCaixa?.Trim();
            if (codigo is not { Length: > 0 }) continue;
            foreach (var l in nota.Itens.Where(i => !usadas.Contains(i) && (i.Ean == codigo || i.EanTributavel == codigo)))
            {
                linhasDe[p.Id].Add(l);
                usadas.Add(l);
            }
        }

        // 2. Nome parecido (pelo menos metade das palavras do produto), dos pares mais parecidos para os menos
        var pares = (from p in produtos
                     where linhasDe[p.Id].Count == 0
                     from l in nota.Itens
                     where !usadas.Contains(l)
                     let nota1 = Parecido(p.Nome, l.Nome)
                     where nota1 >= 0.5
                     orderby nota1 descending, Parecido(l.Nome, p.Nome) descending
                     select (Produto: p, Linha: l)).ToList();
        foreach (var (p, l) in pares)
        {
            if (linhasDe[p.Id].Count > 0 || usadas.Contains(l)) continue;
            linhasDe[p.Id].Add(l);
            usadas.Add(l);
        }

        var achados = new Dictionary<long, ItemNotaNoPedido>();
        foreach (var p in produtos)
            if (Somar(linhasDe[p.Id], p) is { } item)
                achados[p.Id] = item;
        return (achados, nota.Itens.Where(i => !usadas.Contains(i)).ToList());
    }

    /// <summary>Soma as linhas da nota de um produto, na unidade do estoque, com o custo médio e a validade mais curta.</summary>
    private static ItemNotaNoPedido? Somar(List<ItemNota> linhas, Produto produto)
    {
        if (linhas.Count == 0) return null;

        decimal quantidade = 0, valor = 0;
        foreach (var l in linhas)
        {
            var (q, total) = NaUnidadeDoEstoque(l, produto);
            quantidade += q;
            valor += total;
        }
        decimal? custo = quantidade > 0 && valor > 0 ? Math.Round(valor / quantidade, 2) : null;
        return new ItemNotaNoPedido(quantidade, custo, linhas.Select(l => l.Validade).Where(v => v is not null).Min());
    }

    /// <summary>Quantidade em unidades (ou kg) e o valor total da linha.</summary>
    private static (decimal Quantidade, decimal Total) NaUnidadeDoEstoque(ItemNota l, Produto p)
    {
        if (p.PorPeso)
        {
            // Por quilo: usa a unidade que estiver em KG
            if (l.UnidadeComercial.StartsWith("KG")) return (l.QuantidadeComercial, l.QuantidadeComercial * l.ValorUnitarioComercial);
            if (l.UnidadeTributavel.StartsWith("KG")) return (l.QuantidadeTributavel, l.QuantidadeTributavel * l.ValorUnitarioTributavel);
            return (l.QuantidadeComercial * p.UnidadesPorCaixa, l.QuantidadeComercial * l.ValorUnitarioComercial);
        }
        var total = l.QuantidadeComercial * l.ValorUnitarioComercial;
        // Vendido em caixa: multiplica pelas unidades da caixa
        if (Embalagens.Contains(l.UnidadeComercial) || l.Ean == p.CodigoBarrasCaixa?.Trim() && p.UnidadesPorCaixa > 1 && !l.UnidadeComercial.StartsWith("UN"))
        {
            // Se a unidade tributável já está em unidades, ela é mais confiável
            if (l.UnidadeTributavel.StartsWith("UN") && l.QuantidadeTributavel > l.QuantidadeComercial)
                return (l.QuantidadeTributavel, total);
            return (l.QuantidadeComercial * p.UnidadesPorCaixa, total);
        }
        return (l.QuantidadeComercial, total);
    }

    /// <summary>Parte das palavras do nome do produto (sem acento, 3 letras ou mais) que aparecem no nome da nota.</summary>
    private static double Parecido(string produto, string nota)
    {
        var palavras = Palavras(produto);
        if (palavras.Count == 0) return 0;
        var naNota = Palavras(nota);
        return palavras.Count(p => naNota.Any(n => n.StartsWith(p) || p.StartsWith(n))) / (double)palavras.Count;
    }

    private static List<string> Palavras(string texto)
    {
        var semAcento = new string(texto.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
        return semAcento.Split([' ', '-', '/', '.', ',', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length >= 3 && !p.All(char.IsDigit)).Distinct().ToList();
    }
}
