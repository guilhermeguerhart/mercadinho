using System.IO;
using System.Text.Json;

namespace MercadinhoSeuZe.Services;

/// <summary>
/// Dados guardados só neste computador (%LocalAppData%\Mercado\preferencias.json):
/// o usuário lembrado, os bloqueios por senha errada e as sugestões de reposição ignoradas hoje.
/// </summary>
public sealed class PreferenciasLocais
{
    private static readonly string Arquivo = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mercado", "preferencias.json");

    public string? UltimoUsuario { get; set; }
    public Dictionary<string, BloqueioUsuario> Bloqueios { get; set; } = [];
    /// <summary>Id do fornecedor → dia (aaaa-mm-dd) em que a sugestão de reposição foi ignorada.</summary>
    public Dictionary<string, string> SugestoesIgnoradas { get; set; } = [];

    public static PreferenciasLocais Carregar()
    {
        try
        {
            if (File.Exists(Arquivo))
                return JsonSerializer.Deserialize<PreferenciasLocais>(File.ReadAllText(Arquivo)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new();
    }

    public void Salvar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
            File.WriteAllText(Arquivo, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public sealed class BloqueioUsuario
{
    public int Falhas { get; set; }
    public DateTime? Ate { get; set; }
}
