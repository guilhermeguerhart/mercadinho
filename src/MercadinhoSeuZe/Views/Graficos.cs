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
}
