using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Entrega de um pedido: a pessoa confere quanto chegou de cada item, o custo, a validade e, se veio diferente, o
/// motivo; ou importa o XML da nota fiscal para preencher sozinho. Ao registrar, o banco cria um lote no estoque
/// para cada item recebido e marca o pedido como entregue; o que faltou pode virar um pedido novo.
/// </summary>
public partial class EntregaJanela : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private sealed record Linha(ItemPedido Item, Produto? Produto, TextBox Recebido, TextBlock RecebidoErro, TextBox Custo, TextBlock CustoErro,
        TextBox Validade, TextBlock ValidadeErro, FrameworkElement CampoMotivo, ComboBox Motivo);

    private readonly AreaInternaView _area;
    private readonly FornecedorService _servico;
    private readonly Pedido _pedido;
    private readonly Fornecedor? _fornecedor;
    private readonly Func<string, Task> _aoGravar;
    private readonly List<Linha> _linhas = [];

    public EntregaJanela(AreaInternaView area, FornecedorService servico, Pedido pedido, Fornecedor? fornecedor, IReadOnlyList<Produto> produtos,
        Func<string, Task> aoGravar)
    {
        InitializeComponent();
        _area = area;
        _servico = servico;
        _pedido = pedido;
        _fornecedor = fornecedor;
        _aoGravar = aoGravar;

        Subtitulo.Text = $"{fornecedor?.Nome ?? "Fornecedor"} · pedido de {pedido.Data:dd/MM/yyyy}" + (pedido.Previsao is { } p ? $" · previsto para {p:dd/MM/yyyy}" : "");
        DataEntrega.Text = Formulario.Data(DateOnly.FromDateTime(DateTime.Today));
        Formulario.LimparAoDigitar(DataEntrega, DataEntregaErro);
        Formulario.LimparAoDigitar(PrevisaoResto, PrevisaoRestoErro);
        RestoPendente.Checked += (_, _) => CampoPrevisaoResto.Visibility = Visibility.Visible;
        RestoPendente.Unchecked += (_, _) => CampoPrevisaoResto.Visibility = Visibility.Collapsed;

        // Sem o script 005: sem nota, custo, motivo e resto pendente (como na primeira versão)
        var completo = servico.TemAjustes;
        CampoNota.Visibility = BotaoXml.Visibility = completo ? Visibility.Visible : Visibility.Collapsed;
        CabecalhoCusto.Visibility = completo ? Visibility.Visible : Visibility.Hidden;

        // Previsão do que faltar: hoje + prazo, no próximo dia de entrega do fornecedor
        var prazo = fornecedor?.Prazo ?? Fornecedor.PrazoPadrao;
        PrevisaoResto.Text = Formulario.Data(fornecedor?.PrevisaoParaPrazo(prazo) ?? DateOnly.FromDateTime(DateTime.Today).AddDays(prazo));

        foreach (var item in pedido.Itens)
            Adicionar(item, produtos.FirstOrDefault(x => x.Id == item.ProdutoId), completo);
        AtualizarResumo();

        Loaded += (_, _) =>
        {
            if (_linhas.Count == 0) return;
            _linhas[0].Recebido.Focus();
            _linhas[0].Recebido.SelectAll();
        };
    }

    private void Adicionar(ItemPedido item, Produto? produto, bool completo)
    {
        var grade = new Grid { Margin = new Thickness(0, 10, 0, 4) };
        foreach (var largura in new[] { new GridLength(1, GridUnitType.Star), new GridLength(120), new GridLength(12), new GridLength(110), new GridLength(12), new GridLength(120) })
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = largura });

        var nome = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        nome.Children.Add(new TextBlock { Text = item.NomeProduto, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        nome.Children.Add(new TextBlock
        {
            Text = produto is null ? $"Pedido: {item.QuantidadeCurta(item.Quantidade)} · produto excluído do estoque (não cria lote)" : $"Pedido: {item.QuantidadeCurta(item.Quantidade)}",
            FontSize = 12.5, Foreground = (Brush)FindResource(produto is null ? "Ambar700" : "Suave")
        });
        grade.Children.Add(nome);

        var (recebido, recebidoErro, colRecebido) = Campo(Formulario.Quantidade(item.Quantidade), $"Quantidade que chegou de {item.NomeProduto}", item.PorPeso ? "kg" : "un.");
        Grid.SetColumn(colRecebido, 1);
        grade.Children.Add(colRecebido);

        var custoInicial = item.PrecoCusto ?? produto?.CustoCom(_pedido.FornecedorId);
        var (custo, custoErro, colCusto) = Campo(custoInicial is { } c ? Formulario.Preco(c) : "", $"Custo de {item.NomeProduto}", item.PorPeso ? "/kg" : "/un");
        custo.ToolTip = item.PorPeso ? "Custo por kg" : "Custo por unidade";
        colCusto.Visibility = completo ? Visibility.Visible : Visibility.Hidden;
        Grid.SetColumn(colCusto, 3);
        grade.Children.Add(colCusto);

        var (validade, validadeErro, colValidade) = Campo("", $"Validade de {item.NomeProduto}", null);
        validade.ToolTip = "dd/mm/aaaa (vazio = não vence)";
        validade.IsEnabled = produto is not null;
        // Validade = data da entrega + validade padrão do produto, até a pessoa digitar outra
        if (produto?.DiasValidadeSugerida is > 0)
            Formulario.PreencherValidade(DataEntrega, validade, () => produto.DiasValidadeSugerida, new TextBlock())();
        Grid.SetColumn(colValidade, 5);
        grade.Children.Add(colValidade);

        // Motivo, quando chegou diferente do pedido
        var motivo = new ComboBox { Width = 200, MinHeight = 38, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (id, texto) in ItemPedido.Motivos)
            motivo.Items.Add(new ComboBoxItem { Content = texto, Tag = id });
        motivo.SelectedIndex = 0;
        System.Windows.Automation.AutomationProperties.SetName(motivo, $"Motivo da diferença de {item.NomeProduto}");
        var campoMotivo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6), Visibility = Visibility.Collapsed };
        campoMotivo.Children.Add(new TextBlock { Text = "Veio diferente do pedido. Motivo:", FontSize = 13, Foreground = (Brush)FindResource("Ambar700"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        campoMotivo.Children.Add(motivo);

        var bloco = new StackPanel();
        bloco.Children.Add(grade);
        if (completo) bloco.Children.Add(campoMotivo);
        Itens.Children.Add(new Border { BorderBrush = (Brush)FindResource("Linha"), BorderThickness = new Thickness(0, 0, 0, 1), Child = bloco, Padding = new Thickness(0, 0, 0, 6) });

        var linha = new Linha(item, produto, recebido, recebidoErro, custo, custoErro, validade, validadeErro, campoMotivo, motivo);
        _linhas.Add(linha);
        recebido.TextChanged += (_, _) => AtualizarResumo();
        custo.TextChanged += (_, _) => AtualizarResumo();
    }

    /// <summary>Campo de texto com a unidade escrita à direita e o erro embaixo.</summary>
    private (TextBox Campo, TextBlock Erro, StackPanel Coluna) Campo(string texto, string nome, string? sufixo)
    {
        var campo = new TextBox { MaxLength = 12, Text = texto, Padding = new Thickness(12, 0, sufixo is null ? 12 : 34, 0) };
        System.Windows.Automation.AutomationProperties.SetName(campo, nome);
        var erro = new TextBlock { Style = (Style)FindResource("ErroCampo") };
        Formulario.LimparAoDigitar(campo, erro);
        var grade = new Grid();
        grade.Children.Add(campo);
        if (sufixo is not null)
            grade.Children.Add(new TextBlock { Text = sufixo, Foreground = (Brush)FindResource("Suave"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), IsHitTestVisible = false });
        var coluna = new StackPanel();
        coluna.Children.Add(grade);
        coluna.Children.Add(erro);
        return (campo, erro, coluna);
    }

    /// <summary>Mostra o motivo onde chegou diferente, o "o que faltou" e o total da nota.</summary>
    private void AtualizarResumo()
    {
        if (!_servico.TemAjustes) return;
        var faltou = false;
        decimal total = 0;
        var semCusto = 0;
        foreach (var l in _linhas)
        {
            var q = Formulario.LerQuantidade(l.Recebido.Text, l.Item.PorPeso);
            l.CampoMotivo.Visibility = q is not null && q != l.Item.Quantidade ? Visibility.Visible : Visibility.Collapsed;
            if (q < l.Item.Quantidade && l.Produto is not null) faltou = true;
            if (Formulario.LerPreco(l.Custo.Text) is { } c && q is not null) total += c * q.Value;
            else if (q > 0) semCusto++;
        }
        CampoResto.Visibility = faltou ? Visibility.Visible : Visibility.Collapsed;
        Total.Text = total > 0
            ? $"Total da entrega: {total.ToString("C", PtBr)}" + (semCusto > 0 ? $" ({semCusto} {(semCusto == 1 ? "item" : "itens")} sem custo)" : "")
            : "";
    }

    // ------------------------------------------------------------ XML da nota

    private void ImportarXml_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = "XML da nota fiscal (NF-e)",
            Filter = "XML da NF-e (*.xml)|*.xml|Todos os arquivos|*.*"
        };
        if (dialogo.ShowDialog() != true) return;

        AvisoErro.Visibility = Visibility.Collapsed;
        AvisoNota.Visibility = Visibility.Collapsed;
        NotaLida nota;
        try
        {
            nota = NotaFiscalXml.Ler(dialogo.FileName);
        }
        catch (Exception ex) when (ex is FormatException or System.IO.IOException or UnauthorizedAccessException)
        {
            AvisoErroTexto.Text = "Não foi possível ler a nota: " + ex.Message;
            AvisoErro.Visibility = Visibility.Visible;
            return;
        }

        if (nota.Numero is not null) NumeroNota.Text = nota.Numero;
        var (encontrados, sobraram) = NotaFiscalXml.Ligar(nota, _linhas.Where(l => l.Produto is not null).Select(l => l.Produto!).ToList());
        var achados = 0;
        foreach (var l in _linhas.Where(l => l.Produto is not null))
        {
            if (!encontrados.TryGetValue(l.Produto!.Id, out var item))
            {
                // Não está na nota: não veio
                l.Recebido.Text = "0";
                SelecionarMotivo(l, "faltou");
                continue;
            }
            achados++;
            l.Recebido.Text = Formulario.Quantidade(item.Quantidade);
            if (item.CustoUnitario is { } custo) l.Custo.Text = Formulario.Preco(custo);
            if (item.Validade is { } validade) l.Validade.Text = Formulario.Data(validade);
            if (item.Quantidade < l.Item.Quantidade) SelecionarMotivo(l, "faltou");
        }

        var sobrou = sobraram.Select(i => i.Nome).ToList();
        var comProduto = _linhas.Count(l => l.Produto is not null);
        AvisoNotaTexto.Text = $"Nota {nota.Numero ?? "sem número"}" + (nota.Emitente is null ? "" : $" de {nota.Emitente}") +
                              $": {achados} de {comProduto} itens do pedido encontrados. Confira as quantidades antes de registrar." +
                              (sobrou.Count > 0 ? $" Na nota, mas não no pedido: {string.Join(", ", sobrou.Take(5))}{(sobrou.Count > 5 ? "…" : "")}." : "") +
                              (achados < comProduto ? " Itens não encontrados ficaram com 0 (não veio)." : "");
        AvisoNota.Visibility = Visibility.Visible;
        AtualizarResumo();
    }

    private static void SelecionarMotivo(Linha l, string motivo) =>
        l.Motivo.SelectedItem = l.Motivo.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == motivo);

    // ------------------------------------------------------------ Gravação

    private void Cancelar_Click(object sender, RoutedEventArgs e) => _area.FecharJanela();

    private async void Salvar_Click(object sender, RoutedEventArgs e)
    {
        AvisoErro.Visibility = Visibility.Collapsed;
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var entrega = Formulario.LerData(DataEntrega.Text);

        var erros = new List<TextBox>();
        if (!Formulario.Validar(DataEntrega, DataEntregaErro, entrega is not null && entrega <= hoje && entrega >= _pedido.Data,
                entrega is null ? "Use o formato dd/mm/aaaa." : entrega > hoje ? "A entrega não pode ser no futuro." : "Não pode ser antes da data do pedido."))
            erros.Add(DataEntrega);

        var itens = new List<ItemEntrega>();
        foreach (var l in _linhas)
        {
            var recebido = Formulario.LerQuantidade(l.Recebido.Text, l.Item.PorPeso);
            if (!Formulario.Validar(l.Recebido, l.RecebidoErro, recebido is not null, l.Item.PorPeso ? "Peso em kg (0 ou mais)." : "Número inteiro (0 ou mais)."))
                erros.Add(l.Recebido);

            var semCusto = l.Custo.Text.Trim().Length == 0;
            var custo = semCusto ? null : Formulario.LerPreco(l.Custo.Text);
            if (!Formulario.Validar(l.Custo, l.CustoErro, semCusto || custo is not null, "Ex: 4,50."))
                erros.Add(l.Custo);

            var semValidade = l.Validade.Text.Trim().Length == 0;
            var validade = semValidade ? null : Formulario.LerData(l.Validade.Text);
            if (!Formulario.Validar(l.Validade, l.ValidadeErro, (semValidade || validade is not null) && (validade is null || entrega is null || validade >= entrega),
                    !semValidade && validade is null ? "Use dd/mm/aaaa." : "Antes da entrega."))
                erros.Add(l.Validade);
            var motivo = recebido is not null && recebido != l.Item.Quantidade ? (l.Motivo.SelectedItem as ComboBoxItem)?.Tag as string : null;
            itens.Add(new ItemEntrega(l.Item.Id, recebido ?? 0, validade, custo, motivo));
        }

        var resto = CampoResto.Visibility == Visibility.Visible && RestoPendente.IsChecked == true;
        var previsaoResto = resto ? Formulario.LerData(PrevisaoResto.Text) : null;
        if (resto && !Formulario.Validar(PrevisaoResto, PrevisaoRestoErro, previsaoResto is not null && previsaoResto >= hoje,
                previsaoResto is null ? "Use dd/mm/aaaa." : "Não pode ser no passado."))
            erros.Add(PrevisaoResto);

        if (erros.Count > 0)
        {
            erros[0].Focus();
            return;
        }
        if (itens.All(i => i.Recebido == 0))
        {
            AvisoErroTexto.Text = "Nenhum produto chegou? Coloque a quantidade recebida ou cancele o pedido no histórico.";
            AvisoErro.Visibility = Visibility.Visible;
            return;
        }

        BotaoSalvar.IsEnabled = false;
        try
        {
            await _servico.RegistrarEntregaAsync(_pedido.Id, entrega!.Value, itens, NumeroNota.Text.Trim(), resto, previsaoResto);
            var lotes = _linhas.Zip(itens).Count(x => x.First.Produto is not null && x.Second.Recebido > 0);
            await _aoGravar($"Entrega registrada: {lotes} {(lotes == 1 ? "lote novo" : "lotes novos")} no estoque." +
                            (resto ? " O que faltou ficou num pedido novo." : ""));
        }
        catch (ConexaoException ex)
        {
            AvisoErroTexto.Text = ex.Message;
            AvisoErro.Visibility = Visibility.Visible;
            BotaoSalvar.IsEnabled = true;
        }
    }
}
