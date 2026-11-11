using System.Diagnostics;
using System.Windows.Threading;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Testes.Motor;

/// <summary>Que tipo de conferência o teste faz (aparece no relatório).</summary>
public enum Tipo
{
    /// <summary>Usar do jeito certo: tem que funcionar.</summary>
    Certo,
    /// <summary>Usar do jeito errado: o sistema tem que recusar, com mensagem clara.</summary>
    Errado,
    /// <summary>Valores na borda: zero, máximo, muito longo, um a mais que o permitido.</summary>
    Limite,
    /// <summary>Muitos registros de uma vez e tempo de resposta.</summary>
    Volume,
    /// <summary>Quem pode ver e mudar o quê (regras do banco por perfil).</summary>
    Seguranca,
    /// <summary>Os dados guardados batem entre si.</summary>
    Integridade,
    /// <summary>O serviço responde (Auth, banco, funções, e-mail, Storage, sites externos).</summary>
    Api,
    /// <summary>A tela abre sem erro.</summary>
    Tela
}

public enum Situacao { Passou, Falhou, Aviso, Pulado }

/// <summary>O teste encontrou um problema.</summary>
public sealed class FalhaTeste(string mensagem) : Exception(mensagem);

/// <summary>O teste não pôde rodar (sem banco, sem usuário, script não rodado...). Não conta como erro.</summary>
public sealed class PularTeste(string mensagem) : Exception(mensagem);

/// <summary>Funcionou, mas com algo para olhar (lento, serviço opcional fora do ar).</summary>
public sealed class AvisoTeste(string mensagem) : Exception(mensagem);

public sealed record Resultado(string Modulo, string Nome, Tipo Tipo, Situacao Situacao, string Detalhe, TimeSpan Tempo);

/// <summary>Um teste: módulo, nome em português e o que ele faz.</summary>
public sealed record Teste(string Modulo, string Nome, Tipo Tipo, Func<Task> Corpo)
{
    /// <summary>Precisa do banco (com o sistema sem banco, fica como pulado).</summary>
    public bool UsaBanco { get; init; } = true;

    /// <summary>Tempo máximo antes de desistir.</summary>
    public TimeSpan Limite { get; init; } = TimeSpan.FromMinutes(3);
}

/// <summary>Lista de testes de um módulo. Cada arquivo em Testes/ monta a sua.</summary>
public sealed class Lista(string modulo)
{
    public List<Teste> Testes { get; } = [];

    public void Certo(string nome, Func<Task> corpo, bool banco = true) => Add(nome, Tipo.Certo, corpo, banco);
    public void Errado(string nome, Func<Task> corpo, bool banco = true) => Add(nome, Tipo.Errado, corpo, banco);
    public void Limite(string nome, Func<Task> corpo, bool banco = true) => Add(nome, Tipo.Limite, corpo, banco);
    public void Volume(string nome, Func<Task> corpo, bool banco = true) =>
        Testes.Add(new Teste(modulo, nome, Tipo.Volume, corpo) { UsaBanco = banco, Limite = TimeSpan.FromMinutes(10) });
    public void Seguranca(string nome, Func<Task> corpo, bool banco = true) => Add(nome, Tipo.Seguranca, corpo, banco);
    public void Integridade(string nome, Func<Task> corpo) => Add(nome, Tipo.Integridade, corpo, true);
    public void Api(string nome, Func<Task> corpo, bool banco = true) => Add(nome, Tipo.Api, corpo, banco);
    public void Tela(string nome, Func<Task> corpo, bool banco = false) => Add(nome, Tipo.Tela, corpo, banco);

    // Versões síncronas (lógica pura)
    public void Certo(string nome, Action corpo) => Add(nome, Tipo.Certo, Sincrono(corpo), false);
    public void Errado(string nome, Action corpo) => Add(nome, Tipo.Errado, Sincrono(corpo), false);
    public void Limite(string nome, Action corpo) => Add(nome, Tipo.Limite, Sincrono(corpo), false);

    private static Func<Task> Sincrono(Action a) => () => { a(); return Task.CompletedTask; };

    private void Add(string nome, Tipo tipo, Func<Task> corpo, bool banco) =>
        Testes.Add(new Teste(modulo, nome, tipo, corpo) { UsaBanco = banco });
}

/// <summary>Conferências usadas pelos testes. Quando não bate, lança FalhaTeste com o motivo em português.</summary>
public static class Verificar
{
    public static void Verdadeiro(bool condicao, string mensagem)
    {
        if (!condicao) throw new FalhaTeste(mensagem);
    }

    public static void Igual<T>(T esperado, T obtido, string oQue)
    {
        if (!EqualityComparer<T>.Default.Equals(esperado, obtido))
            throw new FalhaTeste($"{oQue}: esperado {Texto(esperado)}, veio {Texto(obtido)}.");
    }

    /// <summary>Valores em dinheiro: aceita diferença de até 1 centavo (arredondamento).</summary>
    public static void Dinheiro(decimal esperado, decimal obtido, string oQue)
    {
        if (Math.Abs(esperado - obtido) > 0.01m)
            throw new FalhaTeste($"{oQue}: esperado R$ {esperado:N2}, veio R$ {obtido:N2}.");
    }

    public static void Nulo(object? valor, string oQue)
    {
        if (valor is not null) throw new FalhaTeste($"{oQue}: esperado vazio, veio {Texto(valor)}.");
    }

    public static T NaoNulo<T>(T? valor, string oQue) where T : class =>
        valor ?? throw new FalhaTeste($"{oQue}: veio vazio.");

    public static T NaoNulo<T>(T? valor, string oQue) where T : struct =>
        valor ?? throw new FalhaTeste($"{oQue}: veio vazio.");

    /// <summary>
    /// A ação tem que ser recusada (exceção). contem = trecho esperado na mensagem. Devolve a mensagem que o sistema deu,
    /// que vai para o relatório.
    /// </summary>
    public static async Task<string> Recusa(Func<Task> acao, string oQue, string? contem = null)
    {
        try
        {
            await acao();
        }
        catch (Exception ex) when (ex is not FalhaTeste and not PularTeste and not AvisoTeste)
        {
            var msg = ex.Message;
            if (contem is not null && !msg.Contains(contem, StringComparison.OrdinalIgnoreCase))
                throw new FalhaTeste($"{oQue}: foi recusado, mas com outra mensagem: \"{msg}\" (esperado algo com \"{contem}\").");
            Ambiente.Nota($"Recusado: \"{msg}\"");
            return msg;
        }
        throw new FalhaTeste($"{oQue}: o sistema ACEITOU, mas deveria recusar.");
    }

    public static string Recusa(Action acao, string oQue, string? contem = null) =>
        Recusa(() => { acao(); return Task.CompletedTask; }, oQue, contem).GetAwaiter().GetResult();

    /// <summary>Mensagem de erro em português e explicando o que fazer (não "Erro no servidor (400)").</summary>
    public static void MensagemClara(string mensagem, string oQue)
    {
        if (mensagem.StartsWith("Erro no servidor", StringComparison.Ordinal))
            throw new AvisoTeste($"{oQue}: foi recusado, mas a mensagem é genérica (\"{mensagem}\"). A tela precisa conferir antes de mandar.");
    }

    /// <summary>Mede o tempo; acima do limite vira aviso no relatório (a internet também pesa).</summary>
    public static async Task<T> Rapido<T>(Func<Task<T>> acao, TimeSpan limite, string oQue)
    {
        var relogio = Stopwatch.StartNew();
        var r = await acao();
        relogio.Stop();
        Ambiente.Nota($"{oQue}: {relogio.Elapsed.TotalSeconds:0.00} s");
        if (relogio.Elapsed > limite)
            Ambiente.AvisoPendente($"{oQue} levou {relogio.Elapsed.TotalSeconds:0.0} s (limite {limite.TotalSeconds:0.#} s).");
        return r;
    }

    public static async Task Rapido(Func<Task> acao, TimeSpan limite, string oQue) =>
        await Rapido(async () => { await acao(); return 0; }, limite, oQue);

    private static string Texto(object? v) => v switch
    {
        null => "(vazio)",
        string s => $"\"{s}\"",
        _ => v.ToString() ?? ""
    };
}

/// <summary>
/// Roda tarefas assíncronas na thread das telas (STA), deixando o WPF trabalhar enquanto espera. Assim os testes de
/// banco e os de tela usam o mesmo caminho do programa de verdade.
/// </summary>
public static class Bomba
{
    public static void Esperar(Task tarefa, TimeSpan limite)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var quadro = new DispatcherFrame();
        tarefa.ContinueWith(_ => dispatcher.BeginInvoke(() => quadro.Continue = false), TaskScheduler.Default);
        var timer = new DispatcherTimer(limite, DispatcherPriority.Normal, (s, _) => { quadro.Continue = false; ((DispatcherTimer)s!).Stop(); }, dispatcher);
        if (!tarefa.IsCompleted) Dispatcher.PushFrame(quadro);
        timer.Stop();
        if (!tarefa.IsCompleted) throw new FalhaTeste($"Passou do tempo máximo ({limite.TotalMinutes:0.#} min) e não terminou.");
        tarefa.GetAwaiter().GetResult();
    }

    /// <summary>Deixa as telas trabalharem (carregar dados, desenhar) por um tempo.</summary>
    public static void Pausar(TimeSpan tempo)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var quadro = new DispatcherFrame();
        var timer = new DispatcherTimer(tempo, DispatcherPriority.Background, (s, _) => { quadro.Continue = false; ((DispatcherTimer)s!).Stop(); }, dispatcher);
        Dispatcher.PushFrame(quadro);
        timer.Stop();
    }

    public static Task PausarAsync(TimeSpan tempo) => Task.Delay(tempo);
}

/// <summary>Atalhos para erros do sistema que não são "falha do teste".</summary>
public static class Erros
{
    /// <summary>Script SQL ainda não rodado no banco: o teste vira "pulado" com o motivo.</summary>
    public static async Task<T> SemScriptPula<T>(Func<Task<T>> acao, string script)
    {
        try
        {
            return await acao();
        }
        catch (Exception ex) when (ex is TabelaInexistenteException or BancoDesatualizadoException)
        {
            throw new PularTeste($"O banco ainda não tem o script {script}.");
        }
    }
}
