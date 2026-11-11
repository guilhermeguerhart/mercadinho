using System.Net;
using System.Net.Http;
using System.Text.Json;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Testes.Motor;

namespace MercadinhoSeuZe.Testes.Testes;

/// <summary>
/// Previsão de compras (semana 7, scripts 016, 019 e 020): as funções da tela respondem rápido, a previsão está em dia
/// e erra menos que "repetir o ano passado", a conta do pedido sugerido fecha (caixas inteiras, estoque e pendente
/// descontados), a faixa de 80% acerta perto de 8 em 10 semanas e o laboratório soma igual por dia, semana e mês.
/// O operador e quem não entrou não leem nada disso nem recalculam. Só lê: pode rodar no banco do mercado.
/// </summary>
public static class PrevisaoTestes
{
    private static readonly string[] TiposDeAlerta = ["falta", "parado", "queda", "alta", "data"];

    public static Lista Montar(Ambiente amb)
    {
        var l = new Lista("Previsão");
        async Task<PrevisaoService> Servico(string papel = "gerente") => new(amb.Api!, await amb.Entrar(papel));
        var hoje = DateOnly.FromDateTime(DateTime.Today);

        // ------------------------------------------------------------ O que precisa dar certo

        l.Certo("As funções da tela respondem em menos de 3 s", async () =>
        {
            var srv = await Servico();
            var tela = await Verificar.Rapido(() => Erros.SemScriptPula(srv.TelaAsync, "019"), TimeSpan.FromSeconds(3), "Topo e teste (previsao_tela)");
            var sugeridos = await Verificar.Rapido(srv.SugeridosAsync, TimeSpan.FromSeconds(3), "Pedido sugerido");
            await Verificar.Rapido(srv.AlertasAsync, TimeSpan.FromSeconds(3), "Alertas");
            var produto = sugeridos.SelectMany(s => s.Itens).Select(i => (long?)i.ProdutoId).FirstOrDefault() ?? tela.Melhores.Select(p => (long?)p.ProdutoId).FirstOrDefault();
            if (produto is { } id) await Verificar.Rapido(() => srv.ProdutoAsync(id), TimeSpan.FromSeconds(3), "Um produto (previsao_produto)");
            try
            {
                await Verificar.Rapido(() => srv.LaboratorioAsync("todos", null, null, "dia"), TimeSpan.FromSeconds(3), "Laboratório, todos os produtos por dia");
            }
            catch (TabelaInexistenteException)
            {
                Ambiente.Nota("Laboratório: o banco ainda não tem o script 020.");
            }
        });

        l.Certo("A previsão foi recalculada nas últimas 26 horas", async () =>
        {
            var tela = await Erros.SemScriptPula((await Servico()).TelaAsync, "019");
            var g = Verificar.NaoNulo(tela.Geral, "A previsão nunca foi calculada (rode select public.previsao_calcular();)");
            var idade = DateTimeOffset.Now - g.CalculadoEm;
            Ambiente.Nota($"Calculada em {g.CalculadoEm.ToLocalTime():dd/MM/yyyy HH:mm} (há {idade.TotalHours:0.#} h), vendas de {g.TreinoIni:dd/MM/yyyy} a {g.TreinoFim:dd/MM/yyyy}");
            if (idade > TimeSpan.FromHours(26))
                Ambiente.AvisoPendente($"A previsão tem {idade.TotalHours:0} h: o agendamento da madrugada (pg_cron, mercado-previsao) não rodou.");
            Verificar.Verdadeiro(g.TreinoFim is null || g.TreinoFim >= hoje.AddDays(-3), $"O modelo usou vendas só até {g.TreinoFim:dd/MM/yyyy}");
        });

        l.Certo("Erra menos que repetir o ano passado e que a média dos 60 dias", async () =>
        {
            var tela = await Erros.SemScriptPula((await Servico()).TelaAsync, "019");
            var g = Verificar.NaoNulo(tela.Geral, "Previsão");
            if (g.ErroSemana is not { } erro) throw new PularTeste("Ainda sem teste: precisa de mais de 1 ano de vendas.");
            Ambiente.Nota($"Último ano, por produto e semana: previsão {erro:P1} · ano passado {g.ErroIngenuo:P1} · média de 60 dias {g.ErroMedia:P1} · viés {g.Vies:P1}");
            Verificar.Verdadeiro(erro < (g.ErroIngenuo ?? decimal.MaxValue), $"A previsão ({erro:P1}) errou mais que repetir o ano passado ({g.ErroIngenuo:P1})");
            Verificar.Verdadeiro(erro < (g.ErroMedia ?? decimal.MaxValue), $"A previsão ({erro:P1}) errou mais que a média dos 60 dias ({g.ErroMedia:P1})");
            if (g.Vies is { } vies && Math.Abs(vies) > 0.05m)
                Ambiente.AvisoPendente($"Viés de {vies:P1}: a previsão está sistematicamente {(vies > 0 ? "acima" : "abaixo")} do que vende.");
            if (tela.Anos.Count > 0)
            {
                var perdeu = tela.Anos.Where(a => (a.Erro ?? 0) >= (a.ErroIngenuo ?? decimal.MaxValue)).Select(a => a.Ano).ToList();
                Ambiente.Nota($"{tela.Anos.Count} anos testados; perdeu do ano passado em: {(perdeu.Count == 0 ? "nenhum" : string.Join(", ", perdeu))}");
                if (perdeu.Count > 0) Ambiente.AvisoPendente($"Em {perdeu.Count} ano(s) a previsão errou mais que repetir o ano passado: {string.Join(", ", perdeu)}.");
            }
        });

        l.Certo("Pedido sugerido: conta certa, caixas fechadas e datas em ordem", async () =>
        {
            var sugeridos = await Erros.SemScriptPula((await Servico()).SugeridosAsync, "019");
            if (sugeridos.Count == 0) throw new PularTeste("Nenhum fornecedor ativo.");
            var itens = 0;
            foreach (var s in sugeridos)
            {
                Verificar.Verdadeiro(s.DataPedido >= hoje, $"{s.Fornecedor}: dia de pedir ({s.DataPedido:dd/MM}) já passou");
                Verificar.Igual(s.DataPedido == hoje, s.PedirHoje, $"{s.Fornecedor}: \"pedir hoje\"");
                Verificar.Verdadeiro(s.Chegada >= s.DataPedido, $"{s.Fornecedor}: chega ({s.Chegada:dd/MM}) antes de pedir ({s.DataPedido:dd/MM})");
                Verificar.Verdadeiro(s.ProximaEntrega > s.Chegada, $"{s.Fornecedor}: a entrega seguinte ({s.ProximaEntrega:dd/MM}) não vem depois da chegada ({s.Chegada:dd/MM})");
                foreach (var i in s.Itens)
                {
                    itens++;
                    var falta = i.Previsto + i.Margem - i.Estoque - i.Pendente;
                    var passo = i.PorPeso ? 1 : Math.Max(i.Caixa, 1);
                    Verificar.Verdadeiro(i.Sugerido > 0, $"{i.Nome}: sugerido {i.Sugerido} (só entra o que falta)");
                    Verificar.Verdadeiro(i.PorPeso ? i.Sugerido == Math.Ceiling(i.Sugerido) : i.Sugerido % passo == 0,
                        $"{i.Nome}: {i.Sugerido} não é {(i.PorPeso ? "kg inteiro" : $"caixa fechada de {passo:0}")}");
                    // previsto e margem chegam arredondados em 0,1 (a conta do banco usa o valor cheio): folga de 0,1
                    Verificar.Verdadeiro(i.Sugerido + 0.11m >= falta && i.Sugerido - falta < passo + 0.11m,
                        $"{i.Nome}: sugerido {i.Sugerido}, mas previsto {i.Previsto} + margem {i.Margem} − estoque {i.Estoque} − pendente {i.Pendente} = {falta:0.###}");
                    Verificar.Verdadeiro(i.Previsto >= 0 && i.Margem >= 0 && i.Estoque >= 0 && i.Pendente >= 0, $"{i.Nome}: número negativo no pedido sugerido");
                    Verificar.Verdadeiro(i.Ate >= s.Chegada, $"{i.Nome}: a conta vai só até {i.Ate:dd/MM}, antes da chegada ({s.Chegada:dd/MM})");
                }
                Verificar.Dinheiro(s.Itens.Sum(i => i.Sugerido * (i.Custo ?? 0)), s.Total, $"{s.Fornecedor}: total x soma dos itens");
            }
            Ambiente.Nota($"{sugeridos.Count} fornecedores, {itens} itens sugeridos, {sugeridos.Count(s => s.PedirHoje)} para pedir hoje");
        });

        l.Integridade("Pedido sugerido só tem fornecedor ativo e produto desse fornecedor", async () =>
        {
            var g = await amb.Entrar("gerente");
            var sugeridos = await Erros.SemScriptPula(new PrevisaoService(amb.Api!, g).SugeridosAsync, "019");
            var fornecedores = (await amb.Api!.ListarAsync<JsonElement>("fornecedores?select=id,ativo", g.Token)).ToDictionary(f => f.Long("id"), f => f.GetProperty("ativo").GetBoolean());
            var produtos = (await amb.Api.ListarAsync<JsonElement>("produtos?select=id,fornecedor_id", g.Token))
                .ToDictionary(p => p.Long("id"), p => p.GetProperty("fornecedor_id").ValueKind == JsonValueKind.Number ? p.Long("fornecedor_id") : (long?)null);
            foreach (var s in sugeridos)
            {
                Verificar.Verdadeiro(fornecedores.TryGetValue(s.FornecedorId, out var ativo) && ativo, $"{s.Fornecedor}: fornecedor desativado ou apagado no pedido sugerido");
                foreach (var i in s.Itens)
                    Verificar.Verdadeiro(produtos.TryGetValue(i.ProdutoId, out var f) && f == s.FornecedorId, $"{i.Nome} aparece no pedido de {s.Fornecedor}, mas é de outro fornecedor");
            }
        });

        l.Certo("Um produto: até 12 semanas atrás, 8 à frente e a faixa em ordem", async () =>
        {
            var srv = await Servico();
            var tela = await Erros.SemScriptPula(srv.TelaAsync, "019");
            var sugeridos = await srv.SugeridosAsync();
            var ids = tela.Melhores.Concat(tela.Piores).Select(p => p.ProdutoId).Concat(sugeridos.SelectMany(s => s.Itens).Select(i => i.ProdutoId)).Distinct().Take(6).ToList();
            if (ids.Count == 0) throw new PularTeste("Nenhum produto com previsão.");
            var segunda = hoje.AddDays(-(((int)hoje.DayOfWeek + 6) % 7));
            foreach (var id in ids)
            {
                var p = Verificar.NaoNulo(await srv.ProdutoAsync(id), $"Produto {id}");
                Verificar.Verdadeiro(p.Passado.Count <= 12, $"{p.Nome}: {p.Passado.Count} semanas passadas (máximo 12)");
                Verificar.Verdadeiro(p.Futuro.Count is >= 1 and <= 8, $"{p.Nome}: {p.Futuro.Count} semanas à frente");
                Verificar.Verdadeiro(p.Futuro.All(w => w.Semana >= segunda), $"{p.Nome}: semana \"futura\" antes desta semana");
                foreach (var w in p.Futuro)
                    Verificar.Verdadeiro(w.Minimo >= 0 && w.Minimo <= w.Previsto + 0.001m && w.Previsto <= w.Maximo + 0.001m,
                        $"{p.Nome}, semana de {w.Semana:dd/MM}: mínimo {w.Minimo}, previsto {w.Previsto}, máximo {w.Maximo} fora de ordem");
                foreach (var serie in new[] { p.Passado.Select(w => w.Semana).ToList(), p.Futuro.Select(w => w.Semana).ToList() })
                    for (var k = 1; k < serie.Count; k++)
                        Verificar.Igual(7, serie[k].DayNumber - serie[k - 1].DayNumber, $"{p.Nome}: distância entre as semanas {serie[k - 1]:dd/MM} e {serie[k]:dd/MM}");
                Verificar.Verdadeiro(p.Passado.All(w => w.Vendido >= 0 && w.Previsto >= 0), $"{p.Nome}: venda ou previsão negativa no passado");
                if (p.IdxSemana is { Count: > 0 } sem)
                {
                    Verificar.Igual(7, sem.Count, $"{p.Nome}: índices do dia da semana");
                    var media = sem.Average();
                    if (Math.Abs(media - 1) > 0.1m) Ambiente.AvisoPendente($"{p.Nome}: os índices do dia da semana têm média {media:0.00} (esperado perto de 1).");
                }
                if (p.IdxMes is { Count: > 0 } mes) Verificar.Igual(12, mes.Count, $"{p.Nome}: índices do mês");
            }
            Ambiente.Nota($"{ids.Count} produtos conferidos");
        });

        l.Certo("A faixa de 80% acerta perto de 8 em 10 semanas (e dias)", async () =>
        {
            var srv = await Servico();
            var semana = await Laboratorio(() => srv.LaboratorioAsync("todos", null, null, "semana"));
            var dia = await srv.LaboratorioAsync("todos", null, null, "dia");
            var fs = Verificar.NaoNulo(semana.Resumo?.DentroFaixa, "Faixa por semana");
            var fd = Verificar.NaoNulo(dia.Resumo?.DentroFaixa, "Faixa por dia");
            Ambiente.Nota($"Dentro da faixa: {fs:P1} das semanas, {fd:P1} dos dias (produto a produto)");
            foreach (var (nome, v) in new[] { ("semanas", fs), ("dias", fd) })
                if (v is < 0.7m or > 0.9m) Ambiente.AvisoPendente($"A faixa de 80% pegou {v:P1} dos {nome}: precisa recalibrar (fica fora de 70% a 90%).");
        });

        l.Certo("Laboratório: dia, semana e mês somam igual; produtos somam o todo", async () =>
        {
            var srv = await Servico();
            var por = new Dictionary<string, LaboratorioPrevisao>();
            foreach (var g in new[] { "dia", "semana", "mes" })
                por[g] = await Laboratorio(() => srv.LaboratorioAsync("todos", null, null, g));
            var total = Verificar.NaoNulo(por["dia"].Resumo?.Vendido, "Vendido no teste");
            foreach (var (g, lab) in por)
            {
                Verificar.Verdadeiro(Math.Abs(lab.Serie.Sum(s => s.Vendido) - total) <= Math.Max(1, total * 0.001m), $"Por {g}: a série soma {lab.Serie.Sum(s => s.Vendido):N2}, o total é {total:N2}");
                Verificar.Verdadeiro(Math.Abs((lab.Resumo?.Previsto ?? 0) - (por["dia"].Resumo?.Previsto ?? 0)) <= 1, $"Por {g}: previsto total diferente do de por dia");
                for (var k = 1; k < lab.Serie.Count; k++)
                    Verificar.Verdadeiro(lab.Serie[k].Periodo > lab.Serie[k - 1].Periodo, $"Por {g}: períodos fora de ordem ({lab.Serie[k - 1].Periodo} e {lab.Serie[k].Periodo})");
            }
            Verificar.Verdadeiro(Math.Abs(por["semana"].Produtos.Sum(p => p.Vendido) - total) <= Math.Max(1, total * 0.001m), "A lista de produtos não soma o total");
            var dias = por["dia"].TesteFim!.Value.DayNumber - por["dia"].TesteIni!.Value.DayNumber + 1;
            Verificar.Igual(dias, por["dia"].Serie.Count, "Dias na série por dia");
            // Erro cai quando junta mais tempo (dia > semana > mês): erros de dias diferentes se compensam
            var (ed, es, em) = (por["dia"].Resumo!.Erro ?? 0, por["semana"].Resumo!.Erro ?? 0, por["mes"].Resumo!.Erro ?? 0);
            Ambiente.Nota($"Erro por produto: dia {ed:P1} · semana {es:P1} · mês {em:P1}");
            Verificar.Verdadeiro(ed >= es && es >= em, $"O erro deveria cair de dia ({ed:P1}) para semana ({es:P1}) e mês ({em:P1})");

            var loja = await srv.LaboratorioAsync("loja", null, null, "semana");
            Ambiente.Nota($"Movimento da loja por semana: erro {loja.Resumo?.Erro:P1} (ano passado {loja.Resumo?.ErroIngenuo:P1})");
            var categoria = por["semana"].CategoriasLista.FirstOrDefault();
            if (categoria is not null)
            {
                var cat = await srv.LaboratorioAsync("categoria", categoria, null, "semana");
                var esperado = por["semana"].Produtos.Where(p => p.Categoria == categoria).Sum(p => p.Vendido);
                Verificar.Verdadeiro(Math.Abs((cat.Resumo?.Vendido ?? 0) - esperado) <= 1, $"Categoria {categoria}: {cat.Resumo?.Vendido:N2} x soma dos produtos {esperado:N2}");
            }
            var produto = por["semana"].Produtos.FirstOrDefault();
            if (produto is not null)
            {
                var p = await srv.LaboratorioAsync("produto", null, produto.ProdutoId, "semana");
                Verificar.Verdadeiro(Math.Abs((p.Resumo?.Vendido ?? 0) - produto.Vendido) <= 0.01m, $"{produto.Produto}: vendido no laboratório x na lista");
                Verificar.Igual(produto.Produto, p.Nome, "Nome do produto no laboratório");
            }
        });

        l.Integridade("Alertas têm tipo conhecido e apontam para um produto que existe", async () =>
        {
            var g = await amb.Entrar("gerente");
            var alertas = await Erros.SemScriptPula(new PrevisaoService(amb.Api!, g).AlertasAsync, "019");
            var produtos = (await amb.Api!.ListarAsync<JsonElement>("produtos?select=id", g.Token)).Select(p => p.Long("id")).ToHashSet();
            foreach (var a in alertas)
            {
                Verificar.Verdadeiro(TiposDeAlerta.Contains(a.Tipo), $"Alerta de tipo desconhecido: \"{a.Tipo}\" ({a.Texto})");
                Verificar.Verdadeiro(a.Texto.Length > 0, $"Alerta sem texto ({a.Tipo}, {a.Produto})");
                if (a.ProdutoId is { } id) Verificar.Verdadeiro(produtos.Contains(id), $"Alerta aponta para o produto {id}, que não existe");
            }
            Ambiente.Nota(alertas.Count == 0 ? "Nenhum alerta" : string.Join(" · ", alertas.GroupBy(a => a.Tipo).Select(x => $"{x.Key}: {x.Count()}")));
        });

        l.Limite("Produto que não existe devolve vazio, sem erro", async () =>
        {
            var p = await Erros.SemScriptPula(async () => await (await Servico()).ProdutoAsync(987654321), "019");
            Verificar.Verdadeiro(p is null || (p.Passado.Count == 0 && p.Futuro.Count == 0), "Veio previsão para um produto que não existe");
        });

        l.Errado("Laboratório recusa escopo e agrupamento inventados", async () =>
        {
            var srv = await Servico();
            await Laboratorio(() => srv.LaboratorioAsync("todos", null, null, "semana"));
            await Verificar.Recusa(() => srv.LaboratorioAsync("planeta", null, null, "semana"), "Escopo inventado", "Escopo inválido");
            await Verificar.Recusa(() => srv.LaboratorioAsync("todos", null, null, "ano"), "Agrupar por ano", "Agrupamento inválido");
            await Verificar.Recusa(() => srv.LaboratorioAsync("categoria", null, null, "semana"), "Categoria sem nome", "Escolha a categoria");
            await Verificar.Recusa(() => srv.LaboratorioAsync("produto", null, null, "semana"), "Produto sem id", "Escolha o produto");
        });

        // ------------------------------------------------------------ Quem pode

        l.Seguranca("Operador não vê a previsão, o pedido sugerido, os alertas nem o laboratório", async () =>
        {
            await Erros.SemScriptPula((await Servico()).TelaAsync, "019");     // o gerente lê: as funções existem
            var op = await Servico("operador");
            await Verificar.Recusa(op.TelaAsync, "Operador lendo a previsão", "gerente");
            await Verificar.Recusa(op.SugeridosAsync, "Operador lendo o pedido sugerido", "gerente");
            await Verificar.Recusa(op.AlertasAsync, "Operador lendo os alertas", "gerente");
            await Verificar.Recusa(() => op.ProdutoAsync(1), "Operador lendo a previsão de um produto", "gerente");
            try { await Verificar.Recusa(() => op.LaboratorioAsync("todos", null, null, "semana"), "Operador no laboratório", "gerente"); }
            catch (FalhaTeste ex) when (ex.Message.Contains("ainda não foram criadas", StringComparison.OrdinalIgnoreCase)) { Ambiente.Nota("Laboratório: sem o script 020."); }
        });

        l.Seguranca("Operador não recalcula a previsão nem mexe no simulador", async () =>
        {
            var op = await amb.Entrar("operador");
            // Recusa precisa vir da regra "só o gerente" (e não do tempo limite: o cálculo leva ~20 s)
            await Verificar.Recusa(() => amb.Api!.ChamarAsync<string>("previsao_calcular", new { }, op.Token), "Operador recalculando a previsão", "gerente");
            await Verificar.Recusa(() => amb.Api!.ChamarAsync<string>("previsao_testar_anos", new { }, op.Token), "Operador rodando o teste de todos os anos", "gerente");
            try
            {
                await Verificar.Recusa(() => amb.Api!.ChamarAsync<string>("simular", new { }, op.Token), "Operador fazendo a loja simular", "gerente");
                await Verificar.Recusa(() => amb.Api!.ChamarAsync<string>("simulacao_ligar", new { p_ligada = true }, op.Token), "Operador ligando o simulador", "gerente");
            }
            catch (FalhaTeste ex) when (ex.Message.Contains("ainda não foram criadas", StringComparison.OrdinalIgnoreCase))
            {
                Ambiente.Nota("Simulador: sem o script 018.");
            }
            await Verificar.Recusa(() => amb.Api!.ChamarAsync<string>("previsao_guardar_teste", new { p_ini = hoje.AddDays(-10), p_fim = hoje }, op.Token), "Operador chamando a função interna do teste");
        });

        l.Seguranca("Operador não lê as tabelas da previsão nem as vendas perdidas", async () =>
        {
            var op = await amb.Entrar("operador");
            foreach (var tabela in new[] { "previsao_geral", "previsao_modelos", "previsao_dias", "previsao_eventos", "previsao_teste_semanas",
                         "previsao_teste_anos", "previsao_teste_dias", "previsao_teste_loja", "simulacao_faltas" })
            {
                long n;
                try { n = await amb.ContarAsync(tabela, op.Token); }
                catch (FalhaTeste ex) when (ex.Message.Contains("404") || ex.Message.Contains("does not exist") || ex.Message.Contains("PGRST205"))
                {
                    Ambiente.Nota($"{tabela}: não existe neste banco");
                    continue;
                }
                catch (FalhaTeste) { continue; }      // recusado (401/403): também está protegido
                Verificar.Igual(0L, n, $"Linhas de {tabela} que o operador consegue ler");
            }
        });

        l.Seguranca("Sem entrar no sistema ninguém chama as funções da previsão", async () =>
        {
            foreach (var f in new[] { "previsao_tela", "pedido_sugerido", "previsao_alertas", "previsao_laboratorio", "previsao_calcular" })
            {
                var (status, corpo, _) = await amb.Http(HttpMethod.Post, $"rest/v1/rpc/{f}", null, new { });
                Verificar.Verdadeiro(status != HttpStatusCode.OK, $"{f} respondeu sem login: {corpo[..Math.Min(corpo.Length, 120)]}");
            }
        });
        return l;
    }

    /// <summary>Laboratório (script 020): sem o script o teste fica "pulado", com o motivo.</summary>
    private static async Task<LaboratorioPrevisao> Laboratorio(Func<Task<LaboratorioPrevisao>> acao)
    {
        LaboratorioPrevisao lab;
        try
        {
            lab = await acao();
        }
        catch (TabelaInexistenteException)
        {
            throw new PularTeste("O banco ainda não tem o script 020 (laboratório da previsão).");
        }
        if (lab.Resumo is null || lab.Serie.Count == 0) throw new PularTeste("Ainda sem teste dia a dia (rode select public.previsao_calcular();).");
        return lab;
    }
}
