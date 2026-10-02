using System.Windows;
using System.Windows.Controls;
using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Views;

/// <summary>Tela após o login enquanto as telas internas não ficam prontas (igual a em-desenvolvimento.html).</summary>
public partial class EmDesenvolvimentoView : UserControl
{
    private readonly MainWindow _janela;

    public EmDesenvolvimentoView(MainWindow janela, Sessao sessao)
    {
        InitializeComponent();
        _janela = janela;

        Ola.Text = $"Olá, {sessao.Nome}! Seu login foi feito com sucesso.";
        Proximas.ItemsSource = sessao.Perfil == Perfil.Gerente
            ? new[] { "Painel com o resumo do dia", "Estoque e fornecedores", "Caixa e fechamento do dia" }
            : new[] { "Caixa", "Fechamento do turno" };
    }

    private void Sair_Click(object sender, RoutedEventArgs e) => _janela.MostrarInicio();
}
