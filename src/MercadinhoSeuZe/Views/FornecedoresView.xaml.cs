using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Fornecedores do gerente (igual a fornecedores.html do site): cadastro, produtos de cada fornecedor com os
/// sinais de estoque, contato rápido (WhatsApp e e-mail com a mensagem pronta), sugestões de reposição e
/// histórico de pedidos. Lê e grava no Supabase (database/004_semana4_fornecedores.sql).
/// </summary>
public partial class FornecedoresView : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly AreaInternaView _area;
    private readonly FornecedorService? _servico;
    private readonly EstoqueService? _estoque;
    private readonly HashSet<long> _abertos = [];          // fornecedores com o drop-down aberto
    private readonly HashSet<long> _todosProdutos = [];    // drop-downs mostrando todos os produtos
    private readonly HashSet<long> _todoHistorico = [];    // drop-downs mostrando todos os pedidos
    private List<Fornecedor> _fornecedores = [];
    private List<Pedido> _pedidos = [];
    private List<Produto> _produtos = [];
    private string _filtro = "todos";
    private bool _compacta;
    private bool _carregou;
    private bool _dadosProntos;
    private Action? _depoisDeCarregar;

    public FornecedoresView(AreaInternaView area, FornecedorService? servico, EstoqueService? estoque)
    {
        InitializeComponent();
        _area = area;
        _servico = servico;
        _estoque = estoque;
        AjustarColunas(CabecalhoTabela);
        IniciarAtalhosDesenvolvimento();
        Loaded += async (_, _) => { if (!_carregou) await CarregarAsync(); };
    }

    // ------------------------------------------------------------ Dados

    private async Task CarregarAsync()
    {
        _carregou = true;
        AvisoErro.Visibility = Visibility.Collapsed;
        if (_servico is null || _estoque is null)
        {
            MostrarErro("Sistema sem configuração do banco de dados. Veja o README.");
            return;
        }

        try
        {
            var fornecedores = _servico.ListarAsync();
            var pedidos = _servico.ListarPedidosAsync();
            var produtos = _estoque.ListarAsync();
            await Task.WhenAll(fornecedores, pedidos, produtos);
            _fornecedores = fornecedores.Result;
            _pedidos = pedidos.Result;
            _produtos = produtos.Result;
            Fornecedor.AplicarPrazos(_produtos, _fornecedores);
        }
        catch (ConexaoException ex)
        {
            MostrarErro(ex.Message);
            return;
        }
        _dadosProntos = true;
        Mostrar();
        var acao = _depoisDeCarregar;
        _depoisDeCarregar = null;
        acao?.Invoke();
    }

    private void MostrarErro(string texto)
    {
        AvisoErroTexto.Text = texto;
        AvisoErro.Visibility = Visibility.Visible;
        Resumo.Text = "Não foi possível carregar os fornecedores.";
    }

    private async void TentarDeNovo_Click(object sender, RoutedEventArgs e) => await CarregarAsync();

    /// <summary>Chamado pelas janelas depois de gravar.</summary>
    public async Task RecarregarAsync(string mensagem)
    {
        _area.FecharJanela();
        _area.MostrarToast(mensagem);
        await CarregarAsync();
    }

    // ------------------------------------------------------------ Regras

    private IEnumerable<Fornecedor> Ativos => _fornecedores.Where(f => f.Ativo);

    /// <summary>Pedidos do fornecedor, do mais novo para o mais antigo.</summary>
    private IEnumerable<Pedido> PedidosDe(Fornecedor f) =>
        _pedidos.Where(p => p.FornecedorId == f.Id).OrderByDescending(p => p.Data).ThenByDescending(p => p.Id);

    private IEnumerable<Produto> ProdutosDe(Fornecedor f) => _produtos.Where(f.Fornece);

    private SituacaoFornecedor Situacao(Fornecedor f)
    {
        var pedidos = PedidosDe(f).ToList();
        if (pedidos.Any(p => p.Atrasado)) return SituacaoFornecedor.Atrasado;
        if (pedidos.Any(p => p.Pendente)) return SituacaoFornecedor.Pendente;
        return pedidos.Count > 0 ? SituacaoFornecedor.EmDia : SituacaoFornecedor.SemPedidos;
    }

    /// <summary>Produto que já está num pedido pendente (de qualquer fornecedor).</summary>
    private bool JaPedido(Produto p) => _pedidos.Any(o => o.Pendente && o.Itens.Any(i => i.ProdutoId == p.Id));

    /// <summary>Quem fornece o produto: o ligado a ele ou o primeiro (A-Z) que entrega a categoria.</summary>
    private Fornecedor? FornecedorDe(Produto p) =>
        Ativos.FirstOrDefault(f => f.Id == p.FornecedorId) ??
        Ativos.OrderBy(f => f.Nome, StringComparer.Create(PtBr, true)).FirstOrDefault(f => p.FornecedorId is null && f.Fornece(p));

    /// <summary>"Pedir o que falta": produtos do fornecedor sem estoque, baixos ou no aviso, que ainda não foram pedidos.</summary>
    private List<Produto> ParaPedir(Fornecedor f) =>
        ProdutosDe(f).Where(p => p.PrecisaRepor && !JaPedido(p)).OrderBy(Urgencia).ThenBy(p => p.Nome, StringComparer.Create(PtBr, true)).ToList();

    private static int Urgencia(Produto p) => p.SemEstoque ? 0 : p.EstoqueBaixo ? 1 : p.AvisoAntecipado ? 2 : 3;

    // ------------------------------------------------------------ Lista

    private void Mostrar()
    {
        var ativos = Ativos.ToList();
        var situacoes = ativos.ToDictionary(f => f.Id, Situacao);
        var pendentes = _pedidos.Count(p => p.Pendente);
        var atrasados = _pedidos.Count(p => p.Atrasado);
        var comFalta = ativos.Count(f => ProdutosDe(f).Any(p => p.SemEstoque || p.EstoqueBaixo));

        KpiTotal.Text = ativos.Count.ToString(PtBr);
        KpiPendentes.Text = pendentes.ToString(PtBr);
        KpiAtrasados.Text = atrasados.ToString(PtBr);
        KpiFalta.Text = comFalta.ToString(PtBr);
        Resumo.Text = $"{Plural(ativos.Count, "fornecedor", "fornecedores")} · {pendentes} {(pendentes == 1 ? "pedido aguardando" : "pedidos aguardando")} entrega · " +
                      $"{atrasados} com atraso";

        MostrarSugestoes();
        MostrarChips(ativos, situacoes);

        var termo = Busca.Text.Trim();
        var lista = (_filtro == "inativos" ? _fornecedores.Where(f => !f.Ativo) : ativos)
            .Where(f => PassaBusca(f, termo) && PassaFiltro(f))
            .OrderBy(f => f.Nome, StringComparer.Create(PtBr, true))
            .ToList();

        Lista.Children.Clear();
        foreach (var f in lista)
            Lista.Children.Add(LinhaFornecedor(f));

        Vazio.Visibility = lista.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Vazio.Text = _fornecedores.Count == 0 ? "Nenhum fornecedor cadastrado ainda. Clique em “Novo fornecedor” para começar."
            : termo.Length > 0 ? $"Nenhum fornecedor encontrado para “{termo}”."
            : "Nenhum fornecedor neste filtro.";
    }

    private bool PassaBusca(Fornecedor f, string termo) =>
        termo.Length == 0 || Contem(f.Nome, termo) || Contem(f.Categorias, termo) || Contem(f.Contato ?? "", termo) ||
        (Fornecedor.SoDigitos(termo).Length >= 4 && Fornecedor.SoDigitos(f.Telefone + (f.Whatsapp ?? "")).Contains(Fornecedor.SoDigitos(termo))) ||
        ProdutosDe(f).Any(p => Contem(p.Nome, termo));

    private bool PassaFiltro(Fornecedor f) => _filtro switch
    {
        "pendente" => PedidosDe(f).Any(p => p.Pendente),
        "atrasado" => PedidosDe(f).Any(p => p.Atrasado),
        "falta" => ProdutosDe(f).Any(p => p.SemEstoque || p.EstoqueBaixo),
        _ => true
    };

    private void MostrarChips(List<Fornecedor> ativos, Dictionary<long, SituacaoFornecedor> situacoes)
    {
        Chips.Children.Clear();
        var inativos = _fornecedores.Count(f => !f.Ativo);
        if (_filtro == "inativos" && inativos == 0) _filtro = "todos";
        (string Id, string Nome, int N)[] chips =
        [
            ("todos", "Todos", ativos.Count),
            ("pendente", "Pendentes", ativos.Count(f => PedidosDe(f).Any(p => p.Pendente))),
            ("atrasado", "Atrasados", situacoes.Count(s => s.Value == SituacaoFornecedor.Atrasado)),
            ("falta", "Com produto em falta", ativos.Count(f => ProdutosDe(f).Any(p => p.SemEstoque || p.EstoqueBaixo))),
            ("inativos", "Desativados", inativos),
        ];
        foreach (var (id, nome, n) in chips)
        {
            if (id == "inativos" && n == 0) continue;
            var chip = new Button
            {
                Style = Recurso<Style>("Chip"),
                Tag = _filtro == id ? "ativo" : null,
                Uid = id,
                Content = new TextBlock { Inlines = { new System.Windows.Documents.Run(nome + " "), new System.Windows.Documents.Run(n.ToString(PtBr)) { FontWeight = FontWeights.Bold } } }
            };
            System.Windows.Automation.AutomationProperties.SetName(chip, $"{nome}: {n}");
            chip.Click += Filtro_Click;
            Chips.Children.Add(chip);
        }
    }

    private FrameworkElement LinhaFornecedor(Fornecedor f)
    {
        var aberto = _abertos.Contains(f.Id);
        var grade = new Grid { MinHeight = 58 };
        AjustarColunas(grade);

        var seta = new Button
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = aberto ? "Fechar detalhes" : "Ver produtos, pedidos e opções",
            Template = BotaoSoConteudo(),
            Content = new ContentControl
            {
                Style = Recurso<Style>("Icone"),
                Tag = Recurso<Geometry>(aberto ? "IconeSetaBaixo" : "IconeSetaDireita"),
                Foreground = Recurso<Brush>(aberto ? "Verde700" : "Suave"),
                Width = 18
            }
        };
        System.Windows.Automation.AutomationProperties.SetName(seta, aberto ? $"Fechar detalhes de {f.Nome}" : $"Abrir detalhes de {f.Nome}");
        seta.Click += (_, _) => Alternar(f.Id);
        Colocar(grade, seta, 5);

        // Fornecedor (no modo compacto também o telefone e os produtos)
        var nome = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 8, 12, 8) };
        nome.Children.Add(new TextBlock { Text = f.Nome, FontWeight = FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(f.Email) && !_compacta)
            nome.Children.Add(new TextBlock { Text = f.Email, FontSize = 13, Foreground = Recurso<Brush>("Suave"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
        if (_compacta)
        {
            nome.Children.Add(new TextBlock { Text = f.Telefone, FontSize = 13, Foreground = Recurso<Brush>("Suave") });
            nome.Children.Add(Produtos(f));
        }
        Colocar(grade, nome, 0);

        if (!_compacta)
        {
            var contato = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            contato.Children.Add(new TextBlock { Text = f.Telefone, FontSize = 14 });
            contato.Children.Add(BotoesContato(f));
            Colocar(grade, contato, 1);
            Colocar(grade, Produtos(f), 2);

            var ultimo = PedidosDe(f).FirstOrDefault();
            Colocar(grade, Celula(ultimo is null ? "—" : ultimo.Data.ToString("dd/MM/yyyy", PtBr), suave: ultimo is null), 3);
        }
        Colocar(grade, PilulaSituacao(f), 4);

        var linha = new Border { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Child = grade };
        linha.MouseLeftButtonUp += (_, e) =>
        {
            // Clicar nos botões de contato não abre o drop-down
            if (e.OriginalSource is DependencyObject o && Ancestral<Button>(o) is null) Alternar(f.Id);
        };

        var bloco = new StackPanel();
        bloco.Children.Add(linha);
        if (aberto) bloco.Children.Add(Detalhes(f));

        return new Border
        {
            BorderBrush = Recurso<Brush>("Linha"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = aberto ? Recurso<Brush>("Fundo") : Brushes.Transparent,
            Opacity = f.Ativo ? 1 : 0.75,
            Child = bloco
        };
    }

    /// <summary>"Grãos, Mercearia" e, embaixo, os sinais de estoque dos produtos do fornecedor.</summary>
    private StackPanel Produtos(Fornecedor f)
    {
        var bloco = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        bloco.Children.Add(new TextBlock { Text = f.Categorias, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, ToolTip = f.Categorias });
        var produtos = ProdutosDe(f).ToList();
        var sem = produtos.Count(p => p.SemEstoque);
        var baixo = produtos.Count(p => p.EstoqueBaixo);
        var aviso = produtos.Count(p => p.AvisoAntecipado);
        var sinais = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        if (sem + baixo > 0)
            sinais.Children.Add(Sinal(true, (sem > 0 ? $"{sem} sem estoque" : "") + (sem > 0 && baixo > 0 ? " · " : "") + (baixo > 0 ? $"{baixo} baixo" : ""),
                "Produtos deste fornecedor sem estoque ou abaixo do mínimo"));
        if (aviso > 0)
            sinais.Children.Add(Sinal(false, $"{aviso} perto do mínimo", "Chegando no mínimo: hora de pedir"));
        if (sinais.Children.Count == 0)
            sinais.Children.Add(new TextBlock
            {
                Text = produtos.Count == 0 ? "nenhum produto ligado" : $"{Plural(produtos.Count, "produto", "produtos")} em dia",
                FontSize = 12.5, Foreground = Recurso<Brush>("Suave")
            });
        bloco.Children.Add(sinais);
        return bloco;
    }

    /// <summary>Bolinha vermelha (falta) ou triângulo amarelo (aviso antecipado) com o texto ao lado.</summary>
    private StackPanel Sinal(bool falta, string texto, string explicacao)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0), ToolTip = explicacao, Background = Brushes.Transparent };
        if (falta)
            s.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = Recurso<Brush>("Vermelho600"), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        else
            s.Children.Add(new ContentControl { Style = Recurso<Style>("Icone"), Tag = Recurso<Geometry>("IconeTriangulo"), Foreground = Recurso<Brush>("Ambar700"), Width = 14, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
        s.Children.Add(new TextBlock { Text = texto, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Recurso<Brush>(falta ? "Vermelho600" : "Ambar700") });
        System.Windows.Automation.AutomationProperties.SetName(s, texto);
        return s;
    }

    private Border PilulaSituacao(Fornecedor f)
    {
        var (texto, fundo, cor) = !f.Ativo ? ("Desativado", "Fundo", "Suave") : Situacao(f) switch
        {
            SituacaoFornecedor.Atrasado => ("Atrasado", "Vermelho50", "Vermelho600"),
            SituacaoFornecedor.Pendente => ("Pendente", "Azul50", "Azul600"),
            SituacaoFornecedor.EmDia => ("Em dia", "Verde50", "Verde700"),
            _ => ("Sem pedidos", "Fundo", "Suave")
        };
        return Etiqueta(texto, fundo, cor);
    }

    private Border Etiqueta(string texto, string fundo, string cor) => new()
    {
        Background = Recurso<Brush>(fundo),
        CornerRadius = new CornerRadius(11),
        Padding = new Thickness(10, 3, 10, 3),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = texto, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Recurso<Brush>(cor), TextWrapping = TextWrapping.NoWrap }
    };

    /// <summary>Contato rápido: WhatsApp, e-mail e copiar telefone (ícones pequenos).</summary>
    private StackPanel BotoesContato(Fornecedor f)
    {
        var botoes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-4, 2, 0, 0) };
        botoes.Children.Add(BotaoIcone("IconeMensagem", $"Abrir conversa com {f.Nome} no WhatsApp", () => AbrirWhatsapp(f, MensagemPedido.Saudacao(f, _servico!.Usuario))));
        if (!string.IsNullOrWhiteSpace(f.Email))
            botoes.Children.Add(BotaoIcone("IconeEmail", $"Escrever e-mail para {f.Email}", () => AbrirEmail(f, "Mercado", MensagemPedido.Saudacao(f, _servico!.Usuario))));
        botoes.Children.Add(BotaoIcone("IconeCopiar", "Copiar telefone", () =>
            _area.MostrarToast(ContatoRapido.Copiar(f.Telefone) ? $"Telefone de {f.Nome} copiado." : "Não foi possível copiar. Tente de novo.")));
        return botoes;
    }

    private Button BotaoIcone(string icone, string dica, Action acao)
    {
        var b = new Button
        {
            Template = BotaoSoConteudo(),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = dica,
            Content = new ContentControl { Style = Recurso<Style>("Icone"), Tag = Recurso<Geometry>(icone), Foreground = Recurso<Brush>("Verde700"), Width = 17 }
        };
        System.Windows.Automation.AutomationProperties.SetName(b, dica);
        b.Click += (_, e) => { e.Handled = true; acao(); };
        return b;
    }

    private void AbrirWhatsapp(Fornecedor f, string mensagem)
    {
        if (!ContatoRapido.AbrirWhatsapp(f, mensagem))
            _area.MostrarToast("Não foi possível abrir o WhatsApp. Confira se há um navegador instalado.");
    }

    private void AbrirEmail(Fornecedor f, string assunto, string mensagem)
    {
        if (!ContatoRapido.AbrirEmail(f, assunto, mensagem))
            _area.MostrarToast("Não foi possível abrir o e-mail. Configure um programa de e-mail no Windows.");
    }

    // ------------------------------------------------------------ Drop-down do fornecedor

    private FrameworkElement Detalhes(Fornecedor f)
    {
        var painel = new StackPanel { Margin = new Thickness(_compacta ? 4 : 12, 0, 8, 18) };
        var paraPedir = ParaPedir(f);

        // Ações
        var acoes = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        if (f.Ativo)
        {
            if (paraPedir.Count > 0)
                acoes.Children.Add(BotaoPequeno($"Pedir o que falta ({paraPedir.Count})", "IconeEnviar", "Verde700", () => AbrirPedido(f, paraPedir), primario: true));
            acoes.Children.Add(BotaoPequeno("Novo pedido", "IconeMais", "Verde700", () => AbrirPedido(f, [])));
        }
        acoes.Children.Add(BotaoPequeno("Editar", "IconeFerramenta", "Verde700", () => AbrirFornecedor(f)));
        if (!f.Ativo)
            acoes.Children.Add(BotaoPequeno("Reativar", "IconeCheck", "Verde700", async () => await AtivarAsync(f, true)));
        else if (PedidosDe(f).Any())
            acoes.Children.Add(BotaoPequeno("Desativar", "IconeLixeira", "Vermelho600", () => ConfirmarDesativar(f)));
        else
            acoes.Children.Add(BotaoPequeno("Excluir", "IconeLixeira", "Vermelho600", () => ConfirmarExclusao(f)));
        painel.Children.Add(acoes);

        // Dados de contato
        var dados = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (!string.IsNullOrWhiteSpace(f.Contato)) dados.Children.Add(Detalhe("Falar com", f.Contato));
        dados.Children.Add(Detalhe("Telefone", f.Telefone));
        dados.Children.Add(Detalhe("WhatsApp", string.IsNullOrWhiteSpace(f.Whatsapp) ? "o mesmo telefone" : f.Whatsapp));
        dados.Children.Add(Detalhe("E-mail", string.IsNullOrWhiteSpace(f.Email) ? "—" : f.Email));
        dados.Children.Add(Detalhe("Prazo padrão de entrega", f.PrazoEntregaDias is null ? $"{Fornecedor.PrazoPadrao} dias (padrão)" : Plural(f.Prazo, "dia", "dias")));
        dados.Children.Add(Detalhe("Pedidos por", f.PrefereEmail ? "E-mail" : "WhatsApp"));
        painel.Children.Add(dados);
        if (!string.IsNullOrWhiteSpace(f.Observacao))
            painel.Children.Add(new TextBlock { Text = f.Observacao, FontSize = 14, Foreground = Recurso<Brush>("Suave"), Margin = new Thickness(0, 0, 0, 10) });

        // Contato rápido (com texto)
        var contato = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        contato.Children.Add(BotaoPequeno("WhatsApp", "IconeMensagem", "Verde700", () => AbrirWhatsapp(f, MensagemPedido.Saudacao(f, _servico!.Usuario)), primeiro: true));
        if (!string.IsNullOrWhiteSpace(f.Email))
            contato.Children.Add(BotaoPequeno("E-mail", "IconeEmail", "Verde700", () => AbrirEmail(f, "Mercado", MensagemPedido.Saudacao(f, _servico!.Usuario))));
        contato.Children.Add(BotaoPequeno("Copiar telefone", "IconeCopiar", "Verde700", () =>
            _area.MostrarToast(ContatoRapido.Copiar(f.Telefone) ? $"Telefone de {f.Nome} copiado." : "Não foi possível copiar. Tente de novo.")));
        painel.Children.Add(contato);

        painel.Children.Add(ListaProdutos(f));
        painel.Children.Add(Historico(f));
        return painel;
    }

    /// <summary>Produtos do fornecedor: primeiro os que precisam de reposição, com os mesmos sinais do Estoque.</summary>
    private FrameworkElement ListaProdutos(Fornecedor f)
    {
        var bloco = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        var produtos = ProdutosDe(f).OrderBy(Urgencia).ThenBy(p => p.Nome, StringComparer.Create(PtBr, true)).ToList();
        bloco.Children.Add(TituloSecao($"Produtos fornecidos ({produtos.Count})"));
        if (produtos.Count == 0)
        {
            bloco.Children.Add(new TextBlock
            {
                Text = "Nenhum produto do estoque é deste fornecedor. Os produtos entram pela categoria (em “o que entrega”) ou pelo campo Fornecedor no cadastro do produto.",
                FontSize = 14, Foreground = Recurso<Brush>("Suave")
            });
            return bloco;
        }

        var todos = _todosProdutos.Contains(f.Id);
        var mostrar = todos ? produtos : produtos.Where(p => p.PrecisaRepor).Take(12).ToList();
        if (mostrar.Count == 0) mostrar = produtos.Take(6).ToList();
        var caixa = new Border { Background = Brushes.White, BorderBrush = Recurso<Brush>("Linha"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 4, 14, 4) };
        var linhas = new StackPanel();
        foreach (var p in mostrar)
        {
            var linha = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var qtd = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var alerta = p.SemEstoque || p.EstoqueBaixo;
            if (alerta)
                qtd.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = Recurso<Brush>("Vermelho600"), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            else if (p.AvisoAntecipado)
                qtd.Children.Add(new ContentControl { Style = Recurso<Style>("Icone"), Tag = Recurso<Geometry>("IconeTriangulo"), Foreground = Recurso<Brush>("Ambar700"), Width = 15, Margin = new Thickness(0, 0, 6, 0) });
            qtd.Children.Add(new TextBlock
            {
                Text = $"{p.Formatar(p.Quantidade)} (mín. {p.Numero(p.EstoqueMinimo)}) · entrega em {Plural(f.PrazoDe(p), "dia", "dias")}",
                FontSize = 14,
                FontWeight = alerta ? FontWeights.Bold : FontWeights.Normal,
                Foreground = Recurso<Brush>(alerta ? "Vermelho600" : "Tinta")
            });
            qtd.ToolTip = p.SemEstoque ? "Sem estoque" : p.EstoqueBaixo ? "Abaixo do mínimo" : p.AvisoAntecipado ? "Chegando no mínimo: hora de pedir" : null;
            DockPanel.SetDock(qtd, Dock.Right);
            linha.Children.Add(qtd);

            var nome = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            nome.Children.Add(new TextBlock { Text = p.Nome, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            if (JaPedido(p))
            {
                var e = Etiqueta("já pedido", "Azul50", "Azul600");
                e.ToolTip = "Está num pedido que ainda não chegou";
                nome.Children.Add(e);
            }
            else if (p.FornecedorId == f.Id)
            {
                var e = Etiqueta("principal", "Verde50", "Verde700");
                e.ToolTip = "Ligado a este fornecedor no cadastro do produto";
                nome.Children.Add(e);
            }
            linha.Children.Add(nome);

            if (linhas.Children.Count > 0) linhas.Children.Add(new Border { Height = 1, Background = Recurso<Brush>("Linha") });
            linhas.Children.Add(linha);
        }
        caixa.Child = linhas;
        bloco.Children.Add(caixa);

        if (produtos.Count > mostrar.Count || todos)
            bloco.Children.Add(Link(todos ? "Mostrar só os que precisam de reposição" : $"Ver todos os {produtos.Count} produtos", () =>
            {
                if (!_todosProdutos.Remove(f.Id)) _todosProdutos.Add(f.Id);
                Mostrar();
            }, margem: new Thickness(0, 8, 0, 0)));
        return bloco;
    }

    /// <summary>Histórico de pedidos (igual ao do site): data, itens, entrega, situação e ações.</summary>
    private FrameworkElement Historico(Fornecedor f)
    {
        var bloco = new StackPanel();
        var pedidos = PedidosDe(f).ToList();
        bloco.Children.Add(TituloSecao($"Histórico de pedidos ({pedidos.Count})"));
        if (pedidos.Count == 0)
        {
            bloco.Children.Add(new TextBlock { Text = "Nenhum pedido feito para este fornecedor ainda.", FontSize = 14, Foreground = Recurso<Brush>("Suave") });
            return bloco;
        }

        var todos = _todoHistorico.Contains(f.Id);
        foreach (var p in todos ? pedidos : pedidos.Take(5))
        {
            var linha = new DockPanel();
            var caixa = new Border
            {
                Background = Brushes.White, BorderBrush = Recurso<Brush>("Linha"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8),
                Opacity = p.Cancelado ? 0.65 : 1, Child = linha
            };

            var acoes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (p.Pendente)
            {
                acoes.Children.Add(Link("Registrar entrega", () => AbrirEntrega(p)));
                acoes.Children.Add(Link("Reenviar", () => AbrirEnvio(f, p)));
                acoes.Children.Add(Link("Cancelar", () => ConfirmarCancelamento(f, p), perigo: true));
            }
            DockPanel.SetDock(acoes, Dock.Right);
            linha.Children.Add(acoes);

            var (sit, fundo, cor) = p.Entregue ? ("Entregue", "Verde50", "Verde700")
                : p.Cancelado ? ("Cancelado", "Fundo", "Suave")
                : p.Atrasado ? ("Atrasado", "Vermelho50", "Vermelho600")
                : ("Aguardando", "Azul50", "Azul600");
            var info = new StackPanel();
            var topo = new WrapPanel();
            topo.Children.Add(new TextBlock { Text = $"Pedido de {p.Data:dd/MM/yyyy}", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            topo.Children.Add(Etiqueta(sit, fundo, cor));
            var entrega = p.Entregue ? $"Entregue em {p.EntregueEm:dd/MM/yyyy}"
                : p.Cancelado ? "Cancelado"
                : p.Previsao is null ? "Sem previsão de entrega" : $"Previsto para {p.Previsao:dd/MM/yyyy}";
            topo.Children.Add(new TextBlock { Text = entrega, FontSize = 13, Foreground = Recurso<Brush>("Suave"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            info.Children.Add(topo);
            info.Children.Add(new TextBlock { Text = p.DescreverItens(), FontSize = 14, Margin = new Thickness(0, 4, 0, 0) });
            // Na entrega, avisa o que veio diferente do pedido
            var diferentes = p.Itens.Where(i => i.QuantidadeRecebida is { } r && r != i.Quantidade)
                .Select(i => $"{i.NomeProduto}: chegou {i.QuantidadeCurta(i.QuantidadeRecebida!.Value)}").ToList();
            if (p.Entregue && diferentes.Count > 0)
                info.Children.Add(new TextBlock { Text = "Diferente do pedido · " + string.Join(" · ", diferentes), FontSize = 13, Foreground = Recurso<Brush>("Ambar700"), Margin = new Thickness(0, 2, 0, 0) });
            if (!string.IsNullOrWhiteSpace(p.Observacao))
                info.Children.Add(new TextBlock { Text = p.Observacao, FontSize = 13, Foreground = Recurso<Brush>("Suave"), Margin = new Thickness(0, 2, 0, 0) });
            linha.Children.Add(info);
            bloco.Children.Add(caixa);
        }
        if (pedidos.Count > 5)
            bloco.Children.Add(Link(todos ? "Mostrar só os últimos 5" : $"Ver os {pedidos.Count} pedidos", () =>
            {
                if (!_todoHistorico.Remove(f.Id)) _todoHistorico.Add(f.Id);
                Mostrar();
            }, margem: new Thickness(0, 0, 0, 0)));
        return bloco;
    }

    private TextBlock TituloSecao(string texto) =>
        new() { Text = texto, FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) };

    private void Alternar(long id)
    {
        if (!_abertos.Remove(id)) _abertos.Add(id);
        Mostrar();
    }

    // ------------------------------------------------------------ Sugestões de reposição

    /// <summary>
    /// Igual às sugestões do site, mas uma por fornecedor: produtos sem estoque ou abaixo do mínimo que ainda não
    /// estão em nenhum pedido pendente. "Ignorar hoje" esconde a sugestão até amanhã.
    /// </summary>
    private void MostrarSugestoes()
    {
        Sugestoes.Children.Clear();
        var hoje = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var prefs = PreferenciasLocais.Carregar();
        // Em falta, ou no ponto de pedido dos produtos com "Pedir antes de acabar" ligado
        var emFalta = _produtos.Where(p => (p.SemEstoque || p.EstoqueBaixo || (p.PedirAntes && p.AvisoAntecipado)) && !JaPedido(p)).ToList();

        foreach (var grupo in emFalta.GroupBy(FornecedorDe).Where(g => g.Key is not null).OrderBy(g => g.Key!.Nome, StringComparer.Create(PtBr, true)))
        {
            var f = grupo.Key!;
            if (prefs.SugestoesIgnoradas.TryGetValue(f.Id.ToString(CultureInfo.InvariantCulture), out var dia) && dia == hoje) continue;
            var nomes = grupo.OrderBy(Urgencia).Select(p => p.Nome).ToList();
            var texto = new TextBlock { FontSize = 14 };
            texto.Inlines.Add(Negrito(nomes[0]));
            if (nomes.Count == 2) { texto.Inlines.Add(new System.Windows.Documents.Run(" e ")); texto.Inlines.Add(Negrito(nomes[1])); }
            if (nomes.Count > 2) texto.Inlines.Add(new System.Windows.Documents.Run($" e mais {nomes.Count - 1} produtos"));
            texto.Inlines.Add(new System.Windows.Documents.Run(nomes.Count == 1 ? " precisa de reposição. Criar pedido para " : " precisam de reposição. Criar pedido para "));
            texto.Inlines.Add(Negrito(f.Nome));
            texto.Inlines.Add(new System.Windows.Documents.Run("?"));

            var acoes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            acoes.Children.Add(BotaoPequeno("Criar pedido", "IconeEnviar", "Verde700", () => AbrirPedido(f, ParaPedir(f)), primario: true));
            acoes.Children.Add(BotaoPequeno("Ignorar hoje", "IconeRelogio", "Suave", () =>
            {
                var p = PreferenciasLocais.Carregar();
                p.SugestoesIgnoradas[f.Id.ToString(CultureInfo.InvariantCulture)] = hoje;
                p.Salvar();
                _area.MostrarToast("Sugestão ignorada por hoje.");
                Mostrar();
            }));
            Sugestoes.Children.Add(CartaoSugestao(texto, acoes));
        }

        // Produtos em falta sem nenhum fornecedor: avisa para ligar
        var semFornecedor = emFalta.Where(p => FornecedorDe(p) is null).Select(p => p.Nome).ToList();
        if (semFornecedor.Count > 0 && Ativos.Any())
        {
            var texto = new TextBlock { FontSize = 14 };
            texto.Inlines.Add(new System.Windows.Documents.Run(semFornecedor.Count == 1 ? "Em falta e sem fornecedor: " : $"{semFornecedor.Count} produtos em falta sem fornecedor: "));
            texto.Inlines.Add(Negrito(string.Join(", ", semFornecedor.Take(4)) + (semFornecedor.Count > 4 ? "…" : "")));
            texto.Inlines.Add(new System.Windows.Documents.Run(". Coloque a categoria deles em “o que entrega” de um fornecedor ou escolha o fornecedor no cadastro do produto."));
            Sugestoes.Children.Add(CartaoSugestao(texto, null));
        }
    }

    private Border CartaoSugestao(TextBlock texto, FrameworkElement? acoes)
    {
        var linha = new DockPanel();
        var icone = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(9), Background = Recurso<Brush>("Ambar50"), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new ContentControl { Style = Recurso<Style>("Icone"), Tag = Recurso<Geometry>("IconeTriangulo"), Foreground = Recurso<Brush>("Ambar700"), Width = 18 }
        };
        DockPanel.SetDock(icone, Dock.Left);
        linha.Children.Add(icone);
        if (acoes is not null)
        {
            DockPanel.SetDock(acoes, _compacta ? Dock.Bottom : Dock.Right);
            if (_compacta) acoes.Margin = new Thickness(0, 10, 0, 0);
            linha.Children.Add(acoes);
        }
        var corpo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        corpo.Children.Add(new TextBlock { Text = "Sugestão de reposição", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Recurso<Brush>("Ambar700") });
        corpo.Children.Add(texto);
        linha.Children.Add(corpo);
        return new Border
        {
            Background = Recurso<Brush>("Ambar50"), BorderBrush = Recurso<Brush>("Linha"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 12), Child = linha
        };
    }

    // ------------------------------------------------------------ Janelas

    private void NovoFornecedor_Click(object sender, RoutedEventArgs e) => AbrirFornecedor(null);

    private void NovoPedido_Click(object sender, RoutedEventArgs e) => NovoPedido();

    /// <summary>Atalho "Novo pedido" do Painel (abre quando os dados chegarem).</summary>
    public void NovoPedido()
    {
        if (!_dadosProntos)
        {
            _depoisDeCarregar = () => AbrirPedido(null, []);
            return;
        }
        AbrirPedido(null, []);
    }

    private bool Pronto()
    {
        if (_servico is null || _estoque is null) { _area.MostrarToast("Sistema sem configuração do banco de dados."); return false; }
        if (AvisoErro.Visibility == Visibility.Visible) { _area.MostrarToast("Os fornecedores ainda não foram carregados."); return false; }
        return true;
    }

    private void AbrirFornecedor(Fornecedor? f)
    {
        if (!Pronto()) return;
        _area.AbrirJanela(new FornecedorJanela(_area, _servico!, f, _fornecedores, _produtos, RecarregarAsync));
    }

    private void AbrirPedido(Fornecedor? f, IReadOnlyList<Produto> itens)
    {
        if (!Pronto()) return;
        if (!Ativos.Any()) { _area.MostrarToast("Cadastre um fornecedor antes de criar pedidos."); return; }
        _area.AbrirJanela(new PedidoJanela(_area, _servico!, Ativos.ToList(), _produtos, f, itens, JaPedido,
            async (mensagem, fornecedor, texto) =>
            {
                await RecarregarAsync(mensagem);
                _area.AbrirJanela(new EnvioPedidoJanela(_area, fornecedor, texto, "Pedido criado"));
            }));
    }

    private void AbrirEnvio(Fornecedor f, Pedido p) =>
        _area.AbrirJanela(new EnvioPedidoJanela(_area, f, MensagemDoPedido(f, p, _produtos, _servico!.Usuario), "Reenviar pedido"));

    /// <summary>Mensagem de um pedido já gravado (para reenviar).</summary>
    public static string MensagemDoPedido(Fornecedor f, Pedido p, IReadOnlyList<Produto> produtos, string usuario) =>
        MensagemPedido.Montar(f, p.Itens.Select(i => new MensagemPedido.Linha(i.NomeProduto, i.Quantidade, i.PorPeso,
            produtos.FirstOrDefault(x => x.Id == i.ProdutoId)?.UnidadesPorCaixa)), p.Previsao, usuario);

    private void AbrirEntrega(Pedido p)
    {
        if (!Pronto()) return;
        _area.AbrirJanela(new EntregaJanela(_area, _servico!, p, _fornecedores.FirstOrDefault(f => f.Id == p.FornecedorId)?.Nome ?? "", _produtos, RecarregarAsync));
    }

    private void ConfirmarCancelamento(Fornecedor f, Pedido p) =>
        _area.AbrirJanela(new ConfirmacaoJanela(_area, "Cancelar pedido?",
            $"O pedido de {p.Data:dd/MM/yyyy} para {f.Nome} ({p.DescreverItens()}) fica no histórico como cancelado. " +
            "Avise o fornecedor se ele já estiver separando a mercadoria.",
            async () => { await _servico!.CancelarPedidoAsync(p.Id); await RecarregarAsync("Pedido cancelado."); },
            "Cancelar pedido", "Voltar"));

    private void ConfirmarExclusao(Fornecedor f) =>
        _area.AbrirJanela(new ConfirmacaoJanela(_area, "Excluir fornecedor?",
            $"“{f.Nome}” será removido. Os produtos ligados a ele ficam sem fornecedor principal. Esta ação não pode ser desfeita.",
            async () => { await _servico!.ExcluirAsync(f.Id); _abertos.Remove(f.Id); await RecarregarAsync($"“{f.Nome}” foi excluído."); }));

    /// <summary>Com pedidos, o fornecedor não é apagado: fica desativado e o histórico continua.</summary>
    private void ConfirmarDesativar(Fornecedor f)
    {
        var pendentes = PedidosDe(f).Count(p => p.Pendente);
        _area.AbrirJanela(new ConfirmacaoJanela(_area, "Desativar fornecedor?",
            $"“{f.Nome}” sai da lista e das sugestões, mas o histórico de pedidos continua guardado (filtro “Desativados”). " +
            (pendentes > 0 ? $"Atenção: há {Plural(pendentes, "pedido pendente", "pedidos pendentes")}. " : "") +
            "Dá para reativar depois.",
            async () => { await _servico!.AtivarAsync(f.Id, false); _abertos.Remove(f.Id); await RecarregarAsync($"“{f.Nome}” foi desativado."); },
            "Desativar"));
    }

    private async Task AtivarAsync(Fornecedor f, bool ativo)
    {
        try
        {
            await _servico!.AtivarAsync(f.Id, ativo);
            await RecarregarAsync($"“{f.Nome}” foi reativado.");
        }
        catch (ConexaoException ex)
        {
            _area.MostrarToast(ex.Message);
        }
    }

    // ------------------------------------------------------------ Filtros e busca

    private void Filtro_Click(object sender, RoutedEventArgs e)
    {
        _filtro = ((FrameworkElement)sender).Uid;
        Mostrar();
    }

    private void Busca_TextChanged(object sender, TextChangedEventArgs e)
    {
        DicaBusca.Visibility = Busca.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_carregou) Mostrar();
    }

    // ------------------------------------------------------------ Peças da tabela

    private void AjustarColunas(Grid grade)
    {
        grade.ColumnDefinitions.Clear();
        GridLength[] larguras = _compacta
            ? [new(1, GridUnitType.Star), new(0), new(0), new(0), new(112), new(32)]
            : [new(1.8, GridUnitType.Star), new(1.3, GridUnitType.Star), new(1.6, GridUnitType.Star), new(110), new(120), new(40)];
        foreach (var l in larguras)
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = l });
    }

    private void AoRedimensionar(object sender, SizeChangedEventArgs e)
    {
        var lado = Math.Max(96, Math.Min(140, (e.NewSize.Width - 4 * 12) / 4));
        foreach (var kpi in Kpis.Children.OfType<Button>())
            kpi.Width = kpi.Height = lado;
        var compacta = e.NewSize.Width < 760;
        if (compacta == _compacta) return;
        _compacta = compacta;
        // Janela estreita: os botões do topo vão para baixo do título
        DockPanel.SetDock(BotoesTopo, compacta ? Dock.Bottom : Dock.Right);
        BotoesTopo.Margin = new Thickness(0, compacta ? 16 : 0, 0, 0);
        BotoesTopo.HorizontalAlignment = compacta ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        AjustarColunas(CabecalhoTabela);
        foreach (TextBlock t in CabecalhoTabela.Children)
            t.Visibility = _compacta && Grid.GetColumn(t) is 1 or 2 or 3 ? Visibility.Collapsed : Visibility.Visible;
        if (_carregou) Mostrar();
    }

    private static void Colocar(Grid grade, UIElement elemento, int coluna)
    {
        Grid.SetColumn(elemento, coluna);
        grade.Children.Add(elemento);
    }

    private TextBlock Celula(string texto, bool suave = false) => new()
    {
        Text = texto,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = Recurso<Brush>(suave ? "Suave" : "Tinta"),
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        Margin = new Thickness(0, 0, 8, 0)
    };

    private StackPanel Detalhe(string rotulo, string valor)
    {
        var bloco = new StackPanel { Margin = new Thickness(0, 0, 32, 8) };
        bloco.Children.Add(new TextBlock { Text = rotulo, FontSize = 12, Foreground = Recurso<Brush>("Suave") });
        bloco.Children.Add(new TextBlock { Text = valor, FontSize = 15, FontWeight = FontWeights.SemiBold });
        return bloco;
    }

    private Button BotaoPequeno(string texto, string icone, string cor, Action acao, bool primario = false, bool primeiro = false)
    {
        var conteudo = new StackPanel { Orientation = Orientation.Horizontal };
        conteudo.Children.Add(new ContentControl { Style = Recurso<Style>("Icone"), Tag = Recurso<Geometry>(icone), Foreground = primario ? Brushes.White : Recurso<Brush>(cor), Width = 15, Margin = new Thickness(0, 0, 6, 0) });
        conteudo.Children.Add(new TextBlock { Text = texto });
        var botao = new Button
        {
            Style = Recurso<Style>(primario ? "BotaoPrimario" : "BotaoFantasma"), Height = 34, FontSize = 14, Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 8, 6), Content = conteudo
        };
        if (!primario) botao.Foreground = Recurso<Brush>(cor);
        botao.Click += (_, e) => { e.Handled = true; acao(); };
        return botao;
    }

    private Button Link(string texto, Action acao, bool perigo = false, Thickness? margem = null)
    {
        var botao = new Button
        {
            Content = texto,
            Style = Recurso<Style>("BotaoLink"),
            Margin = margem ?? new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        if (perigo) botao.Foreground = Recurso<Brush>("Vermelho600");
        botao.Click += (_, e) => { e.Handled = true; acao(); };
        return botao;
    }

    private static ControlTemplate BotaoSoConteudo()
    {
        var borda = new FrameworkElementFactory(typeof(Border));
        borda.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        borda.SetValue(Border.PaddingProperty, new Thickness(4));
        borda.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        return new ControlTemplate(typeof(Button)) { VisualTree = borda };
    }

    private static T? Ancestral<T>(DependencyObject? o) where T : DependencyObject
    {
        while (o is not null && o is not T)
            o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o);
        return o as T;
    }

    /// <summary>Alt+2 (fornecedores de teste) em FornecedoresView.Desenvolvimento.cs; só existe na versão de desenvolvimento.</summary>
    partial void IniciarAtalhosDesenvolvimento();

    private T Recurso<T>(string chave) => (T)FindResource(chave);

    private static bool Contem(string texto, string termo) =>
        PtBr.CompareInfo.IndexOf(texto, termo, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private static string Plural(int n, string um, string varios) => $"{n} {(n == 1 ? um : varios)}";

    private static System.Windows.Documents.Run Negrito(string texto) => new(texto) { FontWeight = FontWeights.SemiBold };
}
