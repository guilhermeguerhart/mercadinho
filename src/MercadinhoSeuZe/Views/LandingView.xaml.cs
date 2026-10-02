using System.Windows;
using System.Windows.Controls;

namespace MercadinhoSeuZe.Views;

public partial class LandingView : UserControl
{
    private readonly MainWindow _janela;

    public LandingView(MainWindow janela)
    {
        InitializeComponent();
        _janela = janela;
    }

    private void Entrar_Click(object sender, RoutedEventArgs e) => _janela.MostrarLogin();
}
