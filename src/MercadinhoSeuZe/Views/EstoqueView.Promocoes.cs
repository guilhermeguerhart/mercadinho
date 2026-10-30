using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Aba Promoções do Estoque (pedido do Guilherme em 30/10, script 008): promoções por produto (preço fixo ou %, com
/// período) e promoções de validade por categoria (começa X dias antes do vencimento, do desconto mínimo ao máximo um
/// dia antes). O caixa cobra sozinho; quando as duas valem, fica o maior desconto.
/// </summary>
public partial class EstoqueView
{
    private string _aba = "produtos";

    private void Aba_Click(object sender, RoutedEventArgs e)
    {
        _aba = ((FrameworkElement)sender).Uid;
        MostrarPromocoes();
        if (_aba == "produtos") Leitor.Focus();
    }

    /// <summary>Mostra as abas (só com o script 008) e monta a aba de promoções.</summary>
    private void MostrarPromocoes()
    {
        var tem = _servico is { TemPromocoes: true };
        if (!tem) _aba = "produtos";
        Abas.Visibility = tem ? Visibility.Visible : Visibility.Collapsed;
        AbaBotaoProdutos.Tag = _aba == "produtos" ? "ativo" : null;
        AbaBotaoPromocoes.Tag = _aba == "promocoes" ? "ativo" : null;
        AbaProdutos.Visibility = _aba == "produtos" ? Visibility.Visible : Visibility.Collapsed;
        AbaPromocoes.Visibility = _aba == "promocoes" ? Visibility.Visible : Visibility.Collapsed;
        var ativas = _promocoes.Count(p => p.Ativa);
        var lotes = _produtos.Sum(p => Promocoes.LotesEmPromocao(p).Count());
        ContadorPromocoes.Text = ativas + lotes > 0 ? (ativas + lotes).ToString(PtBr) : "";
        if (_aba != "promocoes") return;

        AbaPromocoes.Children.Clear();
        AbaPromocoes.Children.Add(CartaoPromocoesProduto());
        AbaPromocoes.Children.Add(CartaoPromocoesValidade());
    }

    // ------------------------------------------------------------ Promoções por produto

    private FrameworkElement CartaoPromocoesProduto()
    {
        var corpo = new StackPanel();
        corpo.Children.Add(TopoCartao("Promoções por produto",
            "O caixa cobra o preço da promoção sozinho, só no período escolhido.", "Nova promoção", () => AbrirPromocao(null)));

        var lista = _promocoes
            .Select(x => (Promocao: x, Produto: _produtos.FirstOrDefault(p => p.Id == x.ProdutoId)))
            .Where(x => x.Produto is not null)
            .OrderBy(x => x.Promocao.Ativa ? 0 : x.Promocao.Agendada ? 1 : 2)
            .ThenBy(x => x.Produto!.Nome, StringComparer.Create(PtBr, CompareOptions.IgnoreCase))
            .ToList();
        if (lista.Count == 0)
            corpo.Children.Add(new TextBlock { Text = "Nenhuma promoção cadastrada. Clique em “Nova promoção” para começar.", FontSize = 14, Style = Recurso<Style>("TextoSuave"), Margin = new Thickness(0, 6, 0, 0) });

        foreach (var (promo, produto) in lista)
        {
            var p = produto!;
            var linha = new DockPanel { Margin = new Thickness(0, 10, 0, 10), Opacity = promo.Encerrada ? 0.65 : 1 };

            var acoes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (!promo.Encerrada)
            {
                acoes.Children.Add(Link("Editar", () => AbrirPromocao(promo)));
                acoes.Children.Add(Link("Encerrar agora", () => ConfirmarEncerrarPromocao(promo, p), perigo: true));
            }
            else acoes.Children.Add(Link("Excluir", () => ConfirmarExcluirPromocao(promo, p), perigo: true));
            DockPanel.SetDock(acoes, Dock.Right);
            linha.Children.Add(acoes);

            var precoNovo = promo.PrecoCom(p.Preco);
            var etiqueta = new TextBlock { FontSize = 14, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            etiqueta.Inlines.Add(new System.Windows.Documents.Run(p.Preco.ToString("C", PtBr)) { FontSize = 12, FontWeight = FontWeights.Normal, TextDecorations = TextDecorations.Strikethrough });
            etiqueta.Inlines.Add(new System.Windows.Documents.Run(" " + precoNovo.ToString("C", PtBr) + (p.PorPeso ? "/kg" : "")));
            var preco = new Border { Style = Recurso<Style>("EtiquetaPreco"), Margin = new Thickness(12, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, Child = etiqueta };
            DockPanel.SetDock(preco, Dock.Right);
            linha.Children.Add(preco);

            var texto = new StackPanel();
            var titulo = new WrapPanel();
            titulo.Children.Add(new TextBlock { Text = p.Nome, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            titulo.Children.Add(Carimbo(promo.Situacao, promo.Ativa ? "Verde" : promo.Agendada ? "Ambar" : "Cinza"));
            texto.Children.Add(titulo);
            var desconto = p.Preco > 0 ? (p.Preco - precoNovo) / p.Preco : 0;
            var sub = $"{(promo.PorPreco ? "Preço fixo" : promo.Texto + " de desconto")} · {promo.Periodo}" +
                      (promo.PorPreco ? $" · {desconto.ToString("P0", PtBr)} mais barato" : "");
            texto.Children.Add(new TextBlock { Text = sub, FontSize = 13, Style = Recurso<Style>("TextoSuave"), TextWrapping = TextWrapping.Wrap });
            if (!promo.Encerrada && p.PrecoCusto is { } custo && precoNovo < custo)
                texto.Children.Add(new TextBlock
                {
                    Text = $"Vende abaixo do custo ({custo.ToString("C", PtBr)}{(p.PorPeso ? "/kg" : "")}).", FontSize = 13,
                    FontWeight = FontWeights.SemiBold, Foreground = Recurso<Brush>("Ambar700")
                });
            linha.Children.Add(texto);
            corpo.Children.Add(new Border { BorderBrush = Recurso<Brush>("Linha"), BorderThickness = new Thickness(0, 1, 0, 0), Child = linha });
        }
        return new Border { Style = Recurso<Style>("CartaoBorda"), Padding = new Thickness(24, 22, 24, 22), Margin = new Thickness(0, 0, 0, 16), Child = corpo };
    }

    private void AbrirPromocao(Promocao? promo)
    {
        if (_servico is null) return;
        var editando = promo is not null;
        var j = new JanelaSimples(_area, editando ? "Editar promoção" : "Nova promoção",
            "Escolha o produto, o desconto e o período. O caixa cobra o preço da promoção sozinho.", 520);
        Produto? escolhido = editando ? _produtos.FirstOrDefault(p => p.Id == promo!.ProdutoId) : null;

        // Produto: busca pelo nome ou pelo leitor (só no cadastro)
        var nomeEscolhido = new TextBlock { FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) };
        var resultados = new StackPanel();
        TextBox? busca = null;
        if (!editando)
        {
            busca = j.Campo("Produto (nome ou código de barras)", "", 60);
            j.Corpo.Children.Add(resultados);
        }
        j.Corpo.Children.Add(nomeEscolhido);

        // Tipo do desconto
        var tipo = promo?.Tipo ?? "preco";
        j.Corpo.Children.Add(new TextBlock { Text = "Tipo de desconto", Style = Recurso<Style>("Rotulo"), Margin = new Thickness(0, 10, 0, 6) });
        var tipos = new WrapPanel();
        var botaoPreco = new Button { Content = "Preço fixo (de R$ X por R$ Y)", Style = Recurso<Style>("Chip"), Margin = new Thickness(0, 0, 8, 0) };
        var botaoPct = new Button { Content = "Porcentagem (%)", Style = Recurso<Style>("Chip") };
        tipos.Children.Add(botaoPreco);
        tipos.Children.Add(botaoPct);
        j.Corpo.Children.Add(tipos);

        var rotuloValor = new TextBlock { Style = Recurso<Style>("Rotulo"), Margin = new Thickness(0, 12, 0, 6) };
        j.Corpo.Children.Add(rotuloValor);
        var valorPreco = new TextBox { Text = promo is { PorPreco: true } ? Formulario.Preco(promo.Valor) : "" };
        Formulario.MascaraDinheiro(valorPreco, vazio: true);
        var valorPct = new TextBox { Text = promo is { PorPreco: false } ? promo.Valor.ToString("0.##", PtBr) : "", MaxLength = 6 };
        System.Windows.Automation.AutomationProperties.SetName(valorPreco, "Preço da promoção em reais");
        System.Windows.Automation.AutomationProperties.SetName(valorPct, "Desconto em porcentagem");
        j.Corpo.Children.Add(valorPreco);
        j.Corpo.Children.Add(valorPct);

        var datas = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        datas.ColumnDefinitions.Add(new ColumnDefinition());
        datas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        datas.ColumnDefinitions.Add(new ColumnDefinition());
        var (blocoInicio, inicio) = CampoData("Começa em", Formulario.Data(promo?.Inicio ?? DateOnly.FromDateTime(DateTime.Today)));
        var (blocoFim, fim) = CampoData("Termina em (vazio = até encerrar)", Formulario.Data(promo?.Fim));
        Grid.SetColumn(blocoFim, 2);
        datas.Children.Add(blocoInicio);
        datas.Children.Add(blocoFim);
        j.Corpo.Children.Add(datas);

        var previa = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
        j.Corpo.Children.Add(previa);

        decimal? Valor() => tipo == "preco" ? Formulario.LerPreco(valorPreco.Text) : LerPorcento(valorPct.Text);

        void Atualizar()
        {
            botaoPreco.Tag = tipo == "preco" ? "ativo" : null;
            botaoPct.Tag = tipo == "percentual" ? "ativo" : null;
            valorPreco.Visibility = tipo == "preco" ? Visibility.Visible : Visibility.Collapsed;
            valorPct.Visibility = tipo == "percentual" ? Visibility.Visible : Visibility.Collapsed;
            rotuloValor.Text = tipo == "preco" ? "Preço da promoção (R$)" : "Desconto (%)";
            nomeEscolhido.Text = escolhido is null ? "" : $"{escolhido.Nome} · preço normal {escolhido.Preco.ToString("C", PtBr)}{(escolhido.PorPeso ? "/kg" : "")}";
            previa.Text = "";
            previa.Foreground = Recurso<Brush>("Verde700");
            if (escolhido is null || Valor() is not { } v || v <= 0) return;
            var teste = new Promocao { Tipo = tipo, Valor = v };
            var novo = teste.PrecoCom(escolhido.Preco);
            if (tipo == "preco" && v >= escolhido.Preco)
            {
                previa.Text = "O preço da promoção precisa ser menor que o preço normal.";
                previa.Foreground = Recurso<Brush>("Vermelho600");
                return;
            }
            var desconto = escolhido.Preco > 0 ? (escolhido.Preco - novo) / escolhido.Preco : 0;
            previa.Text = $"De {escolhido.Preco.ToString("C", PtBr)} por {novo.ToString("C", PtBr)}{(escolhido.PorPeso ? "/kg" : "")} ({desconto.ToString("P0", PtBr)} de desconto).";
            if (escolhido.PrecoCusto is { } custo && novo < custo)
            {
                previa.Text += $" Atenção: fica abaixo do custo ({custo.ToString("C", PtBr)}).";
                previa.Foreground = Recurso<Brush>("Ambar700");
            }
        }

        void MostrarResultados(IEnumerable<Produto> achados)
        {
            resultados.Children.Clear();
            foreach (var p in achados.Take(5))
            {
                var b = new Button
                {
                    Content = $"{p.Nome}  ·  {p.Preco.ToString("C", PtBr)}{(p.PorPeso ? "/kg" : "")}", Style = Recurso<Style>("BotaoLink"),
                    HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4)
                };
                var produto = p;
                b.Click += (_, _) => { escolhido = produto; busca!.Text = ""; resultados.Children.Clear(); Atualizar(); (tipo == "preco" ? valorPreco : valorPct).Focus(); };
                resultados.Children.Add(b);
            }
        }

        if (busca is not null)
        {
            busca.TextChanged += (_, _) =>
            {
                var t = busca.Text.Trim();
                if (t.Length < 2 || t.All(char.IsAsciiDigit)) { resultados.Children.Clear(); return; }
                MostrarResultados(_produtos.Where(p => Contem(p.Nome, t) || Contem(p.Categoria, t))
                    .OrderByDescending(p => p.Nome.StartsWith(t, StringComparison.CurrentCultureIgnoreCase)).ThenBy(p => p.Nome));
            };
            busca.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                var t = busca.Text.Trim();
                var achado = t.All(char.IsAsciiDigit) && t.Length > 0
                    ? CodigoBarras.Ler(_produtos, t)?.Produto
                    : _produtos.Where(p => Contem(p.Nome, t)).OrderByDescending(p => p.Nome.StartsWith(t, StringComparison.CurrentCultureIgnoreCase)).FirstOrDefault();
                if (achado is null) { j.Erro("Nenhum produto com esse nome ou código."); return; }
                escolhido = achado;
                busca.Text = "";
                resultados.Children.Clear();
                Atualizar();
                (tipo == "preco" ? valorPreco : valorPct).Focus();
            };
        }
        botaoPreco.Click += (_, _) => { tipo = "preco"; Atualizar(); valorPreco.Focus(); };
        botaoPct.Click += (_, _) => { tipo = "percentual"; Atualizar(); valorPct.Focus(); };
        valorPreco.TextChanged += (_, _) => { valorPreco.Tag = null; j.Erro(null); Atualizar(); };
        valorPct.TextChanged += (_, _) => { valorPct.Tag = null; j.Erro(null); Atualizar(); };

        j.Botao("Cancelar", false, j.Fechar);
        Button? salvar = null;
        salvar = j.Botao(editando ? "Salvar alterações" : "Criar promoção", true, async () =>
        {
            var campoValor = tipo == "preco" ? valorPreco : valorPct;
            var v = Valor();
            var dataInicio = Formulario.LerData(inicio.Text);
            var dataFim = fim.Text.Trim().Length == 0 ? null : Formulario.LerData(fim.Text);
            string? erro = escolhido is null ? "Escolha o produto da promoção."
                : v is null or <= 0 ? (tipo == "preco" ? "Digite o preço da promoção, por exemplo 6,99." : "Digite a porcentagem, de 1 a 90.")
                : tipo == "preco" && v >= escolhido.Preco ? $"O preço da promoção precisa ser menor que {escolhido.Preco.ToString("C", PtBr)}."
                : tipo == "percentual" && v > 90 ? "O desconto vai até 90%."
                : dataInicio is null ? "Data de início no formato dd/mm/aaaa."
                : fim.Text.Trim().Length > 0 && dataFim is null ? "Data de fim no formato dd/mm/aaaa (ou deixe vazio)."
                : dataFim < dataInicio ? "A promoção não pode terminar antes de começar."
                : !editando && dataFim < DateOnly.FromDateTime(DateTime.Today) ? "A data de fim já passou."
                : null;
            if (erro is not null)
            {
                j.Erro(erro);
                if (escolhido is null) busca?.Focus();
                else if (v is null or <= 0 || erro.Contains("preço") || erro.Contains('%')) { campoValor.Tag = "invalido"; campoValor.Focus(); }
                return;
            }
            salvar!.IsEnabled = false;
            try
            {
                if (editando) await _servico.AtualizarPromocaoAsync(promo!.Id, tipo, v!.Value, dataInicio!.Value, dataFim);
                else await _servico.CriarPromocaoAsync(escolhido!.Id, tipo, v!.Value, dataInicio!.Value, dataFim);
                await RecarregarAsync(editando ? "Promoção atualizada." : $"Promoção de {escolhido!.Nome} criada.");
            }
            catch (ConexaoException ex)
            {
                j.Erro(ex.Message);
                salvar.IsEnabled = true;
            }
        });
        Atualizar();
        j.Loaded += (_, _) => (busca ?? (tipo == "preco" ? valorPreco : valorPct)).Focus();
        _area.AbrirJanela(j);
    }

    private void ConfirmarEncerrarPromocao(Promocao promo, Produto p) =>
        _area.AbrirJanela(new ConfirmacaoJanela(_area, "Encerrar a promoção?",
            $"{p.Nome} volta ao preço normal ({p.Preco.ToString("C", PtBr)}) no caixa a partir de agora. A promoção fica no histórico.",
            async () =>
            {
                await _servico!.EncerrarPromocaoAsync(promo.Id);
                await RecarregarAsync($"Promoção de {p.Nome} encerrada.");
            }, "Encerrar", "Voltar"));

    private void ConfirmarExcluirPromocao(Promocao promo, Produto p) =>
        _area.AbrirJanela(new ConfirmacaoJanela(_area, "Excluir do histórico?",
            $"A promoção encerrada de {p.Nome} ({promo.Texto}, {promo.Periodo}) sai da lista. As vendas feitas com ela continuam iguais.",
            async () =>
            {
                await _servico!.ExcluirPromocaoAsync(promo.Id);
                await RecarregarAsync("Promoção excluída.");
            }));

    // ------------------------------------------------------------ Promoções de validade por categoria

    private FrameworkElement CartaoPromocoesValidade()
    {
        var corpo = new StackPanel();
        corpo.Children.Add(TopoCartao("Promoção de validade por categoria",
            "Perto do vencimento, o desconto começa no mínimo e sobe um pouco a cada dia até o máximo, um dia antes de vencer. " +
            "Vale só para os lotes que estão perto de vencer.", "Nova regra", () => AbrirRegra(null)));

        if (_regras.Count == 0)
            corpo.Children.Add(new TextBlock { Text = "Nenhuma regra cadastrada. Exemplo: Padaria, começa 4 dias antes, de 10% a 40%.", FontSize = 14, Style = Recurso<Style>("TextoSuave"), Margin = new Thickness(0, 6, 0, 0) });

        var hoje = DateOnly.FromDateTime(DateTime.Today);
        foreach (var r in _regras.OrderBy(r => r.Categoria, StringComparer.Create(PtBr, CompareOptions.IgnoreCase)))
        {
            var linha = new DockPanel { Margin = new Thickness(0, 10, 0, 10), Opacity = r.Ativa ? 1 : 0.65 };
            var acoes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            acoes.Children.Add(Link("Editar", () => AbrirRegra(r)));
            acoes.Children.Add(Link("Excluir", () => ConfirmarExcluirRegra(r), perigo: true));
            DockPanel.SetDock(acoes, Dock.Right);
            linha.Children.Add(acoes);

            var lotes = _produtos.Where(p => RegraValidade.Da(_regras, p.Categoria) == r).Sum(p => Promocoes.LotesEmPromocao(p).Count());
            var quantos = new TextBlock
            {
                Text = !r.Ativa ? "Desligada" : lotes == 0 ? "Nenhum lote hoje" : lotes == 1 ? "1 lote hoje" : $"{lotes} lotes hoje",
                FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 16, 0),
                Foreground = Recurso<Brush>(lotes > 0 && r.Ativa ? "Verde700" : "Suave")
            };
            DockPanel.SetDock(quantos, Dock.Right);
            linha.Children.Add(quantos);

            var texto = new StackPanel();
            var cat = CategoriaVisual.Pilula(this, r.Categoria);
            cat.HorizontalAlignment = HorizontalAlignment.Left;
            texto.Children.Add(cat);
            var escada = string.Join(" · ", r.Escada().Select(x => $"{DiasTexto(x.Dias)} {Porcento(x.Desconto)}"));
            texto.Children.Add(new TextBlock { Text = $"Começa {r.DiasAntes} {(r.DiasAntes == 1 ? "dia" : "dias")} antes · {Porcento(r.DescontoMin)} a {Porcento(r.DescontoMax)}", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
            texto.Children.Add(new TextBlock { Text = escada, FontSize = 12.5, Style = Recurso<Style>("TextoSuave"), TextWrapping = TextWrapping.Wrap });
            linha.Children.Add(texto);
            corpo.Children.Add(new Border { BorderBrush = Recurso<Brush>("Linha"), BorderThickness = new Thickness(0, 1, 0, 0), Child = linha });
        }

        // Lotes que estão na promoção hoje (para etiquetar a prateleira)
        var emPromocao = _produtos.SelectMany(p => Promocoes.LotesEmPromocao(p).Select(l => (Produto: p, Lote: l)))
            .OrderBy(x => x.Lote.Validade).ThenBy(x => x.Produto.Nome).ToList();
        if (emPromocao.Count > 0)
        {
            corpo.Children.Add(new TextBlock { Text = "LOTES EM PROMOÇÃO HOJE", Style = Recurso<Style>("Cabecalho"), Margin = new Thickness(0, 18, 0, 6) });
            corpo.Children.Add(new TextBlock { Text = "Etiquete a prateleira com o preço novo. Amanhã o desconto sobe sozinho.", FontSize = 13, Style = Recurso<Style>("TextoSuave"), Margin = new Thickness(0, 0, 0, 6) });
            foreach (var (p, l) in emPromocao)
            {
                var linha = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
                var preco = new Border
                {
                    Style = Recurso<Style>("EtiquetaPreco"), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = Promocoes.PrecoDoLote(p, l).ToString("C", PtBr) + (p.PorPeso ? "/kg" : ""), FontWeight = FontWeights.Bold }
                };
                DockPanel.SetDock(preco, Dock.Right);
                linha.Children.Add(preco);
                var desconto = new TextBlock { Text = $"−{Porcento(Promocoes.DescontoValidade(p, l))}", FontWeight = FontWeights.Bold, Foreground = Recurso<Brush>("Verde700"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
                DockPanel.SetDock(desconto, Dock.Right);
                linha.Children.Add(desconto);
                var dias = l.Validade!.Value.DayNumber - hoje.DayNumber;
                linha.Children.Add(new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis,
                    Text = $"{p.Nome} · {p.Formatar(l.QuantidadeRestante)} · {(dias == 0 ? "vence hoje" : dias == 1 ? "vence amanhã" : $"vence em {dias} dias")} ({l.Validade:dd/MM})"
                });
                corpo.Children.Add(linha);
            }
        }
        return new Border { Style = Recurso<Style>("CartaoBorda"), Padding = new Thickness(24, 22, 24, 22), Child = corpo };
    }

    private void AbrirRegra(RegraValidade? regra)
    {
        if (_servico is null) return;
        var editando = regra is not null;
        var j = new JanelaSimples(_area, editando ? "Editar promoção de validade" : "Nova promoção de validade",
            "O desconto começa no mínimo no primeiro dia e sobe por igual até o máximo, um dia antes do vencimento. No dia do vencimento continua o máximo.", 520);

        j.Corpo.Children.Add(new TextBlock { Text = "Categoria", Style = Recurso<Style>("Rotulo") });
        var usadas = _regras.Where(r => r != regra).Select(r => r.Categoria.Trim().ToLower(PtBr)).ToHashSet();
        var categorias = _produtos.Select(p => p.Categoria.Trim()).Where(c => c.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Where(c => !usadas.Contains(c.ToLower(PtBr)))
            .OrderBy(c => c, StringComparer.Create(PtBr, CompareOptions.IgnoreCase)).ToList();
        if (regra is not null && !categorias.Contains(regra.Categoria, StringComparer.CurrentCultureIgnoreCase)) categorias.Insert(0, regra.Categoria);
        var categoria = new ComboBox { ItemsSource = categorias };
        if (regra is not null) categoria.SelectedItem = categorias.First(c => string.Equals(c, regra.Categoria, StringComparison.CurrentCultureIgnoreCase));
        System.Windows.Automation.AutomationProperties.SetName(categoria, "Categoria");
        j.Corpo.Children.Add(categoria);

        var numeros = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        for (var i = 0; i < 5; i++) numeros.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 1 ? new GridLength(12) : new GridLength(1, GridUnitType.Star) });
        var (blocoDias, dias) = CampoNumero("Dias antes de vencer", regra?.DiasAntes.ToString(PtBr) ?? "3");
        var (blocoMin, minimo) = CampoNumero("Desconto mínimo (%)", regra?.DescontoMin.ToString("0.##", PtBr) ?? "10");
        var (blocoMax, maximo) = CampoNumero("Desconto máximo (%)", regra?.DescontoMax.ToString("0.##", PtBr) ?? "40");
        Grid.SetColumn(blocoMin, 2);
        Grid.SetColumn(blocoMax, 4);
        numeros.Children.Add(blocoDias);
        numeros.Children.Add(blocoMin);
        numeros.Children.Add(blocoMax);
        j.Corpo.Children.Add(numeros);

        var ativa = new CheckBox { Content = "Ligada (o caixa já aplica)", IsChecked = regra?.Ativa ?? true, Margin = new Thickness(0, 12, 0, 0) };
        j.Corpo.Children.Add(ativa);

        var previa = new TextBlock { FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        j.Corpo.Children.Add(previa);

        RegraValidade? Ler() =>
            Formulario.LerInteiro(dias.Text) is { } d && d is >= 1 and <= 60 && LerPorcento(minimo.Text, zeroVale: true) is { } mn && LerPorcento(maximo.Text) is { } mx && mx >= mn && mx <= 90
                ? new RegraValidade { Categoria = (categoria.SelectedItem as string ?? "").Trim(), DiasAntes = d, DescontoMin = mn, DescontoMax = mx, Ativa = true }
                : null;

        void Atualizar()
        {
            j.Erro(null);
            if (Ler() is not { } r)
            {
                previa.Text = "";
                return;
            }
            previa.Foreground = Recurso<Brush>("Verde700");
            previa.Text = "Desconto em cada dia: " + string.Join(" · ", r.Escada().Select(x => $"{DiasTexto(x.Dias)} {Porcento(x.Desconto)}"));
            var exemplo = _produtos.FirstOrDefault(p => string.Equals(p.Categoria.Trim(), r.Categoria, StringComparison.CurrentCultureIgnoreCase) && !p.PorPeso);
            if (exemplo is not null)
                previa.Text += $". Exemplo: {exemplo.Nome} de {exemplo.Preco.ToString("C", PtBr)} vai a {Math.Round(exemplo.Preco * (1 - r.DescontoMax / 100), 2).ToString("C", PtBr)} um dia antes de vencer.";
        }
        dias.TextChanged += (_, _) => Atualizar();
        minimo.TextChanged += (_, _) => Atualizar();
        maximo.TextChanged += (_, _) => Atualizar();
        categoria.SelectionChanged += (_, _) => Atualizar();

        j.Botao("Cancelar", false, j.Fechar);
        Button? salvar = null;
        salvar = j.Botao(editando ? "Salvar alterações" : "Criar regra", true, async () =>
        {
            var nomeCategoria = (categoria.SelectedItem as string ?? "").Trim();
            var d = Formulario.LerInteiro(dias.Text);
            var mn = LerPorcento(minimo.Text, zeroVale: true);
            var mx = LerPorcento(maximo.Text);
            var erro = nomeCategoria.Length == 0 ? "Escolha a categoria."
                : nomeCategoria.Length > 40 ? "Use um nome de categoria com até 40 letras."
                : usadas.Contains(nomeCategoria.ToLower(PtBr)) ? "Esta categoria já tem uma regra. Edite a que existe."
                : d is not (>= 1 and <= 60) ? "Os dias antes vão de 1 a 60."
                : mn is null ? "Desconto mínimo de 0 a 90%."
                : mx is null or > 90 ? "Desconto máximo de 1 a 90%."
                : mx < mn ? "O desconto máximo precisa ser maior ou igual ao mínimo."
                : null;
            if (erro is not null)
            {
                j.Erro(erro);
                return;
            }
            salvar!.IsEnabled = false;
            try
            {
                if (editando) await _servico.AtualizarRegraValidadeAsync(regra!.Id, nomeCategoria, d!.Value, mn!.Value, mx!.Value, ativa.IsChecked == true);
                else await _servico.CriarRegraValidadeAsync(nomeCategoria, d!.Value, mn!.Value, mx!.Value, ativa.IsChecked == true);
                await RecarregarAsync(editando ? "Promoção de validade atualizada." : $"Promoção de validade de {nomeCategoria} criada.");
            }
            catch (ConexaoException ex)
            {
                j.Erro(ex.Message.StartsWith("Já existe") ? "Esta categoria já tem uma regra. Edite a que existe." : ex.Message);
                salvar.IsEnabled = true;
            }
        });
        Atualizar();
        j.Loaded += (_, _) => categoria.Focus();
        _area.AbrirJanela(j);
    }

    private void ConfirmarExcluirRegra(RegraValidade r) =>
        _area.AbrirJanela(new ConfirmacaoJanela(_area, "Excluir a regra?",
            $"Os produtos de {r.Categoria} deixam de ter desconto perto do vencimento. As vendas já feitas continuam iguais.",
            async () =>
            {
                await _servico!.ExcluirRegraValidadeAsync(r.Id);
                await RecarregarAsync($"Promoção de validade de {r.Categoria} excluída.");
            }));

    // ------------------------------------------------------------ Peças

    private FrameworkElement TopoCartao(string titulo, string texto, string botao, Action acao)
    {
        var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var b = new Button { Style = Recurso<Style>("BotaoPrimario"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 0, 0, 0) };
        var conteudo = new StackPanel { Orientation = Orientation.Horizontal };
        conteudo.Children.Add(new ContentControl { Style = Recurso<Style>("Icone"), Tag = Recurso<Geometry>("IconeMais"), Foreground = Brushes.White, Width = 16, Margin = new Thickness(0, 0, 8, 0) });
        conteudo.Children.Add(new TextBlock { Text = botao });
        b.Content = conteudo;
        b.Click += (_, _) => acao();
        DockPanel.SetDock(b, Dock.Right);
        topo.Children.Add(b);
        var textos = new StackPanel();
        textos.Children.Add(new TextBlock { Text = titulo, FontSize = 18, FontWeight = FontWeights.Bold });
        textos.Children.Add(new TextBlock { Text = texto, FontSize = 13, Style = Recurso<Style>("TextoSuave"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        topo.Children.Add(textos);
        return topo;
    }

    /// <summary>Carimbo da situação: Ativa (verde), Agendada (amarelo), Encerrada (cinza).</summary>
    private Border Carimbo(string texto, string cor)
    {
        var (fundo, frente) = cor switch
        {
            "Verde" => ("Verde50", "Verde700"),
            "Ambar" => ("Ambar50", "Ambar700"),
            _ => ("Fundo", "Suave")
        };
        return new Border
        {
            Background = Recurso<Brush>(fundo), CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 1, 7, 1), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = texto, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Recurso<Brush>(frente) }
        };
    }

    private (StackPanel Bloco, TextBox Campo) CampoData(string rotulo, string valor)
    {
        var bloco = new StackPanel();
        bloco.Children.Add(new TextBlock { Text = rotulo, Style = Recurso<Style>("Rotulo") });
        var campo = new TextBox { Text = valor, MaxLength = 10, ToolTip = "dd/mm/aaaa" };
        System.Windows.Automation.AutomationProperties.SetName(campo, rotulo);
        bloco.Children.Add(campo);
        return (bloco, campo);
    }

    private (StackPanel Bloco, TextBox Campo) CampoNumero(string rotulo, string valor)
    {
        var bloco = new StackPanel();
        bloco.Children.Add(new TextBlock { Text = rotulo, Style = Recurso<Style>("Rotulo"), TextWrapping = TextWrapping.Wrap });
        var campo = new TextBox { Text = valor, MaxLength = 6 };
        System.Windows.Automation.AutomationProperties.SetName(campo, rotulo);
        bloco.Children.Add(campo);
        return (bloco, campo);
    }

    /// <summary>"15", "15,5" ou "15%" → 15; null se inválido. zeroVale = aceita 0 (desconto mínimo).</summary>
    private static decimal? LerPorcento(string texto, bool zeroVale = false)
    {
        var t = texto.Replace("%", "").Trim().Replace('.', ',');
        if (!decimal.TryParse(t, NumberStyles.Number, PtBr, out var v)) return null;
        v = Math.Round(v, 2);
        return v < 0 || v > 90 || (!zeroVale && v == 0) ? null : v;
    }

    private static string DiasTexto(int dias) => dias == 0 ? "no dia" : dias == 1 ? "1 dia antes" : $"{dias} dias antes";
}
