using System.IO;
using System.Net;
using System.Text;

namespace MercadinhoSeuZe.Testes.Motor;

/// <summary>Mostra cada resultado no console e grava o relatório em HTML no fim.</summary>
public static class Relatorio
{
    public static void Linha(Resultado r)
    {
        var (cor, marca) = r.Situacao switch
        {
            Situacao.Passou => (ConsoleColor.Green, " OK  "),
            Situacao.Falhou => (ConsoleColor.Red, "ERRO "),
            Situacao.Aviso => (ConsoleColor.Yellow, "AVISO"),
            _ => (ConsoleColor.DarkGray, "PULOU")
        };
        Console.ForegroundColor = cor;
        Console.Write($"[{marca}] ");
        Console.ResetColor();
        Console.Write($"{r.Modulo} · {NomeTipo(r.Tipo)} · {r.Nome}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($" ({r.Tempo.TotalSeconds:0.0} s)");
        Console.ResetColor();
        if (r.Situacao is Situacao.Falhou or Situacao.Aviso or Situacao.Pulado && r.Detalhe.Length > 0)
        {
            Console.ForegroundColor = r.Situacao == Situacao.Pulado ? ConsoleColor.DarkGray : cor;
            Console.WriteLine("        " + r.Detalhe.Split('\n')[0]);
            Console.ResetColor();
        }
    }

    public static string NomeTipo(Tipo t) => t switch
    {
        Tipo.Certo => "Uso certo",
        Tipo.Errado => "Uso errado",
        Tipo.Limite => "Limite",
        Tipo.Volume => "Volume",
        Tipo.Seguranca => "Segurança",
        Tipo.Integridade => "Integridade",
        Tipo.Api => "API",
        _ => "Tela"
    };

    public static string Gravar(IReadOnlyList<Resultado> resultados, Ambiente amb, TimeSpan duracao, IReadOnlyList<string> limpeza,
        IReadOnlyList<string> imagens)
    {
        var h = new StringBuilder();
        string E(string s) => WebUtility.HtmlEncode(s);
        int Conta(Situacao s) => resultados.Count(r => r.Situacao == s);

        h.Append("""
            <!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Testes do Mercado</title><style>
            :root{--fundo:#F1F2F4;--cartao:#fff;--tinta:#1d2430;--suave:#5b6472;--linha:#d9dce1;--azul:#1F3354;--verde:#1f7a45;--vermelho:#B5371C;--ambar:#a86400;--cinza:#7a808a}
            body{margin:0;background:var(--fundo);color:var(--tinta);font:14px/1.45 "Segoe UI",system-ui,sans-serif}
            header{background:var(--azul);color:#fff;padding:20px 24px}header h1{margin:0 0 4px;font-size:22px}header p{margin:0;opacity:.85}
            main{max-width:1100px;margin:0 auto;padding:20px 16px 48px}
            .kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:12px;margin-bottom:20px}
            .kpi{background:var(--cartao);border:1px solid var(--linha);border-radius:8px;padding:12px 14px}.kpi b{display:block;font-size:26px}
            .ok b{color:var(--verde)}.erro b{color:var(--vermelho)}.aviso b{color:var(--ambar)}.pulou b{color:var(--cinza)}
            section{background:var(--cartao);border:1px solid var(--linha);border-radius:8px;padding:14px 16px;margin-bottom:16px}
            h2{font-size:17px;margin:0 0 10px}table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:6px 8px;border-bottom:1px solid var(--linha);vertical-align:top}
            th{font-size:12px;text-transform:uppercase;letter-spacing:.03em;color:var(--suave);border-bottom:2px solid var(--tinta)}
            .s{font-weight:600;white-space:nowrap}.s.Passou{color:var(--verde)}.s.Falhou{color:var(--vermelho)}.s.Aviso{color:var(--ambar)}.s.Pulado{color:var(--cinza)}
            .d{color:var(--suave);white-space:pre-wrap;font-size:13px}.t{white-space:nowrap;color:var(--suave)}
            .amb td:first-child{color:var(--suave);width:230px}
            .telas{display:grid;grid-template-columns:repeat(auto-fill,minmax(240px,1fr));gap:12px}.telas figure{margin:0}.telas img{width:100%;border:1px solid var(--linha);border-radius:6px}
            .telas figcaption{font-size:12px;color:var(--suave)}
            @media (max-width:640px){td.d{min-width:160px}}
            </style></head><body>
            """);
        h.Append($"<header><h1>Testes do sistema Mercado</h1><p>{DateTime.Now:dd/MM/yyyy HH:mm} · {resultados.Count} testes em {duracao.TotalMinutes:0.0} min</p></header><main>");
        h.Append($"""
            <div class="kpis">
            <div class="kpi ok"><b>{Conta(Situacao.Passou)}</b>passaram</div>
            <div class="kpi erro"><b>{Conta(Situacao.Falhou)}</b>com erro</div>
            <div class="kpi aviso"><b>{Conta(Situacao.Aviso)}</b>avisos</div>
            <div class="kpi pulou"><b>{Conta(Situacao.Pulado)}</b>pulados</div>
            </div>
            """);

        h.Append("<section><h2>Onde rodou</h2><table class=\"amb\">");
        h.Append($"<tr><td>Banco</td><td>{E(amb.Url)}{(amb.BancoDoMercado ? " (banco do mercado)" : amb.TemBanco ? " (projeto de teste)" : "")}</td></tr>");
        h.Append($"<tr><td>Configuração lida de</td><td>{E(amb.OrigemConfig ?? "nenhuma")}</td></tr>");
        h.Append($"<tr><td>Vendas, caixa e fechamento do dia</td><td>{(amb.PodeMovimentar ? "testados de verdade (gravam registros)" : "só o que não grava: os testes que gravam venda ficaram como pulados")}</td></tr>");
        h.Append($"<tr><td>Registros de teste apagados</td><td>{(limpeza.Count == 0 ? "todos" : E(string.Join("\n", limpeza)))}</td></tr>");
        h.Append("</table></section>");

        // Resumo por módulo
        h.Append("<section><h2>Por módulo</h2><table><tr><th>Módulo</th><th>Passaram</th><th>Com erro</th><th>Avisos</th><th>Pulados</th></tr>");
        foreach (var g in resultados.GroupBy(r => r.Modulo))
            h.Append($"<tr><td>{E(g.Key)}</td><td>{g.Count(r => r.Situacao == Situacao.Passou)}</td><td>{g.Count(r => r.Situacao == Situacao.Falhou)}</td>" +
                     $"<td>{g.Count(r => r.Situacao == Situacao.Aviso)}</td><td>{g.Count(r => r.Situacao == Situacao.Pulado)}</td></tr>");
        h.Append("</table></section>");

        void Tabela(string titulo, IEnumerable<Resultado> lista)
        {
            var itens = lista.ToList();
            if (itens.Count == 0) return;
            h.Append($"<section><h2>{E(titulo)} ({itens.Count})</h2><table><tr><th>Situação</th><th>Módulo</th><th>Tipo</th><th>Teste</th><th>Detalhe</th><th>Tempo</th></tr>");
            foreach (var r in itens)
                h.Append($"<tr><td class=\"s {r.Situacao}\">{NomeSituacao(r.Situacao)}</td><td>{E(r.Modulo)}</td><td>{NomeTipo(r.Tipo)}</td>" +
                         $"<td>{E(r.Nome)}</td><td class=\"d\">{E(r.Detalhe)}</td><td class=\"t\">{r.Tempo.TotalSeconds:0.0} s</td></tr>");
            h.Append("</table></section>");
        }

        Tabela("O que está com erro", resultados.Where(r => r.Situacao == Situacao.Falhou));
        Tabela("Avisos", resultados.Where(r => r.Situacao == Situacao.Aviso));
        Tabela("Todos os testes", resultados);

        if (imagens.Count > 0)
        {
            h.Append("<section><h2>Telas abertas durante o teste</h2><div class=\"telas\">");
            foreach (var img in imagens)
                h.Append($"<figure><a href=\"{E(img)}\"><img src=\"{E(img)}\" loading=\"lazy\" alt=\"{E(Path.GetFileNameWithoutExtension(img))}\"></a><figcaption>{E(Path.GetFileNameWithoutExtension(img))}</figcaption></figure>");
            h.Append("</div></section>");
        }

        h.Append("</main></body></html>");
        var arquivo = Path.Combine(amb.PastaSaida, "relatorio.html");
        File.WriteAllText(arquivo, h.ToString(), Encoding.UTF8);
        return arquivo;
    }

    private static string NomeSituacao(Situacao s) => s switch
    {
        Situacao.Passou => "Passou",
        Situacao.Falhou => "Erro",
        Situacao.Aviso => "Aviso",
        _ => "Pulado"
    };
}
