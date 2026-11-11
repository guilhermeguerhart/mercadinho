using System.Text.Json;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Testes.Motor;

namespace MercadinhoSeuZe.Testes.Testes;

/// <summary>
/// Promoção por produto e de validade por categoria. Além de gravar e recusar, confere que o preço que a tela mostra
/// (conta do programa) é o mesmo que o caixa cobra (conta do banco, preco_com_promocao) — sem precisar vender.
/// </summary>
public static class PromocoesTestes
{
    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Today);

    public static Lista Montar(Ambiente amb)
    {
        var l = new Lista("Promoções");
        async Task<EstoqueService> Servico() => new(amb.Api!, await amb.Entrar("gerente"));

        async Task<decimal> PrecoNoCaixa(long produtoId, decimal quantidade, decimal preco)
        {
            var g = await amb.Entrar("gerente");
            var r = await Erros.SemScriptPula(() => amb.Api!.ChamarAsync<JsonElement>("preco_com_promocao",
                new { p_produto_id = produtoId, p_quantidade = quantidade, p_preco = preco }, g.Token), "008");
            return r.Dec("subtotal");
        }

        async Task<long> CriarRegra(string categoria, int dias, decimal min, decimal max)
        {
            var est = await Servico();
            await est.CriarRegraValidadeAsync(categoria, dias, min, max, true);
            var (_, regras) = await est.PromocoesAsync();
            var id = regras.First(r => r.Categoria == categoria).Id;
            amb.ApagarNoFim($"regra {categoria}", () => est.ExcluirRegraValidadeAsync(id));
            return id;
        }

        l.Certo("Promoção por preço fixo: o caixa cobra R$ 6,99 em vez de R$ 8,90", async () =>
        {
            var est = await Servico();
            var id = await EstoqueTestes.CriarProduto(amb, "Promo fixa", preco: 8.90m, validade: Hoje.AddDays(200));
            await est.CriarPromocaoAsync(id, "preco", 6.99m, Hoje, Hoje.AddDays(7));
            Verificar.Dinheiro(13.98m, await PrecoNoCaixa(id, 2, 8.90m), "2 unidades no caixa");
            var p = await EstoqueTestes.Ler(amb, id);
            Verificar.Igual("Ativa", p.PromocaoAtiva?.Situacao, "Promoção na tela do Estoque");
            Verificar.Dinheiro(6.99m, Promocoes.PrecoDoLote(p, p.Lotes[0]), "Preço na tela");
        });
        l.Certo("Promoção de 15%: tela e caixa mostram o mesmo preço (R$ 8,90 → R$ 7,57)", async () =>
        {
            var est = await Servico();
            var id = await EstoqueTestes.CriarProduto(amb, "Promo 15", preco: 8.90m, validade: Hoje.AddDays(200));
            await est.CriarPromocaoAsync(id, "percentual", 15, Hoje, null);
            var caixa = await PrecoNoCaixa(id, 1, 8.90m);
            var p = await EstoqueTestes.Ler(amb, id);
            var tela = Promocoes.PrecoDoLote(p, p.Lotes[0]);
            Ambiente.Nota($"Caixa (banco): R$ {caixa:N2} · Tela (programa): R$ {tela:N2}");
            Verificar.Igual(caixa, tela, "Preço na tela x preço cobrado no caixa");
        });
        l.Certo("Promoção de validade: a conta do banco e a do programa dão o mesmo desconto de 6 dias antes até o vencimento", async () =>
        {
            var categoria = $"{amb.Marca} cat";
            await CriarRegra(categoria, 5, 10, 50);
            var g = await amb.Entrar("gerente");
            var regra = new RegraValidade { Categoria = categoria, DiasAntes = 5, DescontoMin = 10, DescontoMax = 50 };
            var diferentes = new List<string>();
            for (var d = -1; d <= 6; d++)
            {
                var banco = await amb.Api!.ChamarAsync<decimal>("desconto_validade",
                    new { p_categoria = categoria.ToLowerInvariant(), p_validade = Hoje.AddDays(d).ToString("yyyy-MM-dd"), p_fora = false }, g.Token);
                var programa = regra.Desconto(Hoje.AddDays(d), Hoje);
                if (banco != programa) diferentes.Add($"vence em {d} dia(s): banco {banco}%, programa {programa}%");
            }
            Verificar.Verdadeiro(diferentes.Count == 0, string.Join("\n", diferentes));
        });
        l.Certo("Maior desconto vence no caixa: lote que vence amanhã com 50% de validade e promoção de 20%", async () =>
        {
            var est = await Servico();
            var categoria = $"{amb.Marca} cat2";
            await CriarRegra(categoria, 3, 10, 50);
            var id = await EstoqueTestes.CriarProduto(amb, "Maior desconto", preco: 10m, quantidade: 1, validade: Hoje.AddDays(1), categoria: categoria);
            await est.CriarLoteAsync(id, 1, Hoje, Hoje.AddDays(100));
            await est.CriarPromocaoAsync(id, "percentual", 20, Hoje, null);
            // 1ª unidade (vence amanhã): 50% → 5,00; 2ª (vence em 100 dias): 20% → 8,00
            Verificar.Dinheiro(13.00m, await PrecoNoCaixa(id, 2, 10m), "2 unidades");
        });
        l.Certo("Encerrar agora libera outra promoção no mesmo período", async () =>
        {
            var est = await Servico();
            var id = await EstoqueTestes.CriarProduto(amb, "Encerrar", preco: 10m);
            await est.CriarPromocaoAsync(id, "percentual", 10, Hoje, null);
            var (promos, _) = await est.PromocoesAsync();
            await est.EncerrarPromocaoAsync(promos.First(x => x.ProdutoId == id).Id);
            await est.CriarPromocaoAsync(id, "percentual", 30, Hoje, null);
            Verificar.Dinheiro(7m, await PrecoNoCaixa(id, 1, 10m), "Preço com a promoção nova");
        });
        l.Certo("Promoção agendada para amanhã não vale hoje", async () =>
        {
            var est = await Servico();
            var id = await EstoqueTestes.CriarProduto(amb, "Agendada", preco: 10m);
            await est.CriarPromocaoAsync(id, "percentual", 50, Hoje.AddDays(1), Hoje.AddDays(3));
            Verificar.Dinheiro(10m, await PrecoNoCaixa(id, 1, 10m), "Preço hoje");
        });

        l.Errado("Preço promocional igual ou maior que o normal é recusado", async () =>
        {
            var est = await Servico();
            var id = await EstoqueTestes.CriarProduto(amb, "Promo cara", preco: 5m);
            await Verificar.Recusa(() => est.CriarPromocaoAsync(id, "preco", 5m, Hoje, null), "Promoção de R$ 5,00 em produto de R$ 5,00", "menor que o preço normal");
            await Verificar.Recusa(() => est.CriarPromocaoAsync(id, "preco", 6m, Hoje, null), "Promoção mais cara", "menor que o preço normal");
        });
        l.Errado("Duas promoções no mesmo período do mesmo produto são recusadas", async () =>
        {
            var est = await Servico();
            var id = await EstoqueTestes.CriarProduto(amb, "Duas promos", preco: 10m);
            await est.CriarPromocaoAsync(id, "percentual", 10, Hoje, Hoje.AddDays(10));
            await Verificar.Recusa(() => est.CriarPromocaoAsync(id, "percentual", 20, Hoje.AddDays(5), null), "Promoção sobreposta", "já tem uma promoção");
            await est.CriarPromocaoAsync(id, "percentual", 20, Hoje.AddDays(11), Hoje.AddDays(12));
        });
        l.Errado("Promoção com fim antes do início, valor zero ou 100% é recusada", async () =>
        {
            var est = await Servico();
            var id = await EstoqueTestes.CriarProduto(amb, "Promo errada", preco: 10m);
            await Verificar.Recusa(() => est.CriarPromocaoAsync(id, "percentual", 10, Hoje, Hoje.AddDays(-1)), "Fim antes do início");
            await Verificar.Recusa(() => est.CriarPromocaoAsync(id, "percentual", 0, Hoje, null), "0%");
            await Verificar.Recusa(() => est.CriarPromocaoAsync(id, "percentual", 100, Hoje, null), "100%");
            await Verificar.Recusa(() => est.CriarPromocaoAsync(id, "preco", -1, Hoje, null), "Preço negativo");
        });
        l.Errado("Regra de validade: categoria repetida, máximo menor que o mínimo e operador são recusados", async () =>
        {
            var est = await Servico();
            var categoria = $"{amb.Marca} cat3";
            await CriarRegra(categoria, 3, 10, 30);
            await Verificar.Recusa(() => est.CriarRegraValidadeAsync(categoria.ToUpperInvariant() + " ", 3, 10, 30, true), "Categoria repetida");
            await Verificar.Recusa(() => est.CriarRegraValidadeAsync(categoria + "x", 3, 40, 30, true), "Máximo menor que o mínimo");
            var op = new EstoqueService(amb.Api!, await amb.Entrar("operador"));
            await Verificar.Recusa(() => op.CriarRegraValidadeAsync(categoria + "y", 3, 10, 30, true), "Operador criando regra");
        });
        l.Limite("Regra de validade: 1 e 60 dias e 90% entram; 0, 61 dias e 91% são recusados", async () =>
        {
            var est = await Servico();
            var c = $"{amb.Marca} lim";
            await CriarRegra(c + "1", 1, 0, 90);
            await CriarRegra(c + "60", 60, 0, 90);
            await Verificar.Recusa(() => est.CriarRegraValidadeAsync(c + "0", 0, 10, 20, true), "0 dias");
            await Verificar.Recusa(() => est.CriarRegraValidadeAsync(c + "61", 61, 10, 20, true), "61 dias");
            await Verificar.Recusa(() => est.CriarRegraValidadeAsync(c + "91", 3, 10, 91, true), "91%");
        });
        l.Limite("Promoção de 99,99% e de preço R$ 0,01", async () =>
        {
            var est = await Servico();
            var a = await EstoqueTestes.CriarProduto(amb, "Promo 99", preco: 100m);
            await est.CriarPromocaoAsync(a, "percentual", 99.99m, Hoje, null);
            Verificar.Dinheiro(0.01m, await PrecoNoCaixa(a, 1, 100m), "99,99% de R$ 100,00");
            var b = await EstoqueTestes.CriarProduto(amb, "Promo 1 centavo", preco: 10m);
            await est.CriarPromocaoAsync(b, "preco", 0.01m, Hoje, null);
            Verificar.Dinheiro(0.01m, await PrecoNoCaixa(b, 1, 10m), "Preço de R$ 0,01");
        });
        l.Limite("Preço no caixa com 200 lotes de validades diferentes", async () =>
        {
            var est = await Servico();
            var categoria = $"{amb.Marca} cat4";
            await CriarRegra(categoria, 10, 5, 50);
            var id = await EstoqueTestes.CriarProduto(amb, "200 lotes", preco: 3m, quantidade: 0, categoria: categoria);
            var g = await amb.Entrar("gerente");
            // Um pedido só com os 200 lotes
            var lotes = Enumerable.Range(0, 200).Select(i => new
            {
                produto_id = id, quantidade_inicial = 1, quantidade_restante = 1,
                data_compra = Hoje.ToString("yyyy-MM-dd"), validade = Hoje.AddDays(i % 30).ToString("yyyy-MM-dd")
            }).ToArray();
            var (status, corpo, _) = await amb.Http(System.Net.Http.HttpMethod.Post, "rest/v1/lotes", g.Token, lotes);
            Verificar.Verdadeiro((int)status < 300, $"Gravar 200 lotes: {(int)status} {corpo}");
            var total = await Verificar.Rapido(() => PrecoNoCaixa(id, 200, 3m), TimeSpan.FromSeconds(2), "Preço de 200 unidades");
            var p = await EstoqueTestes.Ler(amb, id);
            var tela = p.Lotes.Sum(x => Promocoes.PrecoDoLote(p, x));
            Ambiente.Nota($"Caixa: R$ {total:N2} · soma da tela: R$ {tela:N2}");
            // A tela arredonda o preço de cada lote; o caixa arredonda só o total. Até meio centavo por lote é esperado.
            Verificar.Verdadeiro(Math.Abs(tela - total) <= 1.00m, $"200 lotes: caixa R$ {total:N2} x tela R$ {tela:N2}");
            if (tela != total) Ambiente.Nota("Diferença de arredondamento entre a soma da tela e o total do caixa: R$ " + Math.Abs(tela - total).ToString("N2"));
        });
        return l;
    }
}
