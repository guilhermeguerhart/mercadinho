using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Entrega de um pedido: a pessoa confere quanto chegou de cada item e a validade. Ao registrar, o banco cria
/// um lote no estoque para cada item recebido e marca o pedido como entregue (tudo de uma vez).
/// </summary>
public partial class EntregaJanela : UserControl
{
    private sealed record Linha(ItemPedido Item, Produto? Produto, TextBox Recebido, TextBlock RecebidoErro, TextBox Validade, TextBlock ValidadeErro);

    private readonly AreaInternaView _area;
    private readonly FornecedorService _servico;
    private readonly Pedido _pedido;
    private readonly Func<string, Task> _aoGravar;
    private readonly List<Linha> _linhas = [];

    public EntregaJanela(AreaInternaView area, FornecedorService servico, Pedido pedido, string fornecedor, IReadOnlyList<Produto> produtos,
        Func<string, Task> aoGravar)
    {
        InitializeComponent();
        _area = area;
        _servico = servico;
        _pedido = pedido;
        _aoGravar = aoGravar;

        Subtitulo.Text = $"{fornecedor} · pedido de {pedido.Data:dd/MM/yyyy}" + (pedido.Previsao is { } p ? $" · previsto para {p:dd/MM/yyyy}" : "");
        DataEntrega.Text = Formulario.Data(DateOnly.FromDateTime(DateTime.Today));
        Formulario.LimparAoDigitar(DataEntrega, DataEntregaErro);

        foreach (var item in pedido.Itens)
        {
            var produto = produtos.FirstOrDefault(x => x.Id == item.ProdutoId);
            var grade = new Grid { Margin = new Thickness(0, 10, 0, 10) };
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });

            var nome = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            nome.Children.Add(new TextBlock { Text = item.NomeProduto, FontWeight = FontWeights.SemiBold, FontSize = 14 });
            nome.Children.Add(new TextBlock
            {
                Text = produto is null ? $"Pedido: {item.QuantidadeCurta(item.Quantidade)} · produto excluído do estoque (não cria lote)" : $"Pedido: {item.QuantidadeCurta(item.Quantidade)}",
                FontSize = 12.5, Foreground = (Brush)FindResource(produto is null ? "Ambar700" : "Suave")
            });
            grade.Children.Add(nome);

            var recebido = new TextBox { MaxLength = 10, Text = Formulario.Quantidade(item.Quantidade) };
            System.Windows.Automation.AutomationProperties.SetName(recebido, $"Quantidade que chegou de {item.NomeProduto}");
            var recebidoErro = new TextBlock { Style = (Style)FindResource("ErroCampo") };
            Formulario.LimparAoDigitar(recebido, recebidoErro);
            var colRecebido = new StackPanel();
            var campo = new Grid();
            campo.Children.Add(recebido);
            campo.Children.Add(new TextBlock { Text = item.PorPeso ? "kg" : "un.", Foreground = (Brush)FindResource("Suave"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), IsHitTestVisible = false });
            colRecebido.Children.Add(campo);
            colRecebido.Children.Add(recebidoErro);
            Grid.SetColumn(colRecebido, 1);
            grade.Children.Add(colRecebido);

            var validade = new TextBox { MaxLength = 10, ToolTip = "dd/mm/aaaa (vazio = não vence)", IsEnabled = produto is not null };
            System.Windows.Automation.AutomationProperties.SetName(validade, $"Validade de {item.NomeProduto}");
            var validadeErro = new TextBlock { Style = (Style)FindResource("ErroCampo") };
            Formulario.LimparAoDigitar(validade, validadeErro);
            // Validade = data da entrega + validade padrão do produto, até a pessoa digitar outra
            if (produto?.DiasValidadeSugerida is > 0)
                Formulario.PreencherValidade(DataEntrega, validade, () => produto.DiasValidadeSugerida, new TextBlock())();
            var colValidade = new StackPanel();
            colValidade.Children.Add(validade);
            colValidade.Children.Add(validadeErro);
            Grid.SetColumn(colValidade, 3);
            grade.Children.Add(colValidade);

            Itens.Children.Add(new Border { BorderBrush = (Brush)FindResource("Linha"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grade });
            _linhas.Add(new Linha(item, produto, recebido, recebidoErro, validade, validadeErro));
        }

        Loaded += (_, _) =>
        {
            if (_linhas.Count == 0) return;
            _linhas[0].Recebido.Focus();
            _linhas[0].Recebido.SelectAll();
        };
    }

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

            var semValidade = l.Validade.Text.Trim().Length == 0;
            var validade = semValidade ? null : Formulario.LerData(l.Validade.Text);
            if (!Formulario.Validar(l.Validade, l.ValidadeErro, (semValidade || validade is not null) && (validade is null || entrega is null || validade >= entrega),
                    !semValidade && validade is null ? "Use dd/mm/aaaa." : "Antes da entrega."))
                erros.Add(l.Validade);
            itens.Add(new ItemEntrega(l.Item.Id, recebido ?? 0, validade));
        }

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
            await _servico.RegistrarEntregaAsync(_pedido.Id, entrega!.Value, itens);
            var lotes = _linhas.Zip(itens).Count(x => x.First.Produto is not null && x.Second.Recebido > 0);
            await _aoGravar($"Entrega registrada: {lotes} {(lotes == 1 ? "lote novo" : "lotes novos")} no estoque.");
        }
        catch (ConexaoException ex)
        {
            AvisoErroTexto.Text = ex.Message;
            AvisoErro.Visibility = Visibility.Visible;
            BotaoSalvar.IsEnabled = true;
        }
    }
}
