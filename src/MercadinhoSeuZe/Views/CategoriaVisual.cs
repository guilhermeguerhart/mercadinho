using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// Cor e ícone de cada categoria, para reconhecer a categoria de relance (sem depender só da cor:
/// o ícone e o nome sempre aparecem juntos). Categorias novas ganham uma cor fixa pelo nome e o ícone de etiqueta.
/// As cores são as de App.xaml.
/// </summary>
public static class CategoriaVisual
{
    public sealed record Visual(string Icone, string Fundo, string Cor);

    private static readonly (string[] Palavras, Visual Visual)[] Conhecidas =
    [
        (["acougue", "carne", "frango", "peixe"], new("IconeCarne", "Vermelho50", "Vermelho600")),
        (["frios", "embutido"], new("IconeQueijo", "Rosa50", "Rosa600")),
        (["grao", "cereal"], new("IconeTrigo", "Ambar50", "Ambar700")),
        (["mercearia"], new("IconeCesta", "Marrom50", "Marrom600")),
        (["padaria", "pao"], new("IconePao", "Ambar50", "Ambar700")),
        (["laticinio", "leite"], new("IconeGarrafa", "Azul50", "Azul600")),
        (["bebida"], new("IconeCopo", "Ciano50", "Ciano600")),
        (["congelado"], new("IconeFloco", "Azul50", "Azul600")),
        (["hortifruti", "fruta", "verdura", "legume"], new("IconeFolha", "Verde50", "Verde700")),
        (["limpeza"], new("IconeBolhas", "Roxo50", "Roxo600")),
        (["higiene", "perfumaria", "beleza"], new("IconeGota", "Indigo50", "Indigo600")),
        (["pet", "animal"], new("IconePata", "Marrom50", "Marrom600")),
    ];

    private static readonly (string Fundo, string Cor)[] Paleta =
    [
        ("Verde50", "Verde700"), ("Azul50", "Azul600"), ("Ambar50", "Ambar700"), ("Roxo50", "Roxo600"),
        ("Ciano50", "Ciano600"), ("Rosa50", "Rosa600"), ("Indigo50", "Indigo600"), ("Marrom50", "Marrom600"),
    ];

    public static Visual De(string categoria)
    {
        var chave = SemAcento(categoria);
        foreach (var (palavras, visual) in Conhecidas)
            if (palavras.Any(chave.Contains))
                return visual;
        // Mesma categoria sempre com a mesma cor
        var (fundo, cor) = Paleta[(int)((uint)chave.Aggregate(17, (h, c) => h * 31 + c) % Paleta.Length)];
        return new Visual("IconeEtiqueta", fundo, cor);
    }

    /// <summary>Etiqueta colorida com ícone e nome da categoria.</summary>
    public static Border Pilula(FrameworkElement dono, string categoria, double tamanho = 13)
    {
        var v = De(categoria);
        var conteudo = new StackPanel { Orientation = Orientation.Horizontal };
        conteudo.Children.Add(new ContentControl
        {
            Style = (Style)dono.FindResource("Icone"),
            Tag = dono.FindResource(v.Icone),
            Foreground = (Brush)dono.FindResource(v.Cor),
            Width = tamanho + 2,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        conteudo.Children.Add(new TextBlock
        {
            Text = categoria,
            FontSize = tamanho,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)dono.FindResource(v.Cor),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        });
        return new Border
        {
            Background = (Brush)dono.FindResource(v.Fundo),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 3, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = conteudo
        };
    }

    private static string SemAcento(string texto)
    {
        var sb = new StringBuilder();
        foreach (var c in texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }
}
