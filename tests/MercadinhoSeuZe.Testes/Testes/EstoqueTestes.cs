using System.Text.Json;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Testes.Motor;

namespace MercadinhoSeuZe.Testes.Testes;

/// <summary>
/// Estoque pelo EstoqueService do programa (como gerente): cadastrar, editar e excluir produto e lote, por unidade e por
/// quilo, e os valores que o banco tem que recusar. Tudo leva o nome "TESTE AUTOMATICO" e é apagado no fim.
/// </summary>
public static class EstoqueTestes
{
    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Cria um produto de teste (e apaga no fim). Usado também pelos outros módulos.</summary>
    public static async Task<long> CriarProduto(Ambiente amb, string sufixo, decimal preco = 10m, decimal quantidade = 50, bool porPeso = false,
        DateOnly? validade = null, string categoria = "Teste automático", decimal minimo = 5, string? codigo = null, int? plu = null, decimal? custo = null)
    {
        var g = await amb.Entrar("gerente");
        var est = new EstoqueService(amb.Api!, g);
        var nome = $"{amb.Marca} {sufixo}";
        var id = await est.CriarProdutoAsync(new DadosProduto(nome, categoria, preco, minimo, null, codigo, 1, porPeso, null,
            PrecoCusto: custo, CodigoBalanca: plu), quantidade, Hoje.AddDays(-1), validade);
        amb.ApagarNoFim($"produto {nome}", () => est.ExcluirProdutoAsync(id));
        return id;
    }

    public static async Task<Produto> Ler(Ambiente amb, long id)
    {
        var g = await amb.Entrar("gerente");
        var est = new EstoqueService(amb.Api!, g);
        var (produtos, _, _) = await est.ListarComPromocoesAsync();
        return produtos.FirstOrDefault(p => p.Id == id) ?? throw new FalhaTeste($"Produto {id} não apareceu na lista do Estoque.");
    }

    /// <summary>Código DUN-14 novo e válido, para não bater com nenhum cadastrado.</summary>
    public static string CodigoNovo()
    {
        var base13 = "1" + Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString();
        return base13 + OpenFoodFactsService.DigitoVerificador(base13);
    }

    public static Lista Montar(Ambiente amb)
    {
        var l = new Lista("Estoque");
        async Task<EstoqueService> Servico() => new(amb.Api!, await amb.Entrar("gerente"));
        DadosProduto Dados(string nome, decimal preco = 5m, string? codigo = null, decimal minimo = 0, bool porPeso = false, int? dias = null,
            decimal porCaixa = 1, string categoria = "Teste automático") =>
            new($"{amb.Marca} {nome}", categoria, preco, minimo, null, codigo, porCaixa, porPeso, dias);

        l.Certo("Cadastrar produto por unidade com o primeiro lote e ver na lista", async () =>
        {
            var codigo = CodigoNovo();
            var id = await CriarProduto(amb, "Arroz 5kg", preco: 27.90m, quantidade: 40, validade: Hoje.AddDays(180), codigo: codigo, custo: 20m);
            var p = await Ler(amb, id);
            Verificar.Igual(27.90m, p.Preco, "Preço");
            Verificar.Igual(40m, p.Quantidade, "Quantidade");
            Verificar.Igual(codigo, p.CodigoBarrasCaixa, "Código da caixa");
            Verificar.Igual(Hoje.AddDays(180), p.ProximaValidade, "Validade");
            Verificar.Igual(20m, p.PrecoCusto, "Custo");
        });
        l.Certo("Cadastrar produto por quilo com 18,745 kg", async () =>
        {
            var id = await CriarProduto(amb, "Alcatra kg", preco: 49.90m, quantidade: 18.745m, porPeso: true, plu: Random.Shared.Next(90000, 99999));
            var p = await Ler(amb, id);
            Verificar.Verdadeiro(p.PorPeso, "Deveria ser por quilo");
            Verificar.Igual(18.745m, p.Quantidade, "Quantidade em kg");
            Verificar.Igual("18,745 kg", p.Formatar(p.Quantidade), "Texto da quantidade");
        });
        l.Certo("Cadastrar sem quantidade não cria lote", async () =>
        {
            var id = await CriarProduto(amb, "Sem lote", quantidade: 0);
            var p = await Ler(amb, id);
            Verificar.Igual(0, p.Lotes.Count, "Lotes");
            Verificar.Verdadeiro(p.SemEstoque, "Deveria aparecer como sem estoque");
        });
        l.Certo("Editar produto (preço, mínimo, validade padrão) e ler de volta", async () =>
        {
            var est = await Servico();
            var id = await CriarProduto(amb, "Editar");
            await est.AtualizarProdutoAsync(id, Dados("Editar", preco: 12.34m, minimo: 7, dias: 30) with { Categoria = "Mercearia" });
            var p = await Ler(amb, id);
            Verificar.Igual(12.34m, p.Preco, "Preço novo");
            Verificar.Igual(7m, p.EstoqueMinimo, "Mínimo novo");
            Verificar.Igual(30, p.DiasValidade, "Validade padrão");
            Verificar.Igual("Mercearia", p.Categoria, "Categoria");
        });
        l.Certo("Lotes: adicionar, editar (vendido 3 de 10) e excluir", async () =>
        {
            var est = await Servico();
            var id = await CriarProduto(amb, "Lotes", quantidade: 0);
            await est.CriarLoteAsync(id, 10, Hoje.AddDays(-2), Hoje.AddDays(10));
            await est.CriarLoteAsync(id, 5, Hoje, Hoje.AddDays(60));
            var p = await Ler(amb, id);
            Verificar.Igual(15m, p.Quantidade, "Soma dos lotes");
            Verificar.Igual(Hoje.AddDays(10), p.ProximaValidade, "Validade mais próxima");
            var lote = p.Lotes.First(x => x.QuantidadeInicial == 10);
            await est.AtualizarLoteAsync(lote.Id, 10, 7, lote.DataCompra, lote.Validade);
            p = await Ler(amb, id);
            Verificar.Igual(3m, p.Lotes.First(x => x.Id == lote.Id).Vendido, "Vendido do lote");
            await est.ExcluirLoteAsync(lote.Id);
            p = await Ler(amb, id);
            Verificar.Igual(5m, p.Quantidade, "Depois de excluir o lote");
        });
        l.Certo("Excluir produto apaga os lotes junto", async () =>
        {
            var est = await Servico();
            var id = await est.CriarProdutoAsync(Dados("Excluir"), 9, Hoje, null);
            await est.ExcluirProdutoAsync(id);
            var g = await amb.Entrar("gerente");
            Verificar.Igual(0L, await amb.ContarAsync($"lotes?produto_id=eq.{id}", g.Token), "Lotes que sobraram");
        });
        l.Certo("Reservados no caixa são lidos (reservados_hoje)", async () =>
        {
            var est = await Servico();
            var r = await est.ReservadosAsync();
            Ambiente.Nota($"{r.Count} produto(s) reservados agora");
            Verificar.Verdadeiro(r.Values.All(q => q > 0), "Reserva com quantidade zero ou negativa");
        });

        // ---------------------------------------------------------------- Errado
        l.Errado("Nome repetido (mesmo com outra maiúscula) é recusado", async () =>
        {
            var est = await Servico();
            await CriarProduto(amb, "Feijão Repetido");
            var msg = await Verificar.Recusa(() => est.CriarProdutoAsync(Dados("FEIJÃO REPETIDO"), 0, Hoje, null), "Nome repetido");
            Verificar.MensagemClara(msg, "Nome repetido");
        });
        l.Errado("Código de barras repetido é recusado", async () =>
        {
            var est = await Servico();
            var codigo = CodigoNovo();
            await CriarProduto(amb, "Código 1", codigo: codigo);
            var msg = await Verificar.Recusa(() => est.CriarProdutoAsync(Dados("Código 2", codigo: codigo), 0, Hoje, null), "Código repetido");
            Verificar.MensagemClara(msg, "Código repetido");
        });
        l.Errado("Preço zero e negativo são recusados pelo banco", async () =>
        {
            var est = await Servico();
            await Verificar.Recusa(() => est.CriarProdutoAsync(Dados("Preço zero", preco: 0), 0, Hoje, null), "Preço 0");
            await Verificar.Recusa(() => est.CriarProdutoAsync(Dados("Preço negativo", preco: -1), 0, Hoje, null), "Preço -1");
        });
        l.Errado("Nome e categoria vazios (ou só espaços) são recusados", async () =>
        {
            var est = await Servico();
            await Verificar.Recusa(() => est.CriarProdutoAsync(new DadosProduto("   ", "Teste", 1, 0, null, null, 1, false, null), 0, Hoje, null), "Nome em branco");
            await Verificar.Recusa(() => est.CriarProdutoAsync(Dados("Sem categoria", categoria: " "), 0, Hoje, null), "Categoria em branco");
        });
        l.Errado("Lote com validade antes da compra é recusado (e o cadastro do produto é desfeito)", async () =>
        {
            var est = await Servico();
            var nome = Dados("Validade antes").Nome;
            await Verificar.Recusa(() => est.CriarProdutoAsync(Dados("Validade antes"), 10, Hoje, Hoje.AddDays(-1)), "Validade antes da compra");
            var g = await amb.Entrar("gerente");
            Verificar.Igual(0L, await amb.ContarAsync($"produtos?nome=eq.{Uri.EscapeDataString(nome)}", g.Token), "Produto que ficou sem o lote");
        });
        l.Errado("Lote: restante maior que o comprado, negativo e quantidade zero são recusados", async () =>
        {
            var est = await Servico();
            var id = await CriarProduto(amb, "Lote errado", quantidade: 0);
            await Verificar.Recusa(() => est.CriarLoteAsync(id, 0, Hoje, null), "Lote de 0");
            await Verificar.Recusa(() => est.CriarLoteAsync(id, -5, Hoje, null), "Lote negativo");
            await est.CriarLoteAsync(id, 10, Hoje, null);
            var lote = (await Ler(amb, id)).Lotes.Single();
            await Verificar.Recusa(() => est.AtualizarLoteAsync(lote.Id, 10, 11, Hoje, null), "Restante 11 de 10");
            await Verificar.Recusa(() => est.AtualizarLoteAsync(lote.Id, 10, -1, Hoje, null), "Restante -1");
        });
        l.Errado("Lote de produto que não existe é recusado", async () =>
        {
            var est = await Servico();
            await Verificar.Recusa(() => est.CriarLoteAsync(-1, 5, Hoje, null), "Lote do produto -1");
        });
        l.Errado("Produto por unidade com unidades por caixa zero é recusado", async () =>
        {
            var est = await Servico();
            await Verificar.Recusa(() => est.CriarProdutoAsync(Dados("Caixa zero", porCaixa: 0), 0, Hoje, null), "Unidades por caixa 0");
        });
        l.Errado("Editar e excluir produto que não existe avisa \"Nada foi alterado\"", async () =>
        {
            var est = await Servico();
            await Verificar.Recusa(() => est.AtualizarProdutoAsync(-1, Dados("Fantasma")), "Editar produto -1", "Nada foi alterado");
            await Verificar.Recusa(() => est.ExcluirProdutoAsync(-1), "Excluir produto -1", "Nada foi alterado");
        });

        // ---------------------------------------------------------------- Limite
        l.Limite("Nome com 80 letras entra; com 81 é recusado", async () =>
        {
            var est = await Servico();
            var base80 = (amb.Marca + " " + new string('N', 80))[..80];
            var id = await est.CriarProdutoAsync(new DadosProduto(base80, "Teste", 1, 0, null, null, 1, false, null), 0, Hoje, null);
            amb.ApagarNoFim("produto de 80 letras", () => est.ExcluirProdutoAsync(id));
            await Verificar.Recusa(() => est.CriarProdutoAsync(new DadosProduto(base80[..79] + "XY", "Teste", 1, 0, null, null, 1, false, null), 0, Hoje, null), "Nome com 81");
        });
        l.Limite("Nome com acentos, emoji e aspas é guardado igual", async () =>
        {
            var id = await CriarProduto(amb, "Pão de Queijo \"Mineiro\" Ç ã 🧀");
            var p = await Ler(amb, id);
            Verificar.Verdadeiro(p.Nome.EndsWith("Pão de Queijo \"Mineiro\" Ç ã 🧀"), "Nome lido: " + p.Nome);
        });
        l.Limite("Código de barras com 40 caracteres entra; com 41 é recusado", async () =>
        {
            var est = await Servico();
            var c40 = new string('7', 27) + DateTime.Now.Ticks.ToString()[^13..];
            var id = await est.CriarProdutoAsync(Dados("Código 40", codigo: c40), 0, Hoje, null);
            amb.ApagarNoFim("produto com código de 40", () => est.ExcluirProdutoAsync(id));
            await Verificar.Recusa(() => est.CriarProdutoAsync(Dados("Código 41", codigo: c40 + "1"), 0, Hoje, null), "Código com 41");
        });
        l.Limite("Preço: R$ 0,01 e R$ 99.999.999,99 entram; R$ 100.000.000,00 é recusado", async () =>
        {
            var est = await Servico();
            var a = await est.CriarProdutoAsync(Dados("Um centavo", preco: 0.01m), 0, Hoje, null);
            amb.ApagarNoFim("produto de 1 centavo", () => est.ExcluirProdutoAsync(a));
            var b = await est.CriarProdutoAsync(Dados("Preço máximo", preco: 99_999_999.99m), 0, Hoje, null);
            amb.ApagarNoFim("produto de preço máximo", () => est.ExcluirProdutoAsync(b));
            await Verificar.Recusa(() => est.CriarProdutoAsync(Dados("Preço demais", preco: 100_000_000m), 0, Hoje, null), "Preço de 100 milhões");
            var preco3casas = await est.CriarProdutoAsync(Dados("Três casas", preco: 1.239m), 0, Hoje, null);
            amb.ApagarNoFim("produto com 3 casas", () => est.ExcluirProdutoAsync(preco3casas));
            Verificar.Igual(1.24m, (await Ler(amb, preco3casas)).Preco, "Preço 1,239 guardado como");
        });
        l.Limite("Quantidade: 0,001 kg e 999.999.999,999 entram; 1 bilhão é recusado", async () =>
        {
            var est = await Servico();
            var id = await CriarProduto(amb, "Quantidade limite", quantidade: 0, porPeso: true);
            await est.CriarLoteAsync(id, 0.001m, Hoje, null);
            await est.CriarLoteAsync(id, 999_999_999.999m, Hoje, null);
            await Verificar.Recusa(() => est.CriarLoteAsync(id, 1_000_000_000m, Hoje, null), "Lote de 1 bilhão");
            var p = await Ler(amb, id);
            Verificar.Igual(1_000_000_000.000m, p.Quantidade, "Soma dos lotes");
        });
        l.Limite("Validade padrão: 3650 dias entra; 3651 e 0 são recusados", async () =>
        {
            var est = await Servico();
            var id = await CriarProduto(amb, "Validade padrão");
            await est.AtualizarProdutoAsync(id, Dados("Validade padrão", dias: 3650));
            await Verificar.Recusa(() => est.AtualizarProdutoAsync(id, Dados("Validade padrão", dias: 3651)), "3651 dias");
            await Verificar.Recusa(() => est.AtualizarProdutoAsync(id, Dados("Validade padrão", dias: 0)), "0 dias");
        });
        l.Limite("Lote com validade igual à compra e de 29/02", async () =>
        {
            var est = await Servico();
            var id = await CriarProduto(amb, "Validade igual", quantidade: 0);
            await est.CriarLoteAsync(id, 1, Hoje, Hoje);
            await est.CriarLoteAsync(id, 1, new DateOnly(2028, 2, 1), new DateOnly(2028, 2, 29));
            Verificar.Igual(2, (await Ler(amb, id)).Lotes.Count, "Lotes");
        });
        l.Limite("Código da balança (PLU): 1 e 99999 entram; 100000 é recusado", async () =>
        {
            var est = await Servico();
            var id = await CriarProduto(amb, "PLU limite", porPeso: true, quantidade: 0);
            var d = Dados("PLU limite", porPeso: true);
            await est.AtualizarProdutoAsync(id, d with { CodigoBalanca = 99999 });
            await Verificar.Recusa(() => est.AtualizarProdutoAsync(id, d with { CodigoBalanca = 100000 }), "PLU 100000");
            await Verificar.Recusa(() => est.AtualizarProdutoAsync(id, d with { CodigoBalanca = 0 }), "PLU 0");
        });
        return l;
    }
}
