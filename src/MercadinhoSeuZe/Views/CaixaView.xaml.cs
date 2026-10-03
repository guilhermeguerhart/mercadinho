using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>Caixa provisório (igual a caixa.html do site) com o resumo de vendas do dia. A tela real é da semana 5.</summary>
public partial class CaixaView : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public CaixaView()
    {
        InitializeComponent();

        var resumo = DadosExemplo.ResumoDoDia();
        Total.Text = resumo.Total.ToString("C", PtBr);
        Quantidade.Text = resumo.QuantidadeVendas.ToString(PtBr);
        Ticket.Text = resumo.TicketMedio.ToString("C", PtBr);
    }

    private void AoRedimensionar(object sender, SizeChangedEventArgs e) =>
        Resumo.Columns = e.NewSize.Width < 600 ? 1 : 3;
}
