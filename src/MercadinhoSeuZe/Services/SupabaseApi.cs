using System.Net.Http;
using System.Net.Http.Json;

namespace MercadinhoSeuZe.Services;

public class ConexaoException(string mensagem) : Exception(mensagem);

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
}
