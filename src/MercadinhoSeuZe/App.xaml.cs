using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MercadinhoSeuZe;

public partial class App : Application
{
    static App()
    {
        // Acessibilidade (semana 8): botão sem nome para o leitor de tela (só ícone, "×", "‹", "›") ganha o nome da dica
        // (ToolTip) ou um nome padrão. Vale para todos os botões do programa, inclusive os criados pelo código.
        EventManager.RegisterClassHandler(typeof(ButtonBase), FrameworkElement.LoadedEvent, new RoutedEventHandler(NomearBotao));
    }

    public App()
    {
        // Erro não previsto em uma tela: avisa e segue, em vez de fechar o programa no meio de uma venda.
        // Só no programa do mercado: o programa de testes também cria o App e anota esses erros no relatório
        // (uma janela de aviso pararia os testes automáticos).
        if (System.Reflection.Assembly.GetEntryAssembly() != typeof(App).Assembly) return;
        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(
                "Aconteceu um erro inesperado. Confira a tela antes de continuar.\n\n" + e.Exception.Message,
                "Mercado · Sistema de gestão", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
    }

    private static void NomearBotao(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase b || !string.IsNullOrWhiteSpace(AutomationProperties.GetName(b))) return;
        var texto = b.Content as string;
        if (texto is { Length: > 1 }) return;   // botão com texto: o leitor de tela já lê o texto
        var nome = b.ToolTip as string ?? (b.ToolTip as TextBlock)?.Text ?? texto switch
        {
            "×" => "Fechar",
            "‹" => "Anterior",
            "›" => "Próximo",
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(nome)) AutomationProperties.SetName(b, nome);
    }
}
