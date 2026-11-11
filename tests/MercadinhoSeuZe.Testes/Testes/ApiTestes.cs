using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Testes.Motor;

namespace MercadinhoSeuZe.Testes.Testes;

/// <summary>
/// Os serviços que o sistema usa estão respondendo: login do Supabase, banco (tabelas e funções de todos os scripts),
/// Edge Functions do e-mail, Storage dos anexos e os bancos abertos de produtos (Open Food Facts).
/// </summary>
public static class ApiTestes
{
    /// <summary>Tabelas e o script que cria cada uma.</summary>
    public static readonly (string Tabela, string Script)[] Tabelas =
    [
        ("perfis", "001"), ("auditoria_login", "001"), ("pedidos_senha", "001"), ("contatos", "001"),
        ("produtos", "003"), ("lotes", "003"),
        ("fornecedores", "004"), ("pedidos", "004"), ("pedido_itens", "004"),
        ("produto_fornecedores", "005"),
        ("caixas", "006"), ("vendas", "006"), ("venda_itens", "006"), ("venda_item_lotes", "006"),
        ("venda_pagamentos", "007"), ("autorizacoes", "007"), ("venda_itens_cancelados", "007"),
        ("promocoes", "008"), ("promocoes_validade", "008"), ("caixa_movimentos", "008"),
        ("fechamentos_dia", "009"), ("config_mercado", "009"), ("fornecedor_mensagens", "009"),
        ("eventos_calendario", "012"),
    ];

    /// <summary>Funções que o programa e o app de celular chamam, com argumentos que não gravam nada.</summary>
    private static readonly (string Funcao, string Script, object Args)[] Funcoes =
    [
        ("listar_funcionarios", "006", new { }),
        ("meu_perfil", "006", new { }),
        ("reservados_hoje", "009", new { }),
        ("resumo_dia", "009", new { p_dia = DateTime.Today.ToString("yyyy-MM-dd") }),
        ("analise_vendas", "009", new { p_inicio = DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd"), p_fim = DateTime.Today.ToString("yyyy-MM-dd"), p_agrupar = "dia" }),
        ("mensagens_nao_lidas", "009", new { }),
    ];

    public static Lista Montar(Ambiente amb)
    {
        var l = new Lista("APIs e banco");

        l.Api("Login do Supabase (Auth) responde", async () =>
        {
            var (status, corpo, _) = await Verificar.Rapido(() => amb.Http(HttpMethod.Get, "auth/v1/health", null), TimeSpan.FromSeconds(3), "Auth");
            Verificar.Igual(HttpStatusCode.OK, status, "Auth respondeu " + corpo);
        });
        l.Api("Banco (REST) responde com a chave pública e o login do gerente", async () =>
        {
            var g = await amb.Entrar("gerente");
            await Verificar.Rapido(() => amb.Api!.ListarAsync<JsonElement>("perfis?select=id&limit=1", g.Token), TimeSpan.FromSeconds(2), "Primeira consulta");
        });
        l.Api("Todas as tabelas dos scripts 001 a 012 existem", async () =>
        {
            var g = await amb.Entrar("gerente");
            var faltando = new List<string>();
            foreach (var (tabela, script) in Tabelas)
            {
                var (status, corpo, _) = await amb.Http(HttpMethod.Get, $"rest/v1/{tabela}?select=*&limit=1", g.Token);
                if (!((int)status is >= 200 and < 300)) faltando.Add($"{tabela} (script {script}): {(int)status}{(corpo.Contains("PGRST205") ? " não existe" : "")}");
            }
            Verificar.Verdadeiro(faltando.Count == 0, "Tabelas com problema:\n" + string.Join("\n", faltando));
        });
        l.Api("Funções do banco usadas pelas telas e pelo celular respondem", async () =>
        {
            var g = await amb.Entrar("gerente");
            var problemas = new List<string>();
            foreach (var (funcao, script, args) in Funcoes)
            {
                var r = Stopwatch.StartNew();
                try
                {
                    await amb.Api!.ChamarAsync<JsonElement>(funcao, args, g.Token);
                    Ambiente.Nota($"{funcao}: {r.Elapsed.TotalMilliseconds:0} ms");
                }
                catch (TabelaInexistenteException)
                {
                    problemas.Add($"{funcao} (script {script}) não existe");
                }
                catch (ConexaoException ex)
                {
                    problemas.Add($"{funcao}: {ex.Message}");
                }
            }
            Verificar.Verdadeiro(problemas.Count == 0, string.Join("\n", problemas));
        });
        l.Api("Colunas novas dos scripts 005 a 008 existem (o Estoque e o Caixa não caem no modo antigo)", async () =>
        {
            var g = await amb.Entrar("gerente");
            var estoque = new EstoqueService(amb.Api!, g);
            await estoque.ListarAsync();
            await estoque.PromocoesAsync();
            var faltam = new List<string>();
            if (!estoque.TemFornecedores) faltam.Add("004 (fornecedor do produto)");
            if (!estoque.TemAjustes) faltam.Add("005 (custo, vários fornecedores)");
            if (!estoque.TemCaixa) faltam.Add("006 (código da balança)");
            if (!estoque.TemPromocoes) faltam.Add("008 (promoções)");
            var forn = new FornecedorService(amb.Api!, g);
            await forn.ListarAsync();
            if (!forn.TemAjustes) faltam.Add("005 (dias de entrega, pedido mínimo)");
            Verificar.Verdadeiro(faltam.Count == 0, "Scripts que faltam rodar: " + string.Join(", ", faltam));
        });
        l.Api("O programa avisa \"Nada foi alterado\" quando o banco não muda nenhuma linha", async () =>
        {
            // Sem permissão (ou registro que já não existe) o Supabase responde 204 com Content-Range "*/0".
            var g = await amb.Entrar("gerente");
            await Verificar.Recusa(() => amb.Api!.AtualizarAsync("produtos", "id=eq.-1", new { preco = 1 }, g.Token),
                "Alterar produto que não existe (o programa mostra \"salvo\" e nada mudou)", "Nada foi alterado");
        });
        l.Api("Funções do caixa e do fechamento existem (chamadas com dados que o banco recusa)", async () =>
        {
            var g = await amb.Entrar("gerente");
            // Cada chamada usa um id que não existe: se a função existe, ela responde com a mensagem dela.
            var chamadas = new (string Funcao, object Args)[]
            {
                ("resumo_caixa", new { p_caixa_id = -1 }),
                ("reabrir_caixa", new { p_caixa_id = -1 }),
                ("cancelar_venda", new { p_venda_id = -1, p_autorizacao = (long?)null }),
                ("editar_pedido", new { p_pedido_id = -1, p_previsao = (string?)null, p_observacao = (string?)null, p_itens = Array.Empty<object>() }),
                ("registrar_recebimento", new { p_pedido_id = -1, p_entregue_em = DateTime.Today.ToString("yyyy-MM-dd"), p_numero_nota = (string?)null, p_itens = Array.Empty<object>(), p_resto_pendente = false, p_previsao_resto = (string?)null }),
                ("registrar_mensagem_whatsapp", new { p_fornecedor_id = -1, p_pedido_id = (long?)null, p_corpo = "x", p_direcao = "recebida" }),
                ("reabrir_dia", new { p_dia = DateTime.Today.AddDays(30).ToString("yyyy-MM-dd") }),
            };
            var faltam = new List<string>();
            foreach (var (funcao, args) in chamadas)
            {
                try { await amb.Api!.ChamarAsync<JsonElement>(funcao, args, g.Token); }
                catch (TabelaInexistenteException) { faltam.Add(funcao); }
                catch (ConexaoException ex) { Ambiente.Nota($"{funcao}: \"{ex.Message}\""); }
            }
            Verificar.Verdadeiro(faltam.Count == 0, "Funções que não existem no banco: " + string.Join(", ", faltam));
        });
        l.Api("Edge Function enviar-email está publicada", async () =>
        {
            var g = await amb.Entrar("gerente");
            // Tipo inventado: a função responde "Tipo de e-mail desconhecido" (ou que o e-mail não foi configurado) e não envia nada.
            try
            {
                await amb.Api!.FuncaoAsync<JsonElement>("enviar-email", new { tipo = "teste_automatico" }, g.Token);
                throw new FalhaTeste("A função aceitou um tipo de e-mail que não existe.");
            }
            catch (FuncaoInexistenteException)
            {
                throw new AvisoTeste("A função enviar-email ainda não foi publicada no Supabase. O resumo do dia e os pedidos por e-mail abrem o e-mail do computador (veja o README, \"Ligar o e-mail\").");
            }
            catch (ConexaoException ex) when (ex.Message.Contains("ainda não foi configurada"))
            {
                throw new AvisoTeste("A função está publicada, mas a caixa de e-mail (EMAIL_USUARIO e EMAIL_SENHA_APP) ainda não foi configurada nos segredos.");
            }
            catch (ConexaoException ex)
            {
                Ambiente.Nota($"Resposta: \"{ex.Message}\"");
            }
        });
        l.Api("Edge Function ler-emails está publicada e recusa quem não é o agendamento", async () =>
        {
            var g = await amb.Entrar("gerente");
            var (status, corpo, _) = await amb.Http(HttpMethod.Post, "functions/v1/ler-emails", g.Token, new { });
            if (status == HttpStatusCode.NotFound && !corpo.Contains("erro"))
                throw new AvisoTeste("A função ler-emails ainda não foi publicada: as respostas dos fornecedores por e-mail não chegam ao sistema.");
            Verificar.Verdadeiro(status is HttpStatusCode.Unauthorized or HttpStatusCode.ServiceUnavailable,
                $"Sem o segredo do agendamento a função deveria recusar (401), veio {(int)status}: {corpo}");
        });
        l.Api("Storage: pasta dos anexos dos fornecedores existe", async () =>
        {
            var g = await amb.Entrar("gerente");
            var (status, corpo, _) = await amb.Http(HttpMethod.Post, "storage/v1/object/sign/anexos-fornecedores/teste-inexistente.txt", g.Token, new { expiresIn = 60 });
            if (corpo.Contains("Bucket not found", StringComparison.OrdinalIgnoreCase))
                throw new FalhaTeste("O bucket anexos-fornecedores não existe (script 009).");
            Verificar.Verdadeiro(status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
                $"Arquivo inexistente deveria dar \"não encontrado\", veio {(int)status}: {corpo}");
        });
        l.Api("Open Food Facts (busca do nome pelo código de barras) responde", async () =>
        {
            var r = Stopwatch.StartNew();
            var achado = await new OpenFoodFactsService().ProcurarAsync("7894900011517");
            Ambiente.Nota($"{r.Elapsed.TotalSeconds:0.0} s: {achado?.Nome ?? "(nada)"}");
            if (achado is null)
                throw new AvisoTeste("Sem resposta dos bancos abertos de produtos (sem internet ou site fora do ar). O cadastro abre vazio, como previsto.");
        }, banco: false);
        l.Api("Hora do banco igual à do computador (o \"hoje\" do caixa é o mesmo)", async () =>
        {
            var g = await amb.Entrar("gerente");
            var (_, _, r) = await amb.Http(HttpMethod.Get, "rest/v1/perfis?select=id&limit=1", g.Token);
            if (r.Headers.Date is not { } data) throw new PularTeste("O servidor não mandou a hora.");
            var dif = (DateTimeOffset.UtcNow - data).Duration();
            Ambiente.Nota($"Diferença: {dif.TotalSeconds:0} s");
            Verificar.Verdadeiro(dif < TimeSpan.FromMinutes(5), $"O relógio deste computador está {dif.TotalMinutes:0} minutos diferente do servidor. Vendas e validades podem cair no dia errado.");
        });
        l.Volume("20 consultas ao mesmo tempo (vários caixas e o Painel abertos)", async () =>
        {
            var g = await amb.Entrar("gerente");
            var r = Stopwatch.StartNew();
            var tarefas = Enumerable.Range(0, 20).Select(_ => amb.Api!.ListarAsync<JsonElement>("produtos?select=id,nome,preco&limit=50", g.Token));
            await Task.WhenAll(tarefas);
            r.Stop();
            Ambiente.Nota($"20 consultas: {r.Elapsed.TotalSeconds:0.0} s");
            if (r.Elapsed > TimeSpan.FromSeconds(8)) Ambiente.AvisoPendente($"20 consultas juntas levaram {r.Elapsed.TotalSeconds:0.0} s.");
        });
        return l;
    }
}
