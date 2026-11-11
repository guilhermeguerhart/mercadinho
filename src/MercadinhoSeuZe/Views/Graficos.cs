using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Gráficos simples desenhados com os próprios controles do WPF (sem pacote novo): barras deitadas para comparar
/// itens (formas de pagamento, categorias) e colunas para uma sequência no tempo (dias, semanas, meses, horas).
/// Cores e letras de App.xaml. O valor de cada barra aparece escrito ao lado ou ao passar o mouse.
/// </summary>
internal static class Graficos
{
    private static Brush Pincel(string chave) => (Brush)Application.Current.FindResource(chave);
    private static Style Estilo(string chave) => (Style)Application.Current.FindResource(chave);

    /// <summary>Uma barra deitada: rótulo, barra proporcional ao maior valor e o texto do valor.</summary>
    public static FrameworkElement Barra(string rotulo, decimal valor, decimal maximo, string texto, string cor = "Verde600", double larguraRotulo = 150)
    {
        var grade = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(larguraRotulo) });
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grade.Children.Add(new TextBlock
        {
            Text = rotulo, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 12, 0), ToolTip = rotulo
        });

        // A barra ocupa a parte "valor" de uma linha dividida em valor e resto
        var trilho = new Grid { Height = 18, VerticalAlignment = VerticalAlignment.Center };
        var parte = maximo > 0 ? (double)Math.Max(0, valor / maximo) : 0;
        trilho.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(parte, 0.0001), GridUnitType.Star) });
        trilho.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - parte, 0.0001), GridUnitType.Star) });
        trilho.Children.Add(new Border { Background = Pincel(cor), CornerRadius = new CornerRadius(0, 3, 3, 0), MinWidth = valor > 0 ? 3 : 0 });
        Grid.SetColumn(trilho, 1);
        grade.Children.Add(trilho);

        var t = new TextBlock { Text = texto, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), MinWidth = 90, TextAlignment = TextAlignment.Right };
        Grid.SetColumn(t, 2);
        grade.Children.Add(t);
        return grade;
    }

    /// <summary>
    /// Colunas em sequência (o tempo corre da esquerda para a direita). Cada ponto: rótulo embaixo, valor e dica ao passar
    /// o mouse. Com muitos pontos, só alguns rótulos aparecem para não encavalar. destaque = coluna em outra cor (hoje).
    /// </summary>
    public static FrameworkElement Colunas(IReadOnlyList<(string Rotulo, decimal Valor, string Dica)> pontos, double altura = 170, int? destaque = null)
    {
        var raiz = new Grid();
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(altura) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (pontos.Count == 0) return raiz;

        var maximo = pontos.Max(p => p.Valor);
        var passo = Math.Max(1, (int)Math.Ceiling(pontos.Count / 12.0));
        // Linha de base
        var baseLinha = new Border { Height = 1.5, Background = Pincel("Tinta"), VerticalAlignment = VerticalAlignment.Bottom };
        raiz.Children.Add(baseLinha);
        var colunas = new UniformGrid { Rows = 1, Columns = pontos.Count };
        var rotulos = new UniformGrid { Rows = 1, Columns = pontos.Count, Margin = new Thickness(0, 6, 0, 0) };
        for (var i = 0; i < pontos.Count; i++)
        {
            var p = pontos[i];
            var h = maximo > 0 ? (double)(p.Valor / maximo) * (altura - 8) : 0;
            var barra = new Border
            {
                Height = Math.Max(p.Valor > 0 ? 2 : 0, h), VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(pontos.Count > 20 ? 1 : 4, 0, pontos.Count > 20 ? 1 : 4, 0),
                Background = Pincel(i == destaque ? "Etiqueta" : "Verde600"), CornerRadius = new CornerRadius(2, 2, 0, 0),
                BorderBrush = i == destaque ? Pincel("Tinta") : null, BorderThickness = new Thickness(i == destaque ? 1 : 0)
            };
            // Área inteira da coluna responde ao mouse (barra baixa também mostra a dica)
            var celula = new Grid { Background = Brushes.Transparent, ToolTip = p.Dica };
            celula.Children.Add(barra);
            colunas.Children.Add(celula);
            rotulos.Children.Add(new TextBlock
            {
                Text = i % passo == 0 || i == pontos.Count - 1 ? p.Rotulo : "", FontSize = 11.5, Style = Estilo("TextoSuave"),
                HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.None
            });
        }
        raiz.Children.Add(colunas);
        Grid.SetRow(rotulos, 1);
        raiz.Children.Add(rotulos);
        return raiz;
    }

    /// <summary>Uma semana do gráfico de previsão: vendido (passado) ou previsto com a faixa (futuro).</summary>
    public sealed record PontoPrevisao(string Rotulo, decimal? Vendido, decimal Previsto, decimal? Minimo, decimal? Maximo, bool Futuro, string Dica);

    /// <summary>
    /// Previsão de um produto (tela Previsão): semanas passadas em verde escuro com um traço amarelo no que a previsão
    /// esperava; semanas futuras em verde claro com a faixa (risco vertical do mínimo ao máximo).
    /// </summary>
    public static FrameworkElement Previsao(IReadOnlyList<PontoPrevisao> pontos, double altura = 190)
    {
        var raiz = new Grid();
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(altura) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (pontos.Count == 0) return raiz;

        var maximo = pontos.Max(p => Math.Max(Math.Max(p.Vendido ?? 0, p.Previsto), p.Maximo ?? 0));
        if (maximo <= 0) maximo = 1;
        double H(decimal v) => Math.Max(0, (double)(v / maximo) * (altura - 8));
        var passo = Math.Max(1, (int)Math.Ceiling(pontos.Count / 10.0));

        raiz.Children.Add(new Border { Height = 1.5, Background = Pincel("Tinta"), VerticalAlignment = VerticalAlignment.Bottom });
        var colunas = new UniformGrid { Rows = 1, Columns = pontos.Count };
        var rotulos = new UniformGrid { Rows = 1, Columns = pontos.Count, Margin = new Thickness(0, 6, 0, 0) };
        for (var i = 0; i < pontos.Count; i++)
        {
            var p = pontos[i];
            var celula = new Grid { Background = Brushes.Transparent, ToolTip = p.Dica };
            var margem = new Thickness(3, 0, 3, 0);
            if (p.Futuro)
            {
                celula.Children.Add(new Border
                {
                    Height = Math.Max(p.Previsto > 0 ? 2 : 0, H(p.Previsto)), VerticalAlignment = VerticalAlignment.Bottom, Margin = margem,
                    Background = Pincel("Verde100"), BorderBrush = Pincel("Verde600"), BorderThickness = new Thickness(1, 1, 1, 0), CornerRadius = new CornerRadius(2, 2, 0, 0)
                });
                if (p.Minimo is { } minimo && p.Maximo is { } maximoFaixa && maximoFaixa > minimo)
                {
                    celula.Children.Add(new Border
                    {
                        Width = 2, Height = Math.Max(1, H(maximoFaixa) - H(minimo)), VerticalAlignment = VerticalAlignment.Bottom,
                        HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, H(minimo)), Background = Pincel("Tinta")
                    });
                    foreach (var h in new[] { H(maximoFaixa), H(minimo) })
                        celula.Children.Add(new Border
                        {
                            Width = 10, Height = 2, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(0, 0, 0, Math.Max(0, h - 1)), Background = Pincel("Tinta")
                        });
                }
            }
            else
            {
                var vendido = p.Vendido ?? 0;
                celula.Children.Add(new Border
                {
                    Height = Math.Max(vendido > 0 ? 2 : 0, H(vendido)), VerticalAlignment = VerticalAlignment.Bottom, Margin = margem,
                    Background = Pincel("Verde600"), CornerRadius = new CornerRadius(2, 2, 0, 0)
                });
                celula.Children.Add(new Border
                {
                    Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, Math.Max(0, H(p.Previsto) - 1.5)),
                    Background = Pincel("Etiqueta"), BorderBrush = Pincel("Tinta"), BorderThickness = new Thickness(0.5)
                });
            }
            colunas.Children.Add(celula);
            rotulos.Children.Add(new TextBlock
            {
                Text = i % passo == 0 || i == pontos.Count - 1 ? p.Rotulo : "", FontSize = 11.5, Style = Estilo("TextoSuave"),
                HorizontalAlignment = HorizontalAlignment.Center, FontWeight = p.Futuro ? FontWeights.SemiBold : FontWeights.Normal
            });
        }
        raiz.Children.Add(colunas);
        Grid.SetRow(rotulos, 1);
        raiz.Children.Add(rotulos);
        return raiz;
    }
    /// <summary>Um período do laboratório: vendido, previsto com a faixa e o ano passado.</summary>
    public sealed record PontoComparativo(string Rotulo, decimal Vendido, decimal Previsto, decimal Minimo, decimal Maximo, decimal Ingenuo, string Dica, bool Marcado);

    /// <summary>
    /// Laboratório da previsão (versão de desenvolvimento): coluna verde = vendido (cinza nos períodos fora do normal),
    /// traço amarelo = previsto, risco preto = faixa de 80%, traço roxo = ano passado. Com muitos períodos (dias do ano)
    /// o gráfico fica mais largo que a tela e ganha uma barra de rolagem embaixo.
    /// </summary>
    public static FrameworkElement Comparativo(IReadOnlyList<PontoComparativo> pontos, double altura = 220)
    {
        var raiz = new Grid();
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(altura) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (pontos.Count == 0) return raiz;

        var maximo = pontos.Max(p => Math.Max(Math.Max(p.Vendido, p.Previsto), Math.Max(p.Maximo, p.Ingenuo)));
        if (maximo <= 0) maximo = 1;
        double H(decimal v) => Math.Max(0, (double)(v / maximo) * (altura - 8));
        var largura = pontos.Count > 60 ? 7.0 : 0;     // muitos pontos: 7 px cada e rolagem
        var passo = Math.Max(1, (int)Math.Ceiling(pontos.Count / (largura > 0 ? pontos.Count / 9.0 : 12.0)));
        if (largura > 0) raiz.Width = pontos.Count * largura;

        raiz.Children.Add(new Border { Height = 1.5, Background = Pincel("Tinta"), VerticalAlignment = VerticalAlignment.Bottom });
        var colunas = new UniformGrid { Rows = 1, Columns = pontos.Count };
        var rotulos = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        for (var i = 0; i < pontos.Count; i++)
        {
            var p = pontos[i];
            var celula = new Grid { Background = Brushes.Transparent, ToolTip = p.Dica };
            var fino = pontos.Count > 30;
            celula.Children.Add(new Border
            {
                Height = Math.Max(p.Vendido > 0 ? 2 : 0, H(p.Vendido)), VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(fino ? 0.5 : 3, 0, fino ? 0.5 : 3, 0),
                Background = Pincel(p.Marcado ? "Linha" : "Verde600"), CornerRadius = new CornerRadius(fino ? 0 : 2, fino ? 0 : 2, 0, 0)
            });
            if (p.Maximo > p.Minimo)
                celula.Children.Add(new Border
                {
                    Width = fino ? 1 : 2, Height = Math.Max(1, H(p.Maximo) - H(p.Minimo)), VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, H(p.Minimo)), Background = Pincel("Tinta"), Opacity = 0.55
                });
            celula.Children.Add(new Border
            {
                Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, Math.Max(0, H(p.Ingenuo) - 1)),
                Background = Pincel("Roxo600")
            });
            celula.Children.Add(new Border
            {
                Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, Math.Max(0, H(p.Previsto) - 1.5)),
                Background = Pincel("Etiqueta"), BorderBrush = Pincel("Tinta"), BorderThickness = new Thickness(fino ? 0 : 0.5)
            });
            colunas.Children.Add(celula);
        }
        // Rótulos: só alguns, cada um centrado na sua coluna
        rotulos.ColumnDefinitions.Clear();
        for (var i = 0; i < pontos.Count; i++) rotulos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < pontos.Count; i += passo)
        {
            var t = new TextBlock { Text = pontos[i].Rotulo, FontSize = 11.5, Style = Estilo("TextoSuave"), HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetColumn(t, i);
            Grid.SetColumnSpan(t, Math.Min(passo, pontos.Count - i));
            rotulos.Children.Add(t);
        }
        raiz.Children.Add(colunas);
        Grid.SetRow(rotulos, 1);
        raiz.Children.Add(rotulos);
        if (largura <= 0) return raiz;

        // Rolagem só para os lados; a rodinha do mouse continua rolando a página
        var rolagem = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = raiz, Padding = new Thickness(0, 0, 0, 6)
        };
        var moldura = new Border { Child = rolagem };
        rolagem.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            moldura.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = moldura });
        };
        return moldura;
    }
}
