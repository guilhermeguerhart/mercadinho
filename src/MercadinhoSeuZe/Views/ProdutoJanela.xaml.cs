using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>Janela de cadastro (com o primeiro lote) e de edição de produto.</summary>
public partial class ProdutoJanela : UserControl
{
    private readonly EstoqueView _estoque;
    private readonly AreaInternaView _area;
    private readonly EstoqueService _servico;
    private readonly Produto? _produto;
    private readonly IReadOnlyList<Produto> _todos;
    private readonly bool _escolheFornecedor;
    private readonly IReadOnlyList<Fornecedor> _fornecedores = [];

    public ProdutoJanela(EstoqueView estoque, AreaInternaView area, EstoqueService servico, Produto? produto, IReadOnlyList<Produto> todos,
        string? codigoLido = null, ProdutoEncontrado? encontrado = null,
        IReadOnlyList<Fornecedor>? fornecedores = null)
    {
        InitializeComponent();
        _estoque = estoque;
        _area = area;
        _servico = servico;
        _produto = produto;
        _todos = todos;

        Titulo.Text = produto is null ? "Novo produto" : "Editar produto";
        BotaoSalvar.Content = produto is null ? "Cadastrar produto" : "Salvar alterações";
        PrimeiroLote.Visibility = produto is null ? Visibility.Visible : Visibility.Collapsed;

        Nome.Text = produto?.Nome ?? "";
        Categoria.Text = produto?.Categoria ?? "";
        Preco.Text = produto is null ? "" : Formulario.Preco(produto.Preco);
        PorPeso.IsChecked = produto?.PorPeso ?? false;
        Minimo.Text = produto is null ? "" : Formulario.Quantidade(produto.EstoqueMinimo);
        Aviso.Text = produto?.EstoqueAviso is { } av ? Formulario.Quantidade(av) : "";
        CodigoCaixa.Text = produto?.CodigoBarrasCaixa ?? codigoLido ?? "";
        if (produto is null && encontrado is not null)
        {
            // Sugestão do banco aberto: a pessoa confere e completa preço, mínimo e unidades por caixa
            Nome.Text = encontrado.Nome;
            if (encontrado.Categoria is { } categoriaSugerida) Categoria.Text = categoriaSugerida;
            AvisoEncontradoTexto.Text = $"Achamos este código no {encontrado.Fonte}: nome" +
                (encontrado.Categoria is null ? "" : " e categoria") + " já preenchidos. Confira e complete o preço e o estoque.";
            AvisoEncontrado.Visibility = Visibility.Visible;
        }
        UnidadesCaixa.Text = Formulario.Quantidade(produto?.UnidadesPorCaixa ?? 1);
        Quantidade.Text = "0";
        DiasValidade.Text = produto?.DiasValidade?.ToString(CultureInfo.InvariantCulture) ?? "";
        var recalcularValidade = Formulario.PreencherValidade(Compra, Validade, () => Formulario.LerInteiro(DiasValidade.Text) is > 0 and var d ? d : null, DicaValidade);
        DiasValidade.TextChanged += (_, _) => recalcularValidade();
        Compra.Text = Formulario.Data(DateOnly.FromDateTime(DateTime.Today));

        // Igual ao datalist do site: mostra as categorias já usadas
        var categorias = todos.Select(p => p.Categoria).Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();
        CategoriasExistentes.Text = categorias.Count > 0 ? "Já usadas: " + string.Join(", ", categorias) : "";
        CategoriasExistentes.Visibility = categorias.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        Formulario.LimparAoDigitar(Nome, NomeErro);
        Formulario.LimparAoDigitar(Categoria, CategoriaErro);
        Formulario.LimparAoDigitar(Preco, PrecoErro);
        Formulario.LimparAoDigitar(Minimo, MinimoErro);
        Formulario.LimparAoDigitar(Aviso, AvisoAntecipadoErro);
        Formulario.LimparAoDigitar(CodigoCaixa, CodigoCaixaErro);
        Formulario.EnterVaiParaProximo(CodigoCaixa);
        Formulario.LimparAoDigitar(UnidadesCaixa, UnidadesCaixaErro);
        Formulario.LimparAoDigitar(Quantidade, QuantidadeErro);

        // Mostra no campo vazio qual valor será usado
        Minimo.TextChanged += (_, _) => AtualizarDica();
        Aviso.TextChanged += (_, _) => AtualizarDica();
        AtualizarRotulos();
        Formulario.LimparAoDigitar(DiasValidade, DiasValidadeErro);
        Formulario.LimparAoDigitar(Compra, CompraErro);
        Formulario.LimparAoDigitar(Validade, ValidadeErro);

        // Fornecedor principal: os ativos (e o atual, mesmo desativado)
        if (fornecedores is not null)
        {
            _escolheFornecedor = true;
            CampoFornecedor.Visibility = Visibility.Visible;
            EscolhaFornecedor.Items.Add(new ComboBoxItem { Content = "Pela categoria (nenhum escolhido)" });
            foreach (var f in fornecedores.Where(f => f.Ativo || f.Id == produto?.FornecedorId).OrderBy(f => f.Nome, StringComparer.CurrentCultureIgnoreCase))
            {
                var item = new ComboBoxItem { Content = f.Ativo ? f.Nome : f.Nome + " (desativado)", Tag = f.Id };
                EscolhaFornecedor.Items.Add(item);
                if (f.Id == produto?.FornecedorId) EscolhaFornecedor.SelectedItem = item;
            }
            if (EscolhaFornecedor.SelectedIndex < 0) EscolhaFornecedor.SelectedIndex = 0;
            _fornecedores = fornecedores;
            PrazoEntrega.Text = produto?.PrazoEntregaDias?.ToString(CultureInfo.InvariantCulture) ?? "";
            Formulario.LimparAoDigitar(PrazoEntrega, PrazoEntregaErro);
            PrazoEntrega.TextChanged += (_, _) => AtualizarDicaPrazo();
            EscolhaFornecedor.SelectionChanged += (_, _) => AtualizarDicaPrazo();
            Categoria.TextChanged += (_, _) => AtualizarDicaPrazo();
            AtualizarDicaPrazo();

            CampoPedirAntes.Visibility = Visibility.Visible;
            PedirAntes.IsChecked = produto?.PedirAntes ?? false;
            PedirAntes.Checked += (_, _) => AtualizarDicaPedirAntes();
            PedirAntes.Unchecked += (_, _) => AtualizarDicaPedirAntes();
            Minimo.TextChanged += (_, _) => AtualizarDicaPedirAntes();
            PrazoEntrega.TextChanged += (_, _) => AtualizarDicaPedirAntes();
            EscolhaFornecedor.SelectionChanged += (_, _) => AtualizarDicaPedirAntes();
            AtualizarDicaPedirAntes();
        }

        Loaded += (_, _) => Nome.Focus();
    }

    private bool EmQuilos => PorPeso.IsChecked == true;

    /// <summary>Explica o ponto de pedido: quanto sai por dia × prazo de entrega, ou por que ainda não dá para calcular.</summary>
    private void AtualizarDicaPedirAntes()
    {
        if (PedirAntes.IsChecked != true)
        {
            DicaPedirAntes.Text = "Desligado: o aviso amarelo usa o número digitado acima.";
            return;
        }
        var prazo = Formulario.LerInteiro(PrazoEntrega.Text) ?? FornecedorEscolhido()?.Prazo ?? Fornecedor.PrazoPadrao;
        var porDia = _produto?.ConsumoDiario;
        var minimo = Formulario.LerQuantidade(Minimo.Text, EmQuilos);
        if (porDia is null || minimo is null)
        {
            DicaPedirAntes.Text = "Ponto de pedido = mínimo + o que sai por dia × prazo de entrega. Ainda não há saídas registradas " +
                                  "(as vendas dão baixa a partir da semana 5); até lá vale o aviso digitado acima.";
            return;
        }
        var ponto = minimo.Value + porDia.Value * prazo;
        ponto = EmQuilos ? Math.Ceiling(ponto * 10) / 10 : Math.Ceiling(ponto);
        var un = EmQuilos ? " kg" : " un";
        DicaPedirAntes.Text = $"Sai {Formulario.Quantidade(Math.Round(porDia.Value, 1))}{un} por dia × {prazo} dias de entrega + mínimo " +
                              $"{Formulario.Quantidade(minimo.Value)} = aviso em {Formulario.Quantidade(ponto)}{un}. Recalcula sozinho conforme as vendas.";
    }

    private Fornecedor? FornecedorEscolhido()
    {
        var id = (EscolhaFornecedor.SelectedItem as ComboBoxItem)?.Tag as long?;
        return id is null
            ? _fornecedores.Where(x => x.Ativo).OrderBy(x => x.Nome, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault(x => x.ListaCategorias.Any(c => Fornecedor.MesmoTexto(c, Categoria.Text)))
            : _fornecedores.FirstOrDefault(x => x.Id == id);
    }

    /// <summary>No prazo vazio, mostra o prazo padrão do fornecedor que será usado.</summary>
    private void AtualizarDicaPrazo()
    {
        var id = (EscolhaFornecedor.SelectedItem as ComboBoxItem)?.Tag as long?;
        var f = id is null
            ? _fornecedores.Where(x => x.Ativo).OrderBy(x => x.Nome, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault(x => x.ListaCategorias.Any(c => Fornecedor.MesmoTexto(c, Categoria.Text)))
            : _fornecedores.FirstOrDefault(x => x.Id == id);
        DicaPrazoEntrega.Text = PrazoEntrega.Text.Length > 0 ? "" : $"{f?.Prazo ?? Fornecedor.PrazoPadrao} (padrão)";
    }

    /// <summary>Mostra no campo de aviso vazio qual valor será usado.</summary>
    private void AtualizarDica()
    {
        var min = Formulario.LerQuantidade(Minimo.Text, EmQuilos);
        DicaAviso.Text = Aviso.Text.Length > 0 ? ""
            : min is null ? "mínimo + 50%"
            : $"{Formulario.Quantidade(Produto.AvisoPadrao(min.Value, EmQuilos))}{(EmQuilos ? " kg" : "")} (automático)";
    }

    /// <summary>Por quilo, os campos passam a ser em kg.</summary>
    private void AtualizarRotulos()
    {
        var kg = EmQuilos;
        RotuloPreco.Text = kg ? "Preço por kg (R$)" : "Preço (R$)";
        RotuloMinimo.Text = kg ? "Estoque mínimo (kg)" : "Estoque mínimo";
        RotuloAviso.Text = kg ? "Avisar com antecedência em (kg)" : "Avisar com antecedência em";
        RotuloUnidadesCaixa.Text = kg ? "Kg por caixa" : "Unidades por caixa";
        RotuloQuantidade.Text = kg ? "Peso (kg)" : "Quantidade";
        AtualizarDica();
    }

    private void PorPeso_Mudou(object sender, RoutedEventArgs e)
    {
        PorPesoErro.Text = "";
        if (IsLoaded || Minimo is not null) AtualizarRotulos();
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => _area.FecharJanela();

    private async void Salvar_Click(object sender, RoutedEventArgs e)
    {
        AvisoErro.Visibility = Visibility.Collapsed;

        var nome = Nome.Text.Trim();
        var categoria = Categoria.Text.Trim();
        var preco = Formulario.LerPreco(Preco.Text);
        var kg = EmQuilos;
        var minimo = Formulario.LerQuantidade(Minimo.Text, kg);
        var numero = kg ? "Use um número em kg, ex: 12,5." : "Use um número inteiro (0 ou mais).";
        var duplicado = _todos.Any(p => p.Id != _produto?.Id && string.Equals(p.Nome.Trim(), nome, StringComparison.CurrentCultureIgnoreCase));

        // Campos na ordem da tela; o primeiro com erro recebe o foco
        var erros = new List<TextBox>();
        void Checar(TextBox campo, TextBlock erro, bool ok, string msg) { if (!Formulario.Validar(campo, erro, ok, msg)) erros.Add(campo); }

        Checar(Nome, NomeErro, nome.Length > 0 && !duplicado, duplicado ? "Já existe um produto com esse nome." : "Informe o nome do produto.");
        Checar(Categoria, CategoriaErro, categoria.Length > 0, "Informe a categoria.");
        Checar(Preco, PrecoErro, preco > 0, "Informe um preço válido, ex: 8,50.");
        Checar(Minimo, MinimoErro, minimo is not null, numero);

        var avisoVazio = Aviso.Text.Trim().Length == 0;
        var aviso = avisoVazio ? null : Formulario.LerQuantidade(Aviso.Text, kg);
        Checar(Aviso, AvisoAntecipadoErro, avisoVazio || (aviso is not null && (minimo is null || aviso >= minimo)),
            aviso is null ? numero + " Ou deixe vazio." : "Deve ser igual ou maior que o estoque mínimo.");

        // Voltar de quilo para unidade só se nenhum lote tiver quantidade quebrada (ex.: 2,5)
        var quebrado = !kg && _produto is not null && _produto.Lotes.Any(l => l.QuantidadeInicial % 1 != 0 || l.QuantidadeRestante % 1 != 0);
        PorPesoErro.Text = quebrado ? "Este produto tem lotes com peso quebrado; não dá para passar para unidade." : "";
        if (quebrado) erros.Add(Nome);

        var codigo = CodigoCaixa.Text.Trim();
        var codigoRepetido = codigo.Length > 0 && _todos.FirstOrDefault(p => p.Id != _produto?.Id && p.CodigoBarrasCaixa?.Trim() == codigo) is { } dono
            ? dono.Nome : null;
        Checar(CodigoCaixa, CodigoCaixaErro, codigoRepetido is null, $"Este código já é da caixa de “{codigoRepetido}”.");

        var diasVazio = DiasValidade.Text.Trim().Length == 0;
        var diasValidade = diasVazio ? null : Formulario.LerInteiro(DiasValidade.Text);
        Checar(DiasValidade, DiasValidadeErro, diasVazio || diasValidade is > 0 and <= 3650, "Use um número de dias (1 a 3650) ou deixe vazio.");

        var prazoVazio = PrazoEntrega.Text.Trim().Length == 0;
        var prazoEntrega = prazoVazio ? null : Formulario.LerInteiro(PrazoEntrega.Text);
        if (_escolheFornecedor)
            Checar(PrazoEntrega, PrazoEntregaErro, prazoVazio || prazoEntrega is >= 0 and <= 90, "Use 0 a 90 dias ou deixe vazio.");

        var unidades = Formulario.LerQuantidade(UnidadesCaixa.Text, kg);
        Checar(UnidadesCaixa, UnidadesCaixaErro, unidades > 0, kg ? "Use um peso maior que 0, ex: 20." : "Use um número inteiro maior que 0.");

        decimal? quantidade = null;
        DateOnly? compra = null, validade = null;
        if (_produto is null)
        {
            quantidade = Formulario.LerQuantidade(Quantidade.Text, kg);
            compra = Formulario.LerData(Compra.Text);
            validade = Validade.Text.Trim().Length == 0 ? null : Formulario.LerData(Validade.Text);
            var validadeOk = Validade.Text.Trim().Length == 0 || validade is not null;

            Checar(Quantidade, QuantidadeErro, quantidade is not null, numero);
            Checar(Compra, CompraErro, compra is not null && compra <= DateOnly.FromDateTime(DateTime.Today),
                compra is null ? "Use o formato dd/mm/aaaa." : "A data de compra não pode ser no futuro.");
            Checar(Validade, ValidadeErro, validadeOk && (validade is null || compra is null || validade >= compra),
                !validadeOk ? "Use o formato dd/mm/aaaa." : "A validade não pode ser antes da data de compra.");
        }

        if (erros.Count > 0)
        {
            erros[0].Focus();
            return;
        }

        var dados = new DadosProduto(nome, categoria, preco!.Value, minimo!.Value, aviso, codigo.Length > 0 ? codigo : null, unidades!.Value, kg, diasValidade,
            _escolheFornecedor ? (EscolhaFornecedor.SelectedItem as ComboBoxItem)?.Tag as long? : _produto?.FornecedorId,
            _escolheFornecedor ? prazoEntrega : _produto?.PrazoEntregaDias,
            _escolheFornecedor ? PedirAntes.IsChecked == true : _produto?.PedirAntes ?? false);
        BotaoSalvar.IsEnabled = false;
        try
        {
            if (_produto is null)
            {
                await _servico.CriarProdutoAsync(dados, quantidade!.Value, compra!.Value, validade);
                await _estoque.RecarregarAsync($"“{nome}” foi cadastrado.");
            }
            else
            {
                await _servico.AtualizarProdutoAsync(_produto.Id, dados);
                await _estoque.RecarregarAsync($"“{nome}” foi atualizado.");
            }
        }
        catch (ConexaoException ex)
        {
            AvisoErroTexto.Text = ex.Message.StartsWith("Já existe") ? "Já existe um produto com esse nome ou com esse código de barras." : ex.Message;
            AvisoErro.Visibility = Visibility.Visible;
            BotaoSalvar.IsEnabled = true;
        }
    }
}
