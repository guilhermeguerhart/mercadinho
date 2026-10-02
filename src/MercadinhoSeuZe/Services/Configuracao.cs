using System.IO;
using System.Reflection;
using System.Text.Json;

namespace MercadinhoSeuZe.Services;

/// <summary>Lê o appsettings.json embutido no .exe.</summary>
public sealed class Configuracao
{
    public string SupabaseUrl { get; }
    public string SupabaseAnonKey { get; }

    private Configuracao(string url, string anonKey)
    {
        SupabaseUrl = url.TrimEnd('/');
        SupabaseAnonKey = anonKey;
    }

    public static Configuracao? Carregar()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("appsettings.json");
        if (stream is null) return null;

        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("Supabase", out var supabase)) return null;

        var url = supabase.GetProperty("Url").GetString();
        var key = supabase.GetProperty("AnonKey").GetString();
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key) || url.Contains("SEU-PROJETO"))
            return null;

        return new Configuracao(url, key);
    }
}
