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
