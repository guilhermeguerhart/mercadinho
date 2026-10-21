using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace MercadinhoSeuZe.Services;

public class ConexaoException(string mensagem) : Exception(mensagem);

/// <summary>O banco ainda não tem uma coluna nova (o script SQL da semana não foi rodado).</summary>
public class BancoDesatualizadoException(string mensagem) : ConexaoException(mensagem);

/// <summary>Acesso HTTP ao Supabase (Auth e tabelas) usando só a chave pública.</summary>
public sealed class SupabaseApi
{
    public HttpClient Http { get; }

    public SupabaseApi(Configuracao config)
    {
        Http = new HttpClient { BaseAddress = new Uri(config.SupabaseUrl + "/"), Timeout = TimeSpan.FromSeconds(15) };
        Http.DefaultRequestHeaders.Add("apikey", config.SupabaseAnonKey);
    }

    /// <summary>Insere uma linha numa tabela (as regras de acesso do banco decidem se pode).</summary>
    public async Task InserirAsync(string tabela, object linha)
    {
        HttpResponseMessage resposta;
        try
        {
            resposta = await Http.PostAsJsonAsync($"rest/v1/{tabela}", linha);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ConexaoException("Não foi possível conectar ao servidor. Verifique a internet.");
        }

        if (!resposta.IsSuccessStatusCode)
            throw new ConexaoException($"Erro no servidor ({(int)resposta.StatusCode}). Tente novamente.");
    }

    // ------------------------------------------------------------ Tabelas com o usuário logado
    // Estas chamadas vão com o token de quem entrou, para o banco aplicar as regras do perfil (RLS).

    /// <summary>Lê linhas. Ex.: consulta = "produtos?select=*,lotes(*)".</summary>
    public async Task<List<T>> ListarAsync<T>(string consulta, string token)
    {
        using var req = Requisicao(HttpMethod.Get, consulta, token);
        var resposta = await EnviarAsync(req);
        return await resposta.Content.ReadFromJsonAsync<List<T>>() ?? [];
    }

    /// <summary>Insere e devolve a linha gravada (com o id criado pelo banco).</summary>
    public async Task<T> InserirAsync<T>(string tabela, object linha, string token)
    {
        using var req = Requisicao(HttpMethod.Post, tabela, token, linha);
        req.Headers.Add("Prefer", "return=representation");
        var resposta = await EnviarAsync(req);
        return (await resposta.Content.ReadFromJsonAsync<List<T>>() ?? []).FirstOrDefault()
               ?? throw new ConexaoException("Resposta inválida do servidor.");
    }

    /// <summary>Altera as linhas do filtro. Ex.: filtro = "id=eq.5".</summary>
    public async Task AtualizarAsync(string tabela, string filtro, object valores, string token)
    {
        using var req = Requisicao(HttpMethod.Patch, $"{tabela}?{filtro}", token, valores);
        await ExigirAlteracaoAsync(req);
    }

    public async Task ExcluirAsync(string tabela, string filtro, string token)
    {
        using var req = Requisicao(HttpMethod.Delete, $"{tabela}?{filtro}", token);
        await ExigirAlteracaoAsync(req);
    }

    /// <summary>Chama uma função do banco (rest/v1/rpc/...) e devolve a resposta em JSON (ou default).</summary>
    public async Task<T?> ChamarAsync<T>(string funcao, object argumentos, string token)
    {
        using var req = Requisicao(HttpMethod.Post, $"rpc/{funcao}", token, argumentos);
        var resposta = await EnviarAsync(req);
        var corpo = await resposta.Content.ReadAsStringAsync();
        return corpo.Length == 0 ? default : System.Text.Json.JsonSerializer.Deserialize<T>(corpo);
    }

    private HttpRequestMessage Requisicao(HttpMethod metodo, string caminho, string token, object? corpo = null)
    {
        var req = new HttpRequestMessage(metodo, $"rest/v1/{caminho}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (corpo is not null) req.Content = JsonContent.Create(corpo);
        return req;
    }

    /// <summary>Sem permissão o banco não dá erro: só não altera nada. Por isso pede a contagem de linhas alteradas.</summary>
    private async Task ExigirAlteracaoAsync(HttpRequestMessage req)
    {
        req.Headers.Add("Prefer", "return=minimal, count=exact");
        var resposta = await EnviarAsync(req);
        var alteradas = resposta.Content.Headers.ContentRange?.Length
                        ?? (resposta.Headers.TryGetValues("Content-Range", out var v) && long.TryParse(v.First().Split('/').Last(), out var n) ? n : 1);
        if (alteradas == 0)
            throw new ConexaoException("Nada foi alterado. O registro não existe mais ou seu perfil não tem permissão.");
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage req)
    {
        HttpResponseMessage resposta;
        try
        {
            resposta = await Http.SendAsync(req);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ConexaoException("Não foi possível conectar ao servidor. Verifique a internet.");
        }
        if (resposta.IsSuccessStatusCode) return resposta;

        var corpo = await resposta.Content.ReadAsStringAsync();
        if (resposta.StatusCode == HttpStatusCode.BadRequest && corpo.Contains("\"42703\""))
            throw new BancoDesatualizadoException("O banco ainda não tem as colunas novas. Rode o script SQL da semana no Supabase.");
        // Mensagens escritas nas funções do banco (raise exception), já em português
        if (corpo.Contains("\"22023\"") && MensagemDoBanco(corpo) is { } mensagem)
            throw new ConexaoException(mensagem);
        throw new ConexaoException(resposta.StatusCode switch
        {
            HttpStatusCode.Unauthorized when corpo.Contains("JWT expired") => "Sua sessão expirou. Clique em Sair e entre de novo.",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Seu perfil não tem permissão para esta ação.",
            HttpStatusCode.Conflict => "Já existe um registro com esses dados.",
            HttpStatusCode.NotFound when corpo.Contains("PGRST205") || corpo.Contains("PGRST202") || corpo.Contains("does not exist") =>
                "As tabelas desta tela ainda não foram criadas no banco. Rode o script SQL da semana no Supabase.",
            _ => $"Erro no servidor ({(int)resposta.StatusCode}). Tente novamente."
        });
    }

    private static string? MensagemDoBanco(string corpo)
    {
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(corpo);
            return json.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
