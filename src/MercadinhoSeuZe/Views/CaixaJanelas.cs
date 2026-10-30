using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Janelas pequenas do caixa (CPF na nota, desconto, consulta de preço, vendas em espera, sangria, suprimento e
/// fechamento) e do Estoque (promoções), montadas em código com
/// os mesmos estilos das outras janelas: papel com faixa verde, título, conteúdo e botões. Esc fecha.
/// </summary>
internal sealed class JanelaSimples : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly AreaInternaView _area;
    private readonly StackPanel _corpo = new();
    private readonly StackPanel _botoes = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
    private readonly Border _erro;
    private readonly TextBlock _erroTexto;

    public JanelaSimples(AreaInternaView area, string titulo, string? texto, double largura = 440)
    {
        _area = area;
        Width = largura;
        var raiz = new StackPanel();
        raiz.Children.Add(new TextBlock { Text = titulo, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        if (texto is not null)
            raiz.Children.Add(new TextBlock { Text = texto, FontSize = 13.5, Style = Estilo("TextoSuave"), Margin = new Thickness(0, 0, 0, 14) });
        _erroTexto = new TextBlock { Foreground = Pincel("Vermelho600"), FontSize = 14 };
        _erro = new Border { Style = Estilo("Aviso"), Background = Pincel("Vermelho50"), BorderBrush = Pincel("Vermelho600"), Visibility = Visibility.Collapsed, Child = _erroTexto };
        raiz.Children.Add(_erro);
        raiz.Children.Add(_corpo);
        raiz.Children.Add(_botoes);
        Content = new Border { Style = Estilo("Janela"), Padding = new Thickness(24, 22, 24, 20), Child = raiz };
    }

    public StackPanel Corpo => _corpo;

    public void Erro(string? mensagem)
    {
        _erroTexto.Text = mensagem ?? "";
        _erro.Visibility = mensagem is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public Button Botao(string texto, bool principal, Action acao)
    {
        var b = new Button { Content = texto, Style = Estilo(principal ? "BotaoPrimario" : "BotaoFantasma"), Margin = new Thickness(_botoes.Children.Count == 0 ? 0 : 10, 0, 0, 0), IsDefault = principal };
        b.Click += (_, _) => acao();
        _botoes.Children.Add(b);
        return b;
    }

    public TextBox Campo(string rotulo, string valor = "", int max = 20)
    {
        _corpo.Children.Add(new TextBlock { Text = rotulo, Style = Estilo("Rotulo") });
        var campo = new TextBox { Text = valor, MaxLength = max };
        System.Windows.Automation.AutomationProperties.SetName(campo, rotulo);
        _corpo.Children.Add(campo);
        campo.TextChanged += (_, _) => { campo.Tag = null; Erro(null); };
        return campo;
    }

    public void Fechar() => _area.FecharJanela();

    public Style Estilo(string chave) => (Style)Application.Current.FindResource(chave);

    public Brush Pincel(string chave) => (Brush)Application.Current.FindResource(chave);

    public static string Moeda(decimal v) => v.ToString("C", PtBr);
}

/// <summary>Monta as janelas do caixa.</summary>
internal static class CaixaJanelas
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    /// <summary>CPF/CNPJ na nota. aoConfirmar recebe só os dígitos (ou null = sem CPF).</summary>
    public static JanelaSimples Cpf(AreaInternaView area, string? atual, Action<string?> aoConfirmar)
    {
        var j = new JanelaSimples(area, "CPF ou CNPJ na nota", "Pergunte ao cliente se quer o CPF na nota. Ele vai junto com a venda.", 400);
        var campo = j.Campo("CPF ou CNPJ", atual is null ? "" : Documento.Formatar(atual), 18);
        void Confirmar()
        {
            var digitos = Documento.Digitos(campo.Text);
            if (digitos.Length == 0) { aoConfirmar(null); j.Fechar(); return; }
            if (!Documento.Valido(digitos))
            {
                campo.Tag = "invalido";
                j.Erro("CPF ou CNPJ inválido. Confira os números.");
                campo.Focus();
                return;
            }
            aoConfirmar(digitos);
            j.Fechar();
        }
        j.Botao("Sem CPF", false, () => { aoConfirmar(null); j.Fechar(); });
        j.Botao("Confirmar", true, Confirmar);
        j.Loaded += (_, _) => { campo.Focus(); campo.SelectAll(); };
        return j;
    }

    /// <summary>Desconto em R$ ou % sobre o valor dos itens. aoConfirmar recebe o desconto em R$.</summary>
    public static JanelaSimples Desconto(AreaInternaView area, decimal itens, decimal atual, Action<decimal> aoConfirmar)
    {
        var j = new JanelaSimples(area, "Desconto na venda",
            $"Itens: {JanelaSimples.Moeda(itens)}. Digite o desconto em reais (ex.: 5,00) ou em porcentagem (ex.: 10%). O gerente precisa autorizar.", 420);
        var campo = j.Campo("Desconto", atual > 0 ? atual.ToString("N2", PtBr) : "", 10);
        var previa = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = j.Pincel("Verde700"), Margin = new Thickness(0, 10, 0, 0) };
        j.Corpo.Children.Add(previa);
        decimal? Ler()
        {
            var t = campo.Text.Trim();
            if (t.Length == 0) return 0;
            if (t.EndsWith('%'))
                return decimal.TryParse(t.TrimEnd('%').Trim(), NumberStyles.Number, PtBr, out var pct) && pct is >= 0 and <= 100
                    ? Math.Round(itens * pct / 100, 2) : null;
            return Formulario.LerPreco(t);
        }
        campo.TextChanged += (_, _) => previa.Text = Ler() is { } d && d <= itens ? $"Total com desconto: {JanelaSimples.Moeda(itens - d)}" : "";
        j.Botao("Voltar", false, j.Fechar);
        j.Botao("Aplicar desconto", true, () =>
        {
            if (Ler() is not { } d || d > itens)
            {
                campo.Tag = "invalido";
                j.Erro($"Use um valor de R$ 0,00 até {JanelaSimples.Moeda(itens)}, ou uma porcentagem.");
                return;
            }
            j.Fechar();
            aoConfirmar(d);
        });
        j.Loaded += (_, _) => { campo.Focus(); campo.SelectAll(); };
        return j;
    }

    /// <summary>Consulta de preço (F3): lê o código ou o nome e mostra preço, estoque e validade, sem pôr na venda.</summary>
    public static JanelaSimples ConsultaPreco(AreaInternaView area, IReadOnlyList<Produto> produtos)
    {
        var j = new JanelaSimples(area, "Consulta de preço", "Passe o leitor ou digite o nome. Nada entra na venda.", 460);
        var campo = j.Campo("Código ou nome do produto", "", 60);
        var resultado = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        j.Corpo.Children.Add(resultado);
        void Mostrar(Produto? p, decimal? valorEtiqueta = null)
        {
            resultado.Children.Clear();
            if (p is null)
            {
                resultado.Children.Add(new TextBlock { Text = "Nenhum produto encontrado.", FontSize = 14, Style = j.Estilo("TextoSuave") });
                return;
            }
            resultado.Children.Add(new TextBlock { Text = p.Nome, FontSize = 18, FontWeight = FontWeights.Bold });
            var cat = CategoriaVisual.Pilula(j, p.Categoria, 12);
            cat.Margin = new Thickness(0, 4, 0, 10);
            resultado.Children.Add(cat);
            var unidade = p.PorPeso ? " / kg" : "";
            var preco = new TextBlock { FontSize = 28, FontWeight = FontWeights.ExtraBold };
            if (p.PromocaoAtiva is { } promo)
            {
                preco.Inlines.Add(new System.Windows.Documents.Run(JanelaSimples.Moeda(p.Preco)) { FontSize = 16, FontWeight = FontWeights.SemiBold, TextDecorations = TextDecorations.Strikethrough });
                preco.Inlines.Add(new System.Windows.Documents.Run("  " + JanelaSimples.Moeda(promo.PrecoCom(p.Preco)) + unidade));
            }
            else preco.Text = JanelaSimples.Moeda(p.Preco) + unidade;
            resultado.Children.Add(new Border { Style = j.Estilo("EtiquetaPreco"), Padding = new Thickness(12, 2, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Child = preco });
            var linhas = new List<string>();
            if (p.PromocaoAtiva is { } ativa) linhas.Add($"Promoção {ativa.Texto} {ativa.Periodo}");
            foreach (var l in Promocoes.LotesEmPromocao(p).OrderBy(l => l.Validade))
                linhas.Add($"Lote que vence em {l.Validade:dd/MM}: −{Promocoes.DescontoValidade(p, l).ToString("0.##", PtBr)}% hoje " +
                           $"({JanelaSimples.Moeda(Promocoes.PrecoDoLote(p, l))}{unidade}, {p.Formatar(l.QuantidadeRestante)})");
            linhas.Add(p.DisponivelParaVenda > 0 ? $"Disponível: {p.Formatar(p.DisponivelParaVenda)}"
                : p.QuantidadeVencida > 0 && p.Vendavel == 0 ? "Só tem lote vencido: venda bloqueada" : "Sem estoque");
            if (p.Reservado > 0) linhas.Add($"Reservado em vendas abertas: {p.Formatar(p.Reservado)}");
            if (p.Situacao is { } s) linhas.Add($"Próxima validade: {p.ProximaValidade:dd/MM/yyyy} ({s.Texto.ToLower(PtBr)})");
            if (valorEtiqueta is { } v) linhas.Add($"Etiqueta da balança: {JanelaSimples.Moeda(v)} ({Math.Round(v / p.Preco, 3).ToString("0.000", PtBr)} kg)");
            foreach (var l in linhas)
                resultado.Children.Add(new TextBlock { Text = l, FontSize = 14, Margin = new Thickness(0, 8, 0, 0) });
        }
        campo.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            var t = campo.Text.Trim();
            if (t.Length == 0) return;
            if (t.All(char.IsAsciiDigit))
            {
                var leitura = CodigoBarras.Ler(produtos, t);
                Mostrar(leitura?.Produto, leitura?.ValorEtiqueta);
            }
            else
            {
                var n = Normalizar(t);
                Mostrar(produtos.Where(p => Normalizar(p.Nome).Contains(n)).OrderByDescending(p => Normalizar(p.Nome).StartsWith(n)).FirstOrDefault());
            }
            campo.SelectAll();
        };
        j.Botao("Fechar", true, j.Fechar);
        j.Loaded += (_, _) => campo.Focus();
        return j;
    }

    /// <summary>Vendas em espera do caixa, com "Retomar" em cada uma.</summary>
    public static JanelaSimples Espera(AreaInternaView area, IReadOnlyList<Venda> vendas, Func<Venda, Task> retomar)
    {
        var j = new JanelaSimples(area, "Vendas em espera", "Os itens continuam reservados até a venda ser retomada ou cancelada.", 520);
        foreach (var v in vendas)
        {
            var linha = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
            var botao = new Button { Content = "Retomar", Style = j.Estilo("BotaoPrimario"), Height = 40, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(12, 0, 0, 0) };
            var venda = v;
            botao.Click += async (_, _) =>
            {
                botao.IsEnabled = false;
                try { await retomar(venda); j.Fechar(); }
                catch (Services.ConexaoException ex) { j.Erro(ex.Message); botao.IsEnabled = true; }
            };
            DockPanel.SetDock(botao, Dock.Right);
            linha.Children.Add(botao);
            var total = new Border { Style = j.Estilo("EtiquetaPreco"), Margin = new Thickness(12, 0, 0, 0), Child = new TextBlock { Text = JanelaSimples.Moeda(v.Itens.Sum(i => i.Subtotal)), FontWeight = FontWeights.Bold } };
            DockPanel.SetDock(total, Dock.Right);
            linha.Children.Add(total);
            var texto = new StackPanel();
            texto.Children.Add(new TextBlock { Text = $"Venda nº {v.Id} · desde {v.CriadaEm.ToLocalTime():HH:mm}", FontSize = 14, FontWeight = FontWeights.SemiBold });
            texto.Children.Add(new TextBlock { Text = v.ResumoItens, FontSize = 12.5, Style = j.Estilo("TextoSuave"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
            linha.Children.Add(texto);
            j.Corpo.Children.Add(new Border { BorderBrush = j.Pincel("Linha"), BorderThickness = new Thickness(0, 0, 0, 1), Child = linha });
        }
        j.Botao("Fechar", false, j.Fechar);
        return j;
    }

    /// <summary>
    /// Sangria (tirar dinheiro da gaveta para o cofre; depois pede a senha do gerente) ou suprimento (pôr mais troco).
    /// aoConfirmar recebe o valor e o motivo.
    /// </summary>
    public static JanelaSimples Movimento(AreaInternaView area, bool sangria, decimal naGaveta, Func<decimal, string?, Task> aoConfirmar)
    {
        var j = new JanelaSimples(area, sangria ? "Sangria" : "Suprimento",
            sangria
                ? $"Tire o dinheiro da gaveta e guarde no cofre. Na gaveta agora: {JanelaSimples.Moeda(naGaveta)}. O gerente precisa autorizar."
                : "Dinheiro colocado na gaveta para ter troco (por exemplo, moedas trazidas do cofre).", 420);
        var valor = j.Campo("Valor (R$)", "", 12);
        Formulario.MascaraDinheiro(valor, vazio: true);
        var motivo = j.Campo("Motivo (opcional)", "", 120);
        j.Botao("Voltar", false, j.Fechar);
        Button? confirmar = null;
        confirmar = j.Botao(sangria ? "Fazer sangria" : "Registrar suprimento", true, async () =>
        {
            var v = Formulario.LerPreco(valor.Text);
            var erro = v is null or <= 0 ? "Digite o valor, por exemplo 200,00."
                : sangria && v > naGaveta ? $"A sangria não pode passar do dinheiro da gaveta ({JanelaSimples.Moeda(naGaveta)})." : null;
            if (erro is not null)
            {
                valor.Tag = "invalido";
                j.Erro(erro);
                valor.Focus();
                return;
            }
            confirmar!.IsEnabled = false;
            try { await aoConfirmar(v!.Value, motivo.Text.Trim().Length == 0 ? null : motivo.Text.Trim()); }
            catch (Services.ConexaoException ex) { j.Erro(ex.Message); }
            finally { confirmar.IsEnabled = true; }
        });
        j.Loaded += (_, _) => valor.Focus();
        return j;
    }

    /// <summary>Notas e moedas para contar a gaveta no fechamento.</summary>
    private static readonly decimal[] Cedulas = [200, 100, 50, 20, 10, 5, 2];

    /// <summary>
    /// Fechamento do caixa: mostra o que deveria estar na gaveta (troco inicial + dinheiro das vendas + suprimentos −
    /// sangrias), a pessoa conta o dinheiro (o total ou nota por nota) e vê na hora se sobrou ou faltou.
    /// aoConfirmar recebe o contado e a observação.
    /// </summary>
    public static JanelaSimples Fechamento(AreaInternaView area, ResumoCaixa r, Func<decimal, string?, Task> aoConfirmar)
    {
        var j = new JanelaSimples(area, "Fechar o caixa",
            "Conte o dinheiro da gaveta. Cartão, vale e Pix ficam na maquininha: confira com o relatório dela.", 560);
        j.Corpo.Children.Add(TabelaResumo(j, r));

        j.Corpo.Children.Add(new TextBlock { Text = "Contar nota por nota (opcional)", Style = j.Estilo("Rotulo"), Margin = new Thickness(0, 16, 0, 6) });
        var grade = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
        var quantidades = new List<(decimal Valor, TextBox Campo)>();
        foreach (var c in Cedulas)
        {
            var bloco = new StackPanel { Margin = new Thickness(0, 0, 10, 8) };
            bloco.Children.Add(new TextBlock { Text = $"R$ {c:0}", FontSize = 12.5, Style = j.Estilo("TextoSuave") });
            var q = new TextBox { MaxLength = 4, MinHeight = 36, FontSize = 14 };
            System.Windows.Automation.AutomationProperties.SetName(q, $"Quantidade de notas de {c:0} reais");
            q.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsAsciiDigit);
            bloco.Children.Add(q);
            grade.Children.Add(bloco);
            quantidades.Add((c, q));
        }
        var blocoMoedas = new StackPanel { Margin = new Thickness(0, 0, 10, 8) };
        blocoMoedas.Children.Add(new TextBlock { Text = "Moedas (R$)", FontSize = 12.5, Style = j.Estilo("TextoSuave") });
        var moedas = new TextBox { MinHeight = 36, FontSize = 14 };
        System.Windows.Automation.AutomationProperties.SetName(moedas, "Total em moedas, em reais");
        Formulario.MascaraDinheiro(moedas, vazio: true);
        blocoMoedas.Children.Add(moedas);
        grade.Children.Add(blocoMoedas);
        j.Corpo.Children.Add(grade);

        var contado = j.Campo("Dinheiro contado na gaveta (R$)", "", 12);
        Formulario.MascaraDinheiro(contado, vazio: true);
        var diferenca = new TextBlock { FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 0) };
        j.Corpo.Children.Add(diferenca);
        var obs = j.Campo("Observação (se sobrou ou faltou, explique)", "", 200);

        var somando = false;
        void Somar()
        {
            if (somando) return;
            var total = quantidades.Sum(x => int.TryParse(x.Campo.Text, out var n) ? n * x.Valor : 0) + (Formulario.LerPreco(moedas.Text) ?? 0);
            somando = true;
            contado.Text = total > 0 ? Formulario.Preco(total) : "";
            somando = false;
        }
        foreach (var (_, campo) in quantidades) campo.TextChanged += (_, _) => Somar();
        moedas.TextChanged += (_, _) => Somar();
        contado.TextChanged += (_, _) =>
        {
            // Digitar o total à mão limpa a contagem por nota (vale o total digitado)
            if (!somando && contado.IsKeyboardFocusWithin)
            {
                somando = true;
                foreach (var (_, campo) in quantidades) campo.Text = "";
                moedas.Text = "";
                somando = false;
            }
            if (Formulario.LerPreco(contado.Text) is not { } c || contado.Text.Length == 0)
            {
                diferenca.Text = "";
                return;
            }
            var d = c - r.Esperado;
            diferenca.Text = d == 0 ? "A gaveta bateu certinho." : d > 0 ? $"Sobra de {JanelaSimples.Moeda(d)}" : $"Falta de {JanelaSimples.Moeda(-d)}";
            diferenca.Foreground = j.Pincel(d == 0 ? "Verde700" : d > 0 ? "Ambar700" : "Vermelho600");
        };

        if (r.EmEspera > 0)
            j.Corpo.Children.Add(new TextBlock
            {
                Text = r.EmEspera == 1 ? "1 venda em espera será cancelada (os itens voltam a ficar disponíveis)."
                    : $"{r.EmEspera} vendas em espera serão canceladas (os itens voltam a ficar disponíveis).",
                FontSize = 13, Foreground = j.Pincel("Ambar700"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0)
            });

        j.Botao("Voltar", false, j.Fechar);
        Button? fechar = null;
        fechar = j.Botao("Fechar o caixa", true, async () =>
        {
            if (contado.Text.Trim().Length == 0 || Formulario.LerPreco(contado.Text) is not { } c)
            {
                contado.Tag = "invalido";
                j.Erro("Digite quanto dinheiro tem na gaveta (0,00 se estiver vazia).");
                contado.Focus();
                return;
            }
            if (c != r.Esperado && obs.Text.Trim().Length == 0)
            {
                obs.Tag = "invalido";
                j.Erro("A gaveta não bateu. Escreva na observação o que aconteceu (ex.: troco errado).");
                obs.Focus();
                return;
            }
            fechar!.IsEnabled = false;
            try { await aoConfirmar(c, obs.Text.Trim().Length == 0 ? null : obs.Text.Trim()); }
            catch (Services.ConexaoException ex) { j.Erro(ex.Message); }
            finally { fechar.IsEnabled = true; }
        });
        j.Loaded += (_, _) => contado.Focus();
        return j;
    }

    /// <summary>Resumo do caixa em linhas: vendas, formas de pagamento, movimentos e a gaveta (e a contagem, se fechado).</summary>
    public static FrameworkElement TabelaResumo(JanelaSimples j, ResumoCaixa r)
    {
        var painel = new StackPanel();
        void Linha(string rotulo, string valor, bool forte = false, string cor = "Tinta", bool separador = false)
        {
            if (separador) painel.Children.Add(new Border { Height = 1, Background = j.Pincel("Linha"), Margin = new Thickness(0, 6, 0, 6) });
            var d = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var v = new TextBlock { Text = valor, FontSize = forte ? 16 : 14, FontWeight = forte ? FontWeights.Bold : FontWeights.SemiBold, Foreground = j.Pincel(cor) };
            DockPanel.SetDock(v, Dock.Right);
            d.Children.Add(v);
            d.Children.Add(new TextBlock { Text = rotulo, FontSize = forte ? 15 : 14, FontWeight = forte ? FontWeights.Bold : FontWeights.Normal });
            painel.Children.Add(d);
        }
        Linha($"Vendas pagas ({r.Vendas})", JanelaSimples.Moeda(r.TotalVendas), forte: true);
        foreach (var (id, nome, _, _) in FormaPagamento.Todas)
            if (r.Formas.GetValueOrDefault(id) is var valor && valor > 0) Linha("   " + nome, JanelaSimples.Moeda(valor));
        if (r.Promocoes > 0) Linha("Descontos de promoção", "− " + JanelaSimples.Moeda(r.Promocoes), cor: "Suave");
        if (r.Descontos > 0) Linha("Descontos do gerente", "− " + JanelaSimples.Moeda(r.Descontos), cor: "Suave");
        if (r.Canceladas > 0) Linha("Vendas canceladas", r.Canceladas.ToString(PtBr), cor: "Suave");

        Linha("Troco inicial", JanelaSimples.Moeda(r.TrocoInicial), separador: true);
        Linha("+ Dinheiro das vendas (sem o troco dado)", JanelaSimples.Moeda(r.Dinheiro));
        if (r.Suprimentos > 0) Linha("+ Suprimentos", JanelaSimples.Moeda(r.Suprimentos));
        if (r.Sangrias > 0) Linha("− Sangrias", JanelaSimples.Moeda(r.Sangrias));
        Linha(r.FechadoEm is null ? "Deveria ter na gaveta" : "Deveria ter na gaveta no fechamento", JanelaSimples.Moeda(r.Esperado), forte: true, cor: "Verde700");
        if (r.Contado is { } contado)
        {
            Linha("Contado", JanelaSimples.Moeda(contado), forte: true);
            var d = r.Diferenca ?? 0;
            Linha(d == 0 ? "Diferença" : d > 0 ? "Sobra" : "Falta", JanelaSimples.Moeda(Math.Abs(d)), forte: true,
                cor: d == 0 ? "Verde700" : d > 0 ? "Ambar700" : "Vermelho600");
            if (r.Obs is not null) Linha("Observação", r.Obs, cor: "Suave");
        }
        if (r.Movimentos.Count > 0)
        {
            painel.Children.Add(new TextBlock { Text = "Movimentos da gaveta", Style = j.Estilo("Rotulo"), Margin = new Thickness(0, 12, 0, 2) });
            foreach (var m in r.Movimentos)
                painel.Children.Add(new TextBlock
                {
                    FontSize = 13, Style = j.Estilo("TextoSuave"), TextWrapping = TextWrapping.Wrap,
                    Text = $"{m.Hora} · {(m.Sangria ? "Sangria" : "Suprimento")} de {JanelaSimples.Moeda(m.Valor)}" +
                           (m.Motivo is null ? "" : $" · {m.Motivo}") + (m.AutorizadoPor is null ? "" : $" · autorizada por {m.AutorizadoPor}")
                });
        }
        return painel;
    }

    private static string Normalizar(string texto)
    {
        var decomposto = texto.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }
}
