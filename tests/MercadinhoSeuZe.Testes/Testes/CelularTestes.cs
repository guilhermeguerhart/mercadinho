using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Testes.Motor;

namespace MercadinhoSeuZe.Testes.Testes;

/// <summary>
/// App de celular (app-celular/): os arquivos do PWA estão completos, aponta para o mesmo banco do programa com a chave
/// pública, e as funções do banco que ele usa respondem para o gerente e recusam o operador.
/// A tela do celular em si (toque, digital, PIN) não é testada aqui.
/// </summary>
public static class CelularTestes
{
    /// <summary>Pasta do repositório (onde está MercadinhoSeuZe.slnx), procurando a partir do .exe e da pasta atual.</summary>
    public static string? PastaDoRepositorio()
    {
        foreach (var inicio in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var d = new DirectoryInfo(inicio); d is not null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "MercadinhoSeuZe.slnx"))) return d.FullName;
        }
        return null;
    }

    private static string Pasta() =>
        PastaDoRepositorio() is { } r && Directory.Exists(Path.Combine(r, "app-celular"))
            ? Path.Combine(r, "app-celular")
            : throw new PularTeste("Pasta app-celular não encontrada (rode o teste de dentro da pasta do projeto).");

    public static Lista Montar(Ambiente amb)
    {
        var l = new Lista("App de celular");

        l.Certo("Arquivos do app, ícones do manifest e lista do modo sem internet existem", () =>
        {
            var pasta = Pasta();
            var faltam = new List<string>();
            foreach (var a in new[] { "index.html", "app.js", "config.js", "estilo.css", "sw.js", "manifest.webmanifest" })
                if (!File.Exists(Path.Combine(pasta, a))) faltam.Add(a);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(pasta, "manifest.webmanifest")));
            foreach (var icone in manifest.RootElement.GetProperty("icons").EnumerateArray())
                if (!File.Exists(Path.Combine(pasta, icone.GetProperty("src").GetString()!))) faltam.Add(icone.GetProperty("src").GetString()!);
            var sw = File.ReadAllText(Path.Combine(pasta, "sw.js"));
            foreach (Match m in Regex.Matches(sw, "\"\\./([^\"]+)\""))
                if (!File.Exists(Path.Combine(pasta, m.Groups[1].Value))) faltam.Add("sw.js → " + m.Groups[1].Value);
            foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(pasta, "index.html")), "(?:src|href)=\"([^\"#:]+)\""))
                if (!m.Groups[1].Value.StartsWith("http") && !File.Exists(Path.Combine(pasta, m.Groups[1].Value.TrimStart('.', '/')))) faltam.Add("index.html → " + m.Groups[1].Value);
            Verificar.Verdadeiro(faltam.Count == 0, "Faltam: " + string.Join(", ", faltam));
            var nome = manifest.RootElement.GetProperty("name").GetString();
            Verificar.Verdadeiro(!string.IsNullOrWhiteSpace(nome), "Manifest sem nome");
            return Task.CompletedTask;
        }, banco: false);
        l.Certo("O app usa o mesmo banco e a mesma chave pública do programa", () =>
        {
            var config = File.ReadAllText(Path.Combine(Pasta(), "config.js"));
            var url = Regex.Match(config, "supabaseUrl:\\s*\"([^\"]+)\"").Groups[1].Value;
            var chave = Regex.Match(config, "chavePublica:\\s*\"([^\"]+)\"").Groups[1].Value;
            var programa = Configuracao.Carregar() ?? throw new PularTeste("Programa sem appsettings.json.");
            Verificar.Igual(programa.SupabaseUrl, url.TrimEnd('/'), "Endereço do Supabase no app");
            Verificar.Igual(programa.SupabaseAnonKey, chave, "Chave pública no app");
            Verificar.Verdadeiro(chave.StartsWith("sb_publishable"), "A chave do app não é a pública");
            return Task.CompletedTask;
        }, banco: false);
        l.Certo("O app só deixa o gerente entrar (confere o perfil depois do login)", () =>
        {
            var app = File.ReadAllText(Path.Combine(Pasta(), "app.js"));
            Verificar.Verdadeiro(app.Contains("perfil.perfil !== \"gerente\""), "Não achei a conferência do perfil gerente no app.js");
            return Task.CompletedTask;
        }, banco: false);
        l.Certo("Funções que o app chama respondem para o gerente (Hoje, Análises, Avisos, Estoque)", async () =>
        {
            var g = await amb.Entrar("gerente");
            var hoje = DateTime.Today.ToString("yyyy-MM-dd");
            await Verificar.Rapido(() => amb.Api!.ChamarAsync<JsonElement>("resumo_dia", new { p_dia = hoje }, g.Token), TimeSpan.FromSeconds(3), "Aba Hoje");
            await Verificar.Rapido(() => amb.Api!.ChamarAsync<JsonElement>("analise_vendas",
                new { p_inicio = DateTime.Today.AddDays(-29).ToString("yyyy-MM-dd"), p_fim = hoje, p_agrupar = "dia" }, g.Token), TimeSpan.FromSeconds(4), "Aba Análises");
            await amb.Api!.ChamarAsync<JsonElement>("mensagens_nao_lidas", new { }, g.Token);
            await amb.Api.ChamarAsync<JsonElement>("reservados_hoje", new { }, g.Token);
        });
        l.Seguranca("O operador não consegue os dados do app pelo banco", async () =>
        {
            var op = await amb.Entrar("operador");
            await Verificar.Recusa(() => amb.Api!.ChamarAsync<JsonElement>("resumo_dia", new { p_dia = DateTime.Today.ToString("yyyy-MM-dd") }, op.Token), "Resumo pelo operador", "Só o gerente");
            var msgs = await amb.Api!.ChamarAsync<List<JsonElement>>("mensagens_nao_lidas", new { }, op.Token) ?? [];
            Verificar.Igual(0, msgs.Count, "Mensagens de fornecedores que o operador recebe");
        });
        return l;
    }
}
