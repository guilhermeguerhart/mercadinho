#if DESENVOLVIMENTO
using System.Windows;
using System.Windows.Controls;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Aba "Laboratório" da Previsão de compras: só existe na versão de desenvolvimento (publicar.ps1 -Desenvolvimento).
/// Abre o teste do último ano (a previsão treinada até a véspera do teste, conferida com o que vendeu) por dia, semana ou
/// mês, para todos os produtos, uma categoria, um produto ou o movimento da loja, e compara com repetir o ano passado e
/// com a média dos 60 dias antes do teste. Tudo vem pronto do banco (previsao_laboratorio, script 020).
/// </summary>
public partial class PrevisaoView
{
    private LaboratorioPrevisao? _lab;
    private int _labVersao;              // muda a cada escolha: resposta atrasada de uma escolha antiga é ignorada
    private int _labCarregando = -1;     // versão que está sendo lida agora
    private string _labEscopo = "todos";
    private string? _labCategoria;
    private long? _labProduto;
    private string? _labProdutoNome;
    private string _labAgrupar = "semana";
    private string? _labErro;

    private static readonly string[] NomesDias = ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado"];

    private void AdicionarAbaLaboratorio()
    {
        var aba = new Button { Content = "Laboratório (dev)", Style = Estilo("Chip"), Height = 40, MinWidth = 150, Uid = "laboratorio", ToolTip = "Só na versão de desenvolvimento" };
        aba.Click += Aba_Click;
        Abas.Children.Add(aba);
    }

    private async Task CarregarLaboratorioAsync()
    {
        var versao = _labVersao;
        if (_servico is null || _labCarregando == versao) return;
        _labCarregando = versao;
        _labErro = null;
        LaboratorioPrevisao? lab = null;
        string? erro = null;
        try
        {
            lab = await _servico.LaboratorioAsync(_labEscopo, _labCategoria, _labProduto, _labAgrupar);
        }
        catch (FuncaoInexistenteException)
        {
            erro = "O laboratório ainda não foi instalado no banco. Rode no Supabase o script database/020_laboratorio_previsao.sql.";
        }
        catch (TabelaInexistenteException)
        {
            erro = "O laboratório ainda não foi instalado no banco. Rode no Supabase o script database/020_laboratorio_previsao.sql.";
        }
        catch (ConexaoException ex)
        {
            erro = ex.Message;
        }
        if (versao != _labVersao) return;      // a escolha mudou enquanto lia
        _labCarregando = -1;
        _lab = lab;
        _labErro = erro;
        if (_aba == "laboratorio") Mostrar();
    }

    private void TrocarLab(string escopo, string? categoria = null, long? produto = null, string? nomeProduto = null, string? agrupar = null)
    {
        _labEscopo = escopo;
        _labCategoria = categoria;
        _labProduto = produto;
        if (nomeProduto is not null) _labProdutoNome = nomeProduto;
        if (agrupar is not null) _labAgrupar = agrupar;
        _labVersao++;
        _lab = null;
        _labErro = null;
        Mostrar();
    }

    private void MostrarLaboratorio()
    {
        Conteudo.Children.Add(Suave("Versão de desenvolvimento. A previsão aprendeu só com as vendas até a véspera do teste e foi conferida com o que vendeu " +
            "em cada dia do último ano. Comparações: repetir o ano passado (mesmo dia da semana, 52 semanas antes) e a média dos 60 dias antes do teste.", 12));
        Conteudo.Children.Add(ControlesLab());

        if (_labErro is not null)
        {
            Conteudo.Children.Add(Cartao("Não deu para carregar", Suave(_labErro)));
            return;
        }
        if (_lab is null)
        {
            Conteudo.Children.Add(Suave("Carregando…"));
            _ = CarregarLaboratorioAsync();
            return;
        }
        var l = _lab;
        if (l.TesteIni is null || l.Resumo is null || l.Serie.Count == 0)
        {
            Conteudo.Children.Add(Cartao("Ainda sem teste dia a dia", Suave("O teste é guardado a cada cálculo da previsão (precisa de mais de 1 ano de vendas). " +
                "Rode no Supabase: select public.previsao_calcular();")));
            return;
        }

        MostrarResumoLab(l, l.Resumo);
        MostrarSerieLab(l);
        if (_labEscopo != "loja" || _labAgrupar == "dia")
            Conteudo.Children.Add(DuasColunas(CartaoDiaSemana(l), CartaoMeses(l)));
        else
            Conteudo.Children.Add(CartaoMeses(l));
        if (l.Categorias.Count > 0) Conteudo.Children.Add(CartaoCategoriasLab(l));
        if (l.Produtos.Count > 0) Conteudo.Children.Add(CartaoProdutosLab(l));
        if (l.Eventos.Count > 0) Conteudo.Children.Add(CartaoEventosLab(l));
        Conteudo.Children.Add(CartaoAnosLab(l));
    }

    // ------------------------------------------------------------ Escolhas: o que ver e como agrupar

    private FrameworkElement ControlesLab()
    {
        var painel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

        var ver = new WrapPanel();
        ver.Children.Add(Rotulo("Ver:"));
        ver.Children.Add(ChipLab("Todos os produtos", _labEscopo == "todos", () => TrocarLab("todos")));
        ver.Children.Add(ChipLab("Movimento da loja", _labEscopo == "loja", () => TrocarLab("loja")));
        var produto = _labProduto ?? _produto?.ProdutoId;
        var nomeProduto = _labProduto is not null ? _labProdutoNome : _produto?.Nome;
        if (produto is { } id && nomeProduto is not null)
            ver.Children.Add(ChipLab("Produto: " + nomeProduto, _labEscopo == "produto", () => TrocarLab("produto", produto: id, nomeProduto: nomeProduto)));
        painel.Children.Add(ver);

        var categorias = _lab?.CategoriasLista ?? [];
        if (categorias.Count > 0 || _labCategoria is not null)
        {
            var cats = new WrapPanel();
            cats.Children.Add(Rotulo("Categoria:"));
            var nomes = categorias.Count > 0 ? categorias : new List<string> { _labCategoria! };
            foreach (var c in nomes)
            {
                var cat = c;
                cats.Children.Add(ChipLab(cat, _labEscopo == "categoria" && _labCategoria == cat, () => TrocarLab("categoria", categoria: cat), 34));
            }
            painel.Children.Add(cats);
        }

        var agrupar = new WrapPanel();
        agrupar.Children.Add(Rotulo("Agrupar por:"));
        foreach (var (id2, nome) in new[] { ("dia", "Dia"), ("semana", "Semana"), ("mes", "Mês") })
            agrupar.Children.Add(ChipLab(nome, _labAgrupar == id2, () => TrocarLab(_labEscopo, _labCategoria, _labProduto, agrupar: id2)));
        painel.Children.Add(agrupar);
        return painel;
    }

    private TextBlock Rotulo(string texto) =>
        new() { Text = texto, FontSize = 13.5, Style = Estilo("TextoSuave"), VerticalAlignment = VerticalAlignment.Center, Width = 92, Margin = new Thickness(0, 0, 0, 8) };

    private Button ChipLab(string texto, bool ativo, Action acao, double altura = 38)
    {
        var b = new Button { Content = texto, Style = Estilo("Chip"), Height = altura, Margin = new Thickness(0, 0, 8, 8), Tag = ativo ? "ativo" : null };
        b.Click += (_, _) => acao();
        return b;
    }

    // ------------------------------------------------------------ Resumo

    private void MostrarResumoLab(LaboratorioPrevisao l, LabResumo r)
    {
        var grao = l.Agrupar switch { "dia" => "dia", "mes" => "mês", _ => "semana" };
        var porProduto = l.Escopo is "todos" or "categoria";
        var numeros = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        numeros.Children.Add(Numero(Pct(r.Erro), porProduto ? $"erro da previsão (produto × {grao})" : $"erro da previsão (por {grao})"));
        numeros.Children.Add(Numero(Pct(r.ErroIngenuo), "repetir o ano passado"));
        numeros.Children.Add(Numero(Pct(r.ErroMedia), "média dos 60 dias"));
        if (porProduto) numeros.Children.Add(Numero(Pct(r.ErroTotal), $"erro da soma (ano passado {Pct(r.ErroTotalIngenuo)})"));
        numeros.Children.Add(Numero(Sinal(r.Vies), "viés (previu a mais/menos)"));
        numeros.Children.Add(Numero(Pct(r.DentroFaixa), $"dentro da faixa de 80% (±{l.FaixaZ.ToString("0.00", PtBr)})"));
        numeros.Children.Add(Numero($"{r.PeriodosGanhou} de {r.Periodos}", $"{(grao == "mês" ? "meses" : grao + "s")} em que errou menos que o ano passado"));

        var painel = new StackPanel();
        painel.Children.Add(numeros);
        painel.Children.Add(Suave($"Teste de {l.TesteIni:dd/MM/yyyy} a {l.TesteFim:dd/MM/yyyy}. Vendido {LabQtd(l, r.Vendido ?? 0)}, previsto {LabQtd(l, r.Previsto ?? 0)}, " +
            $"ano passado {LabQtd(l, r.Ingenuo ?? 0)}." + (porProduto ? $" {Plural(r.Produtos, "produto", "produtos")}; o erro é medido produto a produto e somado " +
            "(o \"erro da soma\" deixa os erros de produtos diferentes se compensarem)." : ""), 0));
        Conteudo.Children.Add(Cartao(l.Nome ?? "Teste", painel));
    }

    // ------------------------------------------------------------ Gráfico e tabela período a período

    private void MostrarSerieLab(LaboratorioPrevisao l)
    {
        var pontos = l.Serie.Select(s => new Graficos.PontoComparativo(RotuloPeriodo(l, s.Periodo), s.Vendido, s.Previsto, s.Minimo, s.Maximo, s.Ingenuo,
            $"{NomePeriodo(l, s)}: vendeu {LabQtd(l, s.Vendido)} · previsto {LabQtd(l, s.Previsto)} (faixa {LabQtd(l, s.Minimo)} a {LabQtd(l, s.Maximo)}) · " +
            $"ano passado {LabQtd(l, s.Ingenuo)} · erro {Pct(s.Erro)} (ano passado {Pct(s.ErroIngenuo)})" +
            (string.IsNullOrEmpty(s.Eventos) ? "" : $" · {s.Eventos}") + (string.IsNullOrEmpty(s.Anomalias) ? "" : $" · fora do normal: {s.Anomalias}"),
            !string.IsNullOrEmpty(s.Anomalias))).ToList();
        var grafico = new StackPanel();
        grafico.Children.Add(Suave("Coluna verde: o que vendeu (cinza: período fora do normal). Traço amarelo: o que a previsão esperava; risco preto: a faixa de 80%. " +
            "Traço roxo: o ano passado. Passe o mouse nas colunas.", 10));
        grafico.Children.Add(Graficos.Comparativo(pontos, 230));
        Conteudo.Children.Add(Cartao("Vendido x previsto x ano passado", grafico));

        // Tabela
        var colunas = _estreita
            ? new[] { "Período", "Vendido", "Previsto", "Erro", "Ano passado" }
            : new[] { "Período", "Vendido", "Previsto", "Erro", "Ano passado", "Erro ano p.", "Faixa", "Datas" };
        var grade = Tabela(colunas, _estreita ? 92 : 100, ultimaLarga: !_estreita);
        var linha = 0;
        foreach (var s in l.Serie)
        {
            linha++;
            grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var ganhou = (s.Erro ?? 0) <= (s.ErroIngenuo ?? 0);
            Celula(grade, linha, 0, Texto(NomePeriodo(l, s), 13.5));
            Celula(grade, linha, 1, Direita(LabQtd(l, s.Vendido)));
            Celula(grade, linha, 2, Direita(LabQtd(l, s.Previsto)));
            Celula(grade, linha, 3, Direita(Pct(s.Erro), ganhou ? "Verde700" : "Vermelho600", true));
            Celula(grade, linha, 4, Direita(LabQtd(l, s.Ingenuo)));
            if (_estreita) continue;
            Celula(grade, linha, 5, Direita(Pct(s.ErroIngenuo)));
            Celula(grade, linha, 6, Direita(s.Previsto <= 0 ? "—" : s.Vendido < s.Minimo ? "abaixo" : s.Vendido > s.Maximo ? "acima" : "dentro",
                s.ForaDaFaixa ? "Ambar700" : null));
            var datas = string.Join(" · ", new[] { s.Eventos, string.IsNullOrEmpty(s.Anomalias) ? null : "fora do normal: " + s.Anomalias }.Where(x => !string.IsNullOrEmpty(x)));
            Celula(grade, linha, 7, Texto(datas, 12.5, string.IsNullOrEmpty(s.Anomalias) ? "Roxo600" : "Ambar700"));
        }
        Conteudo.Children.Add(Cartao($"Período a período ({l.Serie.Count})", Envolver(
            Suave("Erro em verde: a previsão errou menos que repetir o ano passado naquele período; em vermelho: errou mais.", 8),
            Rolavel(grade, 520))));
    }

    // ------------------------------------------------------------ Dia da semana, mês, categorias, produtos, datas e anos

    private FrameworkElement CartaoDiaSemana(LaboratorioPrevisao l)
    {
        var lista = new StackPanel();
        var maximo = l.DiaSemana.Count == 0 ? 0 : l.DiaSemana.Max(d => Math.Max(d.Erro ?? 0, d.ErroIngenuo ?? 0));
        foreach (var d in l.DiaSemana)
            lista.Children.Add(Graficos.Barra(NomesDias[d.Dow], d.Erro ?? 0, maximo, $"{Pct(d.Erro)} · ano p. {Pct(d.ErroIngenuo)} · viés {Sinal(d.Vies)}",
                (d.Erro ?? 0) <= (d.ErroIngenuo ?? 0) ? "Verde600" : "Vermelho600", 90));
        return Cartao("Erro por dia da semana (no dia)", Envolver(Suave("Sempre medido dia a dia. Verde: errou menos que o ano passado.", 8), lista));
    }

    private FrameworkElement CartaoMeses(LaboratorioPrevisao l)
    {
        var lista = new StackPanel();
        var maximo = l.Meses.Count == 0 ? 0 : l.Meses.Max(m => Math.Max(m.Erro ?? 0, m.ErroIngenuo ?? 0));
        foreach (var m in l.Meses)
            lista.Children.Add(Graficos.Barra(m.Mes.ToString("MMM/yy", PtBr).Replace(".", ""), m.Erro ?? 0, maximo,
                $"{Pct(m.Erro)} · ano p. {Pct(m.ErroIngenuo)} · viés {Sinal(m.Vies)}", (m.Erro ?? 0) <= (m.ErroIngenuo ?? 0) ? "Verde600" : "Vermelho600", 70));
        return Cartao("Erro por mês (no mês inteiro)", Envolver(Suave("Medido no total de cada mês" + (l.Escopo is "todos" or "categoria" ? ", produto a produto." : "."), 8), lista));
    }

    private FrameworkElement CartaoCategoriasLab(LaboratorioPrevisao l)
    {
        var lista = new StackPanel();
        var maximo = l.Categorias.Max(c => Math.Max(c.Erro ?? 0, c.ErroIngenuo ?? 0));
        foreach (var c in l.Categorias)
        {
            var barra = Graficos.Barra($"{c.Categoria} ({c.Produtos})", c.Erro ?? 0, maximo, $"{Pct(c.Erro)} · ano p. {Pct(c.ErroIngenuo)} · viés {Sinal(c.Vies)}",
                (c.Erro ?? 0) <= (c.ErroIngenuo ?? 0) ? "Verde600" : "Vermelho600", 160);
            var cat = c.Categoria;
            var botao = new Button { Content = barra, Style = Estilo("BotaoLink"), HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = "Ver só esta categoria" };
            botao.Click += (_, _) => TrocarLab("categoria", categoria: cat);
            lista.Children.Add(botao);
        }
        return Cartao("Erro por categoria", Envolver(Suave("Clique numa categoria para abrir só ela.", 8), lista));
    }

    private FrameworkElement CartaoProdutosLab(LaboratorioPrevisao l)
    {
        var colunas = _estreita
            ? new[] { "Produto", "Vendido", "Erro", "Ano passado" }
            : new[] { "Produto", "Categoria", "Vendido", "Previsto", "Erro", "Ano passado", "Média 60 d", "Viés" };
        var grade = Tabela(colunas, _estreita ? 92 : 100);
        var linha = 0;
        foreach (var p in l.Produtos)
        {
            linha++;
            grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var porPeso = p.Unidade == "kg";
            var link = new Button { Content = p.Produto, Style = Estilo("BotaoLink"), HorizontalAlignment = HorizontalAlignment.Left, FontSize = 13.5, ToolTip = "Abrir este produto no laboratório" };
            var (id, nome) = (p.ProdutoId, p.Produto);
            link.Click += (_, _) => TrocarLab("produto", produto: id, nomeProduto: nome);
            Celula(grade, linha, 0, link);
            var c = 1;
            if (!_estreita) Celula(grade, linha, c++, Texto(p.Categoria, 13));
            Celula(grade, linha, c++, Direita(Qtd(p.Vendido, porPeso)));
            if (!_estreita) Celula(grade, linha, c++, Direita(Qtd(p.Previsto, porPeso)));
            Celula(grade, linha, c++, Direita(Pct(p.Erro), (p.Erro ?? 0) <= (p.ErroIngenuo ?? 0) ? "Verde700" : "Vermelho600", true));
            Celula(grade, linha, c++, Direita(Pct(p.ErroIngenuo)));
            if (_estreita) continue;
            Celula(grade, linha, c++, Direita(Pct(p.ErroMedia)));
            Celula(grade, linha, c, Direita(Sinal(p.Vies)));
        }
        var grao = l.Agrupar switch { "dia" => "dia a dia", "mes" => "mês a mês", _ => "semana a semana" };
        return Cartao($"Produtos ({l.Produtos.Count})", Envolver(Suave($"Do que mais vende ao que menos vende. Erro medido {grao}. Clique no nome para abrir o produto.", 8), Rolavel(grade, 520)));
    }

    private FrameworkElement CartaoEventosLab(LaboratorioPrevisao l)
    {
        var grade = Tabela(_estreita ? new[] { "Data", "Vendido", "Previsto", "Ano passado" } : new[] { "Data", "Vendido", "Previsto", "Erro", "Ano passado", "Erro ano p." }, _estreita ? 92 : 100);
        var linha = 0;
        foreach (var e in l.Eventos)
        {
            linha++;
            grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Celula(grade, linha, 0, Texto($"{e.Dia.ToString("ddd dd/MM/yy", PtBr).Replace(".", "")} · {e.Evento}", 13.5));
            var erro = e.Vendido > 0 ? Math.Abs(e.Previsto - e.Vendido) / e.Vendido : (decimal?)null;
            var erroAno = e.Vendido > 0 ? Math.Abs(e.Ingenuo - e.Vendido) / e.Vendido : (decimal?)null;
            Celula(grade, linha, 1, Direita(LabQtd(l, e.Vendido)));
            Celula(grade, linha, 2, Direita(LabQtd(l, e.Previsto)));
            if (_estreita)
            {
                Celula(grade, linha, 3, Direita(LabQtd(l, e.Ingenuo)));
                continue;
            }
            Celula(grade, linha, 3, Direita(Pct(erro), (erro ?? 0) <= (erroAno ?? 0) ? "Verde700" : "Vermelho600", true));
            Celula(grade, linha, 4, Direita(LabQtd(l, e.Ingenuo)));
            Celula(grade, linha, 5, Direita(Pct(erroAno)));
        }
        return Cartao($"Datas especiais no teste ({l.Eventos.Count})", Envolver(
            Suave("O dia inteiro do que está selecionado. O ano passado nem sempre caiu na mesma data (Páscoa, Carnaval mudam de dia).", 8), Rolavel(grade, 420)), 16);
    }

    private FrameworkElement CartaoAnosLab(LaboratorioPrevisao l)
    {
        if (l.Anos.Count == 0)
            return Cartao("Todos os anos testados", Suave("Ainda não há teste ano a ano. Rode no Supabase: select public.previsao_testar_anos(); (leva alguns minutos)."), 0);
        var grade = Tabela(_estreita ? new[] { "Ano", "Erro", "Ano passado", "Média 60 d" } : new[] { "Ano", "Erro", "Sem anomalias", "Ano passado", "Média 60 d", "Movimento", "Viés", "Faixa" }, _estreita ? 92 : 100);
        var linha = 0;
        foreach (var a in l.Anos.OrderByDescending(a => a.Ano))
        {
            linha++;
            grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Celula(grade, linha, 0, Texto(a.Ano.ToString(PtBr) + (a.SemanasComAnomalia is > 0 ? $" ({a.SemanasComAnomalia} sem. fora do normal)" : ""), 13.5));
            var c = 1;
            Celula(grade, linha, c++, Direita(Pct(a.Erro), (a.Erro ?? 0) <= (a.ErroIngenuo ?? 0) ? "Verde700" : "Vermelho600", true));
            if (!_estreita) Celula(grade, linha, c++, Direita(Pct(a.ErroNormal)));
            Celula(grade, linha, c++, Direita(Pct(a.ErroIngenuo)));
            Celula(grade, linha, c++, Direita(Pct(a.ErroMedia)));
            if (_estreita) continue;
            Celula(grade, linha, c++, Direita(Pct(a.MovErro)));
            Celula(grade, linha, c++, Direita(Sinal(a.Vies)));
            Celula(grade, linha, c, Direita(Pct(a.DentroFaixa)));
        }
        var ganhou = l.Anos.Count(a => (a.Erro ?? 0) < (a.ErroIngenuo ?? 0));
        return Cartao("Todos os anos testados", Envolver(
            Suave($"Cada ano: a previsão aprendeu só com os 4 anos anteriores e foi conferida semana a semana, produto a produto. " +
                  $"Errou menos que repetir o ano passado em {ganhou} de {l.Anos.Count} anos. \"Movimento\" = erro no número de vendas por dia da loja.", 8),
            grade), 0);
    }

    // ------------------------------------------------------------ Peças

    private Grid Tabela(string[] colunas, double larguraColuna, bool ultimaLarga = false)
    {
        var grade = new Grid();
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });
        for (var c = 1; c < colunas.Length; c++)
            grade.ColumnDefinitions.Add(ultimaLarga && c == colunas.Length - 1
                ? new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 }
                : new ColumnDefinition { Width = new GridLength(larguraColuna) });
        grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c < colunas.Length; c++)
            Celula(grade, 0, c, new TextBlock
            {
                Text = colunas[c], FontSize = 12.5, FontWeight = FontWeights.SemiBold, Style = Estilo("TextoSuave"),
                TextAlignment = c == 0 || (ultimaLarga && c == colunas.Length - 1) ? TextAlignment.Left : TextAlignment.Right
            });
        return grade;
    }

    private static FrameworkElement Rolavel(FrameworkElement conteudo, double alturaMaxima) =>
        new ScrollViewer { Content = conteudo, MaxHeight = alturaMaxima, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 0) };

    private TextBlock Texto(string texto, double tamanho, string? cor = null)
    {
        var t = new TextBlock { Text = texto, FontSize = tamanho, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        if (cor is not null) t.Foreground = Pincel(cor);
        return t;
    }

    private TextBlock Direita(string texto, string? cor = null, bool negrito = false)
    {
        var t = new TextBlock { Text = texto, FontSize = 13.5, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        if (cor is not null) t.Foreground = Pincel(cor);
        if (negrito) t.FontWeight = FontWeights.SemiBold;
        return t;
    }

    private static string Pct(decimal? v) => v is { } x ? x.ToString("P1", PtBr) : "—";

    private static string Sinal(decimal? v) => v is { } x ? (x >= 0 ? "+" : "−") + Math.Abs(x).ToString("P1", PtBr) : "—";

    private static string LabQtd(LaboratorioPrevisao l, decimal v) => l.Unidade switch
    {
        "kg" => Qtd(v, true),
        "un" => Qtd(v, false),
        "vendas" => $"{v.ToString("N0", PtBr)} vendas",
        _ => v.ToString("N0", PtBr),
    };

    private static string RotuloPeriodo(LaboratorioPrevisao l, DateOnly d) =>
        l.Agrupar == "mes" ? d.ToString("MMM/yy", PtBr).Replace(".", "") : d.ToString("dd/MM", PtBr);

    private static string NomePeriodo(LaboratorioPrevisao l, LabPeriodo s) => l.Agrupar switch
    {
        "dia" => s.Periodo.ToString("ddd dd/MM/yy", PtBr).Replace(".", ""),
        "mes" => s.Periodo.ToString("MMMM 'de' yyyy", PtBr),
        _ => $"{(s.Ini ?? s.Periodo):dd/MM} a {(s.Fim ?? s.Periodo.AddDays(6)):dd/MM/yy}",
    };
}
#endif
