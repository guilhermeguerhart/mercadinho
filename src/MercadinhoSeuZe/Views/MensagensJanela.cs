using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Mensagens com um fornecedor (semana 6, pedido do Guilherme): à esquerda o pedido escolhido (itens, situação e
/// previsão), à direita a conversa em balões e o campo para responder. A resposta sai por e-mail pela caixa de pedidos
/// do mercado; o que chegou pelo WhatsApp pode ser colado para ficar registrado (o WhatsApp oficial é da semana 7).
/// Abrir a janela marca as mensagens como lidas. Esc fecha.
/// </summary>
internal sealed class MensagensJanela : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly AreaInternaView _area;
    private readonly MensagensService _servico;
    private readonly Fornecedor _fornecedor;
    private readonly List<Pedido> _pedidos;
    private readonly Action _aoFechar;
    private List<MensagemFornecedor> _mensagens = [];
    private long? _pedidoId;

    private readonly StackPanel _pedido = new();
    private readonly StackPanel _escolhaPedido = new();
    private readonly StackPanel _conversa = new();
    private readonly ScrollViewer _rolagem;
    private readonly TextBox _resposta;
    private readonly TextBlock _erro;
    private readonly Button _enviar;

    public MensagensJanela(AreaInternaView area, MensagensService servico, Fornecedor fornecedor, IEnumerable<Pedido> pedidos, long? pedidoId, Action aoFechar)
    {
        _area = area;
        _servico = servico;
        _fornecedor = fornecedor;
        _pedidos = pedidos.OrderByDescending(p => p.Data).ThenByDescending(p => p.Id).ToList();
        _pedidoId = pedidoId ?? _pedidos.FirstOrDefault(p => p.Pendente)?.Id ?? _pedidos.FirstOrDefault()?.Id;
        _aoFechar = aoFechar;
        Width = 900;

        var raiz = new DockPanel();

        // Topo: nome e como falar
        var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var fechar = new Button { Content = "Fechar", Style = Estilo("BotaoFantasma"), VerticalAlignment = VerticalAlignment.Top };
        fechar.Click += (_, _) => Fechar();
        DockPanel.SetDock(fechar, Dock.Right);
        topo.Children.Add(fechar);
        var titulo = new StackPanel();
        titulo.Children.Add(new TextBlock { Text = $"Mensagens · {fornecedor.Nome}", FontSize = 20, FontWeight = FontWeights.Bold });
        titulo.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(fornecedor.Email)
                ? "Este fornecedor não tem e-mail cadastrado: as respostas por e-mail não chegam aqui. Cadastre o e-mail em Editar."
                : $"E-mail {fornecedor.Email} · as respostas chegam aqui sozinhas (a caixa de pedidos é lida a cada 5 minutos).",
            FontSize = 13, Style = Estilo("TextoSuave"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
        });
        topo.Children.Add(titulo);
        DockPanel.SetDock(topo, Dock.Top);
        raiz.Children.Add(topo);

        var grade = new Grid();
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Esquerda: o pedido (bilhete) e a escolha de outro pedido
        var esquerda = new StackPanel();
        esquerda.Children.Add(new Border
        {
            Background = Pincel("Bilhete"), BorderBrush = Pincel("BordaEtiqueta"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16, 14, 16, 14), Child = _pedido
        });
        esquerda.Children.Add(_escolhaPedido);
        grade.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 520, Content = esquerda });

        // Direita: conversa e resposta
        var direita = new DockPanel();
        var baixo = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        _resposta = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MaxHeight = 160, MaxLength = 5000, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(12, 10, 12, 10), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        System.Windows.Automation.AutomationProperties.SetName(_resposta, "Escreva a resposta");
        _resposta.TextChanged += (_, _) => _erro!.Text = "";
        baixo.Children.Add(new TextBlock { Text = "Responder", Style = Estilo("Rotulo") });
        baixo.Children.Add(_resposta);
        _erro = new TextBlock { Style = Estilo("ErroCampo") };
        baixo.Children.Add(_erro);
        var botoes = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        _enviar = new Button { Style = Estilo("BotaoPrimario"), Margin = new Thickness(0, 0, 10, 6), IsEnabled = !string.IsNullOrWhiteSpace(fornecedor.Email) };
        _enviar.Content = Rotulo("Enviar por e-mail", "IconeEnviar", Brushes.White);
        _enviar.Click += async (_, _) => await EnviarAsync();
        botoes.Children.Add(_enviar);
        var colar = new Button { Style = Estilo("BotaoFantasma"), Margin = new Thickness(0, 0, 10, 6), ToolTip = "Cole aqui o que o fornecedor respondeu pelo WhatsApp para ficar junto com o pedido" };
        colar.Content = Rotulo("Registrar resposta do WhatsApp", "IconeMensagem", Pincel("Verde700"));
        colar.Click += async (_, _) => await RegistrarWhatsappAsync();
        botoes.Children.Add(colar);
        baixo.Children.Add(botoes);
        DockPanel.SetDock(baixo, Dock.Bottom);
        direita.Children.Add(baixo);

        _rolagem = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 340, Content = _conversa };
        direita.Children.Add(new Border { Background = Pincel("Fundo"), BorderBrush = Pincel("Linha"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Child = _rolagem });
        Grid.SetColumn(direita, 2);
        grade.Children.Add(direita);
        raiz.Children.Add(grade);

        Content = new Border { Style = Estilo("Janela"), Padding = new Thickness(24, 22, 24, 20), Child = raiz };
        _conversa.Children.Add(new TextBlock { Text = "Carregando…", Style = Estilo("TextoSuave"), FontSize = 14 });
        MostrarPedido();
        Loaded += async (_, _) => { _resposta.Focus(); await CarregarAsync(); };
        Unloaded += (_, _) => _aoFechar();
    }

    private async Task CarregarAsync()
    {
        try
        {
            _mensagens = await _servico.ConversaAsync(_fornecedor.Id);
            if (_mensagens.Any(m => m.Recebida && m.LidaEm is null))
                await _servico.MarcarLidasAsync(_fornecedor.Id);
        }
        catch (ConexaoException ex)
        {
            _conversa.Children.Clear();
            _conversa.Children.Add(new TextBlock { Text = ex.Message, Foreground = Pincel("Vermelho600"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
            return;
        }
        // Sem pedido escolhido: o da última mensagem
        _pedidoId ??= _mensagens.LastOrDefault(m => m.PedidoId is not null)?.PedidoId;
        MostrarPedido();
        MostrarConversa();
    }

    // ------------------------------------------------------------ Pedido

    private Pedido? PedidoAtual => _pedidos.FirstOrDefault(p => p.Id == _pedidoId);

    private void MostrarPedido()
    {
        _pedido.Children.Clear();
        if (PedidoAtual is not { } p)
        {
            _pedido.Children.Add(new TextBlock { Text = "Sem pedido", FontSize = 16, FontWeight = FontWeights.Bold });
            _pedido.Children.Add(Suave("Este fornecedor ainda não tem pedidos. As mensagens ficam na conversa geral."));
        }
        else
        {
            var situacao = p.Atrasado ? "Atrasado" : p.Pendente ? "Pendente" : p.Entregue ? $"Entregue em {p.EntregueEm:dd/MM/yyyy}" : "Cancelado";
            _pedido.Children.Add(new TextBlock { Text = $"Pedido nº {p.Id}", FontSize = 18, FontWeight = FontWeights.ExtraBold });
            _pedido.Children.Add(new TextBlock
            {
                Text = $"{p.Data:dd/MM/yyyy} · {situacao}", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0),
                Foreground = Pincel(p.Atrasado || p.Cancelado ? "Vermelho600" : p.Pendente ? "Ambar700" : "Verde700")
            });
            if (p.Pendente && p.Previsao is { } prev) _pedido.Children.Add(Suave($"Previsão de entrega: {prev:dd/MM/yyyy}"));
            _pedido.Children.Add(new Border { Height = 1, Background = Pincel("BordaEtiqueta"), Margin = new Thickness(0, 10, 0, 8) });
            foreach (var i in p.Itens)
                _pedido.Children.Add(new TextBlock { Text = $"{i.QuantidadeCurta(i.Quantidade)} {i.NomeProduto}", FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) });
            if (p.Total is { } total) _pedido.Children.Add(new TextBlock { Text = $"Total: {total.ToString("C", PtBr)}", FontSize = 13.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) });
            if (!string.IsNullOrWhiteSpace(p.Observacao)) _pedido.Children.Add(Suave($"Obs.: {p.Observacao}"));
        }

        // Outros pedidos deste fornecedor (os 8 mais novos)
        _escolhaPedido.Children.Clear();
        if (_pedidos.Count > 1)
        {
            _escolhaPedido.Children.Add(new TextBlock { Text = "OUTROS PEDIDOS", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Pincel("Suave"), Margin = new Thickness(0, 16, 0, 6) });
            var chips = new WrapPanel();
            foreach (var outro in _pedidos.Take(8))
            {
                var nao = _mensagens.Count(m => m.PedidoId == outro.Id);
                var chip = new Button
                {
                    Content = $"nº {outro.Id} · {outro.Data:dd/MM}" + (nao > 0 ? $" · {nao} msg." : ""), Style = Estilo("Chip"), FontSize = 13,
                    MinHeight = 32, Tag = outro.Id == _pedidoId ? "ativo" : null
                };
                var id = outro.Id;
                chip.Click += (_, _) => { _pedidoId = id; MostrarPedido(); MostrarConversa(); };
                chips.Children.Add(chip);
            }
            _escolhaPedido.Children.Add(chips);
        }
    }

    // ------------------------------------------------------------ Conversa

    private void MostrarConversa()
    {
        _conversa.Children.Clear();
        if (_mensagens.Count == 0)
        {
            _conversa.Children.Add(Suave("Nenhuma mensagem ainda. Os pedidos enviados por e-mail e as respostas do fornecedor aparecem aqui."));
            return;
        }
        DateTime? dia = null;
        foreach (var m in _mensagens)
        {
            var data = m.CriadaEm.ToLocalTime().Date;
            if (data != dia)
            {
                dia = data;
                _conversa.Children.Add(new TextBlock
                {
                    Text = data == DateTime.Today ? "Hoje" : data == DateTime.Today.AddDays(-1) ? "Ontem" : data.ToString("dddd, dd/MM", PtBr),
                    FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Pincel("Suave"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6)
                });
            }
            _conversa.Children.Add(Balao(m));
        }
        _rolagem.ScrollToBottom();
    }

    /// <summary>Balão: o que o fornecedor mandou à esquerda (branco), o que o mercado mandou à direita (azul-celeste claro).</summary>
    private FrameworkElement Balao(MensagemFornecedor m)
    {
        var deOutroPedido = m.PedidoId is not null && m.PedidoId != _pedidoId;
        var painel = new StackPanel();
        var quem = m.Recebida ? _fornecedor.Nome : m.UsuarioNome ?? "Mercado";
        var meio = m.PorWhatsapp ? "WhatsApp" : "e-mail";
        var cabecalho = $"{quem} · {meio} · {m.CriadaEm.ToLocalTime():HH:mm}";
        if (m.PedidoId is { } pid) cabecalho += $" · pedido nº {pid}";
        painel.Children.Add(new TextBlock { Text = cabecalho, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Pincel("Suave"), Margin = new Thickness(0, 0, 0, 4) });
        if (!string.IsNullOrWhiteSpace(m.Assunto) && !m.PorWhatsapp)
            painel.Children.Add(new TextBlock { Text = m.Assunto, FontSize = 13, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        painel.Children.Add(new TextBox
        {
            Text = m.Corpo.Trim(), FontSize = 14, TextWrapping = TextWrapping.Wrap, IsReadOnly = true, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Padding = new Thickness(0), MinHeight = 0, Style = null, Cursor = System.Windows.Input.Cursors.IBeam
        });
        foreach (var anexo in m.Anexos)
        {
            var botao = new Button { Style = Estilo("BotaoLink"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Content = $"Anexo: {anexo.Nome}" };
            botao.ToolTip = anexo.XmlNota ? "XML da nota fiscal: baixe e use \"Importar XML da nota\" ao registrar a entrega" : "Abrir o anexo";
            var a = anexo;
            botao.Click += async (_, _) => await AbrirAnexoAsync(a);
            painel.Children.Add(botao);
        }
        if (m.Erro is not null)
            painel.Children.Add(new TextBlock { Text = $"Não foi enviado: {m.Erro}", FontSize = 12.5, Foreground = Pincel("Vermelho600"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });

        return new Border
        {
            Background = Pincel(m.Recebida ? "Cartao" : "EtiquetaClara"), BorderBrush = Pincel(m.Recebida ? "Linha" : "BordaEtiqueta"), BorderThickness = new Thickness(1),
            CornerRadius = m.Recebida ? new CornerRadius(2, 10, 10, 10) : new CornerRadius(10, 2, 10, 10),
            Padding = new Thickness(14, 10, 14, 10), Margin = m.Recebida ? new Thickness(0, 4, 70, 4) : new Thickness(70, 4, 0, 4),
            HorizontalAlignment = m.Recebida ? HorizontalAlignment.Left : HorizontalAlignment.Right, Opacity = deOutroPedido ? 0.6 : 1,
            Child = painel
        };
    }

    private async Task AbrirAnexoAsync(AnexoMensagem anexo)
    {
        try
        {
            var link = await _servico.LinkDoAnexoAsync(anexo);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is ConexaoException or System.ComponentModel.Win32Exception)
        {
            _area.MostrarToast(ex is ConexaoException ? ex.Message : "Não foi possível abrir o navegador.");
        }
    }

    // ------------------------------------------------------------ Responder

    private async Task EnviarAsync()
    {
        var texto = _resposta.Text.Trim();
        if (texto.Length == 0)
        {
            _erro.Text = "Escreva a resposta.";
            _resposta.Focus();
            return;
        }
        // Responde no mesmo assunto da última mensagem do pedido (o fornecedor vê como a mesma conversa)
        var ultima = _mensagens.LastOrDefault(m => !m.PorWhatsapp && (m.PedidoId == _pedidoId || _pedidoId is null) && m.Assunto is not null);
        var assunto = ultima?.Assunto is { } a ? (a.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? a : "Re: " + a)
            : _pedidoId is { } id ? MensagemPedido.Assunto(id, _fornecedor.Nome) : $"Mercado para {_fornecedor.Nome}";
        _enviar.IsEnabled = false;
        try
        {
            await _servico.EnviarEmailAsync("resposta", _fornecedor.Id, _pedidoId, assunto, texto);
            _resposta.Text = "";
            _area.MostrarToast($"Resposta enviada para {_fornecedor.Email}.");
            await CarregarAsync();
        }
        catch (FuncaoInexistenteException)
        {
            // Envio pelo servidor ainda não ligado: abre o e-mail no programa do computador
            if (ContatoRapido.AbrirEmail(_fornecedor, assunto, texto)) _area.MostrarToast("O envio pelo sistema ainda não está ligado. Abri o e-mail no programa do computador.");
            else _erro.Text = "O envio pelo sistema ainda não está ligado e não há programa de e-mail no computador.";
        }
        catch (ConexaoException ex)
        {
            _erro.Text = ex.Message;
        }
        finally
        {
            _enviar.IsEnabled = !string.IsNullOrWhiteSpace(_fornecedor.Email);
        }
    }

    private async Task RegistrarWhatsappAsync()
    {
        var texto = _resposta.Text.Trim();
        if (texto.Length == 0)
        {
            _erro.Text = "Cole no campo acima o que o fornecedor respondeu pelo WhatsApp.";
            _resposta.Focus();
            return;
        }
        try
        {
            await _servico.RegistrarWhatsappAsync(_fornecedor.Id, _pedidoId, texto, recebida: true);
            _resposta.Text = "";
            _area.MostrarToast("Resposta do WhatsApp registrada na conversa.");
            await CarregarAsync();
        }
        catch (ConexaoException ex)
        {
            _erro.Text = ex.Message;
        }
    }

    private void Fechar() => _area.FecharJanela();

    // ------------------------------------------------------------ Ajudantes

    private StackPanel Rotulo(string texto, string icone, Brush cor)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(new ContentControl { Style = Estilo("Icone"), Tag = Application.Current.FindResource(icone), Foreground = cor, Width = 16, Margin = new Thickness(0, 0, 8, 0) });
        p.Children.Add(new TextBlock { Text = texto });
        return p;
    }

    private TextBlock Suave(string texto) => new() { Text = texto, FontSize = 13, Style = Estilo("TextoSuave"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    private static Style Estilo(string chave) => (Style)Application.Current.FindResource(chave);

    private static Brush Pincel(string chave) => (Brush)Application.Current.FindResource(chave);
}
