namespace MercadinhoSeuZe.Models;

public enum Perfil
{
    Operador,
    Gerente
}

/// <summary>
/// Usuário logado no sistema. O token do Supabase vale 1 hora; o programa troca por um novo antes de vencer
/// (AuthService.RenovarAsync), então Token muda durante o turno.
/// </summary>
public record Sessao(Guid Id, string Usuario, string Nome, Perfil Perfil, string Token)
{
    public string Token { get; set; } = Token;

    /// <summary>Usado para pedir um token novo sem a senha.</summary>
    public string? RefreshToken { get; set; }

    public DateTimeOffset ExpiraEm { get; set; } = DateTimeOffset.UtcNow.AddHours(1);
}
