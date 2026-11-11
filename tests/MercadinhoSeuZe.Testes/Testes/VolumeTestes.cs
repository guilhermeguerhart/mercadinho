using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Testes.Motor;

namespace MercadinhoSeuZe.Testes.Testes;

/// <summary>
/// Muitos registros: as listas do programa trazem tudo (o Supabase devolve no máximo 1.000 linhas por consulta se
/// ninguém pedir as outras), e quanto tempo as telas levam para ler. Cria 1.200 produtos de teste de uma vez quando o
/// banco tem poucos, e apaga no fim.
/// </summary>
public static class VolumeTestes
{
    public static Lista Montar(Ambiente amb)
    {
        var l = new Lista("Volume");

        l.Volume("Estoque lê TODOS os produtos, mesmo com mais de 1.000", async () =>
        {
            if (amb.Rapido) throw new PularTeste("--rapido: sem os 1.200 produtos de teste.");
            var g = await amb.Entrar("gerente");
            var noBanco = await amb.ContarAsync("produtos", g.Token);
            if (noBanco <= 1000)
            {
                var faltam = (int)(1200 - noBanco);
                var linhas = Enumerable.Range(1, faltam).Select(i => new
                {
                    nome = $"{amb.Marca} V{i:0000}", categoria = (i % 5) switch { 0 => "Bebidas", 1 => "Mercearia", 2 => "Limpeza", 3 => "Padaria", _ => "Frios" },
                    preco = 1 + i % 50, estoque_minimo = i % 10
                }).ToArray();
                amb.ApagarNoFim("produtos de volume", () => amb.Api!.ExcluirAsync("produtos", $"nome=like.{Uri.EscapeDataString(amb.Marca + " V*")}", g.Token));
                var r = Stopwatch.StartNew();
                var (status, corpo, _) = await amb.Http(HttpMethod.Post, "rest/v1/produtos", g.Token, linhas);
                Verificar.Verdadeiro((int)status < 300, $"Gravar {faltam} produtos: {(int)status} {corpo}");
                Ambiente.Nota($"{faltam} produtos de teste gravados em {r.Elapsed.TotalSeconds:0.0} s");
                noBanco = await amb.ContarAsync("produtos", g.Token);
            }
            var est = new EstoqueService(amb.Api!, g);
            var lista = await Verificar.Rapido(() => est.ListarAsync(), TimeSpan.FromSeconds(4), $"Ler {noBanco} produtos com os lotes");
            Ambiente.Nota($"No banco: {noBanco} · a tela do Estoque recebeu: {lista.Count}");
            Verificar.Igual(noBanco, (long)lista.Count, "Produtos que chegam ao Estoque (o resto some da tela, do caixa e dos avisos)");
        });
        l.Volume("Fornecedores lê TODOS os pedidos para o \"último pedido\" de cada um", async () =>
        {
            var g = await amb.Entrar("gerente");
            var noBanco = await amb.ContarAsync("pedidos", g.Token);
            var lidos = await new FornecedorService(amb.Api!, g).ResumoPedidosAsync();
            Ambiente.Nota($"Pedidos no banco: {noBanco} · lidos pela tela: {lidos.Count}");
            Verificar.Igual(noBanco, (long)lidos.Count, "Pedidos que chegam à tela de Fornecedores");
        });
        l.Volume("Pedidos dos últimos 90 dias e pendentes chegam inteiros", async () =>
        {
            var g = await amb.Entrar("gerente");
            var desde = DateTime.Today.AddDays(-FornecedorService.DiasRecentes).ToString("yyyy-MM-dd");
            var noBanco = await amb.ContarAsync($"pedidos?or=(situacao.eq.pendente,data_pedido.gte.{desde})", g.Token);
            var lidos = await Verificar.Rapido(() => new FornecedorService(amb.Api!, g).ListarPedidosAsync(), TimeSpan.FromSeconds(4), "Pedidos de 90 dias com itens");
            Verificar.Igual(noBanco, (long)lidos.Count, "Pedidos recentes");
        });
        l.Volume("Dia com mais vendas cabe na lista \"Vendas de hoje\" (até 1.000)", async () =>
        {
            var g = await amb.Entrar("gerente");
            var a = await FechamentoTestes.SemEstourar(() => new FechamentoService(amb.Api!, g).AnaliseAsync(DateTime.Today.AddDays(-180), DateTime.Today, "dia"), "Análise de 180 dias");
            if (a.Serie.Count == 0) throw new PularTeste("Sem vendas nos últimos 180 dias.");
            var maior = a.Serie.MaxBy(p => p.Vendas)!;
            Ambiente.Nota($"Dia com mais vendas nos últimos 180 dias: {maior.Periodo:dd/MM/yyyy}, {maior.Vendas} vendas");
            if (maior.Vendas > 1000)
                throw new FalhaTeste($"Em {maior.Periodo:dd/MM/yyyy} houve {maior.Vendas} vendas: a lista \"Vendas de hoje\" do Caixa mostraria só 1.000.");
            if (maior.Vendas > 700) Ambiente.AvisoPendente("Os dias mais cheios estão chegando perto do limite de 1.000 vendas da lista do Caixa.");
        });
        l.Volume("Painel: 6 leituras juntas (estoque, promoções, pedidos, vendas, caixas, mensagens) em menos de 5 s", async () =>
        {
            var g = await amb.Entrar("gerente");
            var est = new EstoqueService(amb.Api!, g);
            var forn = new FornecedorService(amb.Api!, g);
            var cx = new CaixaService(amb.Api!, g);
            var msg = new MensagensService(amb.Api!, g);
            await Verificar.Rapido(() => Task.WhenAll(est.ListarComPromocoesAsync(), forn.ListarPendentesAsync(), forn.ListarAsync(),
                cx.VendasDeHojeAsync(), cx.CaixasDeHojeAsync(), msg.NaoLidasAsync()), TimeSpan.FromSeconds(5), "Leituras do Painel");
        });
        l.Volume("Consulta grande de uma vez (todas as vendas de 1 ano com itens) responde ou é cortada sem travar", async () =>
        {
            var g = await amb.Entrar("gerente");
            var desde = DateTime.Today.AddYears(-1).ToString("yyyy-MM-dd");
            var r = Stopwatch.StartNew();
            try
            {
                var (status, corpo, _) = await amb.Http(HttpMethod.Get, $"rest/v1/vendas?dia=gte.{desde}&select=id,total,venda_itens(*)", g.Token);
                r.Stop();
                Ambiente.Nota($"{(int)status} em {r.Elapsed.TotalSeconds:0.0} s, {corpo.Length / 1024:N0} KB");
                Verificar.Verdadeiro((int)status < 500, $"O banco recusou a consulta grande com erro {(int)status}: {corpo[..Math.Min(200, corpo.Length)]}");
            }
            catch (TaskCanceledException)
            {
                Ambiente.AvisoPendente($"A consulta grande passou de {amb.Api!.Http.Timeout.TotalSeconds:0} s e o programa desistiu (sem travar).");
            }
            // Depois dela, o banco continua respondendo
            await amb.Api!.ListarAsync<JsonElement>("perfis?select=id&limit=1", g.Token);
        });
        return l;
    }
}
