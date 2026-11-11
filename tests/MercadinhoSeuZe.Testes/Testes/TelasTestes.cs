using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Testes.Motor;
using MercadinhoSeuZe.Views;

namespace MercadinhoSeuZe.Testes.Testes;

/// <summary>
/// Abre cada tela do programa numa janela fora da vista, com o gerente e com o operador (e sem banco), espera carregar
/// os dados de verdade, confere que não deu erro e guarda uma foto da tela para o relatório.
/// </summary>
public static class TelasTestes
{
    public static List<string> ErrosNaoTratados { get; set; } = [];
    public static List<string> Imagens { get; } = [];

    private static Window? _janela;

    /// <summary>Frases que as telas mostram quando algo deu errado ao carregar.</summary>
    private static readonly string[] FrasesDeErro =
        ["Não foi possível", "ainda não foram criadas", "Erro no servidor", "Sua sessão expirou", "não tem permissão", "Resposta inválida"];

    public static Lista Montar(Ambiente amb)
    {
        var l = new Lista("Telas");
        var janelaFalsa = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var espera = TimeSpan.FromSeconds(6);

        l.Tela("Tela inicial e tela de login", () =>
        {
            Mostrar(new LandingView(janelaFalsa, amb.Api), "Tela inicial", TimeSpan.FromSeconds(1), amb);
            var auth = amb.Api is null ? null : new AuthService(amb.Api, new PreferenciasLocais());
            Mostrar(new LoginView(janelaFalsa, auth, new PreferenciasLocais()), "Login", TimeSpan.FromSeconds(1), amb);
            return Task.CompletedTask;
        });

        foreach (var (id, nome) in new[] { ("painel", "Painel"), ("estoque", "Estoque"), ("fornecedores", "Fornecedores"), ("caixa", "Caixa (gerente)"),
                     ("funcionarios", "Funcionários"), ("fechamento", "Fechamento do dia"), ("previsao", "Previsão de compras") })
        {
            l.Tela($"{nome} abre com os dados do banco, sem erro", async () =>
            {
                var g = await amb.Entrar("gerente");
                var area = new AreaInternaView(janelaFalsa, g, amb.Api);
                Colocar(area);
                area.Abrir(id);
                Mostrar(area, nome, espera, amb);
            }, banco: true);
        }
        l.Tela("Caixa do operador abre sem erro (sem o resumo de vendas)", async () =>
        {
            var op = await amb.Entrar("operador");
            var area = new AreaInternaView(janelaFalsa, op, amb.Api);
            Mostrar(area, "Caixa (operador)", espera, amb);
            Verificar.Verdadeiro(!TextosVisiveis(area).Any(t => t.Contains("Vendas de hoje", StringComparison.OrdinalIgnoreCase)),
                "O operador está vendo a lista \"Vendas de hoje\" (é só do gerente)");
        }, banco: true);
        l.Tela("Janelas: Meu perfil e maquininha de teste", async () =>
        {
            var g = await amb.Entrar("gerente");
            var area = new AreaInternaView(janelaFalsa, g, amb.Api);
            Colocar(area);
            area.AbrirJanela(new MeuPerfilJanela(area, g, new FuncionarioService(amb.Api!, g)));
            Mostrar(area, "Meu perfil", TimeSpan.FromSeconds(3), amb);
            area.FecharJanela();
            area.AbrirJanela(new MaquininhaJanela(area, new MaquininhaEmulada(), new Cobranca("credito", 123.45m, 3), "TESTE1"));
            Mostrar(area, "Maquininha", TimeSpan.FromSeconds(2), amb);
            area.FecharJanela();
        }, banco: true);
        l.Tela("Sem internet: todas as telas abrem e avisam, sem fechar o programa", () =>
        {
            var falsa = new Sessao(Guid.NewGuid(), "teste", "Teste", Perfil.Gerente, "token-falso");
            var area = new AreaInternaView(janelaFalsa, falsa, null);
            Colocar(area);
            foreach (var id in new[] { "painel", "estoque", "fornecedores", "caixa", "funcionarios", "fechamento", "previsao" })
            {
                area.Abrir(id);
                Mostrar(area, "Sem banco - " + id, TimeSpan.FromSeconds(1), amb, aceitarAvisoDeErro: true, foto: id == "estoque");
            }
            return Task.CompletedTask;
        });
        l.Tela("Previsão de compras: as abas e a previsão de um produto", async () =>
        {
            var g = await amb.Entrar("gerente");
            var area = new AreaInternaView(janelaFalsa, g, amb.Api);
            Colocar(area);
            var tela = (PrevisaoView)area.Abrir("previsao")!;
            Mostrar(area, "Previsão - pedido sugerido", espera, amb, foto: false);
            foreach (var aba in tela.Abas.Children.OfType<Button>().Where(b => b.Uid != "pedido").ToList())
            {
                aba.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Mostrar(area, "Previsão - " + aba.Uid, TimeSpan.FromSeconds(aba.Uid == "produto" ? 2 : 1), amb);
            }
            // Aba Produto: abre o primeiro da lista de resultados (produtos com alerta) ou busca "a"
            tela.Abas.Children.OfType<Button>().First(b => b.Uid == "produto").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Bomba.Pausar(TimeSpan.FromSeconds(1));
            if (!tela.ResultadosBusca.Children.OfType<Button>().Any()) tela.Busca.Text = "a";
            Bomba.Pausar(TimeSpan.FromMilliseconds(300));
            var primeiro = tela.ResultadosBusca.Children.OfType<Button>().FirstOrDefault();
            if (primeiro is null) throw new PularTeste("Nenhum produto para abrir na aba Produto.");
            primeiro.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Mostrar(area, "Previsão - um produto", TimeSpan.FromSeconds(3), amb);
        }, banco: true);
        l.Tela("Telas em janela pequena (1024 × 600) e grande (1920 × 1080)", async () =>
        {
            var g = await amb.Entrar("gerente");
            foreach (var (w, h) in new[] { (1024, 600), (1920, 1080) })
            {
                Janela().Width = w;
                Janela().Height = h;
                var area = new AreaInternaView(janelaFalsa, g, amb.Api);
                Colocar(area);
                area.Abrir("estoque");
                Mostrar(area, $"Estoque {w}x{h}", TimeSpan.FromSeconds(4), amb);
            }
            Janela().Width = 1366;
            Janela().Height = 820;
        }, banco: true);
        return l;
    }

    private static Window Janela()
    {
        if (_janela is not null) return _janela;
        _janela = new Window
        {
            Title = "Teste das telas",
            Width = 1366, Height = 820, Left = -30000, Top = -30000,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
            Background = (Brush?)Application.Current.TryFindResource("Fundo") ?? Brushes.White
        };
        _janela.Show();
        return _janela;
    }

    private static void Colocar(FrameworkElement conteudo)
    {
        Janela().Content = conteudo;
        conteudo.UpdateLayout();
    }

    private static void Mostrar(FrameworkElement conteudo, string nome, TimeSpan espera, Ambiente amb, bool aceitarAvisoDeErro = false, bool foto = true)
    {
        var antes = ErrosNaoTratados.Count;
        if (!ReferenceEquals(Janela().Content, conteudo)) Colocar(conteudo);
        Bomba.Pausar(espera);
        conteudo.UpdateLayout();
        if (foto) Fotografar(conteudo, nome, amb);
        var novos = ErrosNaoTratados.Skip(antes).ToList();
        if (novos.Count > 0) throw new FalhaTeste($"{nome}: erro não tratado na tela: {string.Join(" | ", novos)}");
        if (!aceitarAvisoDeErro)
        {
            var erros = TextosVisiveis(conteudo).Where(t => FrasesDeErro.Any(f => t.Contains(f, StringComparison.OrdinalIgnoreCase))).Distinct().ToList();
            if (erros.Count > 0) throw new FalhaTeste($"{nome} mostrou: {string.Join(" | ", erros)}");
        }
    }

    /// <summary>Textos que estão aparecendo na tela.</summary>
    public static IEnumerable<string> TextosVisiveis(DependencyObject raiz)
    {
        var fila = new Queue<DependencyObject>([raiz]);
        while (fila.Count > 0)
        {
            var d = fila.Dequeue();
            if (d is UIElement { IsVisible: false }) continue;
            if (d is TextBlock { Text.Length: > 0 } tb) yield return tb.Text;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) fila.Enqueue(VisualTreeHelper.GetChild(d, i));
        }
    }

    private static void Fotografar(FrameworkElement elemento, string nome, Ambiente amb)
    {
        try
        {
            var w = (int)Math.Max(1, elemento.ActualWidth);
            var h = (int)Math.Max(1, elemento.ActualHeight);
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            var fundo = new DrawingVisual();
            using (var dc = fundo.RenderOpen())
                dc.DrawRectangle((Brush?)Application.Current.TryFindResource("Fundo") ?? Brushes.White, null, new Rect(0, 0, w, h));
            bmp.Render(fundo);
            bmp.Render(elemento);
            var pasta = Path.Combine(amb.PastaSaida, "telas");
            Directory.CreateDirectory(pasta);
            var arquivo = string.Concat(nome.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".png";
            using var f = File.Create(Path.Combine(pasta, arquivo));
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            enc.Save(f);
            Imagens.Add("telas/" + arquivo);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            Ambiente.Nota("Não consegui guardar a foto da tela: " + ex.Message);
        }
    }
}
