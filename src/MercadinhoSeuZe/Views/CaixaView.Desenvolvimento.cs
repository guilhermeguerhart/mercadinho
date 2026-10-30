#if DESENVOLVIMENTO
using System.Globalization;
using System.Windows.Input;
using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Views;

/// <summary>
/// SÓ PARA DESENVOLVIMENTO: Alt+2 no Caixa "passa no leitor" o código de um produto cadastrado que pode ser vendido,
/// para testar sem leitor. Por unidade: o código da unidade (EAN-13 tirado do DUN-14 da caixa). Por quilo com código
/// na balança: uma etiqueta da balança com um peso de 0,3 a 2 kg. Sem código: digita o nome.
/// Só é compilado em Debug ou com "publicar.ps1 -Desenvolvimento"; o .exe normal não tem o atalho.
/// </summary>
public partial class CaixaView
{
    private readonly Random _sorteio = new();

    partial void IniciarAtalhosDesenvolvimento()
    {
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.System || e.SystemKey != Key.D2 || Keyboard.Modifiers != ModifierKeys.Alt || _caixa is null) return;
            e.Handled = true;
            var vendaveis = _produtos.Where(p => p.DisponivelParaVenda > 0).ToList();
            if (vendaveis.Count == 0)
            {
                _area.MostrarToast("Nenhum produto com estoque para vender. Dê entrada de lotes no Estoque (Alt+2 lá).");
                return;
            }
            var p = vendaveis[_sorteio.Next(vendaveis.Count)];
            Busca.Text = Codigo(p);
            Busca.Focus();
            _ = LerBuscaAsync();
        };
    }

    private string Codigo(Produto p)
    {
        if (p.PorPeso && p.CodigoBalanca is { } plu)
        {
            // Etiqueta "2 CCCC 0 VVVVVV D" (ou 5 dígitos de código) com o preço de um peso entre 0,3 e 2 kg
            var peso = Math.Min(p.DisponivelParaVenda, Math.Round((decimal)(0.3 + _sorteio.NextDouble() * 1.7), 3));
            var centavos = (int)Math.Round(peso * p.Preco * 100);
            var codigo = plu < 10000 ? $"2{plu:0000}0{centavos:000000}" : $"2{plu:00000}0{centavos:00000}";
            return CodigoBarras.ComDigito(codigo);
        }
        if (p.CodigoBarrasCaixa is { Length: 14 } dun && dun.All(char.IsAsciiDigit))
            return CodigoBarras.ComDigito(dun[1..13]);
        return p.CodigoBarrasCaixa ?? p.Nome;
    }
}
#endif
