using System.Text.Json;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Testes.Motor;

namespace MercadinhoSeuZe.Testes.Testes;

/// <summary>
/// Fechamento do dia e Análises: ler o resumo de qualquer dia, as análises por dia/semana/mês e a configuração;
/// recusar dia futuro, período e agrupamento inválidos e dia passado sem senha. Fechar e reabrir o dia de verdade só
/// num projeto Supabase de teste (fecha os caixas de todo mundo).
/// </summary>
public static class FechamentoTestes
{
    /// <summary>Erro 500 do Supabase nas análises = a consulta passou do tempo limite do banco (8 s para usuários logados).</summary>
    public static async Task<T> SemEstourar<T>(Func<Task<T>> acao, string oQue)
    {
        try
        {
            return await acao();
        }
        catch (ConexaoException ex) when (ex.Message.Contains("(500)"))
        {
            throw new FalhaTeste($"{oQue}: o banco passou do tempo limite (8 s) e a tela mostraria \"Erro no servidor (500)\". Com anos de vendas a função analise_vendas fica lenta.");
        }
    }

    public static Lista Montar(Ambiente amb)
    {
        var l = new Lista("Fechamento e Análises");
        async Task<FechamentoService> Servico() => new(amb.Api!, await amb.Entrar("gerente"));
        var hoje = DateTime.Today;

        l.Certo("Resumo de hoje, de ontem e do mesmo dia da semana passada", async () =>
        {
            var srv = await Servico();
            foreach (var dia in new[] { hoje, hoje.AddDays(-1), hoje.AddDays(-7) })
            {
                var r = await Verificar.Rapido(() => srv.ResumoAsync(dia), TimeSpan.FromSeconds(3), $"Resumo de {dia:dd/MM}");
                Verificar.Igual(dia.Date, r.Dia.Date, "Dia do resumo");
                Verificar.Verdadeiro(r.Vendas >= 0 && r.Total >= 0, "Totais negativos");
                Ambiente.Nota($"{dia:dd/MM}: {r.Vendas} vendas, R$ {r.Total:N2}, {(r.Fechado ? "fechado" : "aberto")}");
            }
        });
        l.Certo("Análises por dia (30), semana (12) e mês (12) respondem e somam igual", async () =>
        {
            var srv = await Servico();
            foreach (var (inicio, agrupar) in new[] { (hoje.AddDays(-29), "dia"), (hoje.AddDays(-7 * 12), "semana"), (hoje.AddMonths(-12), "mes") })
            {
                var a = await SemEstourar(() => Verificar.Rapido(() => srv.AnaliseAsync(inicio, hoje, agrupar), TimeSpan.FromSeconds(4), $"Análise por {agrupar}"), $"Análise por {agrupar}");
                Verificar.Dinheiro(a.Total, a.Serie.Sum(p => p.Total), $"Total x soma da série por {agrupar}");
                Verificar.Igual(a.Vendas, a.Serie.Sum(p => p.Vendas), $"Vendas x soma da série por {agrupar}");
                Verificar.Dinheiro(a.Total, a.Formas.Values.Sum(), $"Total x soma das formas de pagamento por {agrupar}");
            }
        });
        l.Certo("Configuração do e-mail do resumo é lida", async () =>
        {
            var srv = await Servico();
            var c = await srv.ConfigAsync();
            Ambiente.Nota($"E-mails do resumo: {c.EmailsResumo ?? "(nenhum)"} · automático: {(c.ResumoAutomatico ? "sim" : "não")}");
            if (string.IsNullOrWhiteSpace(c.EmailsResumo)) Ambiente.AvisoPendente("Nenhum e-mail cadastrado para receber o resumo do dia.");
        });

        l.Errado("Fechar dia que ainda não chegou é recusado", async () =>
        {
            var srv = await Servico();
            await Verificar.Recusa(() => srv.FecharDiaAsync(hoje.AddDays(1), null), "Fechar amanhã", "ainda não chegou");
        });
        l.Errado("Dia que já passou só muda com a senha do gerente", async () =>
        {
            var srv = await Servico();
            // Sem o script 012 o banco fecharia o dia antigo sem pedir senha: confere antes que o 012 está lá
            try { await amb.ContarAsync("eventos_calendario", srv.Sessao.Token); }
            catch (FalhaTeste) { throw new PularTeste("O banco ainda não tem o script 012 (senha do gerente em dia passado)."); }
            await Verificar.Recusa(() => srv.FecharDiaAsync(hoje.AddDays(-400), null), "Fechar dia de mais de um ano atrás sem senha");
            await Verificar.Recusa(() => srv.ReabrirDiaAsync(hoje.AddDays(-400)), "Reabrir dia antigo sem senha");
        });
        l.Errado("Reabrir dia que não está fechado é recusado", async () =>
        {
            var srv = await Servico();
            await Verificar.Recusa(() => srv.ReabrirDiaAsync(hoje.AddDays(5)), "Reabrir dia futuro", "não está fechado");
        });
        l.Errado("Análise com período invertido ou agrupamento inventado é recusada", async () =>
        {
            var srv = await Servico();
            await Verificar.Recusa(() => srv.AnaliseAsync(hoje, hoje.AddDays(-10), "dia"), "Início depois do fim", "Período inválido");
            await Verificar.Recusa(() => srv.AnaliseAsync(hoje.AddDays(-10), hoje, "ano"), "Agrupar por ano", "Agrupamento inválido");
        });
        l.Errado("Salvar configuração com e-mail de pedidos inválido é recusado", async () =>
        {
            var srv = await Servico();
            var atual = await srv.ConfigAsync();
            await Verificar.Recusa(() => srv.SalvarConfigAsync(new Models.ConfigMercado
            {
                EmailsResumo = atual.EmailsResumo, EmailPedidos = "isto não é e-mail", ResumoAutomatico = atual.ResumoAutomatico
            }), "E-mail de pedidos inválido");
        });

        l.Limite("Resumo de um dia de 10 anos atrás (sem vendas) e de 29/02", async () =>
        {
            var srv = await Servico();
            var r = await srv.ResumoAsync(hoje.AddYears(-10));
            Verificar.Igual(0, r.Vendas, "Vendas de 10 anos atrás");
            Verificar.Igual(0m, r.TicketMedio, "Ticket médio sem vendas");
            await srv.ResumoAsync(new DateTime(2024, 2, 29));
        });
        l.Limite("Análise: período de até 800 dias entra; 801 dias é recusado", async () =>
        {
            var srv = await Servico();
            await Verificar.Recusa(() => srv.AnaliseAsync(hoje.AddDays(-800), hoje, "mes"), "801 dias", "Período inválido");
            var a = await SemEstourar(() => Verificar.Rapido(() => srv.AnaliseAsync(hoje.AddDays(-799), hoje, "mes"), TimeSpan.FromSeconds(5), "800 dias por mês"), "800 dias por mês");
            Verificar.Verdadeiro(a.Serie.Count <= 28, $"Série com {a.Serie.Count} meses");
            Ambiente.Nota($"800 dias: {a.Vendas:N0} vendas, R$ {a.Total:N2}");
        });
        l.Limite("Análise de 20 anos por dia (pedido exagerado) não derruba o banco", async () =>
        {
            var srv = await Servico();
            try
            {
                var a = await Verificar.Rapido(() => srv.AnaliseAsync(hoje.AddYears(-20), hoje, "dia"), TimeSpan.FromSeconds(15), "20 anos por dia");
                Ambiente.Nota($"{a.Serie.Count} pontos");
            }
            catch (ConexaoException ex)
            {
                Ambiente.Nota($"Recusado: \"{ex.Message}\"");
            }
            // Depois do pedido pesado, o banco continua respondendo
            await srv.ResumoAsync(hoje);
        });

        l.Certo("Fechar o dia com todos os caixas fechados, ver o resumo gravado e reabrir", async () =>
        {
            if (amb.BancoDoMercado)
                throw new PularTeste("Fechar o dia fecha os caixas de todo mundo: só roda num projeto Supabase de teste.");
            var srv = await Servico();
            var caixa = new CaixaService(amb.Api!, srv.Sessao);
            var r = await srv.ResumoAsync(hoje);
            if (r.Fechado) await srv.ReabrirDiaAsync(hoje);
            var fechei = new List<long>();
            foreach (var c in (await caixa.CaixasDeHojeAsync()).Where(c => !c.Fechado))
            {
                var resumo = await caixa.ResumoAsync(c.Id);
                if (resumo.EmEspera > 0 || (await caixa.VendaAbertaAsync(c.Id)) is not null)
                    throw new PularTeste($"O caixa de {c.OperadorNome} tem venda em andamento.");
                await caixa.FecharAsync(c.Id, resumo.Esperado, "Teste automático");
                fechei.Add(c.Id);
            }
            try
            {
                var fechado = await srv.FecharDiaAsync(hoje, "Teste automático");
                Verificar.Verdadeiro(fechado.Fechado, "O resumo devolvido não diz que o dia está fechado");
                await Verificar.Recusa(() => srv.FecharDiaAsync(hoje, null), "Fechar duas vezes", "já foi fechado");
                await srv.ReabrirDiaAsync(hoje);
                Verificar.Verdadeiro(!(await srv.ResumoAsync(hoje)).Fechado, "O dia continua fechado depois de reabrir");
            }
            finally
            {
                foreach (var id in fechei) await caixa.ReabrirAsync(id);
            }
        });
        l.Errado("Fechar o dia com caixa aberto é recusado", async () =>
        {
            var srv = await Servico();
            var caixa = new CaixaService(amb.Api!, srv.Sessao);
            var abertos = (await caixa.CaixasDeHojeAsync()).Where(c => !c.Fechado).ToList();
            var r = await srv.ResumoAsync(hoje);
            if (abertos.Count == 0 || r.Fechado) throw new PularTeste("Nenhum caixa aberto hoje (ou o dia já está fechado).");
            await Verificar.Recusa(() => srv.FecharDiaAsync(hoje, null), "Fechar o dia com caixa aberto", "Feche antes os caixas");
        });
        return l;
    }
}
