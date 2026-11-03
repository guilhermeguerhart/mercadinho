using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Fechamento do dia (semana 6, só o gerente). Aba "Resumo do dia": indicadores, caixas do dia (o gerente fecha daqui o
/// caixa de outra pessoa), formas de pagamento, mais vendidos, vendas por hora, cancelamentos e o cartão "Fechar o dia",
/// que grava a foto do dia (fechar_dia) e manda o resumo por e-mail. Aba "Análises": vendas por dia, semana e mês.
/// Com "Ocultar vendas" ligado, os valores aparecem como R$ ••••• (ValoresVenda).
/// </summary>
public partial class FechamentoView : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly AreaInternaView _area;
    private readonly FechamentoService? _servico;
    private readonly CaixaService? _caixa;

    private DateTime _dia = DateTime.Today;
    private ResumoDoDia? _resumo;
    private ConfigMercado? _config;
    private string _agrupar = "dia";
    private AnaliseVendas? _analise;
    private bool _abaAnalises;
    private bool _estreita;

    public FechamentoView(AreaInternaView area, FechamentoService? servico, CaixaService? caixa)
    {
        InitializeComponent();
        _area = area;
        _servico = servico;
        _caixa = caixa;
        ChaveOcultar.IsChecked = ValoresVenda.Ocultos;
        Loaded += async (_, _) =>
        {
            ValoresVenda.Mudou += Redesenhar;
            await CarregarDiaAsync();
        };
        Unloaded += (_, _) => ValoresVenda.Mudou -= Redesenhar;
    }

    // ------------------------------------------------------------ Dados

    private async Task CarregarDiaAsync()
    {
        AvisoErro.Visibility = Visibility.Collapsed;
        if (_servico is null)
        {
            MostrarErro("Sistema sem configuração do banco de dados. Veja o README.");
            return;
        }
        DiaTexto.Text = NomeDoDia(_dia);
        BotaoProximo.IsEnabled = _dia < DateTime.Today;
        BotaoHoje.Visibility = _dia == DateTime.Today ? Visibility.Collapsed : Visibility.Visible;
        try
        {
            var resumo = _servico.ResumoAsync(_dia);
            var config = _config is null ? _servico.ConfigAsync() : Task.FromResult(_config);
            await Task.WhenAll(resumo, config);
            _resumo = resumo.Result;
            _config = config.Result;
        }
        catch (TabelaInexistenteException)
        {
            MostrarErro("As tabelas do fechamento ainda não foram criadas no banco. Rode o script database/009_semana6_fechamento_mensagens.sql no Supabase.");
            return;
        }
        catch (ConexaoException ex)
        {
            MostrarErro(ex.Message);
            return;
        }
        MostrarDia();
    }

    private async Task CarregarAnaliseAsync()
    {
        if (_servico is null) return;
        AvisoErro.Visibility = Visibility.Collapsed;
        var hoje = DateTime.Today;
        var (inicio, fim) = _agrupar switch
        {
            "semana" => (InicioDaSemana(hoje).AddDays(-7 * 11), hoje),
            "mes" => (new DateTime(hoje.Year, hoje.Month, 1).AddMonths(-11), hoje),
            _ => (hoje.AddDays(-29), hoje),
        };
        try
        {
            _analise = await _servico.AnaliseAsync(inicio, fim, _agrupar);
        }
        catch (TabelaInexistenteException)
        {
            MostrarErro("As análises precisam do script database/009_semana6_fechamento_mensagens.sql rodado no Supabase.");
            return;
        }
        catch (ConexaoException ex)
        {
            MostrarErro(ex.Message);
            return;
        }
        MostrarAnalise();
    }

    private void MostrarErro(string mensagem)
    {
        AvisoErroTexto.Text = mensagem;
        AvisoErro.Visibility = Visibility.Visible;
        Subtitulo.Text = "Não foi possível carregar o fechamento.";
    }

    private async void TentarDeNovo_Click(object sender, RoutedEventArgs e)
    {
        if (_abaAnalises) await CarregarAnaliseAsync();
        else await CarregarDiaAsync();
    }

    private void Redesenhar()
    {
        ChaveOcultar.IsChecked = ValoresVenda.Ocultos;
        if (_resumo is not null) MostrarDia();
        if (_analise is not null) MostrarAnalise();
    }

    private void ChaveOcultar_Click(object sender, RoutedEventArgs e) => ValoresVenda.Definir(ChaveOcultar.IsChecked == true);

    // ------------------------------------------------------------ Navegação

    private async void Aba_Click(object sender, RoutedEventArgs e)
    {
        _abaAnalises = ((Button)sender).Uid == "analises";
        AbaBotaoDia.Tag = _abaAnalises ? null : "ativo";
        AbaBotaoAnalises.Tag = _abaAnalises ? "ativo" : null;
        AbaDia.Visibility = _abaAnalises ? Visibility.Collapsed : Visibility.Visible;
        AbaAnalises.Visibility = _abaAnalises ? Visibility.Visible : Visibility.Collapsed;
        if (_abaAnalises && _analise is null) await CarregarAnaliseAsync();
    }

    private async void Periodo_Click(object sender, RoutedEventArgs e)
    {
        _agrupar = ((Button)sender).Uid;
        foreach (var b in Periodos.Children.OfType<Button>()) b.Tag = b.Uid == _agrupar ? "ativo" : null;
        await CarregarAnaliseAsync();
    }

    private async void DiaAnterior_Click(object sender, RoutedEventArgs e)
    {
        _dia = _dia.AddDays(-1);
        await CarregarDiaAsync();
    }

    private async void DiaProximo_Click(object sender, RoutedEventArgs e)
    {
        if (_dia >= DateTime.Today) return;
        _dia = _dia.AddDays(1);
        await CarregarDiaAsync();
    }

    /// <summary>Escolher o dia num calendário (até hoje).</summary>
    private void Calendario_Click(object sender, RoutedEventArgs e)
    {
        var j = new JanelaSimples(_area, "Escolher o dia", "Clique no dia para ver o fechamento. Dias que já passaram só mudam com a senha do gerente.", 400);
        var calendario = new System.Windows.Controls.Calendar
        {
            SelectedDate = _dia, DisplayDate = _dia, DisplayDateEnd = DateTime.Today, FirstDayOfWeek = DayOfWeek.Sunday,
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("pt-BR")
        };
        calendario.SelectedDatesChanged += async (_, _) =>
        {
            if (calendario.SelectedDate is not { } escolhido || escolhido.Date == _dia) return;
            _dia = escolhido.Date;
            j.Fechar();
            await CarregarDiaAsync();
        };
        // O calendário do WPF é pequeno: a Viewbox aumenta sem mudar o desenho
        j.Corpo.Children.Add(new Viewbox { Width = 340, HorizontalAlignment = HorizontalAlignment.Center, Child = calendario });
        j.Botao("Hoje", false, async () => { j.Fechar(); _dia = DateTime.Today; await CarregarDiaAsync(); });
        j.Botao("Fechar", true, j.Fechar);
        _area.AbrirJanela(j);
        j.Loaded += (_, _) => calendario.Focus();
    }

    /// <summary>
    /// Dia que já passou só muda com a senha do gerente (script 012), mesmo para o gerente logado. Hoje passa direto.
    /// Devolve se pode seguir e o id da autorização.
    /// </summary>
    private async Task<(bool Ok, long? Id)> AutorizarDiaPassadoAsync(string acao)
    {
        if (_dia >= DateTime.Today) return (true, null);
        if (_caixa is null) return (false, null);
        var janela = new AutorizacaoJanela(_area, _caixa, "alterar_dia", null,
            $"{acao} em {_dia:dd/MM/yyyy}, um dia que já passou. Digite o usuário e a senha de um gerente para confirmar.");
        _area.AbrirJanela(janela);
        var id = await janela.Resultado;
        return (id is not null, id);
    }

    private async void Hoje_Click(object sender, RoutedEventArgs e)
    {
        _dia = DateTime.Today;
        await CarregarDiaAsync();
    }

    private void AoRedimensionar(object sender, SizeChangedEventArgs e)
    {
        var estreita = e.NewSize.Width < 900;
        if (estreita == _estreita) return;
        _estreita = estreita;
        Redesenhar();
    }

    // ------------------------------------------------------------ Resumo do dia

    private void MostrarDia()
    {
        var r = _resumo!;
        var hoje = _dia == DateTime.Today;
        DiaTexto.Text = NomeDoDia(_dia);
        var abertos = r.Caixas.Count(c => c.FechadoEm is null);
        Subtitulo.Text = r.Caixas.Count == 0 ? "Nenhum caixa foi aberto neste dia."
            : $"{Plural(r.Caixas.Count, "caixa", "caixas")} no dia · {(abertos == 0 ? "todos fechados" : Plural(abertos, "aberto", "abertos"))}";

        // Situação do dia
        var (texto, fundo, cor) = r.Fechado ? ($"Dia fechado por {r.Fechamento!.FechadoPor} às {r.Fechamento.FechadoEm.ToLocalTime():HH:mm}", "Verde50", "Verde700")
            : r.Fechamento is { Automatico: true } ? ("Resumo automático enviado; falta fechar", "Ambar50", "Ambar700")
            : hoje ? ("Dia em andamento", "Azul50", "Azul600")
            : ("Dia não fechado", "Ambar50", "Ambar700");
        SituacaoTexto.Text = texto;
        Situacao.Background = Pincel(fundo);
        SituacaoTexto.Foreground = Pincel(cor);

        // Indicadores
        KpisDia.Children.Clear();
        var anterior = r.SemanaAnterior;
        var variacao = anterior is { Total: > 0 } ? (r.Total - anterior.Total) / anterior.Total : (decimal?)null;
        KpisDia.Children.Add(Kpi("IconeCarteira", "Verde700", ValoresVenda.Moeda(r.Total), "Vendido",
            variacao is { } v && !ValoresVenda.Ocultos ? $"{Sinal(v)} sobre {NomeCurto(anterior!.Dia ?? _dia.AddDays(-7))} ({Moeda(anterior.Total)})" : "Vendas pagas do dia"));
        KpisDia.Children.Add(Kpi("IconeCaixaRegistradora", "Verde700", ValoresVenda.Numero(r.Vendas), "Vendas", $"{r.ItensVendidos} itens vendidos"));
        KpisDia.Children.Add(Kpi("IconeTendencia", "Verde700", ValoresVenda.Moeda(r.TicketMedio), "Ticket médio", "Valor médio por venda"));
        KpisDia.Children.Add(Kpi("IconeEtiqueta", "Ambar700", ValoresVenda.Moeda(r.Descontos + r.Promocoes), "Descontos",
            $"Promoções {Moeda(r.Promocoes)} · na venda {Moeda(r.Descontos)}"));
        if (!_estreita)
            KpisDia.Children.Add(Kpi("IconeTendencia", "Verde700", r.Margem is { } m && !ValoresVenda.Ocultos ? m.ToString("P0", PtBr) : ValoresVenda.Ocultos ? "•••" : "—", "Margem",
                r.Margem is null ? "Cadastre o custo nos lotes para ver a margem" : $"Lucro {Moeda(r.Lucro)} sobre {Moeda(r.VendidoComCusto)} com custo"));
        var d = r.DiferencaCaixas;
        KpisDia.Children.Add(Kpi("IconeAlerta", d < 0 ? "Vermelho600" : d > 0 ? "Ambar700" : "Verde700",
            r.Caixas.All(c => c.Diferenca is null) ? "—" : d == 0 ? "Bateu" : (d > 0 ? "+" : "−") + Moeda(Math.Abs(d)),
            "Gaveta", "Sobra (+) ou falta (−) somada dos caixas fechados"));
        KpisDia.Columns = KpisDia.Children.Count;

        ConteudoDia.Children.Clear();
        ConteudoDia.Children.Add(DuasColunas(CartaoCaixas(r), CartaoFormas(r.Formas, r.Total, "Formas de pagamento")));
        ConteudoDia.Children.Add(DuasColunas(CartaoMaisVendidos(r.MaisVendidos, "Mais vendidos do dia"), CartaoPorHora(r)));
        if (r.ItensCancelados.Count > 0 || r.Canceladas > 0) ConteudoDia.Children.Add(CartaoCancelamentos(r));
        ConteudoDia.Children.Add(CartaoFechar(r));
    }

    private FrameworkElement CartaoCaixas(ResumoDoDia r)
    {
        var lista = new StackPanel();
        if (r.Caixas.Count == 0)
            lista.Children.Add(Suave("Nenhum caixa foi aberto neste dia."));
        foreach (var c in r.Caixas)
        {
            var linha = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
            var direita = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var total = new TextBlock { Text = ValoresVenda.Moeda(c.Total), FontSize = 15, FontWeight = FontWeights.Bold };
            direita.Children.Add(new Border { Style = Estilo("EtiquetaPreco"), HorizontalAlignment = HorizontalAlignment.Right, Child = total });
            if (c.FechadoEm is null)
            {
                var fechar = new Button { Content = "Fechar este caixa", Style = Estilo("BotaoLink"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
                System.Windows.Automation.AutomationProperties.SetName(fechar, $"Fechar o caixa de {c.OperadorNome}");
                var caixa = c;
                fechar.Click += async (_, _) => await FecharCaixaAsync(caixa);
                direita.Children.Add(fechar);
            }
            DockPanel.SetDock(direita, Dock.Right);
            linha.Children.Add(direita);

            var texto = new StackPanel();
            texto.Children.Add(new TextBlock { Text = c.OperadorNome, FontSize = 15, FontWeight = FontWeights.SemiBold });
            texto.Children.Add(Suave(c.FechadoEm is null
                ? $"Aberto às {c.HoraAbertura} · ainda não fechou · {Plural(c.Vendas, "venda", "vendas")}"
                : $"{c.HoraAbertura} às {c.HoraFechamento} · {Plural(c.Vendas, "venda", "vendas")}" + (c.FechadoPor is { } por && por != c.OperadorNome ? $" · fechado por {por}" : "")));
            var gaveta = $"Gaveta: troco {Moeda(c.TrocoInicial)}";
            if (c.Sangrias > 0) gaveta += $" · sangrias {Moeda(c.Sangrias)}";
            if (c.Suprimentos > 0) gaveta += $" · suprimentos {Moeda(c.Suprimentos)}";
            gaveta += c.Contado is { } contado ? $" · esperado {Moeda(c.Esperado)}, contado {Moeda(contado)}" : $" · na gaveta agora {Moeda(c.Esperado)}";
            texto.Children.Add(Suave(gaveta));
            if (c.Diferenca is { } dif)
            {
                var (rotulo, fundo, cor) = dif == 0 ? ("Bateu", "Verde50", "Verde700") : dif > 0 ? ($"Sobrou {Moeda(dif)}", "Ambar50", "Ambar700") : ($"Faltou {Moeda(-dif)}", "Vermelho50", "Vermelho600");
                var etiqueta = new Border
                {
                    Background = Pincel(fundo), CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left, ToolTip = c.Obs is null ? null : $"Observação: {c.Obs}",
                    Child = new TextBlock { Text = rotulo + (c.Obs is null ? "" : $" · “{c.Obs}”"), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Pincel(cor), TextTrimming = TextTrimming.CharacterEllipsis }
                };
                texto.Children.Add(etiqueta);
            }
            linha.Children.Add(texto);
            if (lista.Children.Count > 0) lista.Children.Add(new Border { Height = 1, Background = Pincel("Linha") });
            lista.Children.Add(linha);
        }
        return Cartao("Caixas do dia", lista);
    }

    /// <summary>O gerente fecha o caixa de outra pessoa que foi embora sem fechar (mesma janela do Caixa).</summary>
    private async Task FecharCaixaAsync(CaixaDoDia c)
    {
        if (_caixa is null) return;
        ResumoCaixa resumo;
        try
        {
            resumo = await _caixa.ResumoAsync(c.CaixaId);
        }
        catch (ConexaoException ex)
        {
            _area.MostrarToast(ex.Message);
            return;
        }
        _area.AbrirJanela(CaixaJanelas.Fechamento(_area, resumo, async (contado, obs) =>
        {
            long? autorizacao = null;
            if (_dia < DateTime.Today)
            {
                _area.FecharJanela();
                var (ok, id) = await AutorizarDiaPassadoAsync($"Fechar o caixa de {c.OperadorNome}");
                if (!ok) return;
                autorizacao = id;
            }
            var r = await _caixa.FecharAsync(c.CaixaId, contado, obs, autorizacao);
            _area.FecharJanela();
            var d = r.Diferenca ?? 0;
            _area.MostrarToast(d == 0 ? $"Caixa de {c.OperadorNome} fechado. A gaveta bateu."
                : d > 0 ? $"Caixa de {c.OperadorNome} fechado com sobra de {Moeda(d)}." : $"Caixa de {c.OperadorNome} fechado com falta de {Moeda(-d)}.");
            await CarregarDiaAsync();
        }));
    }

    private FrameworkElement CartaoFormas(Dictionary<string, decimal> formas, decimal total, string titulo)
    {
        var lista = new StackPanel();
        if (formas.Count == 0) lista.Children.Add(Suave("Nenhum pagamento recebido."));
        var maximo = formas.Count == 0 ? 0 : formas.Values.Max();
        foreach (var (forma, valor) in formas.OrderByDescending(f => f.Value))
            lista.Children.Add(Graficos.Barra(FormaPagamento.Nome(forma), valor, maximo,
                ValoresVenda.Ocultos ? ValoresVenda.Moeda(valor) : $"{Moeda(valor)} · {(total > 0 ? valor / total : 0):P0}"));
        return Cartao(titulo, lista);
    }

    private FrameworkElement CartaoMaisVendidos(List<ProdutoVendido> produtos, string titulo)
    {
        var lista = new StackPanel();
        if (produtos.Count == 0) lista.Children.Add(Suave("Nenhum produto vendido."));
        var maximo = produtos.Count == 0 ? 0 : produtos.Max(p => p.Valor);
        var n = 0;
        foreach (var p in produtos)
            lista.Children.Add(Graficos.Barra($"{++n}. {p.Nome}", p.Valor, maximo,
                ValoresVenda.Ocultos ? p.QuantidadeTexto : $"{p.QuantidadeTexto} · {Moeda(p.Valor)}", "Verde600", 190));
        return Cartao(titulo, lista);
    }

    private FrameworkElement CartaoPorHora(ResumoDoDia r)
    {
        var conteudo = new StackPanel();
        if (r.PorHora.Count == 0)
        {
            conteudo.Children.Add(Suave("Nenhuma venda paga neste dia."));
            return Cartao("Vendas por hora", conteudo);
        }
        // Do primeiro ao último horário com venda, sem pular horas vazias
        var de = r.PorHora.Min(h => h.Hora);
        var ate = r.PorHora.Max(h => h.Hora);
        var pontos = Enumerable.Range(de, ate - de + 1).Select(h =>
        {
            var hora = r.PorHora.FirstOrDefault(x => x.Hora == h);
            var total = hora?.Total ?? 0;
            return ($"{h}h", total, $"{h}h às {h + 1}h: {Plural(hora?.Vendas ?? 0, "venda", "vendas")} · {ValoresVenda.Moeda(total)}");
        }).ToList();
        var pico = r.PorHora.OrderByDescending(h => h.Total).First();
        conteudo.Children.Add(Suave($"Horário de mais movimento: {pico.Hora}h às {pico.Hora + 1}h ({Plural(pico.Vendas, "venda", "vendas")}). Passe o mouse nas colunas."));
        conteudo.Children.Add(new Border { Height = 12 });
        conteudo.Children.Add(Graficos.Colunas(pontos, 150, pontos.FindIndex(p => p.Item1 == $"{pico.Hora}h")));
        return Cartao("Vendas por hora", conteudo);
    }

    private FrameworkElement CartaoCancelamentos(ResumoDoDia r)
    {
        var lista = new StackPanel();
        if (r.Canceladas > 0)
            lista.Children.Add(new TextBlock { Text = $"{Plural(r.Canceladas, "venda paga cancelada", "vendas pagas canceladas")} ({ValoresVenda.Moeda(r.CanceladasValor)}).", FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var i in r.ItensCancelados)
        {
            var qtd = i.Unidade == "kg" ? $"{i.Quantidade:0.###} kg" : $"{i.Quantidade:0} un.";
            lista.Children.Add(new TextBlock
            {
                Text = $"{i.CriadoEm.ToLocalTime():HH:mm} · {i.Nome} ({qtd}, {ValoresVenda.Moeda(i.Valor)}) · caixa {i.OperadorNome}" +
                       (i.AutorizadoPor is null ? "" : $" · autorizado por {i.AutorizadoPor}"),
                FontSize = 13.5, Margin = new Thickness(0, 3, 0, 3), TextWrapping = TextWrapping.Wrap
            });
        }
        return Cartao("Cancelamentos", lista, margemBaixo: 16);
    }

    /// <summary>Cartão final: fechar o dia (com observação) ou, já fechado, o e-mail e "Reabrir o dia".</summary>
    private FrameworkElement CartaoFechar(ResumoDoDia r)
    {
        var painel = new StackPanel();
        var emails = string.IsNullOrWhiteSpace(_config?.EmailsResumo) ? null : _config!.EmailsResumo;
        if (_dia < DateTime.Today)
            painel.Children.Add(new Border
            {
                Style = Estilo("Aviso"), Background = Pincel("Ambar50"), BorderBrush = Pincel("Ambar700"),
                Child = new TextBlock
                {
                    Text = "Dia que já passou: fechar o dia, reabrir ou fechar um caixa pedem o usuário e a senha de um gerente.",
                    Foreground = Pincel("Ambar700"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap
                }
            });

        if (r.Fechado)
        {
            var f = r.Fechamento!;
            painel.Children.Add(new TextBlock
            {
                Text = $"Fechado por {f.FechadoPor} em {f.FechadoEm.ToLocalTime():dd/MM 'às' HH:mm}." + (f.Obs is null ? "" : $" Observação: “{f.Obs}”."),
                FontSize = 14, TextWrapping = TextWrapping.Wrap
            });
            painel.Children.Add(Suave(f.EmailEnviadoEm is { } enviado ? $"Resumo enviado por e-mail para {f.EmailPara} às {enviado.ToLocalTime():HH:mm}."
                : f.EmailErro is { } erro ? $"O e-mail não saiu: {erro}" : "O resumo ainda não foi enviado por e-mail."));
            var botoes = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            var enviar = new Button { Content = f.EmailEnviadoEm is null ? "Enviar o resumo por e-mail" : "Enviar de novo", Style = Estilo("BotaoPrimario"), Margin = new Thickness(0, 0, 10, 8) };
            enviar.Click += async (_, _) => await EnviarEmailAsync(enviar);
            botoes.Children.Add(enviar);
            var reabrir = new Button { Content = "Reabrir o dia", Style = Estilo("BotaoFantasma"), Margin = new Thickness(0, 0, 10, 8) };
            reabrir.Click += (_, _) => ConfirmarReabrir();
            botoes.Children.Add(reabrir);
            painel.Children.Add(botoes);
        }
        else
        {
            var abertos = r.Caixas.Where(c => c.FechadoEm is null).Select(c => c.OperadorNome).ToList();
            painel.Children.Add(Suave(abertos.Count > 0
                ? $"Para fechar o dia, feche antes {(abertos.Count == 1 ? "o caixa" : "os caixas")} de {string.Join(", ", abertos)} (no Caixa ou em \"Fechar este caixa\", acima)."
                : "Confira os caixas e os totais. Ao fechar, o resumo fica guardado e vai por e-mail" + (emails is null ? "." : $" para {emails}.")));
            if (r.Fechamento is { Automatico: true } auto)
                painel.Children.Add(Suave($"O resumo automático das 23h já foi gravado{(auto.EmailEnviadoEm is null ? "" : " e enviado")}. Fechar agora troca pelo resumo final."));
            painel.Children.Add(new TextBlock { Text = "Observação do dia (opcional)", Style = Estilo("Rotulo"), Margin = new Thickness(0, 14, 0, 6) });
            var obs = new TextBox { MaxLength = 300 };
            System.Windows.Automation.AutomationProperties.SetName(obs, "Observação do dia");
            painel.Children.Add(obs);
            var erro = new TextBlock { Style = Estilo("ErroCampo") };
            painel.Children.Add(erro);
            var fechar = new Button { Style = Estilo("BotaoPrimario"), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = abertos.Count == 0 };
            fechar.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new ContentControl { Style = Estilo("Icone"), Tag = FindResource("IconeCheck"), Foreground = Brushes.White, Width = 18, Margin = new Thickness(0, 0, 8, 0) },
                    new TextBlock { Text = "Fechar o dia e enviar o resumo" }
                }
            };
            fechar.Click += async (_, _) => await FecharDiaAsync(fechar, obs.Text, erro);
            painel.Children.Add(fechar);
        }

        // E-mails que recebem o resumo
        var linhaEmail = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var alterar = new Button { Content = emails is null ? "Cadastrar e-mails" : "Alterar", Style = Estilo("BotaoLink"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        alterar.Click += (_, _) => EditarEmails();
        DockPanel.SetDock(alterar, Dock.Right);
        linhaEmail.Children.Add(alterar);
        linhaEmail.Children.Add(Suave(emails is null ? "Nenhum e-mail cadastrado para receber o resumo." : $"O resumo vai para: {emails}"));
        painel.Children.Add(new Border { BorderBrush = Pincel("Linha"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 16, 0, 0), Child = linhaEmail });

        return Cartao(r.Fechado ? "Dia fechado" : "Fechar o dia", painel, margemBaixo: 0);
    }

    private async Task FecharDiaAsync(Button botao, string obs, TextBlock erro)
    {
        if (_servico is null) return;
        var (autorizado, autorizacao) = await AutorizarDiaPassadoAsync("Fechar o dia");
        if (!autorizado) return;
        botao.IsEnabled = false;
        erro.Text = "";
        try
        {
            _resumo = await _servico.FecharDiaAsync(_dia, obs.Trim().Length == 0 ? null : obs.Trim(), autorizacao);
        }
        catch (ConexaoException ex)
        {
            erro.Text = ex.Message;
            botao.IsEnabled = true;
            return;
        }
        _area.MostrarToast("Dia fechado. Enviando o resumo por e-mail…");
        MostrarDia();
        await EnviarEmailAsync(null);
    }

    /// <summary>Manda o resumo pela Edge Function. Sem ela publicada (ou sem e-mail configurado), abre o e-mail no PC.</summary>
    private async Task EnviarEmailAsync(Button? botao)
    {
        if (_servico is null || _resumo is null) return;
        if (string.IsNullOrWhiteSpace(_config?.EmailsResumo))
        {
            _area.MostrarToast("Cadastre os e-mails que recebem o resumo.");
            EditarEmails();
            return;
        }
        if (botao is not null) botao.IsEnabled = false;
        try
        {
            var para = await _servico.EnviarResumoAsync(_dia);
            _area.MostrarToast($"Resumo enviado para {para}.");
        }
        catch (FuncaoInexistenteException)
        {
            // Edge Function ainda não publicada: abre o e-mail pronto no programa de e-mail do computador
            var texto = ResumoEmTexto(_resumo);
            var assunto = $"Fechamento do dia {_dia:dd/MM/yyyy} - Mercado";
            if (!AbrirEmail(_config!.EmailsResumo!, assunto, texto)) ContatoRapido.Copiar(texto);
            _area.MostrarToast("O envio automático ainda não está ligado. Abri o e-mail no programa do computador.");
        }
        catch (ConexaoException ex)
        {
            _area.MostrarToast(ex.Message);
        }
        finally
        {
            if (botao is not null) botao.IsEnabled = true;
        }
        await CarregarDiaAsync();
    }

    private static bool AbrirEmail(string para, string assunto, string corpo)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                $"mailto:{para.Replace(" ", "")}?subject={Uri.EscapeDataString(assunto)}&body={Uri.EscapeDataString(corpo.Replace("\n", "\r\n"))}") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>O resumo em texto simples (para o e-mail aberto no computador quando a Edge Function não existe).</summary>
    private static string ResumoEmTexto(ResumoDoDia r)
    {
        static string Real(decimal v) => v.ToString("C", PtBr);   // o e-mail leva os valores mesmo com "Ocultar vendas"
        var t = new System.Text.StringBuilder();
        t.AppendLine($"Fechamento do dia {r.Dia:dd/MM/yyyy}");
        t.AppendLine();
        t.AppendLine($"Vendido: {Real(r.Total)} em {Plural(r.Vendas, "venda", "vendas")} (ticket médio {Real(r.TicketMedio)})");
        if (r.SemanaAnterior is { } a) t.AppendLine($"Mesmo dia da semana passada: {Real(a.Total)}");
        t.AppendLine($"Descontos: {Real(r.Descontos)} · Promoções: {Real(r.Promocoes)}");
        t.AppendLine();
        t.AppendLine("Formas de pagamento:");
        foreach (var (forma, valor) in r.Formas.OrderByDescending(f => f.Value)) t.AppendLine($"- {FormaPagamento.Nome(forma)}: {Real(valor)}");
        t.AppendLine();
        t.AppendLine("Caixas:");
        foreach (var c in r.Caixas)
            t.AppendLine($"- {c.OperadorNome}: {Real(c.Total)} ({Plural(c.Vendas, "venda", "vendas")})" +
                         (c.Diferenca is { } d ? d == 0 ? ", gaveta bateu" : d > 0 ? $", sobra de {Real(d)}" : $", falta de {Real(-d)}" : ", não fechou"));
        if (r.MaisVendidos.Count > 0)
        {
            t.AppendLine();
            t.AppendLine("Mais vendidos:");
            foreach (var p in r.MaisVendidos.Take(5)) t.AppendLine($"- {p.Nome}: {p.QuantidadeTexto}, {Real(p.Valor)}");
        }
        return t.ToString();
    }

    private void EditarEmails()
    {
        if (_servico is null) return;
        var j = new JanelaSimples(_area, "E-mails do resumo do dia", "Quem recebe o resumo quando o dia é fechado. Separe vários e-mails com vírgula.", 480);
        var campo = j.Campo("E-mails", _config?.EmailsResumo ?? "", 500);
        var automatico = new CheckBox { Content = "Mandar o resumo sozinho às 23h se ninguém fechar o dia", IsChecked = _config?.ResumoAutomatico ?? true, Margin = new Thickness(0, 14, 0, 0) };
        j.Corpo.Children.Add(automatico);
        j.Botao("Cancelar", false, j.Fechar);
        j.Botao("Salvar", true, async () =>
        {
            var lista = campo.Text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).ToList();
            if (lista.Any(e => !System.Text.RegularExpressions.Regex.IsMatch(e, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")))
            {
                campo.Tag = "invalido";
                j.Erro("Confira os e-mails: algum está incompleto.");
                return;
            }
            var config = _config ?? new ConfigMercado();
            config.EmailsResumo = lista.Count == 0 ? null : string.Join(", ", lista);
            config.ResumoAutomatico = automatico.IsChecked == true;
            try
            {
                await _servico.SalvarConfigAsync(config);
            }
            catch (ConexaoException ex)
            {
                j.Erro(ex.Message);
                return;
            }
            _config = config;
            j.Fechar();
            _area.MostrarToast("E-mails do resumo salvos.");
            if (_resumo is not null) MostrarDia();
        });
        _area.AbrirJanela(j);
        j.Loaded += (_, _) => campo.Focus();
    }

    private void ConfirmarReabrir()
    {
        if (_servico is null) return;
        _area.AbrirJanela(new ConfirmacaoJanela(_area, "Reabrir o dia?",
            $"O fechamento de {_dia:dd/MM/yyyy} é desfeito para você corrigir e fechar de novo. Os caixas continuam como estão.",
            async () =>
            {
                _area.FecharJanela();
                var (ok, autorizacao) = await AutorizarDiaPassadoAsync("Reabrir o dia");
                if (!ok) return;
                try
                {
                    await _servico.ReabrirDiaAsync(_dia, autorizacao);
                }
                catch (ConexaoException ex)
                {
                    _area.MostrarToast(ex.Message);
                    return;
                }
                _area.FecharJanela();
                _area.MostrarToast("Dia reaberto.");
                await CarregarDiaAsync();
            }, "Reabrir", "Voltar"));
    }

    // ------------------------------------------------------------ Análises

    private void MostrarAnalise()
    {
        var a = _analise!;
        var nomePeriodo = a.Agrupar switch { "semana" => "nas últimas 12 semanas", "mes" => "nos últimos 12 meses", _ => "nos últimos 30 dias" };
        KpisAnalise.Children.Clear();
        KpisAnalise.Children.Add(Kpi("IconeCarteira", "Verde700", ValoresVenda.Moeda(a.Total), "Vendido",
            a.Variacao is { } v && !ValoresVenda.Ocultos ? $"{Sinal(v)} sobre o período anterior ({Moeda(a.Anterior.Total)})" : $"Vendas pagas {nomePeriodo}"));
        KpisAnalise.Children.Add(Kpi("IconeCaixaRegistradora", "Verde700", ValoresVenda.Numero(a.Vendas), "Vendas", $"Ticket médio {ValoresVenda.Moeda(a.TicketMedio)}"));
        KpisAnalise.Children.Add(Kpi("IconeTendencia", "Verde700", a.Margem is { } m && !ValoresVenda.Ocultos ? m.ToString("P0", PtBr) : ValoresVenda.Ocultos ? "•••" : "—", "Margem",
            a.Margem is null ? "Cadastre o custo nos lotes para ver a margem" : $"Sobre {Moeda(a.VendidoComCusto)} vendidos com custo cadastrado"));
        KpisAnalise.Children.Add(Kpi("IconeEtiqueta", "Ambar700", ValoresVenda.Moeda(a.Descontos + a.Promocoes), "Descontos", $"Promoções {Moeda(a.Promocoes)} · na venda {Moeda(a.Descontos)}"));
        if (!_estreita)
            KpisAnalise.Children.Add(Kpi("IconeTriangulo", a.Perdas.Lotes > 0 ? "Vermelho600" : "Verde700", Moeda(a.Perdas.Valor), "Perdas",
                $"{Plural(a.Perdas.Lotes, "lote venceu", "lotes venceram")} com unidades (valor de custo)"));
        KpisAnalise.Columns = KpisAnalise.Children.Count;

        ConteudoAnalise.Children.Clear();
        var serie = new StackPanel();
        var pontos = a.Serie.Select(p => (Rotulo(p.Periodo, a.Agrupar), p.Total, $"{RotuloLongo(p.Periodo, a.Agrupar)}: {ValoresVenda.Moeda(p.Total)} em {Plural(p.Vendas, "venda", "vendas")}")).ToList();
        if (a.Serie.Count > 0 && a.Serie.MaxBy(p => p.Total) is { Total: > 0 } melhor)
            serie.Children.Add(Suave($"Melhor {(a.Agrupar == "mes" ? "mês" : a.Agrupar)}: {RotuloLongo(melhor.Periodo, a.Agrupar)} ({ValoresVenda.Moeda(melhor.Total)}). Passe o mouse nas colunas."));
        else serie.Children.Add(Suave("Nenhuma venda paga no período."));
        serie.Children.Add(new Border { Height = 12 });
        serie.Children.Add(Graficos.Colunas(pontos, 190, pontos.Count - 1));
        ConteudoAnalise.Children.Add(Cartao(a.Agrupar switch { "semana" => "Vendas por semana", "mes" => "Vendas por mês", _ => "Vendas por dia" }, serie));

        var categorias = new StackPanel();
        if (a.Categorias.Count == 0) categorias.Children.Add(Suave("Nenhuma venda no período."));
        var maxCat = a.Categorias.Count == 0 ? 0 : a.Categorias.Max(c => c.Valor);
        foreach (var c in a.Categorias.Take(12))
            categorias.Children.Add(Graficos.Barra(c.Categoria, c.Valor, maxCat,
                ValoresVenda.Ocultos ? ValoresVenda.Moeda(c.Valor) : $"{Moeda(c.Valor)} · {(a.Total > 0 ? c.Valor / a.Total : 0):P0}"));
        ConteudoAnalise.Children.Add(DuasColunas(Cartao("Por categoria", categorias), CartaoFormas(a.Formas, a.Total, "Por forma de pagamento")));

        var semana = new StackPanel();
        semana.Children.Add(Suave("Média vendida em cada dia da semana (só os dias que tiveram venda)."));
        semana.Children.Add(new Border { Height = 8 });
        var maxDia = a.DiasSemana.Count == 0 ? 0 : a.DiasSemana.Max(d => d.Media);
        foreach (var dia in Enumerable.Range(1, 6).Append(0))
        {
            var d = a.DiasSemana.FirstOrDefault(x => x.DiaSemana == dia);
            semana.Children.Add(Graficos.Barra(PtBr.DateTimeFormat.DayNames[dia], d?.Media ?? 0, maxDia, ValoresVenda.Moeda(d?.Media ?? 0), "Verde600", 110));
        }
        ConteudoAnalise.Children.Add(DuasColunas(Cartao("Dias da semana", semana), CartaoMaisVendidos(a.MaisVendidos, "Mais vendidos do período")));

        var c2 = a.Cancelamentos;
        ConteudoAnalise.Children.Add(Cartao("Cancelamentos no período", Suave(c2.Vendas == 0 && c2.Itens == 0 ? "Nenhum cancelamento."
            : $"{Plural(c2.Vendas, "venda paga cancelada", "vendas pagas canceladas")} e {Plural(c2.Itens, "item tirado", "itens tirados")} das vendas ({ValoresVenda.Moeda(c2.ValorItens)})."), margemBaixo: 0));
    }

    private static string Rotulo(DateTime p, string agrupar) => agrupar switch
    {
        "mes" => p.ToString("MMM", PtBr).TrimEnd('.'),
        _ => p.ToString("dd/MM", PtBr),
    };

    private static string RotuloLongo(DateTime p, string agrupar) => agrupar switch
    {
        "mes" => p.ToString("MMMM 'de' yyyy", PtBr),
        "semana" => $"semana de {p:dd/MM} a {p.AddDays(6):dd/MM}",
        _ => p.ToString("ddd, dd/MM", PtBr),
    };

    // ------------------------------------------------------------ Peças da tela

    /// <summary>Indicador da faixa (mesmo visual do Estoque, sem clique).</summary>
    private Button Kpi(string icone, string cor, string valor, string rotulo, string dica)
    {
        var conteudo = new StackPanel();
        conteudo.Children.Add(new Border
        {
            Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Left,
            Child = new ContentControl { Style = Estilo("Icone"), Tag = FindResource(icone), Foreground = Pincel(cor), Width = 16 }
        });
        conteudo.Children.Add(new TextBlock { Text = valor, Style = Estilo("KpiValor"), TextTrimming = TextTrimming.CharacterEllipsis });
        conteudo.Children.Add(new TextBlock { Text = rotulo, Style = Estilo("KpiRotulo") });
        conteudo.Children.Add(new TextBlock { Text = dica, Style = Estilo("KpiSub"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        return new Button { Style = Estilo("Kpi"), Content = conteudo, Cursor = System.Windows.Input.Cursors.Arrow, Focusable = false, ToolTip = dica };
    }

    private Border Cartao(string titulo, UIElement conteudo, double margemBaixo = 16)
    {
        var painel = new StackPanel();
        painel.Children.Add(new TextBlock { Text = titulo, FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });
        painel.Children.Add(conteudo);
        return new Border { Style = Estilo("CartaoBorda"), Padding = new Thickness(24, 22, 24, 22), Margin = new Thickness(0, 0, 0, margemBaixo), Child = painel };
    }

    /// <summary>Dois cartões lado a lado; na janela estreita, um embaixo do outro.</summary>
    private FrameworkElement DuasColunas(FrameworkElement esquerda, FrameworkElement direita)
    {
        if (_estreita)
        {
            var pilha = new StackPanel();
            pilha.Children.Add(esquerda);
            pilha.Children.Add(direita);
            return pilha;
        }
        var grade = new Grid();
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grade.Children.Add(esquerda);
        Grid.SetColumn(direita, 2);
        grade.Children.Add(direita);
        return grade;
    }

    private TextBlock Suave(string texto) => new() { Text = texto, FontSize = 13.5, Style = Estilo("TextoSuave"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };

    private Style Estilo(string chave) => (Style)FindResource(chave);

    private Brush Pincel(string chave) => (Brush)FindResource(chave);

    private static string Moeda(decimal v) => ValoresVenda.Moeda(v);

    private static string Sinal(decimal variacao) => (variacao >= 0 ? "+" : "−") + Math.Abs(variacao).ToString("P0", PtBr);

    private static string Plural(int n, string um, string varios) => $"{n.ToString(PtBr)} {(n == 1 ? um : varios)}";

    private static DateTime InicioDaSemana(DateTime d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    private static string NomeDoDia(DateTime d)
    {
        var texto = d.ToString("dddd, d 'de' MMMM", PtBr);
        texto = char.ToUpper(texto[0], PtBr) + texto[1..];
        return d == DateTime.Today ? $"Hoje · {texto}" : d == DateTime.Today.AddDays(-1) ? $"Ontem · {texto}" : texto;
    }

    private static string NomeCurto(DateTime d) => d.ToString("ddd dd/MM", PtBr);
}
