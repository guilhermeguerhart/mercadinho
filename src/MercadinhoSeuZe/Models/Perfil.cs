namespace MercadinhoSeuZe.Models;

public enum Papel
{
    Caixa,
    Dono
}

public record Perfil(Guid Id, string Nome, Papel Papel);
