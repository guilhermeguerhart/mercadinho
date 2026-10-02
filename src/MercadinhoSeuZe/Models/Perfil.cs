namespace MercadinhoSeuZe.Models;

public enum Perfil
{
    Operador,
    Gerente
}

/// <summary>Usuário logado no sistema.</summary>
public record Sessao(Guid Id, string Usuario, string Nome, Perfil Perfil, string Token);
