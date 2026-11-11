using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using MercadinhoSeuZe.Testes.Motor;
using MercadinhoSeuZe.Testes.Testes;

namespace MercadinhoSeuZe.Testes;

/// <summary>
/// MercadinhoSeuZe.Testes.exe: roda todos os testes do sistema e abre o relatório.
///
/// Opções (podem ir juntas):
///   --sem-banco   só a lógica e as telas sem dados (não usa a internet)
///   --sem-telas   não abre as telas
///   --rapido      pula os testes de volume (milhares de registros)
///   --movimento   no banco do mercado, testa também abrir caixa, vender, cancelar e fechar o dia
///                 (grava vendas e caixas que não dá para apagar; num projeto de teste isso já roda sempre)
///   --modulo X    só os testes do módulo X (ex.: --modulo Caixa)
///   --sem-pausa   não espera o Enter no fim e não abre o relatório
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "Mercado · Testes do sistema";
        Ambiente amb;
        try
        {
            amb = Ambiente.Criar(args);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(ex.Message);
            Console.ResetColor();
            return 2;
        }

        // Estilos, cores e ícones do programa (App.xaml) para as telas abrirem iguais ao .exe do mercado
        // Caminhos como "/Assets/mercado.ico" (ícone da janela) são do programa do mercado, não deste .exe de testes
        try { Application.ResourceAssembly = typeof(App).Assembly; }
        catch (InvalidOperationException)
        {
            // Já foi lido antes (fica preso ao .exe de testes): troca direto no campo interno do WPF
            typeof(Application).GetField("_resourceAssembly",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.SetValue(null, typeof(App).Assembly);
        }
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var errosDeTela = new List<string>();
        Dispatcher.CurrentDispatcher.UnhandledException += (_, e) =>
        {
            errosDeTela.Add(e.Exception.GetBaseException().Message);
            e.Handled = true;
        };
        TelasTestes.ErrosNaoTratados = errosDeTela;

        Cabecalho(amb);
        amb.GuardarPreferencias();

        var listas = new List<Lista>
        {
            LogicaTestes.Montar(),
            ApiTestes.Montar(amb),
            LoginTestes.Montar(amb),
            SegurancaTestes.Montar(amb),
            EstoqueTestes.Montar(amb),
            PromocoesTestes.Montar(amb),
            FornecedoresTestes.Montar(amb),
            FuncionariosTestes.Montar(amb),
            CaixaTestes.Montar(amb),
            FechamentoTestes.Montar(amb),
            PrevisaoTestes.Montar(amb),
            CelularTestes.Montar(amb),
            IntegridadeTestes.Montar(amb),
            VolumeTestes.Montar(amb),
        };
        if (!amb.SemTelas) listas.Add(TelasTestes.Montar(amb));

        var modulo = Valor(args, "--modulo");
        var testes = listas.SelectMany(l => l.Testes)
            .Where(t => modulo is null || t.Modulo.Equals(modulo, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var resultados = new List<Resultado>();
        var relogio = Stopwatch.StartNew();
        foreach (var t in testes) resultados.Add(Rodar(t, amb));

        Console.WriteLine();
        Console.WriteLine("Apagando o que os testes criaram...");
        var limpeza = new List<string>();
        try
        {
            var tarefa = amb.LimparAsync();
            Bomba.Esperar(tarefa, TimeSpan.FromMinutes(5));
            limpeza = tarefa.Result;
        }
        catch (Exception ex)
        {
            limpeza.Add(ex.Message);
        }
        amb.DevolverPreferencias();
        relogio.Stop();

        var arquivo = Relatorio.Gravar(resultados, amb, relogio.Elapsed, limpeza, TelasTestes.Imagens);
        Resumo(resultados, arquivo, limpeza);

        var semPausa = args.Contains("--sem-pausa");
        if (!semPausa)
        {
            try { Process.Start(new ProcessStartInfo(arquivo) { UseShellExecute = true }); } catch { }
            if (!Console.IsInputRedirected)
            {
                Console.WriteLine("Aperte Enter para fechar.");
                Console.ReadLine();
            }
        }
        app.Shutdown();
        return resultados.Any(r => r.Situacao == Situacao.Falhou) ? 1 : 0;
    }

    private static Resultado Rodar(Teste t, Ambiente amb)
    {
        Ambiente.ComecarTeste();
        var relogio = Stopwatch.StartNew();
        Situacao situacao;
        string detalhe;
        if (t.UsaBanco && !amb.TemBanco)
        {
            situacao = Situacao.Pulado;
            detalhe = "Rodando sem banco (--sem-banco ou sem configuração).";
        }
        else
        {
            try
            {
                Bomba.Esperar(t.Corpo(), t.Limite);
                var (_, avisos) = Ambiente.TerminarTeste();
                situacao = avisos.Count > 0 ? Situacao.Aviso : Situacao.Passou;
                detalhe = string.Join("\n", avisos);
            }
            catch (Exception ex)
            {
                var e = ex is AggregateException { InnerException: { } inner } ? inner : ex;
                (situacao, detalhe) = e switch
                {
                    FalhaTeste => (Situacao.Falhou, e.Message),
                    PularTeste => (Situacao.Pulado, e.Message),
                    AvisoTeste => (Situacao.Aviso, e.Message),
                    _ => (Situacao.Falhou, $"Erro inesperado ({e.GetType().Name}): {e.Message}")
                };
            }
        }
        relogio.Stop();
        var (notas, _) = Ambiente.TerminarTeste();
        if (notas.Count > 0) detalhe = string.Join("\n", new[] { detalhe }.Where(d => d.Length > 0).Concat(notas));
        var r = new Resultado(t.Modulo, t.Nome, t.Tipo, situacao, detalhe, relogio.Elapsed);
        Relatorio.Linha(r);
        return r;
    }

    private static void Cabecalho(Ambiente amb)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Mercado · Testes do sistema");
        Console.ResetColor();
        Console.WriteLine($"Banco: {amb.Url}{(amb.BancoDoMercado ? " (banco do mercado)" : amb.TemBanco ? " (projeto de teste)" : "")}");
        if (amb.TemBanco && !amb.PodeMovimentar)
            Console.WriteLine("Vendas, caixa e fechamento do dia: só o que não grava (use --movimento para testar tudo).");
        Console.WriteLine($"Tudo o que o teste criar leva o nome \"{Ambiente.Prefixo}\" e é apagado no fim.");
        Console.WriteLine();
    }

    private static void Resumo(List<Resultado> r, string arquivo, List<string> limpeza)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"{r.Count(x => x.Situacao == Situacao.Passou)} passaram  ");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write($"{r.Count(x => x.Situacao == Situacao.Falhou)} com erro  ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"{r.Count(x => x.Situacao == Situacao.Aviso)} avisos  ");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"{r.Count(x => x.Situacao == Situacao.Pulado)} pulados");
        Console.ResetColor();
        foreach (var l in limpeza)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Não consegui apagar: " + l);
            Console.ResetColor();
        }
        Console.WriteLine($"Relatório: {arquivo}");
    }

    private static string? Valor(string[] args, string nome)
    {
        var i = Array.IndexOf(args, nome);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
