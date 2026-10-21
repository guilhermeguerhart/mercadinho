#if DESENVOLVIMENTO
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// SÓ PARA DESENVOLVIMENTO: Alt+2 na tela de Fornecedores cadastra fornecedores de teste que cobrem as categorias
/// do catálogo do leitor simulado (Alt+2 no Estoque), para os produtos já aparecerem ligados a eles.
/// Telefones e e-mails são fictícios (domínio .example). Só é compilado em Debug ou com
/// "publicar.ps1 -Desenvolvimento"; o .exe normal não tem o atalho.
/// </summary>
public partial class FornecedoresView
{
    private static readonly DadosFornecedor[] FornecedoresTeste =
    [
        new("Distribuidora Boa Safra", "(11) 4002-1122", null, "pedidos@boasafra.example", "Carla", "Grãos, Mercearia", 3, null, "Pedido mínimo R$ 300."),
        new("Laticínios Real", "(11) 4002-5588", null, "vendas@laticiniosreal.example", "Rodrigo", "Laticínios, Frios", 2, null, null),
        new("Limp Distribuidora", "(11) 4002-3344", null, null, null, "Limpeza, Higiene", 5, null, "Entrega só às terças."),
        new("Frigorífico Bom Corte", "(11) 4002-7799", null, "pedidos@bomcorte.example", "Seu Antônio", "Açougue", 1, null, "Peso das caixas varia: conferir a etiqueta."),
        new("Bebidas Express", "(11) 4002-6611", null, "comercial@bebidasexpress.example", null, "Bebidas", 2, null, null),
        new("Atacadão Central", "(11) 4002-9090", null, "atendimento@atacadaocentral.example", "Júlia", "Padaria, Congelados, Hortifrúti, Pet", 4, null, null),
    ];

    partial void IniciarAtalhosDesenvolvimento()
    {
        PreviewKeyDown += (_, e) =>
        {
            var tecla = e.Key == Key.System ? e.SystemKey : e.Key;
            if (Keyboard.Modifiers == ModifierKeys.Alt && tecla is Key.D2 or Key.NumPad2)
            {
                _area.AbrirJanela(CriarJanelaTeste());
                e.Handled = true;
            }
        };
    }

    private List<DadosFornecedor> TesteQueFalta() =>
        FornecedoresTeste.Where(t => !_fornecedores.Any(f => Models.Fornecedor.MesmoTexto(f.Nome, t.Nome))).ToList();

    private FrameworkElement CriarJanelaTeste()
    {
        var corpo = new StackPanel();
        var cabecalho = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var fechar = new Button { Content = "×", Style = Recurso<Style>("LinkSuave"), FontSize = 24, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Top };
        fechar.Click += (_, _) => _area.FecharJanela();
        DockPanel.SetDock(fechar, Dock.Right);
        cabecalho.Children.Add(fechar);
        var titulos = new StackPanel();
        titulos.Children.Add(new TextBlock { Text = "Fornecedores de teste (Alt+2)", FontSize = 22, FontWeight = FontWeights.Bold });
        titulos.Children.Add(new TextBlock
        {
            Text = "Só na versão de desenvolvimento. Cobrem as categorias do leitor simulado do Estoque. Telefones e e-mails são fictícios: " +
                   "para testar o envio pelo WhatsApp, edite um deles e coloque o seu número.",
            FontSize = 13, Foreground = Recurso<Brush>("Suave")
        });
        cabecalho.Children.Add(titulos);
        corpo.Children.Add(cabecalho);

        var faltam = TesteQueFalta();
        var lista = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        foreach (var t in FornecedoresTeste)
        {
            var linha = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var situacao = new TextBlock
            {
                Text = faltam.Contains(t) ? "falta" : "já cadastrado",
                FontSize = 12, Foreground = Recurso<Brush>(faltam.Contains(t) ? "Ambar700" : "Verde700"), Margin = new Thickness(12, 0, 0, 0)
            };
            DockPanel.SetDock(situacao, Dock.Right);
            linha.Children.Add(situacao);
            linha.Children.Add(new TextBlock { Text = $"{t.Nome} · {t.Categorias}", FontSize = 14 });
            lista.Children.Add(linha);
        }
        corpo.Children.Add(lista);

        var botao = new Button
        {
            Style = Recurso<Style>("BotaoPrimario"),
            Content = faltam.Count > 0 ? $"Cadastrar os {faltam.Count} que faltam" : "Todos já estão cadastrados",
            IsEnabled = faltam.Count > 0 && _servico is not null
        };
        botao.Click += async (_, _) =>
        {
            botao.IsEnabled = false;
            int certos = 0;
            string? erro = null;
            foreach (var t in faltam)
            {
                try { await _servico!.CriarAsync(t); certos++; }
                catch (ConexaoException ex) { erro ??= ex.Message; }
            }
            await RecarregarAsync($"{certos} fornecedores de teste cadastrados." + (erro is null ? "" : $" Erro: {erro}"));
        };
        corpo.Children.Add(botao);

        return new UserControl
        {
            Width = 520,
            Content = new Border
            {
                Style = Recurso<Style>("CartaoBorda"),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24, 22, 24, 20),
                Effect = (System.Windows.Media.Effects.Effect)FindResource("SombraJanela"),
                Child = corpo
            }
        };
    }
}
#endif
