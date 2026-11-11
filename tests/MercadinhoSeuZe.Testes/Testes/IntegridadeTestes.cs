using System.Text.Json;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Testes.Motor;

namespace MercadinhoSeuZe.Testes.Testes;

/// <summary>
/// Os dados guardados no banco batem entre si (só leitura, como gerente): vendas x itens x pagamentos x baixa nos
/// lotes, gaveta dos caixas fechados, resumo do dia x vendas, fechamentos gravados x resumo de hoje, promoções,
/// reservas e o que ficou para trás (vendas abertas de outros dias, caixas sem fechar, dias sem fechamento).
/// </summary>
public static class IntegridadeTestes
{
    private const int Dias = 30;

    public static Lista Montar(Ambiente amb)
    {
        var l = new Lista("Integridade dos dados");
        var desde = DateTime.Today.AddDays(-Dias).ToString("yyyy-MM-dd");
        async Task<string> Token() => (await amb.Entrar("gerente")).Token;

        l.Integridade($"Vendas pagas ({Dias} dias): total = soma dos itens − desconto = soma dos pagamentos", async () =>
        {
            var vendas = await amb.Api!.ListarAsync<JsonElement>(
                $"vendas?situacao=eq.paga&dia=gte.{desde}&select=id,total,desconto,troco,venda_itens(subtotal),venda_pagamentos(forma,valor,recebido,troco)&order=id.desc&limit=1000",
                await Token());
            var erros = new List<string>();
            var semPagamento = 0;
            foreach (var v in vendas)
            {
                var total = v.Dec("total");
                var itens = v.GetProperty("venda_itens").EnumerateArray().Sum(i => i.Dec("subtotal"));
                if (Math.Abs(itens - v.Dec("desconto") - total) > 0.01m) erros.Add($"Venda {v.Long("id")}: itens {itens:N2} − desconto {v.Dec("desconto"):N2} ≠ total {total:N2}");
                var pags = v.GetProperty("venda_pagamentos").EnumerateArray().ToList();
                if (pags.Count == 0) { semPagamento++; continue; }
                var pago = pags.Sum(p => p.Dec("valor"));
                if (Math.Abs(pago - total) > 0.01m) erros.Add($"Venda {v.Long("id")}: pagamentos {pago:N2} ≠ total {total:N2}");
                foreach (var p in pags.Where(p => p.Str("forma") == "dinheiro" && p.TryGetProperty("recebido", out var r) && r.ValueKind == JsonValueKind.Number))
                    if (Math.Abs(p.Dec("recebido") - p.Dec("valor") - p.Dec("troco")) > 0.01m)
                        erros.Add($"Venda {v.Long("id")}: troco {p.Dec("troco"):N2} ≠ recebido {p.Dec("recebido"):N2} − valor {p.Dec("valor"):N2}");
            }
            Ambiente.Nota($"{vendas.Count} vendas conferidas");
            if (vendas.Count == 0) throw new PularTeste($"Nenhuma venda paga nos últimos {Dias} dias.");
            if (semPagamento > 0) Ambiente.AvisoPendente($"{semPagamento} venda(s) paga(s) sem as formas de pagamento gravadas (vendas antigas, de antes do script 007?).");
            Verificar.Verdadeiro(erros.Count == 0, $"{erros.Count} venda(s) com conta errada:\n" + string.Join("\n", erros.Take(15)));
        });
        l.Integridade("Itens vendidos (7 dias): a baixa nos lotes é igual à quantidade vendida", async () =>
        {
            var semana = DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd");
            var tk = await Token();
            var itens = await FechamentoTestes.SemEstourar(() => amb.Api!.ListarAsync<JsonElement>(
                $"venda_itens?select=id,nome_produto,quantidade,venda_item_lotes(quantidade),vendas!inner(situacao,dia)&vendas.situacao=eq.paga&vendas.dia=gte.{semana}&order=id.desc&limit=300",
                tk), "Baixas por lote dos últimos 7 dias");
            var erros = itens.Where(i =>
                {
                    var baixas = i.GetProperty("venda_item_lotes").EnumerateArray().ToList();
                    return baixas.Count > 0 && Math.Abs(baixas.Sum(b => b.Dec("quantidade")) - i.Dec("quantidade")) > 0.0005m;
                })
                .Select(i => $"Item {i.Long("id")} ({i.Str("nome_produto")}): vendeu {i.Dec("quantidade")}, baixou {i.GetProperty("venda_item_lotes").EnumerateArray().Sum(b => b.Dec("quantidade"))}")
                .ToList();
            var semBaixa = itens.Count(i => i.GetProperty("venda_item_lotes").GetArrayLength() == 0);
            Ambiente.Nota($"{itens.Count} itens conferidos");
            if (itens.Count == 0) throw new PularTeste($"Nenhum item vendido nos últimos {Dias} dias.");
            if (semBaixa > 0) Ambiente.AvisoPendente($"{semBaixa} item(ns) vendido(s) sem a baixa por lote gravada (o estoque não diminuiu por eles).");
            Verificar.Verdadeiro(erros.Count == 0, string.Join("\n", erros.Take(15)));
        });
        l.Integridade("Caixas fechados: diferença = contado − esperado", async () =>
        {
            var caixas = await amb.Api!.ListarAsync<JsonElement>(
                $"caixas?fechado_em=not.is.null&dia=gte.{desde}&select=id,operador_nome,dia,fechado_contado,fechado_esperado,fechado_diferenca&limit=1000", await Token());
            var erros = caixas.Where(c => c.TryGetProperty("fechado_contado", out var x) && x.ValueKind == JsonValueKind.Number &&
                                          Math.Abs(c.Dec("fechado_contado") - c.Dec("fechado_esperado") - c.Dec("fechado_diferenca")) > 0.01m)
                .Select(c => $"Caixa {c.Long("id")} ({c.Str("operador_nome")}, {c.Str("dia")})").ToList();
            Ambiente.Nota($"{caixas.Count} caixas fechados conferidos");
            Verificar.Verdadeiro(erros.Count == 0, string.Join("\n", erros));
        });
        l.Integridade("Resumo de hoje = soma das vendas pagas de hoje", async () =>
        {
            var token = await Token();
            var hoje = DateTime.Today.ToString("yyyy-MM-dd");
            var vendas = await amb.Api!.ListarAsync<JsonElement>($"vendas?dia=eq.{hoje}&situacao=eq.paga&select=total&limit=5000", token);
            var resumo = await amb.Api.ChamarAsync<ResumoDoDia>("resumo_dia", new { p_dia = hoje }, token)
                         ?? throw new FalhaTeste("resumo_dia vazio");
            Verificar.Igual(vendas.Count, resumo.Vendas, "Quantidade de vendas");
            Verificar.Dinheiro(vendas.Sum(v => v.Dec("total")), resumo.Total, "Total");
            Verificar.Dinheiro(resumo.Total, resumo.Formas.Values.Sum(), "Total x formas de pagamento");
        });
        l.Integridade("Análise por dia e resumo de cada dia dão o mesmo total (últimos 7 dias)", async () =>
        {
            var token = await Token();
            var srv = new FechamentoService(amb.Api!, await amb.Entrar("gerente"));
            var a = await srv.AnaliseAsync(DateTime.Today.AddDays(-7), DateTime.Today.AddDays(-1), "dia");
            var erros = new List<string>();
            for (var d = DateTime.Today.AddDays(-7); d < DateTime.Today; d = d.AddDays(1))
            {
                var r = await srv.ResumoAsync(d);
                var ponto = a.Serie.FirstOrDefault(p => p.Periodo.Date == d.Date)?.Total ?? 0;
                if (Math.Abs(ponto - r.Total) > 0.01m) erros.Add($"{d:dd/MM}: análise R$ {ponto:N2}, resumo R$ {r.Total:N2}");
            }
            Verificar.Verdadeiro(erros.Count == 0, string.Join("\n", erros));
        });
        l.Integridade("Dias fechados: o total gravado no fechamento continua igual ao de hoje (ninguém mexeu depois)", async () =>
        {
            var token = await Token();
            var fechamentos = await amb.Api!.ListarAsync<JsonElement>("fechamentos_dia?automatico=eq.false&select=dia,resumo&order=dia.desc&limit=15", token);
            if (fechamentos.Count == 0) throw new PularTeste("Nenhum dia fechado ainda.");
            var erros = new List<string>();
            foreach (var f in fechamentos)
            {
                var dia = f.Str("dia")!;
                var atual = await amb.Api.ChamarAsync<JsonElement>("resumo_dia", new { p_dia = dia }, token);
                var gravado = f.GetProperty("resumo");
                if (Math.Abs(gravado.Dec("total") - atual.Dec("total")) > 0.01m || gravado.Long("vendas") != atual.Long("vendas"))
                    erros.Add($"{dia}: no fechamento R$ {gravado.Dec("total"):N2} ({gravado.Long("vendas")} vendas), hoje R$ {atual.Dec("total"):N2} ({atual.Long("vendas")} vendas)");
            }
            Ambiente.Nota($"{fechamentos.Count} dias conferidos");
            Verificar.Verdadeiro(erros.Count == 0, "Vendas mudaram depois do fechamento:\n" + string.Join("\n", erros));
        });
        l.Integridade("Reservas de hoje não passam do que dá para vender", async () =>
        {
            var g = await amb.Entrar("gerente");
            var est = new EstoqueService(amb.Api!, g);
            var reservas = await est.ReservadosAsync();
            if (reservas.Count == 0) throw new PularTeste("Nada reservado agora.");
            var produtos = (await est.ListarAsync()).ToDictionary(p => p.Id);
            var erros = reservas.Where(r => produtos.TryGetValue(r.Key, out var p) && r.Value > p.Vendavel + 0.0005m)
                .Select(r => $"{produtos[r.Key].Nome}: reservado {r.Value}, vendável {produtos[r.Key].Vendavel}").ToList();
            Verificar.Verdadeiro(erros.Count == 0, string.Join("\n", erros));
        });
        l.Integridade("Promoções: nenhum produto com duas promoções valendo ao mesmo tempo e nenhum preço fixo acima do normal", async () =>
        {
            var g = await amb.Entrar("gerente");
            var est = new EstoqueService(amb.Api!, g);
            var (produtos, promocoes, _) = await est.ListarComPromocoesAsync();
            var porId = produtos.ToDictionary(p => p.Id);
            var erros = promocoes.Where(p => p.Ativa).GroupBy(p => p.ProdutoId).Where(x => x.Count() > 1)
                .Select(x => $"{(porId.TryGetValue(x.Key, out var p) ? p.Nome : x.Key.ToString())}: {x.Count()} promoções ativas").ToList();
            erros.AddRange(promocoes.Where(p => p.Ativa && p.PorPreco && porId.TryGetValue(p.ProdutoId, out var pr) && p.Valor >= pr.Preco)
                .Select(p => $"{porId[p.ProdutoId].Nome}: promoção R$ {p.Valor:N2} ≥ preço R$ {porId[p.ProdutoId].Preco:N2}"));
            Verificar.Verdadeiro(erros.Count == 0, string.Join("\n", erros));
        });
        l.Integridade("Sempre existe pelo menos um gerente ativo", async () =>
        {
            var lista = await new FuncionarioService(amb.Api!, await amb.Entrar("gerente")).ListarAsync();
            Verificar.Verdadeiro(lista.Any(f => f.Gerente && f.Ativo), "Nenhum gerente ativo");
            Ambiente.Nota($"{lista.Count(f => f.Ativo)} funcionários ativos, {lista.Count(f => f.Gerente && f.Ativo)} gerentes");
        });
        l.Integridade("O que ficou para trás: vendas abertas de outros dias, caixas sem fechar e dias sem fechamento", async () =>
        {
            var token = await Token();
            var hoje = DateTime.Today.ToString("yyyy-MM-dd");
            var abertas = await amb.ContarAsync($"vendas?situacao=in.(aberta,espera)&dia=lt.{hoje}", token);
            var caixas = await amb.ContarAsync($"caixas?fechado_em=is.null&dia=lt.{hoje}", token);
            var analise = await new FechamentoService(amb.Api!, await amb.Entrar("gerente")).AnaliseAsync(DateTime.Today.AddDays(-Dias), DateTime.Today.AddDays(-1), "dia");
            var comVenda = analise.Serie.Where(p => p.Vendas > 0).Select(p => p.Periodo.ToString("yyyy-MM-dd")).ToList();
            var fechados = (await amb.Api.ListarAsync<JsonElement>($"fechamentos_dia?dia=gte.{desde}&select=dia,automatico", token))
                .Where(f => f.TryGetProperty("automatico", out var a) && !a.GetBoolean()).Select(f => f.Str("dia")).ToHashSet();
            var semFechar = comVenda.Count(d => !fechados.Contains(d));
            Ambiente.Nota($"Vendas abertas de outros dias: {abertas} · caixas de outros dias sem fechar: {caixas} · dias com venda sem fechamento ({Dias} dias): {semFechar}");
            var avisos = new List<string>();
            if (abertas > 0) avisos.Add($"{abertas} venda(s) aberta(s) ou em espera de dias anteriores (são canceladas quando a pessoa abrir o caixa de novo)");
            if (caixas > 0) avisos.Add($"{caixas} caixa(s) de dias anteriores que nunca foram fechados (a gaveta não foi conferida)");
            if (semFechar > 0) avisos.Add($"{semFechar} dia(s) com vendas nos últimos {Dias} dias sem \"Fechar o dia\"");
            if (avisos.Count > 0) throw new AvisoTeste(string.Join("\n", avisos));
        });
        l.Integridade("Produtos vendidos abaixo do custo e lotes vencidos ainda no estoque", async () =>
        {
            var g = await amb.Entrar("gerente");
            var produtos = await new EstoqueService(amb.Api!, g).ListarAsync();
            var abaixo = produtos.Where(p => !p.Nome.StartsWith(Ambiente.Prefixo) && p.PrecoCusto is { } c && c > p.Preco).ToList();
            var vencidos = produtos.Where(p => !p.Nome.StartsWith(Ambiente.Prefixo) && p.QuantidadeVencida > 0).ToList();
            Ambiente.Nota($"{produtos.Count} produtos lidos");
            var avisos = new List<string>();
            if (abaixo.Count > 0) avisos.Add($"{abaixo.Count} produto(s) com custo maior que o preço de venda: " + string.Join(", ", abaixo.Take(8).Select(p => p.Nome)));
            if (vencidos.Count > 0) avisos.Add($"{vencidos.Count} produto(s) com lote vencido ainda no estoque (o caixa bloqueia; é perda): " + string.Join(", ", vencidos.Take(8).Select(p => p.Nome)));
            if (avisos.Count > 0) throw new AvisoTeste(string.Join("\n", avisos));
        });
        return l;
    }
}
