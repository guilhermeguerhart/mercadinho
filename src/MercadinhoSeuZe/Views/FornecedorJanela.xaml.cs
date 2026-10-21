using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>Janela de cadastro e edição de fornecedor, com a mensagem de pedido dele.</summary>
public partial class FornecedorJanela : UserControl
{
    private readonly AreaInternaView _area;
    private readonly FornecedorService _servico;
    private readonly Fornecedor? _fornecedor;
    private readonly IReadOnlyList<Fornecedor> _todos;
    private readonly Func<string, Task> _aoGravar;

    public FornecedorJanela(AreaInternaView area, FornecedorService servico, Fornecedor? fornecedor, IReadOnlyList<Fornecedor> todos,
        IReadOnlyList<Produto> produtos, Func<string, Task> aoGravar)
    {
        InitializeComponent();
        _area = area;
        _servico = servico;
        _fornecedor = fornecedor;
        _todos = todos;
        _aoGravar = aoGravar;

        Titulo.Text = fornecedor is null ? "Novo fornecedor" : "Editar fornecedor";
        BotaoSalvar.Content = fornecedor is null ? "Cadastrar fornecedor" : "Salvar alterações";

        Nome.Text = fornecedor?.Nome ?? "";
        Telefone.Text = fornecedor?.Telefone ?? "";
        Whatsapp.Text = fornecedor?.Whatsapp ?? "";
        Email.Text = fornecedor?.Email ?? "";
        Contato.Text = fornecedor?.Contato ?? "";
        Categorias.Text = fornecedor?.Categorias ?? "";
        Prazo.Text = fornecedor?.PrazoEntregaDias?.ToString(CultureInfo.InvariantCulture) ?? "";
        Modelo.Text = fornecedor?.Modelo ?? MensagemPedido.ModeloPadrao;
        Observacao.Text = fornecedor?.Observacao ?? "";
        Envio.SelectedIndex = fornecedor?.EnvioPreferido == "email" ? 1 : 0;
        Envio.SelectionChanged += (_, _) => { EnvioErro.Text = ""; Email.Tag = null; EmailErro.Text = ""; };
        CamposModelo.Text = "Campos que o programa troca: " + MensagemPedido.Campos;

        // Máscara igual à do site: (11) 4002-1122
        Mascarar(Telefone);
        Mascarar(Whatsapp);
        Whatsapp.TextChanged += (_, _) => DicaWhatsapp.Visibility = Whatsapp.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        DicaWhatsapp.Visibility = Whatsapp.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Prazo.TextChanged += (_, _) => DicaPrazo.Visibility = Prazo.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        DicaPrazo.Visibility = Prazo.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Modelo.TextChanged += (_, _) => BotaoModeloPadrao.Visibility = Modelo.Text == MensagemPedido.ModeloPadrao ? Visibility.Collapsed : Visibility.Visible;
        BotaoModeloPadrao.Visibility = Modelo.Text == MensagemPedido.ModeloPadrao ? Visibility.Collapsed : Visibility.Visible;

        // Categorias do estoque para clicar e adicionar (como o datalist do site)
        var categorias = produtos.Select(p => p.Categoria.Trim()).Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var c in categorias)
        {
            var pilula = CategoriaVisual.Pilula(this, c, 12);
            var botao = new Button
            {
                Content = pilula, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 0, 6, 6),
                ToolTip = $"Adicionar “{c}”", Template = SoConteudo()
            };
            System.Windows.Automation.AutomationProperties.SetName(botao, $"Adicionar categoria {c}");
            botao.Click += (_, _) => AdicionarCategoria(c);
            CategoriasExistentes.Children.Add(botao);
        }

        Formulario.LimparAoDigitar(Nome, NomeErro);
        Formulario.LimparAoDigitar(Telefone, TelefoneErro);
        Formulario.LimparAoDigitar(Whatsapp, WhatsappErro);
        Formulario.LimparAoDigitar(Email, EmailErro);
        Formulario.LimparAoDigitar(Contato, ContatoErro);
        Formulario.LimparAoDigitar(Categorias, CategoriasErro);
        Formulario.LimparAoDigitar(Prazo, PrazoErro);
        Formulario.LimparAoDigitar(Modelo, ModeloErro);

        Loaded += (_, _) => Nome.Focus();
    }

    private static void Mascarar(TextBox campo) =>
        campo.TextChanged += (_, _) =>
        {
            var formatado = Fornecedor.MascaraTelefone(campo.Text);
            if (formatado == campo.Text) return;
            campo.Text = formatado;
            campo.CaretIndex = formatado.Length;
        };

    private void AdicionarCategoria(string categoria)
    {
        var atuais = Categorias.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (atuais.Any(c => Fornecedor.MesmoTexto(c, categoria))) return;
        atuais.Add(categoria);
        Categorias.Text = string.Join(", ", atuais);
        Categorias.CaretIndex = Categorias.Text.Length;
    }

    private void ModeloPadrao_Click(object sender, RoutedEventArgs e) => Modelo.Text = MensagemPedido.ModeloPadrao;

    private void Cancelar_Click(object sender, RoutedEventArgs e) => _area.FecharJanela();

    private async void Salvar_Click(object sender, RoutedEventArgs e)
    {
        AvisoErro.Visibility = Visibility.Collapsed;

        var nome = Nome.Text.Trim();
        var telefone = Telefone.Text.Trim();
        var whatsapp = Whatsapp.Text.Trim();
        var email = Email.Text.Trim();
        var contato = Contato.Text.Trim();
        var categorias = string.Join(", ", Categorias.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var prazoVazio = Prazo.Text.Trim().Length == 0;
        var prazo = prazoVazio ? null : Formulario.LerInteiro(Prazo.Text);
        var modelo = Modelo.Text.Trim();
        var duplicado = _todos.Any(f => f.Id != _fornecedor?.Id && Fornecedor.MesmoTexto(f.Nome, nome));

        var erros = new List<TextBox>();
        void Checar(TextBox campo, TextBlock erro, bool ok, string msg) { if (!Formulario.Validar(campo, erro, ok, msg)) erros.Add(campo); }

        Checar(Nome, NomeErro, nome.Length > 0 && !duplicado, duplicado ? "Já existe um fornecedor com esse nome." : "Informe o nome do fornecedor.");
        Checar(Telefone, TelefoneErro, Fornecedor.SoDigitos(telefone).Length >= 10, "Informe o telefone com DDD.");
        Checar(Whatsapp, WhatsappErro, whatsapp.Length == 0 || Fornecedor.SoDigitos(whatsapp).Length >= 10, "Informe o WhatsApp com DDD ou deixe vazio.");
        var porEmail = (Envio.SelectedItem as ComboBoxItem)?.Tag as string == "email";
        Checar(Email, EmailErro, (email.Length == 0 && !porEmail) || System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"),
            email.Length == 0 ? "Informe o e-mail para enviar os pedidos por e-mail." : "E-mail inválido.");
        Checar(Contato, ContatoErro, true, "");
        Checar(Categorias, CategoriasErro, categorias.Length > 0, "Informe o que esse fornecedor entrega.");
        Checar(Prazo, PrazoErro, prazoVazio || prazo is >= 0 and <= 90, "Use um número de dias (0 a 90) ou deixe vazio.");
        Checar(Modelo, ModeloErro, modelo.Contains("{itens}"), "A mensagem precisa ter {itens}, para a lista de produtos entrar nela.");

        if (erros.Count > 0)
        {
            erros[0].Focus();
            return;
        }

        var dados = new DadosFornecedor(nome, telefone, whatsapp.Length > 0 ? whatsapp : null, email.Length > 0 ? email : null,
            contato.Length > 0 ? contato : null, categorias, prazo,
            modelo == MensagemPedido.ModeloPadrao.Trim() ? null : modelo,
            Observacao.Text.Trim() is { Length: > 0 } obs ? obs : null, porEmail ? "email" : "whatsapp");
        BotaoSalvar.IsEnabled = false;
        try
        {
            if (_fornecedor is null)
            {
                await _servico.CriarAsync(dados);
                await _aoGravar($"“{nome}” foi cadastrado.");
            }
            else
            {
                await _servico.AtualizarAsync(_fornecedor.Id, dados);
                await _aoGravar($"“{nome}” foi atualizado.");
            }
        }
        catch (ConexaoException ex)
        {
            AvisoErroTexto.Text = ex.Message.StartsWith("Já existe") ? "Já existe um fornecedor com esse nome." : ex.Message;
            AvisoErro.Visibility = Visibility.Visible;
            BotaoSalvar.IsEnabled = true;
        }
    }

    private static ControlTemplate SoConteudo()
    {
        var borda = new FrameworkElementFactory(typeof(Border));
        borda.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        borda.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        return new ControlTemplate(typeof(Button)) { VisualTree = borda };
    }
}
