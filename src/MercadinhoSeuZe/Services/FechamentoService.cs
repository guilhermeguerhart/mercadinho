using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

/// <summary>
/// Fechamento do dia e análises (semana 6, script database/009_semana6_fechamento_mensagens.sql). Os totais são
/// calculados no banco (resumo_dia, analise_vendas) para a tela, o e-mail e o celular mostrarem os mesmos números.
/// O e-mail sai pela Edge Function enviar-email (supabase/functions), que guarda a senha do e-mail; o programa só
/// manda o token de quem está logado.
/// </summary>
public sealed class FechamentoService(SupabaseApi api, Sessao sessao)
{
    public Sessao Sessao => sessao;

    private static string Data(DateTime dia) => dia.ToString("yyyy-MM-dd");

    /// <summary>Só o gerente: resumo de um dia (hoje ou outro).</summary>
    public async Task<ResumoDoDia> ResumoAsync(DateTime dia) =>
        await api.ChamarAsync<ResumoDoDia>("resumo_dia", new { p_dia = Data(dia) }, sessao.Token)
        ?? throw new ConexaoException("Resposta inválida do servidor.");

    /// <summary>
    /// Fecha o dia (todos os caixas precisam estar fechados) e devolve o resumo gravado. Dia que já passou precisa da
    /// autorização do gerente (script 012).
    /// </summary>
    public async Task<ResumoDoDia> FecharDiaAsync(DateTime dia, string? observacao, long? autorizacao = null) =>
        await api.ChamarAsync<ResumoDoDia>("fechar_dia", Argumentos(new() { ["p_dia"] = Data(dia), ["p_obs"] = observacao }, autorizacao), sessao.Token)
        ?? throw new ConexaoException("Resposta inválida do servidor.");

    public Task ReabrirDiaAsync(DateTime dia, long? autorizacao = null) =>
        api.ChamarAsync<object>("reabrir_dia", Argumentos(new() { ["p_dia"] = Data(dia) }, autorizacao), sessao.Token);

    /// <summary>p_autorizacao só vai quando existe (o banco sem o script 012 não conhece esse parâmetro).</summary>
    internal static Dictionary<string, object?> Argumentos(Dictionary<string, object?> argumentos, long? autorizacao)
    {
        if (autorizacao is not null) argumentos["p_autorizacao"] = autorizacao;
        return argumentos;
    }

    /// <summary>Vendas de um período agrupadas por "dia", "semana" ou "mes".</summary>
    public async Task<AnaliseVendas> AnaliseAsync(DateTime inicio, DateTime fim, string agrupar) =>
        await api.ChamarAsync<AnaliseVendas>("analise_vendas", new { p_inicio = Data(inicio), p_fim = Data(fim), p_agrupar = agrupar }, sessao.Token)
        ?? throw new ConexaoException("Resposta inválida do servidor.");

    public async Task<ConfigMercado> ConfigAsync() =>
        (await api.ListarAsync<ConfigMercado>("config_mercado?select=emails_resumo,email_pedidos,resumo_automatico&id=eq.1", sessao.Token)).FirstOrDefault()
        ?? new ConfigMercado();

    public Task SalvarConfigAsync(ConfigMercado config) =>
        api.AtualizarAsync("config_mercado", "id=eq.1",
            new { emails_resumo = config.EmailsResumo, email_pedidos = config.EmailPedidos, resumo_automatico = config.ResumoAutomatico, atualizado_em = DateTimeOffset.UtcNow },
            sessao.Token);

    /// <summary>
    /// Manda o resumo do dia por e-mail para os endereços da configuração (Edge Function enviar-email).
    /// Devolve para quem foi. Sem a função publicada: FuncaoInexistenteException (a tela oferece o e-mail pelo programa do PC).
    /// </summary>
    public async Task<string> EnviarResumoAsync(DateTime dia)
    {
        var r = await api.FuncaoAsync<RespostaEnvio>("enviar-email", new { tipo = "resumo_dia", dia = Data(dia) }, sessao.Token);
        return r?.Para ?? "";
    }

    private sealed record RespostaEnvio([property: System.Text.Json.Serialization.JsonPropertyName("para")] string? Para);
}
