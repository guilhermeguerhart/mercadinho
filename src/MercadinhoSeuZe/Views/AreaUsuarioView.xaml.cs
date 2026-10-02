using System.Windows;
using System.Windows.Controls;
using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Views;

/// <summary>Área após o login: mostra só os módulos que o perfil pode acessar.</summary>
public partial class AreaUsuarioView : UserControl
{
    public sealed record Modulo(string Nome, string Descricao, string Previsao);

    private static readonly Modulo Caixa =
        new("Caixa", "Registrar vendas e receber pagamentos.", "Disponível na semana 5");

    private static readonly Modulo[] ModulosDono =
    [
        new("Painel", "Resumo de vendas do dia e avisos de estoque baixo.", "Disponível na semana 2"),
        new("Estoque", "Produtos, lotes, validades e filtros.", "Disponível na semana 3"),
        new("Fornecedores", "Cadastro, contato rápido e histórico de pedidos.", "Disponível na semana 4"),
        Caixa,
        new("Fechamento do dia", "Resumo das vendas enviado por e-mail.", "Disponível na semana 6"),
        new("Previsão de compras", "Sugestões e pedidos automáticos.", "Disponível na semana 7"),
    ];

    private readonly MainWindow _janela;

    public AreaUsuarioView(MainWindow janela, Perfil perfil)
    {
        InitializeComponent();
        _janela = janela;

        Saudacao.Text = $"Olá, {perfil.Nome}!";
        if (perfil.Papel == Papel.Dono)
        {
            DescricaoPerfil.Text = "Perfil dono: acesso ao estoque e a todas as informações do sistema.";
            Modulos.ItemsSource = ModulosDono;
        }
        else
        {
            DescricaoPerfil.Text = "Perfil caixa: acesso apenas ao sistema de caixa.";
            Modulos.ItemsSource = new[] { Caixa };
        }
    }

    private void Sair_Click(object sender, RoutedEventArgs e) => _janela.MostrarInicio();
}
