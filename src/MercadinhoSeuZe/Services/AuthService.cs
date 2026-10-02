using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

/// <summary>Resultado de uma tentativa de login, no mesmo formato do site de referência.</summary>
public sealed record ResultadoLogin(
    Sessao? Sessao = null,
    string? Erro = null,
    int? TentativasRestantes = null,
    TimeSpan? Bloqueado = null)
{
    public bool Ok => Sessao is not null;
}

/// <summary>
/// Login pelo Supabase Auth com nome de usuário, perfil (gerente/operador),
/// bloqueio temporário após senhas erradas e auditoria de cada tentativa.
/// </summary>
public sealed class AuthService(SupabaseApi api, PreferenciasLocais preferencias)
{
    public const string DominioInterno = "mercado.local";
    public const int MaxTentativas = 5;
    public static readonly TimeSpan TempoBloqueio = TimeSpan.FromMinutes(5);

    /// <summary>"ana.caixa" vira "ana.caixa@mercado.local"; quem digitar um e-mail completo usa ele mesmo.</summary>
    public static string EmailDoUsuario(string usuario) =>
        usuario.Contains('@') ? usuario : $"{usuario}@{DominioInterno}";

    public TimeSpan TempoBloqueado(string usuario)
    {
        if (!preferencias.Bloqueios.TryGetValue(usuario, out var info) || info.Ate is null) return TimeSpan.Zero;
        var restante = info.Ate.Value - DateTime.UtcNow;
        return restante > TimeSpan.Zero ? restante : TimeSpan.Zero;
    }

    public async Task<ResultadoLogin> EntrarAsync(string usuario, string senha)
    {
        usuario = usuario.Trim().ToLowerInvariant();

        var restante = TempoBloqueado(usuario);
        if (restante > TimeSpan.Zero)
        {
            _ = RegistrarTentativaAsync(usuario, false);
            return new ResultadoLogin(Bloqueado: restante);
        }

        SessaoAuth? auth;
        try
        {
            auth = await ObterTokenAsync(usuario, senha);
        }
        catch (ConexaoException ex)
        {
            return new ResultadoLogin(Erro: ex.Message);
        }

        if (auth is null)
        {
            _ = RegistrarTentativaAsync(usuario, false);
            return RegistrarFalha(usuario);
        }

        Sessao sessao;
        try
        {
            sessao = await ObterPerfilAsync(auth);
        }
        catch (ConexaoException ex)
        {
            return new ResultadoLogin(Erro: ex.Message);
        }

        _ = RegistrarTentativaAsync(usuario, true);
        preferencias.Bloqueios.Remove(usuario);
        preferencias.Salvar();
        return new ResultadoLogin(sessao);
    }

    /// <summary>Guarda o pedido para o gerente. Sempre responde igual, para não revelar quais usuários existem.</summary>
    public async Task SolicitarRecuperacaoAsync(string usuario)
    {
        try
        {
            await api.InserirAsync("pedidos_senha", new { usuario = usuario.Trim().ToLowerInvariant() });
        }
        catch (ConexaoException)
        {
        }
    }

    private ResultadoLogin RegistrarFalha(string usuario)
    {
        var info = preferencias.Bloqueios.TryGetValue(usuario, out var atual) && atual.Ate is null
            ? atual
            : new BloqueioUsuario();
        info.Falhas++;

        if (info.Falhas >= MaxTentativas)
        {
            preferencias.Bloqueios[usuario] = new BloqueioUsuario { Ate = DateTime.UtcNow + TempoBloqueio };
            preferencias.Salvar();
            return new ResultadoLogin(Bloqueado: TempoBloqueio);
        }

        preferencias.Bloqueios[usuario] = info;
        preferencias.Salvar();
        return new ResultadoLogin(Erro: "Usuário ou senha inválidos.", TentativasRestantes: MaxTentativas - info.Falhas);
    }

    private async Task RegistrarTentativaAsync(string usuario, bool sucesso)
    {
        try
        {
            await api.InserirAsync("auditoria_login", new { usuario, sucesso });
        }
        catch (ConexaoException)
        {
            // A auditoria não pode impedir o login.
        }
    }

    /// <summary>Retorna null quando usuário ou senha estão errados.</summary>
    private async Task<SessaoAuth?> ObterTokenAsync(string usuario, string senha)
    {
        HttpResponseMessage resposta;
        try
        {
            resposta = await api.Http.PostAsJsonAsync("auth/v1/token?grant_type=password",
                new { email = EmailDoUsuario(usuario), password = senha });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ConexaoException("Não foi possível conectar ao servidor. Verifique a internet.");
        }

        if (resposta.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            return null;
        if (!resposta.IsSuccessStatusCode)
            throw new ConexaoException($"Erro no servidor ({(int)resposta.StatusCode}). Tente novamente.");

        return await resposta.Content.ReadFromJsonAsync<SessaoAuth>()
               ?? throw new ConexaoException("Resposta inválida do servidor.");
    }

    private async Task<Sessao> ObterPerfilAsync(SessaoAuth auth)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"rest/v1/perfis?id=eq.{auth.Usuario.Id}&select=id,usuario,nome,perfil,ativo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        HttpResponseMessage resposta;
        try
        {
            resposta = await api.Http.SendAsync(req);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ConexaoException("Não foi possível conectar ao servidor. Verifique a internet.");
        }
        if (!resposta.IsSuccessStatusCode)
            throw new ConexaoException("Não foi possível carregar o perfil do usuário.");

        var linha = (await resposta.Content.ReadFromJsonAsync<List<PerfilLinha>>() ?? []).FirstOrDefault()
                    ?? throw new ConexaoException("Usuário sem perfil cadastrado. Fale com o gerente.");
        if (!linha.Ativo)
            throw new ConexaoException("Usuário desativado. Fale com o gerente.");

        var perfil = linha.Perfil == "gerente" ? Perfil.Gerente : Perfil.Operador;
        return new Sessao(linha.Id, linha.Usuario, linha.Nome, perfil, auth.AccessToken);
    }

    private sealed record SessaoAuth(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("user")] UsuarioAuth Usuario);

    private sealed record UsuarioAuth([property: JsonPropertyName("id")] Guid Id);

    private sealed record PerfilLinha(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("usuario")] string Usuario,
        [property: JsonPropertyName("nome")] string Nome,
        [property: JsonPropertyName("perfil")] string Perfil,
        [property: JsonPropertyName("ativo")] bool Ativo);
}
