using System.Text.Json;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Testes.Motor;

namespace MercadinhoSeuZe.Testes.Testes;

/// <summary>
/// Caixa pelo CaixaService do programa. Primeiro o que não grava nada (o banco recusa antes). Depois, só quando o
/// ambiente permite movimento (projeto de teste ou --movimento): abrir o caixa, vender com promoção, balança, desconto,
/// pagamento dividido, venda em espera, cancelamentos com a senha do gerente, sangria, suprimento e fechamento.
/// </summary>
public static class CaixaTestes
{
    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Today);

    public static Lista Montar(Ambiente amb)
    {
        var l = new Lista("Caixa");
        async Task<CaixaService> Operador(string papel = "operador") => new(amb.Api!, await amb.Entrar(papel));
        async Task<CaixaService> Gerente() => new(amb.Api!, await amb.Entrar("gerente"));
        UsuarioTeste G() => amb.Usuarios["gerente"];

        void ExigirMovimento()
        {
            if (!amb.PodeMovimentar)
                throw new PularTeste("Grava venda/caixa, que não dá para apagar. Roda num projeto Supabase de teste ou com --movimento.");
        }

        // Caixa do operador aberto hoje, sem venda pela metade (cancela a que sobrou de uma rodada anterior).
        async Task<(CaixaService Op, long CaixaId)> CaixaPronto(string papel = "operador")
        {
            var op = await Operador(papel);
            var caixa = await op.MeuCaixaAsync();
            if (caixa?.Fechado == true) await (await Gerente()).ReabrirAsync(caixa.Id);
            var id = caixa?.Id ?? await op.AbrirAsync(150m);
            if (await op.VendaAbertaAsync(id) is { } aberta)
            {
                var aut = await op.AutorizarAsync(G().Usuario, G().Senha, "cancelar_venda", aberta.Id);
                await op.CancelarAsync(aberta.Id, aut);
            }
            return (op, id);
        }

        async Task CancelarVendaAberta(CaixaService op, long caixaId)
        {
            if (await op.VendaAbertaAsync(caixaId) is { } v)
            {
                var aut = await op.AutorizarAsync(G().Usuario, G().Senha, "cancelar_venda", v.Id);
                await op.CancelarAsync(v.Id, aut);
            }
        }

        // ---------------------------------------------------------------- Sem gravar
        l.Errado("Troco inicial negativo ou acima de R$ 100.000 é recusado", async () =>
        {
            var op = await Operador();
            await Verificar.Recusa(() => op.AbrirAsync(-1), "Troco -1", "troco inicial válido");
            await Verificar.Recusa(() => op.AbrirAsync(100000.01m), "Troco de R$ 100.000,01", "troco inicial válido");
        });
        l.Errado("Vender sem abrir o caixa é recusado", async () =>
        {
            var op = await Operador("operador2");
            if (await op.MeuCaixaAsync() is not null) throw new PularTeste($"{amb.Usuarios["operador2"].Usuario} já abriu o caixa hoje.");
            var p = await EstoqueTestes.CriarProduto(amb, "Sem caixa");
            await Verificar.Recusa(() => op.ReservarAsync(p, 1, true), "Vender sem caixa", "Abra o caixa");
            await Verificar.Recusa(() => op.MovimentoAsync("suprimento", 10, null, null), "Suprimento sem caixa", "caixa precisa estar aberto");
        });
        l.Errado("Venda, caixa e autorização que não existem são recusados", async () =>
        {
            var op = await Operador();
            await Verificar.Recusa(() => op.FinalizarAsync(-1, [new PagamentoVenda { Forma = "pix", Valor = 1 }], null), "Finalizar venda -1");
            await Verificar.Recusa(() => op.SuspenderAsync(-1), "Pôr em espera a venda -1");
            await Verificar.Recusa(() => op.RetomarAsync(-1), "Retomar a venda -1");
            await Verificar.Recusa(() => op.ResumoAsync(-1), "Resumo do caixa -1", "não encontrado");
            await Verificar.Recusa(() => op.FecharAsync(-1, 0, null), "Fechar o caixa -1", "não encontrado");
            await Verificar.Recusa(() => op.AutorizarAsync("x", "y", "apagar_tudo", null), "Autorização com ação inventada", "Ação inválida");
        });
        l.Errado("Operador não reabre caixa (só o gerente)", async () =>
        {
            var op = await Operador();
            await Verificar.Recusa(() => op.ReabrirAsync(-1), "Operador reabrindo caixa", "Só o gerente");
        });

        // ---------------------------------------------------------------- Venda completa
        l.Certo("Venda completa: leitor, promoção, balança, desconto, CPF e pagamento em dinheiro + crédito", async () =>
        {
            ExigirMovimento();
            var (op, caixaId) = await CaixaPronto();
            var a = await EstoqueTestes.CriarProduto(amb, "Caixa A", preco: 5m, quantidade: 10, validade: Hoje.AddDays(100));
            var plu = Random.Shared.Next(90000, 99999);
            var b = await EstoqueTestes.CriarProduto(amb, "Caixa B kg", preco: 40m, quantidade: 5, porPeso: true, validade: Hoje.AddDays(5), plu: plu);
            var r1 = await op.ReservarAsync(a, 2, true);
            Verificar.Dinheiro(10m, r1.Subtotal, "2 × R$ 5,00");
            var r2 = await op.ReservarAsync(a, 1, true);
            Verificar.Igual(3m, r2.Quantidade, "Leitor somando no mesmo item");
            var etiqueta = CodigoBarras.Etiqueta([new Produto { Id = b, UnidadeMedida = "kg", Preco = 40m, CodigoBalanca = plu }],
                CodigoBarras.ComDigito($"2{plu:00000}002000"));
            var leitura = Verificar.NaoNulo(etiqueta, "Etiqueta da balança");
            var rb = await op.ReservarAsync(b, leitura.PesoEtiqueta!.Value, true, leitura.ValorEtiqueta);
            Verificar.Dinheiro(20m, rb.Subtotal, "Etiqueta de R$ 20,00");
            Verificar.Igual(0.5m, rb.Quantidade, "Peso da etiqueta");

            // Diminuir um item lido precisa do gerente
            await Verificar.Recusa(() => op.ReservarAsync(a, 2, false), "Diminuir item sem o gerente", "autorização do gerente");
            var autItem = Verificar.NaoNulo(await op.AutorizarAsync(G().Usuario, G().Senha, "cancelar_item", r1.VendaId), "Autorização do gerente");
            await op.ReservarAsync(a, 2, false, null, autItem);

            await Verificar.Recusa(() => op.AplicarDescontoAsync(r1.VendaId, 1m, null), "Desconto sem o gerente", "autorização do gerente");
            var autDesc = await op.AutorizarAsync(G().Usuario, G().Senha, "desconto", r1.VendaId);
            var total = await op.AplicarDescontoAsync(r1.VendaId, 1m, autDesc);
            Verificar.Dinheiro(29m, total, "Total com desconto (10 + 20 − 1)");

            var resultado = await op.FinalizarAsync(r1.VendaId,
            [
                new PagamentoVenda { Forma = "dinheiro", Valor = 9m, Recebido = 50m, Troco = 41m },
                new PagamentoVenda { Forma = "credito", Valor = 20m, Parcelas = 3, Bandeira = "Visa", Nsu = "123456", FinalCartao = "1234" },
            ], "52998224725");
            Verificar.Dinheiro(29m, resultado.Total, "Total pago");
            Verificar.Dinheiro(41m, resultado.Troco, "Troco");
            var pa = await EstoqueTestes.Ler(amb, a);
            Verificar.Igual(8m, pa.Quantidade, "Estoque de A depois da venda");
            Verificar.Igual(4.5m, (await EstoqueTestes.Ler(amb, b)).Quantidade, "Estoque de B (kg)");
            var venda = (await (await Gerente()).VendasDeHojeAsync()).FirstOrDefault(v => v.Id == r1.VendaId)
                        ?? throw new FalhaTeste("A venda não aparece em \"Vendas de hoje\".");
            Verificar.Igual("Dinheiro + Crédito", venda.ResumoFormas, "Formas");
            Verificar.Igual("52998224725", venda.CpfNota, "CPF na nota");

            // O gerente cancela a venda paga: o estoque volta
            await (await Gerente()).CancelarAsync(r1.VendaId);
            Verificar.Igual(10m, (await EstoqueTestes.Ler(amb, a)).Quantidade, "Estoque de A depois do cancelamento");
        });
        l.Errado("No caixa: mais do que tem, fração de produto por unidade, só lote vencido, quantidade negativa e produto que não existe", async () =>
        {
            ExigirMovimento();
            var (op, caixaId) = await CaixaPronto();
            var a = await EstoqueTestes.CriarProduto(amb, "Pouco", preco: 2m, quantidade: 3, validade: Hoje.AddDays(30));
            var vencido = await EstoqueTestes.CriarProduto(amb, "Vencido", preco: 2m, quantidade: 5, validade: Hoje.AddDays(-1));
            await Verificar.Recusa(() => op.ReservarAsync(a, 4, true), "4 de 3", "Só há");
            await Verificar.Recusa(() => op.ReservarAsync(a, 1.5m, true), "1,5 de produto por unidade", "por unidade");
            await Verificar.Recusa(() => op.ReservarAsync(vencido, 1, true), "Lote vencido", "lote vencido");
            await Verificar.Recusa(() => op.ReservarAsync(a, -1, true), "Quantidade -1", "Quantidade inválida");
            await Verificar.Recusa(() => op.ReservarAsync(-1, 1, true), "Produto -1", "não existe");
            await CancelarVendaAberta(op, caixaId);
        });
        l.Errado("Pagamento errado: falta dinheiro, forma inventada, CPF inválido, 13 parcelas e venda vazia", async () =>
        {
            ExigirMovimento();
            var (op, caixaId) = await CaixaPronto();
            var a = await EstoqueTestes.CriarProduto(amb, "Pagamento", preco: 10m, quantidade: 5, validade: Hoje.AddDays(30));
            var r = await op.ReservarAsync(a, 1, true);
            await Verificar.Recusa(() => op.FinalizarAsync(r.VendaId, [], null), "Sem pagamento", "Receba o pagamento");
            await Verificar.Recusa(() => op.FinalizarAsync(r.VendaId, [new PagamentoVenda { Forma = "pix", Valor = 9.99m }], null), "Pagou 9,99 de 10,00", "não fecham");
            await Verificar.Recusa(() => op.FinalizarAsync(r.VendaId, [new PagamentoVenda { Forma = "pix", Valor = 10.01m }], null), "Pagou 10,01 de 10,00", "não fecham");
            await Verificar.Recusa(() => op.FinalizarAsync(r.VendaId, [new PagamentoVenda { Forma = "dinheiro", Valor = 10m, Recebido = 5m }], null), "Recebeu 5 de 10 em dinheiro");
            await Verificar.Recusa(() => op.FinalizarAsync(r.VendaId, [new PagamentoVenda { Forma = "cheque", Valor = 10m }], null), "Cheque", "Pagamento inválido");
            await Verificar.Recusa(() => op.FinalizarAsync(r.VendaId, [new PagamentoVenda { Forma = "pix", Valor = 10m }], "12345678900"), "CPF inválido", "CPF ou CNPJ");
            await Verificar.Recusa(() => op.FinalizarAsync(r.VendaId, [new PagamentoVenda { Forma = "credito", Valor = 10m, Parcelas = 13 }], null), "13 parcelas");
            await Verificar.Recusa(() => op.FinalizarAsync(r.VendaId, [new PagamentoVenda { Forma = "pix", Valor = -10m }], null), "Pagamento negativo");
            var aut = await op.AutorizarAsync(G().Usuario, G().Senha, "desconto", r.VendaId);
            await Verificar.Recusa(() => op.AplicarDescontoAsync(r.VendaId, 10.01m, aut), "Desconto maior que a venda", "entre R$ 0,00");
            await Verificar.Recusa(() => (amb.Api!).ChamarAsync<JsonElement>("cancelar_venda", new { p_venda_id = r.VendaId, p_autorizacao = (long?)null }, op.Sessao.Token),
                "Operador cancelando venda com itens sem o gerente", "autorização do gerente");
            await CancelarVendaAberta(op, caixaId);
        });
        l.Errado("Senha do gerente errada não autoriza; autorização usada uma vez não vale de novo", async () =>
        {
            ExigirMovimento();
            var (op, caixaId) = await CaixaPronto();
            var a = await EstoqueTestes.CriarProduto(amb, "Autorização", preco: 3m, quantidade: 5, validade: Hoje.AddDays(30));
            var r = await op.ReservarAsync(a, 2, true);
            Verificar.Nulo(await op.AutorizarAsync(G().Usuario, "senha-errada", "cancelar_item", r.VendaId), "Autorização com senha errada");
            Verificar.Nulo(await op.AutorizarAsync(amb.Usuarios["operador"].Usuario, amb.Usuarios["operador"].Senha, "cancelar_item", r.VendaId), "Operador autorizando como gerente");
            var aut = await op.AutorizarAsync(G().Usuario, G().Senha, "cancelar_item", r.VendaId);
            await op.ReservarAsync(a, 1, false, null, aut);
            await Verificar.Recusa(() => op.ReservarAsync(a, 0, false, null, aut), "Mesma autorização de novo (tirar o item)");
            await Verificar.Recusa(() => op.AplicarDescontoAsync(r.VendaId, 1m, aut), "Autorização de item usada para desconto", "autorização do gerente");
            await CancelarVendaAberta(op, caixaId);
        });
        l.Certo("Venda em espera: suspender, atender outro cliente e retomar", async () =>
        {
            ExigirMovimento();
            var (op, caixaId) = await CaixaPronto();
            var a = await EstoqueTestes.CriarProduto(amb, "Espera", preco: 4m, quantidade: 5, validade: Hoje.AddDays(30));
            var r = await op.ReservarAsync(a, 2, true);
            await op.SuspenderAsync(r.VendaId);
            Verificar.Verdadeiro((await op.VendasEmEsperaAsync(caixaId)).Any(v => v.Id == r.VendaId), "Venda não está em espera");
            var reservado = (await new EstoqueService(amb.Api!, op.Sessao).ReservadosAsync()).GetValueOrDefault(a);
            Verificar.Igual(2m, reservado, "Itens da venda em espera continuam reservados");
            var outra = await op.ReservarAsync(a, 1, true);
            Verificar.Verdadeiro(outra.VendaId != r.VendaId, "A venda nova deveria ser outra");
            await Verificar.Recusa(() => op.RetomarAsync(r.VendaId), "Retomar com outra venda aberta", "Termine ou ponha em espera");
            await CancelarVendaAberta(op, caixaId);
            await op.RetomarAsync(r.VendaId);
            await CancelarVendaAberta(op, caixaId);
            Verificar.Igual(0m, (await new EstoqueService(amb.Api!, op.Sessao).ReservadosAsync()).GetValueOrDefault(a), "Reserva depois de cancelar");
        });
        l.Seguranca("Dois caixas tentando vender a última unidade ao mesmo tempo: só um consegue", async () =>
        {
            ExigirMovimento();
            var (op1, c1) = await CaixaPronto("operador");
            var (op2, c2) = await CaixaPronto("operador2");
            var a = await EstoqueTestes.CriarProduto(amb, "Última unidade", preco: 1m, quantidade: 1, validade: Hoje.AddDays(30));
            var t1 = Tentar(() => op1.ReservarAsync(a, 1, true));
            var t2 = Tentar(() => op2.ReservarAsync(a, 1, true));
            await Task.WhenAll(t1, t2);
            Verificar.Igual(1, (t1.Result ? 1 : 0) + (t2.Result ? 1 : 0), "Caixas que conseguiram reservar a única unidade");
            await CancelarVendaAberta(op1, c1);
            await CancelarVendaAberta(op2, c2);
        });
        l.Certo("Suprimento, sangria com o gerente, resumo da gaveta e fechamento com falta; o gerente reabre", async () =>
        {
            ExigirMovimento();
            var (op, caixaId) = await CaixaPronto();
            var antes = await op.ResumoAsync(caixaId);
            await op.MovimentoAsync("suprimento", 50m, "Troco extra (teste)", null);
            await Verificar.Recusa(() => op.MovimentoAsync("sangria", 10m, "teste", null), "Sangria sem o gerente", "autorização do gerente");
            var aut = await op.AutorizarAsync(G().Usuario, G().Senha, "sangria", null);
            var depois = await op.MovimentoAsync("sangria", 10m, "Cofre (teste)", aut);
            Verificar.Dinheiro(antes.Esperado + 40m, depois.Esperado, "Gaveta depois de +50 e −10");
            Verificar.Dinheiro(depois.TrocoInicial + depois.Dinheiro + depois.Suprimentos - depois.Sangrias, depois.Esperado, "Conta da gaveta (troco + dinheiro + suprimentos − sangrias)");
            var aut2 = await op.AutorizarAsync(G().Usuario, G().Senha, "sangria", null);
            await Verificar.Recusa(() => op.MovimentoAsync("sangria", depois.Esperado + 0.01m, "teste", aut2), "Sangria maior que a gaveta", "não pode passar");
            await Verificar.Recusa(() => op.MovimentoAsync("roubo", 1m, null, null), "Movimento inventado", "inválido");
            await Verificar.Recusa(() => op.MovimentoAsync("suprimento", 0m, null, null), "Suprimento de R$ 0,00", "valor válido");

            var fechado = await op.FecharAsync(caixaId, depois.Esperado - 1m, "Teste automático: falta de R$ 1,00");
            Verificar.Dinheiro(-1m, fechado.Diferenca ?? 0, "Diferença (falta de R$ 1,00)");
            var a = await EstoqueTestes.CriarProduto(amb, "Depois de fechar", preco: 1m, quantidade: 1, validade: Hoje.AddDays(30));
            await Verificar.Recusa(() => op.ReservarAsync(a, 1, true), "Vender com o caixa fechado", "já foi fechado");
            await Verificar.Recusa(() => op.FecharAsync(caixaId, 0, null), "Fechar duas vezes", "já foi fechado");
            await (await Gerente()).ReabrirAsync(caixaId);
            Verificar.Verdadeiro((await op.MeuCaixaAsync())?.Fechado == false, "O gerente reabriu, mas o caixa continua fechado");
        });
        l.Limite("Item com 9.999 unidades, 50 leituras seguidas do mesmo produto e venda de R$ 99.999,99", async () =>
        {
            ExigirMovimento();
            var (op, caixaId) = await CaixaPronto();
            var a = await EstoqueTestes.CriarProduto(amb, "Muitos", preco: 0.01m, quantidade: 10_000, validade: Hoje.AddDays(30));
            var r = await op.ReservarAsync(a, 9_999, true);
            Verificar.Dinheiro(99.99m, r.Subtotal, "9.999 × R$ 0,01");
            await Verificar.Recusa(() => op.ReservarAsync(a, 100_000, false), "100.000 unidades num item", "Quantidade inválida");
            await CancelarVendaAberta(op, caixaId);
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            ResultadoReserva? ultimo = null;
            for (var i = 0; i < 50; i++) ultimo = await op.ReservarAsync(a, 1, true);
            relogio.Stop();
            Ambiente.Nota($"50 leituras: {relogio.Elapsed.TotalSeconds:0.0} s ({relogio.Elapsed.TotalMilliseconds / 50:0} ms cada)");
            Verificar.Igual(50m, ultimo!.Quantidade, "Quantidade depois de 50 leituras");
            if (relogio.Elapsed.TotalMilliseconds / 50 > 800) Ambiente.AvisoPendente($"Cada leitura no caixa leva {relogio.Elapsed.TotalMilliseconds / 50:0} ms.");
            await CancelarVendaAberta(op, caixaId);
            var caro = await EstoqueTestes.CriarProduto(amb, "Caro", preco: 99_999.99m, quantidade: 1, validade: Hoje.AddDays(30));
            var rc = await op.ReservarAsync(caro, 1, true);
            var fim = await op.FinalizarAsync(rc.VendaId, [new PagamentoVenda { Forma = "pix", Valor = 99_999.99m }], null);
            Verificar.Dinheiro(99_999.99m, fim.Total, "Venda de R$ 99.999,99");
            await (await Gerente()).CancelarAsync(rc.VendaId);
        });
        return l;
    }

    private static async Task<bool> Tentar(Func<Task> acao)
    {
        try { await acao(); return true; }
        catch (ConexaoException) { return false; }
    }
}
