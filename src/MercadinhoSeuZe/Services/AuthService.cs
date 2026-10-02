using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

public class LoginException(string mensagem) : Exception(mensagem);

/// <summary>Login pelo Supabase Auth e leitura do perfil (caixa/dono) do usuário.</summary>
public sealed class AuthService
{
    private readonly Configuracao _config;
    private readonly HttpClient _http;

    public AuthService(Configuracao config)
    {
        _config = config;
        _http = new HttpClient { BaseAddress = new Uri(config.SupabaseUrl + "/"), Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Add("apikey", config.SupabaseAnonKey);
    }

    public async Task<Perfil> EntrarAsync(string email, string senha)
    {
        var token = await ObterTokenAsync(email, senha);
        return await ObterPerfilAsync(token);
    }

    private async Task<SessaoAuth> ObterTokenAsync(string email, string senha)
    {
        HttpResponseMessage resposta;
        try
        {
            resposta = await _http.PostAsJsonAsync("auth/v1/token?grant_type=password", new { email, password = senha });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new LoginException("Não foi possível conectar ao servidor. Verifique a internet.");
        }

        if (resposta.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            throw new LoginException("E-mail ou senha incorretos.");
        if (!resposta.IsSuccessStatusCode)
            throw new LoginException($"Erro no servidor ({(int)resposta.StatusCode}). Tente novamente.");

        return await resposta.Content.ReadFromJsonAsync<SessaoAuth>()
               ?? throw new LoginException("Resposta inválida do servidor.");
    }

    private async Task<Perfil> ObterPerfilAsync(SessaoAuth sessao)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"rest/v1/perfis?id=eq.{sessao.Usuario.Id}&select=id,nome,papel,ativo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessao.AccessToken);

        var resposta = await _http.SendAsync(req);
        if (!resposta.IsSuccessStatusCode)
            throw new LoginException("Não foi possível carregar o perfil do usuário.");

        var linhas = await resposta.Content.ReadFromJsonAsync<List<PerfilLinha>>() ?? [];
        var linha = linhas.FirstOrDefault()
                    ?? throw new LoginException("Usuário sem perfil cadastrado. Fale com o dono do mercado.");
        if (!linha.Ativo)
            throw new LoginException("Usuário desativado. Fale com o dono do mercado.");

        var papel = linha.Papel == "dono" ? Papel.Dono : Papel.Caixa;
        return new Perfil(linha.Id, linha.Nome, papel);
    }

    private sealed record SessaoAuth(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("user")] UsuarioAuth Usuario);

    private sealed record UsuarioAuth([property: JsonPropertyName("id")] Guid Id);

    private sealed record PerfilLinha(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("nome")] string Nome,
        [property: JsonPropertyName("papel")] string Papel,
        [property: JsonPropertyName("ativo")] bool Ativo);
}
