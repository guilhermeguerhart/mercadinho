using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Previsão de compras (semana 7, só o gerente). Indicadores no topo e 4 abas:
/// "Pedido sugerido" (cada fornecedor: quando pedir, quando chega, o que pedir; abre o pedido já preenchido),
/// "Produto" (12 semanas vendidas x esperadas, 8 semanas à frente com a faixa, dia da semana, mês e datas especiais),
/// "Teste da previsão" (erro ano a ano contra repetir o ano passado) e "Alertas". Na versão de desenvolvimento, mais a aba
/// "Laboratório" (PrevisaoView.Laboratorio.cs).
/// Nada é calculado aqui: tudo vem pronto do banco (previsao_tela, pedido_sugerido, previsao_produto, previsao_alertas).
/// </summary>
public partial class PrevisaoView : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly AreaInternaView _area;
    private readonly PrevisaoService? _servico;
    private readonly FornecedorService? _fornecedores;
    private readonly EstoqueService? _estoque;

    private PrevisaoTela? _tela;
    private List<PedidoSugerido> _sugeridos = [];
    private List<AlertaPrevisao> _alertas = [];
    private List<Produto>? _produtos;
    private PrevisaoProduto? _produto;
    private string _aba = "pedido";
    private bool _estreita;

    public PrevisaoView(AreaInternaView area, PrevisaoService? servico, FornecedorService? fornecedores, EstoqueService? estoque)
    {
        InitializeComponent();
        _area = area;
        _servico = servico;
        _fornecedores = fornecedores;
        _estoque = estoque;
#if DESENVOLVIMENTO
        AdicionarAbaLaboratorio();
#endif
        Loaded += async (_, _) => await CarregarAsync();
    }

    // ------------------------------------------------------------ Dados

    private async Task CarregarAsync()
    {
        AvisoErro.Visibility = Visibility.Collapsed;
        if (_servico is null)
        {
            MostrarErro("Sistema sem configuração do banco de dados. Veja o README.");
            return;
        }
        BotaoAtualizar.IsEnabled = false;
        try
        {
            var tela = _servico.TelaAsync();
            var sugeridos = _servico.SugeridosAsync();
            var alertas = _servico.AlertasAsync();
            await Task.WhenAll(tela, sugeridos, alertas);
            _tela = tela.Result;
            _sugeridos = sugeridos.Result;
            _alertas = alertas.Result;
        }
        catch (FuncaoInexistenteException)
        {
            MostrarErro("A previsão ainda não foi instalada no banco. Rode no Supabase os scripts database/016_semana7_previsao.sql e database/019_semana7_tela_previsao.sql.");
            return;
        }
        catch (TabelaInexistenteException)
        {
            MostrarErro("A previsão ainda não foi instalada no banco. Rode no Supabase os scripts database/016_semana7_previsao.sql e database/019_semana7_tela_previsao.sql.");
            return;
        }
        catch (ConexaoException ex)
        {
            MostrarErro(ex.Message);
            return;
        }
        finally
        {
            BotaoAtualizar.IsEnabled = true;
        }
        if (_produto is not null) await CarregarProdutoAsync(_produto.ProdutoId, trocarAba: false);
#if DESENVOLVIMENTO
        _lab = null;     // "Atualizar" também lê o laboratório de novo
#endif
        Mostrar();
    }

    private void MostrarErro(string mensagem)
    {
        AvisoErroTexto.Text = mensagem;
        AvisoErro.Visibility = Visibility.Visible;
        Subtitulo.Text = "Não foi possível carregar a previsão.";
    }

    private async void Atualizar_Click(object sender, RoutedEventArgs e) => await CarregarAsync();

    private async Task<List<Produto>?> ProdutosAsync()
    {
        if (_produtos is not null || _estoque is null) return _produtos;
        try
        {
            _produtos = await _estoque.ListarAsync();
        }
        catch (ConexaoException ex)
        {
            _area.MostrarToast(ex.Message);
        }
        return _produtos;
    }

    private async Task CarregarProdutoAsync(long produtoId, bool trocarAba = true)
    {
        if (_servico is null) return;
        try
        {
            _produto = await _servico.ProdutoAsync(produtoId);
        }
        catch (ConexaoException ex)
        {
            _area.MostrarToast(ex.Message);
            return;
        }
        if (trocarAba) TrocarAba("produto");
        else Mostrar();
    }

    // ------------------------------------------------------------ Navegação

    private void Aba_Click(object sender, RoutedEventArgs e) => TrocarAba(((Button)sender).Uid);

    private async void TrocarAba(string aba)
    {
        _aba = aba;
        foreach (var b in Abas.Children.OfType<Button>()) b.Tag = b.Uid == aba ? "ativo" : null;
        BuscaProduto.Visibility = aba == "produto" ? Visibility.Visible : Visibility.Collapsed;
        Mostrar();
        if (aba == "produto")
        {
            await ProdutosAsync();
            MostrarResultadosBusca();
            if (_produto is null) Busca.Focus();
        }
    }

    private void AoRedimensionar(object sender, SizeChangedEventArgs e)
    {
        var estreita = e.NewSize.Width < 900;
        if (estreita == _estreita) return;
        _estreita = estreita;
        if (_tela is not null) Mostrar();
    }

    private void Mostrar()
    {
        if (_tela is null) return;
        MostrarTopo(_tela);
        Conteudo.Children.Clear();
        switch (_aba)
        {
            case "produto": MostrarProduto(); break;
            case "teste": MostrarTeste(_tela); break;
            case "alertas": MostrarAlertas(); break;
#if DESENVOLVIMENTO
            case "laboratorio": MostrarLaboratorio(); break;
#endif
            default: MostrarPedidos(); break;
        }
    }

    // ------------------------------------------------------------ Topo

    private void MostrarTopo(PrevisaoTela t)
    {
        var g = t.Geral;
        Subtitulo.Text = g is null
            ? "A previsão ainda não foi calculada. Rode no Supabase: select public.previsao_calcular();"
            : $"Calculada em {g.CalculadoEm.ToLocalTime():dd/MM 'às' HH:mm} com as vendas de {g.TreinoIni:MM/yyyy} até {g.TreinoFim:dd/MM/yyyy}. Recalcula sozinha toda madrugada.";

        Kpis.Children.Clear();
        Kpis.Children.Add(Kpi("IconeTendencia", "Verde700", g?.ErroSemana is { } erro ? erro.ToString("P0", PtBr) : "—", "Erro da previsão",
            g?.ErroIngenuo is { } ing ? $"Por produto e semana, no último ano. Repetir o ano passado erraria {ing:P0}." : "Precisa de mais de 1 ano de vendas para testar"));
        var perdidas = t.Perdidas;
        Kpis.Children.Add(Kpi("IconeAlerta", perdidas is { Clientes: > 0 } ? "Vermelho600" : "Verde700",
            perdidas is null ? "—" : perdidas.Clientes.ToString("N0", PtBr), "Vendas perdidas",
            perdidas is null ? "Ainda não há registro de falta" : $"Clientes sem o produto nos últimos 7 dias ({Plural(perdidas.Produtos, "produto", "produtos")})"));
        var hoje = ParaPedirHoje().ToList();
        Kpis.Children.Add(Kpi("IconeCaminhao", hoje.Count > 0 ? "Ambar700" : "Verde700", hoje.Count.ToString(PtBr), "Pedir hoje",
            hoje.Count == 0 ? "Nenhum fornecedor para pedir hoje" : string.Join(", ", hoje.Select(s => s.Fornecedor))));
        Kpis.Children.Add(Kpi("IconeAlerta", _alertas.Count > 0 ? "Ambar700" : "Verde700", _alertas.Count.ToString(PtBr), "Alertas",
            _alertas.Count == 0 ? "Nada fora do normal" : "Faltas, produtos parados e datas chegando"));
        Kpis.Columns = Kpis.Children.Count;
        AbaAlertas.Content = _alertas.Count > 0 ? $"Alertas ({_alertas.Count})" : "Alertas";
    }

    private IEnumerable<PedidoSugerido> ParaPedirHoje() => _sugeridos.Where(s => s.PedirHoje && !s.TemPendente && s.Itens.Count > 0);

    // ------------------------------------------------------------ Aba "Pedido sugerido"

    private void MostrarPedidos()
    {
        Conteudo.Children.Add(Suave("Para cada fornecedor: o próximo dia de pedido (para chegar num dia em que ele entrega), e quanto pedir para durar até a " +
            "entrega seguinte. Sugerido = o que deve vender até lá + uma margem de segurança − o estoque − o que já foi pedido, em caixas fechadas.", 16));
        if (_sugeridos.Count == 0)
        {
            Conteudo.Children.Add(Cartao("Nenhum fornecedor ativo", Suave("Cadastre os fornecedores e ligue cada produto ao seu fornecedor (cadastro do produto).")));
            return;
        }
        foreach (var s in _sugeridos.OrderByDescending(s => s.PedirHoje && !s.TemPendente).ThenBy(s => s.DataPedido).ThenBy(s => s.Fornecedor, StringComparer.Create(PtBr, true)))
            Conteudo.Children.Add(CartaoFornecedor(s));
    }

    private FrameworkElement CartaoFornecedor(PedidoSugerido s)
    {
        var painel = new StackPanel();

        // Quando pedir e quando chega
        var situacao = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        if (s.TemPendente)
            situacao.Children.Add(Etiqueta("Pedido a caminho", "Azul50", "Azul600"));
        else if (s.PedirHoje)
            situacao.Children.Add(Etiqueta("Pedir hoje", "Ambar50", "Ambar700"));
        else
            situacao.Children.Add(Etiqueta($"Próximo pedido: {NomeDia(s.DataPedido)}", "Verde50", "Verde700"));
        situacao.Children.Add(new TextBlock
        {
            Text = $"Pedido feito {(s.PedirHoje ? "hoje" : NomeDia(s.DataPedido))} chega {NomeDia(s.Chegada)} e precisa durar até {NomeDia(s.ProximaEntrega)}.",
            FontSize = 13.5, Style = Estilo("TextoSuave"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap
        });
        painel.Children.Add(situacao);
        if (s.TemPendente)
            painel.Children.Add(Suave("Já tem pedido pendente deste fornecedor: o que foi pedido já está descontado da sugestão.", 8));

        if (s.Itens.Count == 0)
        {
            painel.Children.Add(Suave("Nada a pedir: o estoque e o que já foi pedido cobrem até a entrega seguinte."));
            return Cartao(s.Fornecedor, painel);
        }

        // Tabela dos itens
        var colunas = _estreita ? new[] { "Produto", "Estoque", "Vende até lá", "Pedir" } : new[] { "Produto", "Estoque", "Já pedido", "Vende até lá", "Margem", "Pedir" };
        var grade = new Grid();
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var c = 1; c < colunas.Length; c++) grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_estreita ? 90 : 105) });
        var linha = 0;
        grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c < colunas.Length; c++)
            Celula(grade, linha, c, new TextBlock { Text = colunas[c], FontSize = 12.5, FontWeight = FontWeights.SemiBold, Style = Estilo("TextoSuave"), TextAlignment = c == 0 ? TextAlignment.Left : TextAlignment.Right });
        foreach (var i in s.Itens)
        {
            linha++;
            grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var nome = new StackPanel();
            var link = new Button { Content = i.Nome, Style = Estilo("BotaoLink"), HorizontalAlignment = HorizontalAlignment.Left, FontSize = 14, ToolTip = "Ver a previsão deste produto" };
            var id = i.ProdutoId;
            link.Click += async (_, _) => await CarregarProdutoAsync(id);
            nome.Children.Add(link);
            if (!string.IsNullOrEmpty(i.Eventos))
                nome.Children.Add(new TextBlock { Text = $"Inclui: {i.Eventos}", FontSize = 12, Foreground = Pincel("Ambar700"), TextWrapping = TextWrapping.Wrap });
            Celula(grade, linha, 0, nome);
            var valores = new List<string> { Qtd(i.Estoque, i.PorPeso) };
            if (!_estreita) valores.Add(i.Pendente > 0 ? Qtd(i.Pendente, i.PorPeso) : "—");
            valores.Add(Qtd(i.Previsto, i.PorPeso));
            if (!_estreita) valores.Add("+" + Qtd(i.Margem, i.PorPeso));
            for (var c = 0; c < valores.Count; c++)
                Celula(grade, linha, c + 1, new TextBlock { Text = valores[c], FontSize = 14, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            var caixas = !i.PorPeso && i.Caixa > 1 ? $"{i.Sugerido / i.Caixa:0} cx de {i.Caixa:0}" : null;
            var pedir = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            pedir.Children.Add(new TextBlock { Text = Qtd(i.Sugerido, i.PorPeso), FontSize = 15, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Right });
            if (caixas is not null) pedir.Children.Add(new TextBlock { Text = caixas, FontSize = 12, Style = Estilo("TextoSuave"), TextAlignment = TextAlignment.Right });
            Celula(grade, linha, colunas.Length - 1, pedir);
        }
        painel.Children.Add(grade);

        // Rodapé: total, pedido mínimo e o botão
        var rodape = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var botao = new Button { Content = "Revisar e criar pedido", Style = Estilo("BotaoPrimario"), Height = 42, Padding = new Thickness(18, 0, 18, 0) };
        botao.Click += async (_, _) => await AbrirPedidoAsync(s);
        DockPanel.SetDock(botao, Dock.Right);
        rodape.Children.Add(botao);
        var total = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        total.Children.Add(new TextBlock { Text = $"Custo estimado: {s.Total.ToString("C", PtBr)} · {Plural(s.Itens.Count, "item", "itens")}", FontSize = 14, FontWeight = FontWeights.SemiBold });
        if (s.PedidoMinimo is { } minimo && s.Total < minimo)
            total.Children.Add(new TextBlock { Text = $"Abaixo do pedido mínimo do fornecedor ({minimo.ToString("C", PtBr)}).", FontSize = 13, Foreground = Pincel("Ambar700") });
        rodape.Children.Add(total);
        painel.Children.Add(rodape);
        return Cartao(s.Fornecedor, painel);
    }

    /// <summary>Abre o mesmo "Novo pedido" do Fornecedores, já com os itens e as quantidades sugeridas.</summary>
    private async Task AbrirPedidoAsync(PedidoSugerido s)
    {
        if (_fornecedores is null) return;
        List<Fornecedor> fornecedores;
        List<Pedido> pendentes;
        try
        {
            var f = _fornecedores.ListarAsync();
            var p = _fornecedores.ListarPendentesAsync();
            await Task.WhenAll(f, p);
            fornecedores = f.Result;
            pendentes = p.Result;
        }
        catch (ConexaoException ex)
        {
            _area.MostrarToast(ex.Message);
            return;
        }
        var produtos = await ProdutosAsync();
        if (produtos is null) return;
        var fornecedor = fornecedores.FirstOrDefault(f => f.Id == s.FornecedorId);
        if (fornecedor is null)
        {
            _area.MostrarToast("Fornecedor não encontrado. Atualize a tela.");
            return;
        }
        var itens = s.Itens.Select(i => produtos.FirstOrDefault(p => p.Id == i.ProdutoId)).OfType<Produto>().ToList();
        var quantidades = s.Itens.ToDictionary(i => i.ProdutoId, i => i.Sugerido);
        _area.AbrirJanela(new PedidoJanela(_area, _fornecedores, fornecedores.Where(f => f.Ativo).ToList(), produtos, fornecedor, itens,
            produto => pendentes.Any(o => o.Pendente && o.Itens.Any(i => i.ProdutoId == produto.Id)),
            async (mensagem, forn, texto, pedidoId) =>
            {
                _area.MostrarToast(mensagem);
                await CarregarAsync();
                _area.AbrirJanela(new EnvioPedidoJanela(_area, forn, texto, "Pedido criado", pedidoId, _area.Mensagens()));
            },
            quantidades: quantidades));
    }

    // ------------------------------------------------------------ Aba "Produto"

    private void Busca_TextChanged(object sender, TextChangedEventArgs e)
    {
        DicaBusca.Visibility = Busca.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        MostrarResultadosBusca();
    }

    private void MostrarResultadosBusca()
    {
        ResultadosBusca.Children.Clear();
        var texto = Busca.Text.Trim();
        IEnumerable<(long Id, string Nome)> lista;
        if (texto.Length == 0)
        {
            // Sem busca: os produtos com alerta primeiro
            lista = _alertas.Where(a => a.ProdutoId is not null).Select(a => (a.ProdutoId!.Value, a.Produto)).DistinctBy(x => x.Item1).Take(8);
            if (lista.Any())
                ResultadosBusca.Children.Add(new TextBlock { Text = "Com alerta:", FontSize = 13, Style = Estilo("TextoSuave"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) });
        }
        else
        {
            var comparar = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;
            lista = (_produtos ?? []).Where(p => comparar.IndexOf(p.Nome, texto, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
                .OrderBy(p => p.Nome, StringComparer.Create(PtBr, true)).Take(12).Select(p => (p.Id, p.Nome));
        }
        foreach (var (id, nome) in lista)
        {
            var chip = new Button { Content = nome, Style = Estilo("Chip"), Margin = new Thickness(0, 0, 8, 6), Tag = _produto?.ProdutoId == id ? "ativo" : null };
            chip.Click += async (_, _) => await CarregarProdutoAsync(id);
            ResultadosBusca.Children.Add(chip);
        }
    }

    private void MostrarProduto()
    {
        if (_produto is not { } p)
        {
            Conteudo.Children.Add(Suave("Busque um produto para ver quanto ele vendeu, quanto a previsão esperava e o que vem pela frente."));
            return;
        }
        MostrarResultadosBusca();
        var painel = new StackPanel();
        var aviso = p.Situacao switch
        {
            "pouco_historico" => "Pouco histórico: a previsão se apoia mais no jeito da categoria e da loja.",
            "de_epoca" => "Produto de época: vende quase só nas datas especiais (veja a lista abaixo).",
            "sem_vendas" => "Este produto ainda não tem vendas: não há previsão.",
            _ => null,
        };
        if (aviso is not null) painel.Children.Add(Suave(aviso, 10));

        // Números do produto
        var numeros = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        if (p.MediaDia is { } media) numeros.Children.Add(Numero(Qtd(media, p.PorPeso), "por dia, em média"));
        if (p.CrescimentoAno is { } cresc) numeros.Children.Add(Numero((cresc >= 0 ? "+" : "−") + Math.Abs(cresc).ToString("P0", PtBr), "crescimento por ano"));
        if (p.ParteDatas is { } datas && datas > 0) numeros.Children.Add(Numero(datas.ToString("P0", PtBr), "vendido em datas especiais"));
        if (p.ErroTeste is { } erro) numeros.Children.Add(Numero(erro.ToString("P0", PtBr),
            p.ErroIngenuo is { } ing ? $"erro no teste (ano passado: {ing:P0})" : "erro no teste"));
        painel.Children.Add(numeros);

        // Gráfico: passado + futuro
        var pontos = new List<Graficos.PontoPrevisao>();
        foreach (var w in p.Passado)
            pontos.Add(new(w.Semana.ToString("dd/MM", PtBr), w.Vendido, w.Previsto, null, null, false,
                $"Semana de {w.Semana:dd/MM}: vendeu {Qtd(w.Vendido, p.PorPeso)}, a previsão esperava {Qtd(w.Previsto, p.PorPeso)}" +
                (w.Datas > 0 ? $" (incluindo {Qtd(w.Datas, p.PorPeso)} de datas especiais)" : "")));
        foreach (var w in p.Futuro)
            pontos.Add(new(w.Semana.ToString("dd/MM", PtBr), null, w.Previsto, w.Minimo, w.Maximo, true,
                $"Semana de {w.Semana:dd/MM}: previsão {Qtd(w.Previsto, p.PorPeso)} (entre {Qtd(w.Minimo, p.PorPeso)} e {Qtd(w.Maximo, p.PorPeso)})" +
                (string.IsNullOrEmpty(w.Eventos) ? "" : $" · {w.Eventos}")));
        if (pontos.Count > 0)
        {
            painel.Children.Add(Suave("Verde escuro: o que vendeu em cada semana; traço amarelo: o que a previsão esperava. Verde claro: as próximas semanas, " +
                "com a faixa onde a venda deve cair em 8 de cada 10 semanas. Passe o mouse nas colunas.", 10));
            painel.Children.Add(Graficos.Previsao(pontos, 200));
        }
        Conteudo.Children.Add(Cartao($"{p.Nome} · {p.Categoria}", painel));

        // Dia da semana e mês
        if (p.IdxSemana is { Count: 7 } sem && p.IdxMes is { Count: 12 } mes)
        {
            var dias = new[] { "Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado" };
            var listaDias = new StackPanel();
            var maxDia = sem.Max();
            for (var d = 0; d < 7; d++)
                listaDias.Children.Add(Graficos.Barra(dias[d], sem[d], maxDia, Indice(sem[d]), sem[d] >= 1 ? "Verde600" : "Ambar700", 110));
            var meses = PtBr.DateTimeFormat.MonthNames;
            var listaMeses = new StackPanel();
            var maxMes = mes.Max();
            for (var m = 0; m < 12; m++)
                listaMeses.Children.Add(Graficos.Barra(char.ToUpper(meses[m][0], PtBr) + meses[m][1..], mes[m], maxMes, Indice(mes[m]), mes[m] >= 1 ? "Verde600" : "Ambar700", 110));
            Conteudo.Children.Add(DuasColunas(Cartao("Dia da semana", Envolver(Suave("Comparado a um dia médio.", 8), listaDias)),
                Cartao("Mês do ano", Envolver(Suave("Comparado a um mês médio.", 8), listaMeses))));
        }

        // Datas especiais
        var listaDatas = new StackPanel();
        if (p.Datas.Count == 0) listaDatas.Children.Add(Suave("Nenhuma data especial muda a venda deste produto."));
        var maxData = p.Datas.Count == 0 ? 0 : p.Datas.Max(d => d.AMais);
        foreach (var d in p.Datas.Take(12))
            listaDatas.Children.Add(Graficos.Barra(d.Evento, d.AMais, maxData, "+" + Qtd(d.AMais, p.PorPeso), "Roxo600", 220));
        Conteudo.Children.Add(Cartao("Datas especiais (a mais no dia, hoje)", listaDatas, 0));
    }

    // ------------------------------------------------------------ Aba "Teste da previsão"

    private void MostrarTeste(PrevisaoTela t)
    {
        var painel = new StackPanel();
        painel.Children.Add(Suave("Para cada ano, a previsão aprendeu só com os 4 anos anteriores e foi conferida semana a semana, produto a produto, " +
            "com o que vendeu de verdade. Comparação: repetir as vendas do mesmo período do ano passado.", 12));
        if (t.Anos.Count == 0)
        {
            painel.Children.Add(Suave(t.Geral?.ErroSemana is { } erro
                ? $"Último ano ({t.Geral.TesteIni:dd/MM/yyyy} a {t.Geral.TesteFim:dd/MM/yyyy}): erro de {erro:P0}; repetir o ano passado: {t.Geral.ErroIngenuo:P0}. " +
                  "Para ver ano a ano, rode no Supabase: select public.previsao_testar_anos(); (leva alguns minutos)."
                : "Ainda não há teste: precisa de mais de 1 ano de vendas."));
        }
        else
        {
            var maximo = t.Anos.Max(a => Math.Max(a.Erro ?? 0, a.ErroIngenuo ?? 0));
            foreach (var a in t.Anos.OrderByDescending(a => a.Ano))
                painel.Children.Add(Graficos.Barra(a.Ano.ToString(PtBr), a.Erro ?? 0, maximo,
                    $"{a.Erro:P0} · ano passado {a.ErroIngenuo:P0}", (a.Erro ?? 0) <= (a.ErroIngenuo ?? 0) ? "Verde600" : "Vermelho600", 70));
            var ganhou = t.Anos.Count(a => (a.Erro ?? 0) < (a.ErroIngenuo ?? 0));
            painel.Children.Add(Suave($"A previsão errou menos que repetir o ano passado em {ganhou} de {t.Anos.Count} anos.", 0));
        }
        Conteudo.Children.Add(Cartao("Erro por ano (por produto e semana)", painel));

        Conteudo.Children.Add(DuasColunas(CartaoErros("Onde mais erra", t.Piores, "Vermelho600"), CartaoErros("Onde mais acerta", t.Melhores, "Verde600")));
        Conteudo.Children.Add(Cartao("Por que ainda erra?", Suave(
            "Boa parte do erro é sorte: mesmo sabendo exatamente quantos clientes viriam em cada dia, a escolha de cada cliente varia. " +
            "Nos testes com os dados de exemplo, só cerca de 1,5 ponto do erro semanal é culpa da previsão. Por isso o pedido usa a margem " +
            "de segurança e olha o ciclo do fornecedor (2 semanas erram bem menos que 1)."), 0));
    }

    private FrameworkElement CartaoErros(string titulo, List<ErroProduto> produtos, string cor)
    {
        var lista = new StackPanel();
        if (produtos.Count == 0) lista.Children.Add(Suave("Sem teste ainda."));
        var maximo = produtos.Count == 0 ? 0 : produtos.Max(p => p.Erro);
        foreach (var p in produtos)
            lista.Children.Add(Graficos.Barra(p.Produto, p.Erro, maximo, p.Erro.ToString("P0", PtBr), cor, 190));
        return Cartao(titulo, lista);
    }

    // ------------------------------------------------------------ Aba "Alertas"

    private void MostrarAlertas()
    {
        if (_alertas.Count == 0)
        {
            Conteudo.Children.Add(Cartao("Nenhum alerta", Suave("Nenhum produto parado, nenhuma venda fora do normal e nenhuma data especial nos próximos 21 dias.")));
            return;
        }
        var grupos = new (string Tipo, string Titulo)[]
        {
            ("falta", "Clientes sem o produto"), ("parado", "Produtos parados"), ("queda", "Vendendo menos que o normal"),
            ("alta", "Vendendo mais que o normal"), ("data", "Datas especiais chegando"),
        };
        foreach (var (tipo, titulo) in grupos)
        {
            var doTipo = _alertas.Where(a => a.Tipo == tipo).ToList();
            if (doTipo.Count == 0) continue;
            var lista = new StackPanel();
            foreach (var a in doTipo)
            {
                if (lista.Children.Count > 0) lista.Children.Add(new Border { Height = 1, Background = Pincel("Linha") });
                var linha = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
                if (a.ProdutoId is { } id)
                {
                    var ver = new Button { Content = "Ver produto", Style = Estilo("BotaoLink"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
                    ver.Click += async (_, _) => await CarregarProdutoAsync(id);
                    DockPanel.SetDock(ver, Dock.Right);
                    linha.Children.Add(ver);
                }
                var texto = new StackPanel();
                texto.Children.Add(new TextBlock { Text = a.Produto, FontSize = 15, FontWeight = FontWeights.SemiBold });
                texto.Children.Add(new TextBlock { Text = a.Texto, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                linha.Children.Add(texto);
                lista.Children.Add(linha);
            }
            Conteudo.Children.Add(Cartao($"{titulo} ({doTipo.Count})", lista));
        }
    }

    // ------------------------------------------------------------ Peças da tela

    private Button Kpi(string icone, string cor, string valor, string rotulo, string dica)
    {
        var conteudo = new StackPanel();
        conteudo.Children.Add(new Border
        {
            Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Left,
            Child = new ContentControl { Style = Estilo("Icone"), Tag = FindResource(icone), Foreground = Pincel(cor), Width = 16 }
        });
        conteudo.Children.Add(new TextBlock { Text = valor, Style = Estilo("KpiValor"), TextTrimming = TextTrimming.CharacterEllipsis });
        conteudo.Children.Add(new TextBlock { Text = rotulo, Style = Estilo("KpiRotulo") });
        conteudo.Children.Add(new TextBlock { Text = dica, Style = Estilo("KpiSub"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        return new Button { Style = Estilo("Kpi"), Content = conteudo, Cursor = System.Windows.Input.Cursors.Arrow, Focusable = false, ToolTip = dica };
    }

    private Border Cartao(string titulo, UIElement conteudo, double margemBaixo = 16)
    {
        var painel = new StackPanel();
        painel.Children.Add(new TextBlock { Text = titulo, FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap });
        painel.Children.Add(conteudo);
        return new Border { Style = Estilo("CartaoBorda"), Padding = new Thickness(24, 22, 24, 22), Margin = new Thickness(0, 0, 0, margemBaixo), Child = painel };
    }

    private FrameworkElement DuasColunas(FrameworkElement esquerda, FrameworkElement direita)
    {
        if (_estreita)
        {
            var pilha = new StackPanel();
            pilha.Children.Add(esquerda);
            pilha.Children.Add(direita);
            return pilha;
        }
        var grade = new Grid();
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grade.Children.Add(esquerda);
        Grid.SetColumn(direita, 2);
        grade.Children.Add(direita);
        return grade;
    }

    private static StackPanel Envolver(params UIElement[] filhos)
    {
        var p = new StackPanel();
        foreach (var f in filhos) p.Children.Add(f);
        return p;
    }

    private static void Celula(Grid grade, int linha, int coluna, FrameworkElement elemento)
    {
        elemento.Margin = new Thickness(coluna == 0 ? 0 : 8, 6, 0, 6);
        Grid.SetRow(elemento, linha);
        Grid.SetColumn(elemento, coluna);
        grade.Children.Add(elemento);
    }

    private Border Etiqueta(string texto, string fundo, string cor) => new()
    {
        Background = Pincel(fundo), CornerRadius = new CornerRadius(3), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = texto, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Pincel(cor) }
    };

    private FrameworkElement Numero(string valor, string rotulo)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 32, 8) };
        p.Children.Add(new TextBlock { Text = valor, FontSize = 22, FontWeight = FontWeights.Bold });
        p.Children.Add(new TextBlock { Text = rotulo, FontSize = 12.5, Style = Estilo("TextoSuave") });
        return p;
    }

    private TextBlock Suave(string texto, double margemBaixo = 2) =>
        new() { Text = texto, FontSize = 13.5, Style = Estilo("TextoSuave"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, margemBaixo) };

    private Style Estilo(string chave) => (Style)FindResource(chave);

    private Brush Pincel(string chave) => (Brush)FindResource(chave);

    private static string Qtd(decimal v, bool porPeso) =>
        porPeso ? $"{v.ToString(v >= 100 ? "0" : "0.#", PtBr)} kg" : $"{Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", PtBr)} un.";

    private static string Indice(decimal i) => (i >= 1 ? "+" : "−") + Math.Abs(i - 1).ToString("P0", PtBr);

    private static string Plural(int n, string um, string varios) => $"{n.ToString(PtBr)} {(n == 1 ? um : varios)}";

    private static string NomeDia(DateOnly d)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        if (d == hoje) return "hoje";
        if (d == hoje.AddDays(1)) return "amanhã";
        return d.ToString("ddd dd/MM", PtBr).Replace(".", "");
    }
}
