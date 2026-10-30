using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

/// <summary>Funcionário na tela Funcionários (função listar_funcionarios do script 006).</summary>
public sealed class Funcionario
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("usuario")] public string Usuario { get; set; } = "";
    [JsonPropertyName("nome")] public string Nome { get; set; } = "";
    [JsonPropertyName("perfil")] public string Perfil { get; set; } = "operador";
    [JsonPropertyName("ativo")] public bool Ativo { get; set; }
    [JsonPropertyName("criado_em")] public DateTimeOffset CriadoEm { get; set; }
    [JsonPropertyName("ultimo_acesso")] public DateTimeOffset? UltimoAcesso { get; set; }
    /// <summary>Já abriu caixa (tem vendas no nome dele): remover só desativa.</summary>
    [JsonPropertyName("tem_vendas")] public bool TemVendas { get; set; }
    /// <summary>Pediu nova senha na tela de login e ainda não foi atendido.</summary>
    [JsonPropertyName("pediu_senha")] public DateTimeOffset? PediuSenha { get; set; }

    public bool Gerente => Perfil == "gerente";
    public string NomeFuncao => Gerente ? "Gerente" : "Operador";
}

/// <summary>Dados da janela "Meu perfil" (função meu_perfil).</summary>
public sealed record DadosMeuPerfil(
    [property: JsonPropertyName("acesso_anterior")] DateTimeOffset? AcessoAnterior,
    [property: JsonPropertyName("criado_em")] DateTimeOffset? CriadoEm,
    [property: JsonPropertyName("vendas_hoje")] int VendasHoje,
    [property: JsonPropertyName("total_hoje")] decimal TotalHoje);

/// <summary>
/// Funcionários (só o gerente) e o próprio perfil (todos). Criar usuário e trocar a senha de outro passam por
/// funções do banco: o .exe continua só com a chave pública.
/// </summary>
public sealed class FuncionarioService(SupabaseApi api, Sessao sessao)
{
    public const int SenhaMinima = 8;

    public Sessao Sessao => sessao;

    public async Task<List<Funcionario>> ListarAsync() =>
        await api.ChamarAsync<List<Funcionario>>("listar_funcionarios", new { }, sessao.Token) ?? [];

    public Task CriarAsync(string usuario, string nome, string perfil, string senha) =>
        api.ChamarAsync<Guid>("criar_funcionario", new { p_usuario = usuario, p_nome = nome, p_perfil = perfil, p_senha = senha }, sessao.Token);

    public Task EditarAsync(Guid id, string nome, string perfil, bool ativo) =>
        api.ChamarAsync<object>("editar_funcionario", new { p_id = id, p_nome = nome, p_perfil = perfil, p_ativo = ativo }, sessao.Token);

    public Task RedefinirSenhaAsync(Guid id, string senha) =>
        api.ChamarAsync<object>("redefinir_senha_funcionario", new { p_id = id, p_senha = senha }, sessao.Token);

    /// <summary>Devolve "desativado" (tem vendas) ou "excluido".</summary>
    public async Task<string> RemoverAsync(Guid id) =>
        await api.ChamarAsync<string>("remover_funcionario", new { p_id = id }, sessao.Token) ?? "excluido";

    public Task<DadosMeuPerfil?> MeuPerfilAsync() => api.ChamarAsync<DadosMeuPerfil>("meu_perfil", new { }, sessao.Token);

    /// <summary>
    /// Troca a própria senha: confere a senha atual entrando de novo e grava a nova pelo login do Supabase
    /// (PUT auth/v1/user com o token de quem está logado).
    /// </summary>
    public async Task TrocarMinhaSenhaAsync(string atual, string nova)
    {
        HttpResponseMessage resposta;
        try
        {
            resposta = await api.Http.PostAsJsonAsync("auth/v1/token?grant_type=password",
                new { email = AuthService.EmailDoUsuario(sessao.Usuario), password = atual });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ConexaoException("Não foi possível conectar ao servidor. Verifique a internet.");
        }
        if (resposta.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            throw new SenhaAtualErradaException();
        if (!resposta.IsSuccessStatusCode)
            throw new ConexaoException($"Erro no servidor ({(int)resposta.StatusCode}). Tente novamente.");

        using var req = new HttpRequestMessage(HttpMethod.Put, "auth/v1/user") { Content = JsonContent.Create(new { password = nova }) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessao.Token);
        try
        {
            resposta = await api.Http.SendAsync(req);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ConexaoException("Não foi possível conectar ao servidor. Verifique a internet.");
        }
        if (resposta.IsSuccessStatusCode) return;
        var corpo = await resposta.Content.ReadAsStringAsync();
        throw new ConexaoException(
            corpo.Contains("same_password") || corpo.Contains("should be different") ? "A senha nova precisa ser diferente da atual."
            : corpo.Contains("weak_password") || corpo.Contains("at least") ? "Senha fraca: use pelo menos 8 caracteres, com letras e números."
            : resposta.StatusCode == HttpStatusCode.Unauthorized ? "Sua sessão expirou. Clique em Sair e entre de novo."
            : $"Erro no servidor ({(int)resposta.StatusCode}). Tente novamente.");
    }
}

public sealed class SenhaAtualErradaException() : ConexaoException("A senha atual está errada.");
