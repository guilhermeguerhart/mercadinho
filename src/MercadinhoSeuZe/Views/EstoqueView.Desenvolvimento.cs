#if DESENVOLVIMENTO
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// SÓ PARA DESENVOLVIMENTO: Alt+2 na tela de Estoque abre o leitor de código de barras simulado.
/// Escolher um produto do catálogo gera sempre um código de caixa novo (DUN-14 válido) e "passa" ele no
/// leitor, como se fosse uma caixa nova; o cadastro já vem preenchido com dados do catálogo.
/// "Caixas já cadastradas" passa o código de um produto existente (entrada de lote).
/// Só é compilado em Debug ou com "publicar.ps1 -Desenvolvimento"; o .exe normal não tem o atalho.
/// </summary>
public partial class EstoqueView
{
    /// <summary>PorCaixa: unidades por caixa, ou kg por caixa quando PorPeso (açougue).</summary>
    private sealed record ItemCatalogo(string Nome, string Categoria, decimal Preco, decimal PorCaixa, int? DiasValidade, bool PorPeso = false);

    private static readonly ItemCatalogo[] Catalogo =
    [
        // Grãos e mercearia
        new("Arroz branco 5kg", "Grãos", 24.90m, 6, 365), new("Arroz integral 1kg", "Grãos", 8.99m, 10, 300),
        new("Feijão carioca 1kg", "Grãos", 8.50m, 10, 240), new("Feijão preto 1kg", "Grãos", 9.20m, 10, 240),
        new("Lentilha 500g", "Grãos", 7.90m, 12, 365), new("Grão-de-bico 500g", "Grãos", 9.90m, 12, 365),
        new("Açúcar refinado 1kg", "Mercearia", 4.79m, 10, 720), new("Açúcar cristal 5kg", "Mercearia", 19.90m, 4, 720),
        new("Sal refinado 1kg", "Mercearia", 2.49m, 20, 1095), new("Óleo de soja 900ml", "Mercearia", 7.20m, 20, 365),
        new("Azeite extravirgem 500ml", "Mercearia", 34.90m, 12, 540), new("Café torrado 500g", "Mercearia", 18.90m, 10, 270),
        new("Café solúvel 200g", "Mercearia", 16.50m, 12, 540), new("Farinha de trigo 1kg", "Mercearia", 5.49m, 10, 180),
        new("Farinha de mandioca 500g", "Mercearia", 6.20m, 12, 240), new("Macarrão espaguete 500g", "Mercearia", 4.59m, 20, 540),
        new("Macarrão parafuso 500g", "Mercearia", 4.79m, 20, 540), new("Molho de tomate 340g", "Mercearia", 3.29m, 24, 365),
        new("Extrato de tomate 140g", "Mercearia", 2.99m, 24, 540), new("Milho verde em lata 170g", "Mercearia", 4.39m, 24, 730),
        new("Ervilha em lata 170g", "Mercearia", 4.19m, 24, 730), new("Sardinha em lata 125g", "Mercearia", 6.90m, 50, 1095),
        new("Atum ralado 170g", "Mercearia", 9.49m, 48, 1095), new("Biscoito cream cracker 400g", "Mercearia", 5.99m, 20, 240),
        new("Biscoito recheado 140g", "Mercearia", 2.99m, 48, 270), new("Achocolatado em pó 400g", "Mercearia", 8.79m, 24, 365),
        new("Fermento em pó 100g", "Mercearia", 4.49m, 24, 365), new("Maionese 500g", "Mercearia", 9.90m, 12, 180),
        new("Ketchup 400g", "Mercearia", 8.49m, 12, 300), new("Vinagre de álcool 750ml", "Mercearia", 3.19m, 12, 1095),
        new("Gelatina sabor morango 25g", "Mercearia", 1.99m, 72, 540), new("Leite condensado 395g", "Mercearia", 6.99m, 27, 300),
        new("Creme de leite 200g", "Mercearia", 3.79m, 27, 300), new("Aveia em flocos 250g", "Mercearia", 5.29m, 24, 300),
        // Laticínios e frios
        new("Leite integral 1L", "Laticínios", 5.80m, 12, 120), new("Leite desnatado 1L", "Laticínios", 5.99m, 12, 120),
        new("Iogurte natural 170g", "Laticínios", 3.49m, 24, 30), new("Iogurte de morango 900g", "Laticínios", 11.90m, 6, 35),
        new("Queijo muçarela fatiado 150g", "Laticínios", 9.90m, 20, 45), new("Requeijão cremoso 200g", "Laticínios", 8.49m, 24, 60),
        new("Manteiga com sal 200g", "Laticínios", 12.90m, 20, 120), new("Margarina 500g", "Laticínios", 8.79m, 12, 150),
        new("Presunto fatiado 200g", "Frios", 8.99m, 20, 30), new("Salsicha hot dog 500g", "Frios", 9.49m, 12, 60),
        new("Mortadela fatiada 200g", "Frios", 5.99m, 20, 30), new("Ovos brancos dúzia", "Hortifrúti", 11.90m, 30, 30),
        // Bebidas
        new("Água mineral 500ml", "Bebidas", 1.99m, 12, 365), new("Água mineral 1,5L", "Bebidas", 3.49m, 6, 365),
        new("Refrigerante cola 2L", "Bebidas", 9.99m, 6, 180), new("Refrigerante guaraná 2L", "Bebidas", 8.49m, 6, 180),
        new("Refrigerante lata 350ml", "Bebidas", 4.29m, 12, 270), new("Suco de laranja 1L", "Bebidas", 7.99m, 12, 240),
        new("Suco de uva integral 1,5L", "Bebidas", 17.90m, 6, 365), new("Cerveja lata 350ml", "Bebidas", 3.99m, 12, 180),
        new("Cerveja long neck 330ml", "Bebidas", 5.49m, 24, 180), new("Energético 250ml", "Bebidas", 8.99m, 24, 365),
        new("Chá gelado limão 1,5L", "Bebidas", 6.49m, 6, 180), new("Água de coco 1L", "Bebidas", 9.90m, 12, 240),
        // Limpeza
        new("Sabão em pó 1,6kg", "Limpeza", 21.90m, 9, null), new("Sabão líquido 3L", "Limpeza", 34.90m, 4, null),
        new("Detergente líquido 500ml", "Limpeza", 2.79m, 24, null), new("Água sanitária 2L", "Limpeza", 6.49m, 6, 180),
        new("Desinfetante lavanda 2L", "Limpeza", 9.99m, 6, null), new("Amaciante 2L", "Limpeza", 14.90m, 6, null),
        new("Esponja multiuso (3 un.)", "Limpeza", 4.99m, 40, null), new("Limpador multiuso 500ml", "Limpeza", 5.49m, 12, null),
        new("Saco de lixo 50L (10 un.)", "Limpeza", 7.90m, 30, null), new("Palha de aço (8 un.)", "Limpeza", 3.49m, 30, null),
        // Higiene
        new("Papel higiênico (12 rolos)", "Higiene", 19.90m, 4, null), new("Creme dental 90g", "Higiene", 4.99m, 72, 730),
        new("Sabonete em barra 85g", "Higiene", 2.49m, 72, 900), new("Shampoo 350ml", "Higiene", 14.90m, 12, 900),
        new("Condicionador 350ml", "Higiene", 15.90m, 12, 900), new("Desodorante aerosol 150ml", "Higiene", 13.90m, 12, 730),
        new("Escova de dentes", "Higiene", 6.99m, 72, null), new("Absorvente (8 un.)", "Higiene", 5.49m, 48, 1095),
        new("Fralda descartável M (30 un.)", "Higiene", 49.90m, 4, null), new("Papel toalha (2 rolos)", "Higiene", 6.99m, 12, null),
        // Padaria e congelados
        new("Pão de forma 500g", "Padaria", 8.49m, 12, 10), new("Bisnaguinha 300g", "Padaria", 7.99m, 12, 12),
        new("Bolo pronto de laranja 300g", "Padaria", 9.90m, 12, 15), new("Torrada 160g", "Padaria", 5.79m, 20, 180),
        new("Pizza congelada 460g", "Congelados", 18.90m, 8, 120), new("Lasanha congelada 600g", "Congelados", 19.90m, 8, 150),
        new("Hambúrguer congelado (12 un.)", "Congelados", 22.90m, 8, 180), new("Batata pré-frita 1kg", "Congelados", 16.90m, 10, 365),
        new("Sorvete 2L", "Congelados", 24.90m, 6, 365), new("Peito de frango congelado 1kg", "Congelados", 17.90m, 10, 365),
        // Açougue (vendido por quilo; preço por kg, caixa em kg)
        new("Picanha bovina", "Açougue", 79.90m, 10, 10, true), new("Alcatra bovina", "Açougue", 49.90m, 15, 10, true),
        new("Contrafilé bovino", "Açougue", 54.90m, 15, 10, true), new("Fraldinha bovina", "Açougue", 44.90m, 12, 10, true),
        new("Patinho bovino", "Açougue", 42.90m, 15, 10, true), new("Acém bovino", "Açougue", 32.90m, 20, 10, true),
        new("Costela bovina", "Açougue", 29.90m, 20, 10, true), new("Carne moída de primeira", "Açougue", 39.90m, 10, 3, true),
        new("Músculo bovino", "Açougue", 34.90m, 15, 10, true), new("Cupim bovino", "Açougue", 42.90m, 12, 10, true),
        new("Lombo suíno", "Açougue", 26.90m, 15, 10, true), new("Costelinha suína", "Açougue", 27.90m, 15, 10, true),
        new("Pernil suíno", "Açougue", 21.90m, 20, 10, true), new("Bisteca suína", "Açougue", 22.90m, 15, 8, true),
        new("Linguiça toscana", "Açougue", 21.90m, 10, 15, true), new("Linguiça calabresa", "Açougue", 29.90m, 10, 30, true),
        new("Frango inteiro", "Açougue", 12.90m, 20, 7, true), new("Coxa e sobrecoxa de frango", "Açougue", 14.90m, 20, 7, true),
        new("Filé de peito de frango", "Açougue", 24.90m, 15, 7, true), new("Asa de frango", "Açougue", 18.90m, 15, 7, true),
        new("Coração de frango", "Açougue", 29.90m, 5, 5, true), new("Filé de tilápia", "Açougue", 54.90m, 10, 5, true),
        // Pet
        new("Ração para cães 3kg", "Pet", 39.90m, 4, 365), new("Ração para gatos 1kg", "Pet", 19.90m, 10, 365),
        new("Areia sanitária 4kg", "Pet", 14.90m, 5, null),
    ];

    private static readonly Random Sorteio = new();

    partial void IniciarAtalhosDesenvolvimento()
    {
        PreviewKeyDown += (_, e) =>
        {
            var tecla = e.Key == Key.System ? e.SystemKey : e.Key;
            if (Keyboard.Modifiers == ModifierKeys.Alt && tecla is Key.D2 or Key.NumPad2)
            {
                _area.AbrirJanela(CriarLeitorSimulado());
                e.Handled = true;
            }
        };
    }

    /// <summary>Código de caixa DUN-14/GTIN-14 com dígito verificador certo, que ainda não existe no estoque.</summary>
    private string GerarCodigoCaixa()
    {
        while (true)
        {
            var digitos = "1789" + string.Concat(Enumerable.Range(0, 9).Select(_ => Sorteio.Next(10)));
            var soma = digitos.Select((c, i) => (c - '0') * (i % 2 == 0 ? 3 : 1)).Sum();
            var codigo = digitos + (10 - soma % 10) % 10;
            if (_produtos.All(p => p.CodigoBarrasCaixa != codigo)) return codigo;
        }
    }

    /// <summary>Simula o leitor: escreve o código no campo e "aperta Enter", igual ao aparelho.</summary>
    private void PassarNoLeitor(string codigo, ItemCatalogo? modelo)
    {
        _area.FecharJanela();
        var janela = ProcessarCodigo(codigo);
        if (janela is not ProdutoJanela cadastro || modelo is null) return;

        // Caixa nova: preenche o cadastro com os dados do catálogo para agilizar o teste
        cadastro.Nome.Text = modelo.Nome;
        cadastro.Categoria.Text = modelo.Categoria;
        cadastro.PorPeso.IsChecked = modelo.PorPeso;
        cadastro.DiasValidade.Text = modelo.DiasValidade?.ToString(CultureInfo.InvariantCulture) ?? "";
        cadastro.Preco.Text = modelo.Preco.ToString("N2", new CultureInfo("pt-BR"));
        cadastro.Minimo.Text = Formulario.Quantidade(modelo.PorCaixa);
        cadastro.UnidadesCaixa.Text = Formulario.Quantidade(modelo.PorCaixa);
        cadastro.Quantidade.Text = Formulario.Quantidade(SortearQuantidade(modelo, Sorteio.Next(1, 5)));
    }

    /// <summary>Carne: o peso da caixa varia um pouco (ex.: 2 caixas de 15 kg = 29,84 kg).</summary>
    private static decimal SortearQuantidade(ItemCatalogo modelo, int caixas) => modelo.PorPeso
        ? Math.Round(caixas * modelo.PorCaixa * (decimal)(0.97 + Sorteio.NextDouble() * 0.06), 3)
        : caixas * modelo.PorCaixa;


    /// <summary>Produtos do catálogo que ainda não estão no estoque (pelo nome).</summary>
    private List<ItemCatalogo> CatalogoQueFalta() => Catalogo
        .Where(c => !_produtos.Any(p => string.Equals(p.Nome.Trim(), c.Nome, StringComparison.CurrentCultureIgnoreCase)))
        .ToList();

    /// <summary>
    /// Passa 1 caixa de cada produto do catálogo que ainda falta: cadastra o produto com um código de caixa
    /// novo e cria o primeiro lote com o conteúdo de uma caixa. Grava no banco com o login atual.
    /// </summary>
    private async Task PassarTodasQueFaltamAsync(Button botao)
    {
        var faltam = CatalogoQueFalta();
        if (faltam.Count == 0 || _servico is null) return;

        botao.IsEnabled = false;
        int certos = 0;
        var erros = new List<string>();
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        foreach (var item in faltam)
        {
            botao.Content = $"Passando caixas… {certos + erros.Count + 1} de {faltam.Count}";
            var dados = new DadosProduto(item.Nome, item.Categoria, item.Preco, item.PorCaixa, null,
                GerarCodigoCaixa(), item.PorCaixa, item.PorPeso, item.DiasValidade);
            try
            {
                await _servico.CriarProdutoAsync(dados, SortearQuantidade(item, 1), hoje,
                    item.DiasValidade is { } dias ? hoje.AddDays(dias) : null);
                certos++;
                // Guarda o código para o próximo sorteio não repetir
                _produtos.Add(new Produto { Nome = item.Nome, CodigoBarrasCaixa = dados.CodigoBarrasCaixa });
            }
            catch (ConexaoException ex)
            {
                erros.Add($"{item.Nome}: {ex.Message}");
                if (erros.Count >= 3 && certos == 0) break;   // banco fora do ar ou sem permissão: para logo
            }
        }

        var mensagem = $"{certos} produtos cadastrados pelo leitor simulado.";
        if (erros.Count > 0)
            mensagem += $" {erros.Count} falharam (primeiro erro: {erros[0]})";
        await RecarregarAsync(mensagem);
    }

    private FrameworkElement CriarLeitorSimulado()
    {
        var corpo = new StackPanel();

        var cabecalho = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var fechar = new Button { Content = "×", Style = Recurso<Style>("LinkSuave"), FontSize = 24, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Top };
        fechar.Click += (_, _) => _area.FecharJanela();
        DockPanel.SetDock(fechar, Dock.Right);
        cabecalho.Children.Add(fechar);
        var titulos = new StackPanel();
        titulos.Children.Add(new TextBlock { Text = "Leitor simulado (Alt+2)", FontSize = 22, FontWeight = FontWeights.Bold });
        titulos.Children.Add(new TextBlock
        {
            Text = "Só na versão de desenvolvimento. Cada clique no catálogo gera um código de caixa novo e passa no leitor.",
            FontSize = 13, Foreground = Recurso<Brush>("Suave")
        });
        cabecalho.Children.Add(titulos);
        corpo.Children.Add(cabecalho);

        // Uma caixa de cada produto do catálogo que ainda não está no estoque
        var faltam = CatalogoQueFalta();
        var todas = new Button
        {
            Style = Recurso<Style>("BotaoPrimario"),
            Content = faltam.Count > 0
                ? $"Passar 1 caixa de cada produto que falta ({faltam.Count})"
                : "Todos os produtos do catálogo já estão no estoque",
            IsEnabled = faltam.Count > 0,
            Margin = new Thickness(0, 0, 0, 16)
        };
        todas.Click += async (_, _) => await PassarTodasQueFaltamAsync(todas);
        corpo.Children.Add(todas);

        // Caixas já cadastradas: testa a entrada de lote
        var comCodigo = _produtos.Where(p => p.CodigoBarrasCaixa is not null).OrderBy(p => p.Nome).ToList();
        if (comCodigo.Count > 0)
        {
            corpo.Children.Add(new TextBlock { Text = "Caixas já cadastradas (entrada de lote)", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Recurso<Brush>("Suave"), Margin = new Thickness(0, 0, 0, 4) });
            var existentes = new StackPanel();
            foreach (var p in comCodigo)
                existentes.Children.Add(ItemLista(p.Nome, $"{p.CodigoBarrasCaixa} · {p.Numero(p.UnidadesPorCaixa)} {p.Sigla}", () => PassarNoLeitor(p.CodigoBarrasCaixa!, null)));
            corpo.Children.Add(new ScrollViewer { Content = existentes, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 14) });
        }

        // Catálogo com busca
        corpo.Children.Add(new TextBlock { Text = $"Catálogo ({Catalogo.Length} produtos, código novo a cada clique)", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Recurso<Brush>("Suave"), Margin = new Thickness(0, 0, 0, 6) });
        var busca = new TextBox { MaxLength = 40, Margin = new Thickness(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(busca, "Filtrar catálogo");
        corpo.Children.Add(busca);
        var lista = new StackPanel();
        corpo.Children.Add(new ScrollViewer { Content = lista, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        void Preencher()
        {
            lista.Children.Clear();
            var termo = busca.Text.Trim();
            foreach (var item in Catalogo.Where(c => termo.Length == 0
                         || c.Nome.Contains(termo, StringComparison.CurrentCultureIgnoreCase)
                         || c.Categoria.Contains(termo, StringComparison.CurrentCultureIgnoreCase)))
                lista.Children.Add(ItemLista(item.Nome, $"{item.Categoria} · {Formulario.Quantidade(item.PorCaixa)} {(item.PorPeso ? "kg" : "un.")}/caixa", () => PassarNoLeitor(GerarCodigoCaixa(), item)));
        }
        busca.TextChanged += (_, _) => Preencher();
        Preencher();

        var cartao = new Border
        {
            Style = Recurso<Style>("CartaoBorda"),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24, 22, 24, 20),
            Effect = (System.Windows.Media.Effects.Effect)FindResource("SombraJanela"),
            Child = corpo
        };
        var raiz = new UserControl { Width = 520, Content = cartao };
        raiz.Loaded += (_, _) => busca.Focus();
        return raiz;
    }

    private Button ItemLista(string titulo, string detalhe, Action acao)
    {
        var linha = new DockPanel();
        var direita = new TextBlock { Text = detalhe, FontSize = 12, Foreground = Recurso<Brush>("Suave"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(direita, Dock.Right);
        linha.Children.Add(direita);
        linha.Children.Add(new TextBlock { Text = titulo, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var botao = new Button
        {
            Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 6, 8, 6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = linha
        };
        System.Windows.Automation.AutomationProperties.SetName(botao, titulo);
        botao.Click += (_, _) => acao();
        return botao;
    }
}
#endif
