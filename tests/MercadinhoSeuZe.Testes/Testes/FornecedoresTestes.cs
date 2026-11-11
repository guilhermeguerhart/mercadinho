using System.Net.Http;
using System.Text.Json;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Testes.Motor;

namespace MercadinhoSeuZe.Testes.Testes;

/// <summary>
/// Fornecedores, pedidos, entrega (inteira e parcial, com custo e nota), mensagens e vários fornecedores por produto,
/// pelo FornecedorService do programa. Fornecedores e pedidos de teste são apagados no fim.
/// </summary>
public static class FornecedoresTestes
{
    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Today);

    public static async Task<long> CriarFornecedor(Ambiente amb, string sufixo, string categorias = "Teste automático", string? dias = null)
    {
        var g = await amb.Entrar("gerente");
        var srv = new FornecedorService(amb.Api!, g);
        var nome = $"{amb.Marca} {sufixo}";
        await srv.CriarAsync(new DadosFornecedor(nome, "(11) 4002-1122", null, "teste@example.com", "Contato Teste", categorias, 2, null, null,
            DiasEntrega: dias));
        var id = (await amb.Api!.ListarAsync<JsonElement>($"fornecedores?nome=eq.{Uri.EscapeDataString(nome)}&select=id", g.Token)).Single().Long("id");
        amb.ApagarNoFim($"fornecedor {nome}", async () =>
        {
            await amb.Http(HttpMethod.Delete, $"rest/v1/pedidos?fornecedor_id=eq.{id}", g.Token);
            await srv.ExcluirAsync(id);
        });
        return id;
    }

    public static Lista Montar(Ambiente amb)
    {
        var l = new Lista("Fornecedores");
        async Task<FornecedorService> Servico() => new(amb.Api!, await amb.Entrar("gerente"));
        DadosFornecedor Dados(string nome, string telefone = "11988887777", string? email = null, int? prazo = 3, string? dias = null,
            string? modelo = null, string? obs = null) =>
            new($"{amb.Marca} {nome}", telefone, null, email, null, "Teste", prazo, modelo, obs, DiasEntrega: dias);
        async Task<Pedido> LerPedido(long id)
        {
            var srv = await Servico();
            var pedidos = await srv.ListarPedidosAsync();
            return pedidos.FirstOrDefault(p => p.Id == id) ?? throw new FalhaTeste($"Pedido {id} não apareceu na lista.");
        }

        l.Certo("Cadastrar, editar (dias de entrega e pedido mínimo), desativar e reativar fornecedor", async () =>
        {
            var srv = await Servico();
            var id = await CriarFornecedor(amb, "Laticínios");
            await srv.AtualizarAsync(id, Dados("Laticínios", dias: "1,3") with { PedidoMinimo = 300m, Categorias = "Laticínios, Frios" });
            var f = (await srv.ListarAsync()).First(x => x.Id == id);
            Verificar.Igual("seg, qua", f.TextoDiasEntrega, "Dias de entrega");
            Verificar.Igual(300m, f.PedidoMinimo, "Pedido mínimo");
            await srv.AtivarAsync(id, false);
            Verificar.Verdadeiro(!(await srv.ListarAsync()).First(x => x.Id == id).Ativo, "Deveria estar desativado");
            await srv.AtivarAsync(id, true);
        });
        l.Certo("Pedido completo: criar, editar, receber metade com custo e nota, o resto vira pedido novo", async () =>
        {
            var srv = await Servico();
            var fid = await CriarFornecedor(amb, "Pedido completo");
            var p1 = await EstoqueTestes.CriarProduto(amb, "Item pedido A", quantidade: 0);
            var p2 = await EstoqueTestes.CriarProduto(amb, "Item pedido B", quantidade: 0, porPeso: true);
            var pid = await srv.CriarPedidoAsync(fid, Hoje.AddDays(3), "teste", [new(p1, 10, 2.50m), new(p2, 5.5m, 30m)]);
            await srv.EditarPedidoAsync(pid, Hoje.AddDays(4), "teste editado", [new(p1, 12, 2.50m), new(p2, 5.5m, 30m)]);
            var pedido = await LerPedido(pid);
            Verificar.Igual(12m, pedido.Itens.First(i => i.ProdutoId == p1).Quantidade, "Quantidade depois de editar");
            Verificar.Dinheiro(12 * 2.5m + 5.5m * 30m, pedido.Total ?? 0, "Total do pedido");
            var itemA = pedido.Itens.First(i => i.ProdutoId == p1);
            var itemB = pedido.Itens.First(i => i.ProdutoId == p2);
            await srv.RegistrarEntregaAsync(pid, Hoje, [new(itemA.Id, 6, Hoje.AddDays(90), 2.40m, "faltou"), new(itemB.Id, 5.5m, Hoje.AddDays(10), 31m)],
                "NF123", restoPendente: true, previsaoResto: Hoje.AddDays(5));
            var entregue = await LerPedido(pid);
            Verificar.Igual("entregue", entregue.Situacao, "Situação do pedido");
            Verificar.Igual("NF123", entregue.NumeroNota, "Número da nota");
            var resto = (await srv.ListarPedidosAsync()).FirstOrDefault(p => p.PedidoOrigemId == pid)
                        ?? throw new FalhaTeste("O que faltou não virou pedido novo.");
            Verificar.Igual(6m, resto.Itens.Single().Quantidade, "Quantidade do pedido do resto");
            var produto = await EstoqueTestes.Ler(amb, p1);
            Verificar.Igual(6m, produto.Quantidade, "Estoque depois da entrega");
            Verificar.Igual(2.40m, produto.PrecoCusto, "Custo atualizado pela entrega");
            Verificar.Igual(Hoje.AddDays(90), produto.ProximaValidade, "Validade do lote recebido");
            Verificar.Verdadeiro(produto.Vinculos.Any(v => v.FornecedorId == fid), "O produto deveria ficar ligado ao fornecedor (último custo)");
        });
        l.Certo("Cancelar pedido pendente (fica no histórico)", async () =>
        {
            var srv = await Servico();
            var fid = await CriarFornecedor(amb, "Cancelar");
            var p = await EstoqueTestes.CriarProduto(amb, "Item cancelar", quantidade: 0);
            var pid = await srv.CriarPedidoAsync(fid, null, null, [new(p, 1)]);
            await srv.CancelarPedidoAsync(pid);
            Verificar.Igual("cancelado", (await srv.PedidosDoFornecedorAsync(fid)).Single().Situacao, "Situação");
        });
        l.Certo("Vários fornecedores por produto (\"Também compro de\")", async () =>
        {
            var g = await amb.Entrar("gerente");
            var est = new EstoqueService(amb.Api!, g);
            var f1 = await CriarFornecedor(amb, "Vinculo 1");
            var f2 = await CriarFornecedor(amb, "Vinculo 2");
            var p = await EstoqueTestes.CriarProduto(amb, "Vinculos");
            await est.DefinirFornecedoresAsync(p, [f1, f2]);
            Verificar.Igual(2, (await EstoqueTestes.Ler(amb, p)).Vinculos.Count, "Fornecedores ligados");
            await est.DefinirFornecedoresAsync(p, [f2]);
            Verificar.Igual(f2, (await EstoqueTestes.Ler(amb, p)).Vinculos.Single().FornecedorId, "Depois de tirar um");
        });
        l.Certo("Mensagem do WhatsApp registrada aparece na conversa (já como lida, foi o gerente que colou)", async () =>
        {
            var g = await amb.Entrar("gerente");
            var msg = new MensagensService(amb.Api!, g);
            var fid = await CriarFornecedor(amb, "Mensagens");
            await Erros.SemScriptPula(async () => { await msg.RegistrarWhatsappAsync(fid, null, "Pedido confirmado, chega quinta.", true); return 0; }, "009");
            var conversa = await msg.ConversaAsync(fid);
            Verificar.Igual(1, conversa.Count, "Mensagens na conversa");
            Verificar.Verdadeiro(conversa[0].PorWhatsapp && conversa[0].Recebida, "Canal e direção da mensagem");
            Verificar.Verdadeiro(conversa[0].LidaEm is not null, "Mensagem colada pelo gerente deveria já estar lida");
            Verificar.Verdadeiro(!(await msg.NaoLidasAsync()).Any(n => n.FornecedorId == fid), "Não deveria contar como não lida");
            await msg.MarcarLidasAsync(fid);
        });

        // ---------------------------------------------------------------- Errado
        l.Errado("Telefone com 9 dígitos, e-mail inválido e nome em branco são recusados", async () =>
        {
            var srv = await Servico();
            await Verificar.Recusa(() => srv.CriarAsync(Dados("Fone curto", telefone: "119888877")), "Telefone com 9 dígitos");
            await Verificar.Recusa(() => srv.CriarAsync(Dados("Fone longo", telefone: "119888877771")), "Telefone com 12 dígitos");
            await Verificar.Recusa(() => srv.CriarAsync(Dados("Email", email: "fulano@semponto")), "E-mail sem ponto");
            await Verificar.Recusa(() => srv.CriarAsync(Dados("") with { Nome = "  " }), "Nome em branco");
        });
        l.Errado("Dias de entrega \"7\", \"seg\" e \"1,,3\" são recusados", async () =>
        {
            var srv = await Servico();
            foreach (var d in new[] { "7", "seg", "1,,3", "1;3" })
                await Verificar.Recusa(() => srv.CriarAsync(Dados("Dias " + d, dias: d)), $"Dias de entrega \"{d}\"");
        });
        l.Errado("Pedido sem itens, com produto que não existe ou quantidade zero é recusado", async () =>
        {
            var srv = await Servico();
            var fid = await CriarFornecedor(amb, "Pedido errado");
            await Verificar.Recusa(() => srv.CriarPedidoAsync(fid, null, null, []), "Pedido sem itens", "pelo menos um item");
            await Verificar.Recusa(() => srv.CriarPedidoAsync(fid, null, null, [new(-1, 5)]), "Produto inexistente", "não existe");
            var p = await EstoqueTestes.CriarProduto(amb, "Item zero", quantidade: 0);
            await Verificar.Recusa(() => srv.CriarPedidoAsync(fid, null, null, [new(p, 0)]), "Quantidade zero");
            await Verificar.Recusa(() => srv.CriarPedidoAsync(fid, Hoje.AddDays(-3), null, [new(p, 1)]), "Previsão antes do pedido");
            Verificar.Igual(0, (await srv.PedidosDoFornecedorAsync(fid)).Count, "Pedidos gravados pela metade");
        });
        l.Errado("Receber de novo, editar ou cancelar pedido já entregue é recusado", async () =>
        {
            var srv = await Servico();
            var fid = await CriarFornecedor(amb, "Já entregue");
            var p = await EstoqueTestes.CriarProduto(amb, "Item entregue", quantidade: 0);
            var pid = await srv.CriarPedidoAsync(fid, null, null, [new(p, 3)]);
            var item = (await LerPedido(pid)).Itens.Single();
            await srv.RegistrarEntregaAsync(pid, Hoje, [new(item.Id, 3, null)]);
            await Verificar.Recusa(() => srv.RegistrarEntregaAsync(pid, Hoje, [new(item.Id, 3, null)]), "Receber duas vezes", "não está mais pendente");
            await Verificar.Recusa(() => srv.EditarPedidoAsync(pid, null, null, [new(p, 5)]), "Editar entregue", "ainda não chegou");
            await Verificar.Recusa(() => srv.CancelarPedidoAsync(pid), "Cancelar entregue", "Nada foi alterado");
            Verificar.Igual(3m, (await EstoqueTestes.Ler(amb, p)).Quantidade, "Estoque não pode dobrar");
        });
        l.Errado("Fornecedor com pedidos não é apagado (só desativado)", async () =>
        {
            var srv = await Servico();
            var fid = await CriarFornecedor(amb, "Com pedido");
            var p = await EstoqueTestes.CriarProduto(amb, "Item histórico", quantidade: 0);
            await srv.CriarPedidoAsync(fid, null, null, [new(p, 1)]);
            await Verificar.Recusa(() => srv.ExcluirAsync(fid), "Apagar fornecedor com pedido");
        });
        l.Errado("Mensagem vazia, com mais de 5.000 letras ou de pedido de outro fornecedor é recusada", async () =>
        {
            var g = await amb.Entrar("gerente");
            var msg = new MensagensService(amb.Api!, g);
            var srv = await Servico();
            var f1 = await CriarFornecedor(amb, "Msg 1");
            var f2 = await CriarFornecedor(amb, "Msg 2");
            var p = await EstoqueTestes.CriarProduto(amb, "Item msg", quantidade: 0);
            var pid = await srv.CriarPedidoAsync(f2, null, null, [new(p, 1)]);
            await Verificar.Recusa(() => msg.RegistrarWhatsappAsync(f1, null, "", true), "Mensagem vazia");
            await Verificar.Recusa(() => msg.RegistrarWhatsappAsync(f1, null, new string('a', 5001), true), "Mensagem de 5.001 letras");
            await Verificar.Recusa(() => msg.RegistrarWhatsappAsync(f1, pid, "oi", true), "Pedido de outro fornecedor", "não é deste fornecedor");
            await msg.RegistrarWhatsappAsync(f1, null, new string('a', 5000), true);
        });

        // ---------------------------------------------------------------- Limite
        l.Limite("Prazo de entrega 0 e 90 dias entram; 91 e -1 são recusados", async () =>
        {
            var srv = await Servico();
            var fid = await CriarFornecedor(amb, "Prazo");
            await srv.AtualizarAsync(fid, Dados("Prazo", prazo: 0));
            await srv.AtualizarAsync(fid, Dados("Prazo", prazo: 90));
            await Verificar.Recusa(() => srv.AtualizarAsync(fid, Dados("Prazo", prazo: 91)), "Prazo 91");
            await Verificar.Recusa(() => srv.AtualizarAsync(fid, Dados("Prazo", prazo: -1)), "Prazo -1");
        });
        l.Limite("Modelo de mensagem com 1.000 letras e observação com 300 entram; 1.001 e 301 são recusados", async () =>
        {
            var srv = await Servico();
            var fid = await CriarFornecedor(amb, "Textos");
            await srv.AtualizarAsync(fid, Dados("Textos", modelo: new string('m', 1000), obs: new string('o', 300)));
            await Verificar.Recusa(() => srv.AtualizarAsync(fid, Dados("Textos", modelo: new string('m', 1001))), "Modelo com 1.001");
            await Verificar.Recusa(() => srv.AtualizarAsync(fid, Dados("Textos", obs: new string('o', 301))), "Observação com 301");
        });
        l.Limite("Pedido com 150 itens e quantidade de 999.999.999,999", async () =>
        {
            var srv = await Servico();
            var g = await amb.Entrar("gerente");
            var fid = await CriarFornecedor(amb, "Pedido grande");
            // 150 produtos de uma vez (um pedido só ao banco) e apagados no fim pelo prefixo
            var linhas = Enumerable.Range(1, 150).Select(i => new { nome = $"{amb.Marca} PG {i:000}", categoria = "Teste", preco = 1 }).ToArray();
            var (status, corpo, _) = await amb.Http(HttpMethod.Post, "rest/v1/produtos", g.Token, linhas, "return=representation");
            Verificar.Verdadeiro((int)status < 300, $"Gravar 150 produtos: {(int)status} {corpo}");
            var ids = JsonDocument.Parse(corpo).RootElement.EnumerateArray().Select(e => e.Long("id")).ToList();
            amb.ApagarNoFim("150 produtos do pedido grande", () => amb.Api!.ExcluirAsync("produtos", $"id=in.({string.Join(",", ids)})", g.Token));
            var itens = ids.Select((id, i) => new ItemNovoPedido(id, i == 0 ? 999_999_999.999m : i + 1, 1.99m)).ToList();
            var pid = await Verificar.Rapido(() => srv.CriarPedidoAsync(fid, Hoje.AddDays(2), null, itens), TimeSpan.FromSeconds(5), "Criar pedido de 150 itens");
            var pedido = await LerPedido(pid);
            Verificar.Igual(150, pedido.Itens.Count, "Itens gravados");
            Verificar.Igual(999_999_999.999m, pedido.Itens.Max(i => i.Quantidade), "Maior quantidade");
            await Verificar.Recusa(() => srv.CriarPedidoAsync(fid, null, null, [new(ids[0], 1_000_000_000m)]), "Quantidade de 1 bilhão");
        });
        return l;
    }
}
