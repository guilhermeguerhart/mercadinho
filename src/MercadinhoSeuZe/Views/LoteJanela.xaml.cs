using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>Janela para adicionar um lote (mercadoria que chegou) ou corrigir um lote existente.</summary>
public partial class LoteJanela : UserControl
{
    private readonly EstoqueView _estoque;
    private readonly AreaInternaView _area;
    private readonly EstoqueService _servico;
    private readonly Produto _produto;
    private readonly Lote? _lote;

    /// <param name="pelaCaixa">true quando a janela abriu pelo leitor de código de barras da caixa.</param>
    /// <param name="pedido">Pedido pendente com este produto: a janela sugere registrar a entrega dele.</param>
    public LoteJanela(EstoqueView estoque, AreaInternaView area, EstoqueService servico, Produto produto, Lote? lote, bool pelaCaixa = false,
        Pedido? pedido = null, string? fornecedor = null, Action? registrarEntrega = null)
    {
        InitializeComponent();
        _estoque = estoque;
        _area = area;
        _servico = servico;
        _produto = produto;
        _lote = lote;

        Titulo.Text = lote is null ? "Novo lote" : "Editar lote";
        Subtitulo.Text = produto.PorPeso ? $"{produto.Nome} · vendido por quilo" : produto.Nome;
        RotuloInicial.Text = produto.PorPeso ? "Peso comprado (kg)" : "Quantidade comprada";
        RotuloRestante.Text = produto.PorPeso ? "Peso restante (kg)" : "Quantidade restante";
        BotaoSalvar.Content = lote is null ? "Adicionar lote" : "Salvar alterações";
        // No lote novo, a quantidade restante é a comprada
        CampoRestante.Visibility = lote is null ? Visibility.Hidden : Visibility.Visible;

        Inicial.Text = lote is null ? "" : Formulario.Quantidade(lote.QuantidadeInicial);
        Restante.Text = lote is null ? "" : Formulario.Quantidade(lote.QuantidadeRestante);
        Compra.Text = Formulario.Data(lote?.DataCompra ?? DateOnly.FromDateTime(DateTime.Today));
        Validade.Text = Formulario.Data(lote?.Validade);
        // Lote novo de produto com validade pré-definida: a validade já vem preenchida (compra + dias)
        if (lote is null && produto.DiasValidadeSugerida is > 0)
            Formulario.PreencherValidade(Compra, Validade, () => produto.DiasValidadeSugerida, DicaValidade,
                produto.DiasValidade is null ? "validade do último lote" : "validade padrão do produto")();

        Formulario.LimparAoDigitar(Inicial, InicialErro);
        Formulario.LimparAoDigitar(Restante, RestanteErro);
        Formulario.LimparAoDigitar(Compra, CompraErro);
        Formulario.LimparAoDigitar(Validade, ValidadeErro);

        Formulario.EnterVaiParaProximo(Caixas);
        Formulario.EnterVaiParaProximo(Inicial);

        // Chegou mercadoria de um pedido: melhor registrar a entrega (dá baixa no pedido e cria os lotes de uma vez)
        if (pedido is not null && registrarEntrega is not null && lote is null)
        {
            var item = pedido.Itens.First(i => i.ProdutoId == produto.Id);
            AvisoPedidoTexto.Text = $"Há um pedido pendente com {(string.IsNullOrEmpty(fornecedor) ? "o fornecedor" : fornecedor)} " +
                                    $"(de {pedido.Data:dd/MM/yyyy}, {item.QuantidadeCurta(item.Quantidade)} de {produto.Nome}" +
                                    (pedido.Itens.Count > 1 ? $" e mais {pedido.Itens.Count - 1} {(pedido.Itens.Count == 2 ? "item" : "itens")}" : "") +
                                    "). Se a mercadoria é desse pedido, registre a entrega: o pedido fica como entregue e os lotes entram juntos.";
            BotaoEntregaPedido.Click += (_, _) => registrarEntrega();
            AvisoPedido.Visibility = Visibility.Visible;
        }

        if (pelaCaixa && lote is null)
        {
            // Cada caixa traz "unidades por caixa"; a quantidade do lote é calculada (e pode ser corrigida).
            CampoCaixas.Visibility = Visibility.Visible;
            CaixaLida.Text = $"Caixa lida: {produto.CodigoBarrasCaixa}" +
                             (produto.PorPeso ? ". O peso das caixas de carne varia: confira na etiqueta e corrija o peso comprado." : "");
            UnidadesPorCaixaTexto.Text = $"× {produto.Numero(produto.UnidadesPorCaixa)} {produto.Sigla} por caixa";
            Caixas.TextChanged += (_, _) =>
            {
                CaixasErro.Text = "";
                if (Formulario.LerInteiro(Caixas.Text) is > 0 and var n)
                    Inicial.Text = Formulario.Quantidade(n * produto.UnidadesPorCaixa);
            };
            Caixas.Text = "1";
            Loaded += (_, _) => { Caixas.Focus(); Caixas.SelectAll(); };
        }
        else
        {
            Loaded += (_, _) => Inicial.Focus();
        }
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => _area.FecharJanela();

    private async void Salvar_Click(object sender, RoutedEventArgs e)
    {
        AvisoErro.Visibility = Visibility.Collapsed;

        var kg = _produto.PorPeso;
        var inicial = Formulario.LerQuantidade(Inicial.Text, kg);
        var restante = _lote is null ? inicial : Formulario.LerQuantidade(Restante.Text, kg);
        var compra = Formulario.LerData(Compra.Text);
        var semValidade = Validade.Text.Trim().Length == 0;
        var validade = semValidade ? null : Formulario.LerData(Validade.Text);

        var erros = new List<TextBox>();
        void Checar(TextBox campo, TextBlock erro, bool ok, string msg) { if (!Formulario.Validar(campo, erro, ok, msg)) erros.Add(campo); }

        if (CampoCaixas.Visibility == Visibility.Visible)
            Checar(Caixas, CaixasErro, Formulario.LerInteiro(Caixas.Text) > 0, "Use um número inteiro maior que 0.");
        Checar(Inicial, InicialErro, inicial > 0, kg ? "Use um peso maior que 0, ex: 18,750." : "Use um número inteiro maior que 0.");
        if (_lote is not null)
            Checar(Restante, RestanteErro, restante is not null && (inicial is null || restante <= inicial),
                restante is null ? (kg ? "Use um peso em kg (0 ou mais)." : "Use um número inteiro (0 ou mais).")
                    : kg ? "Não pode passar do peso comprado." : "Não pode passar da quantidade comprada.");
        Checar(Compra, CompraErro, compra is not null && compra <= DateOnly.FromDateTime(DateTime.Today),
            compra is null ? "Use o formato dd/mm/aaaa." : "A data de compra não pode ser no futuro.");
        Checar(Validade, ValidadeErro, (semValidade || validade is not null) && (validade is null || compra is null || validade >= compra),
            !semValidade && validade is null ? "Use o formato dd/mm/aaaa." : "A validade não pode ser antes da data de compra.");

        if (erros.Count > 0)
        {
            erros[0].Focus();
            return;
        }

        BotaoSalvar.IsEnabled = false;
        try
        {
            if (_lote is null)
            {
                await _servico.CriarLoteAsync(_produto.Id, inicial!.Value, compra!.Value, validade);
                await _estoque.RecarregarAsync($"Lote de {_produto.Nome} adicionado.");
            }
            else
            {
                await _servico.AtualizarLoteAsync(_lote.Id, inicial!.Value, restante!.Value, compra!.Value, validade);
                await _estoque.RecarregarAsync($"Lote de {_produto.Nome} atualizado.");
            }
        }
        catch (ConexaoException ex)
        {
            AvisoErroTexto.Text = ex.Message;
            AvisoErro.Visibility = Visibility.Visible;
            BotaoSalvar.IsEnabled = true;
        }
    }
}
