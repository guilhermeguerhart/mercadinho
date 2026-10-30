using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>Janela "Meu perfil": nome, usuário, função, acesso anterior, vendas de hoje e troca da própria senha.</summary>
public partial class MeuPerfilJanela : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly AreaInternaView _area;
    private readonly FuncionarioService? _servico;

    public MeuPerfilJanela(AreaInternaView area, Sessao sessao, FuncionarioService? servico)
    {
        InitializeComponent();
        _area = area;
        _servico = servico;

        Avatar.Text = sessao.Nome[..1].ToUpperInvariant();
        Nome.Text = sessao.Nome;
        FuncaoTexto.Text = sessao.Perfil == Perfil.Gerente ? "Gerente" : "Operador de caixa";
        Usuario.Text = $"usuário: {sessao.Usuario}";
        foreach (var campo in new[] { SenhaAtual, SenhaNova, SenhaRepetida })
            campo.PasswordChanged += (_, _) => { campo.Tag = null; ErroDe(campo).Text = ""; };

        Loaded += async (_, _) =>
        {
            SenhaAtual.Focus();
            await CarregarAsync();
        };
    }

    private async Task CarregarAsync()
    {
        if (_servico is null) return;
        try
        {
            if (await _servico.MeuPerfilAsync() is not { } dados) return;
            Detalhes.Children.Add(Detalhe("Acesso anterior", dados.AcessoAnterior is { } a ? a.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm", PtBr) : "primeiro acesso"));
            if (dados.CriadoEm is { } desde) Detalhes.Children.Add(Detalhe("No sistema desde", desde.ToLocalTime().ToString("dd/MM/yyyy", PtBr)));
            Detalhes.Children.Add(Detalhe("Minhas vendas hoje", dados.VendasHoje == 0 ? "nenhuma"
                : $"{dados.VendasHoje} · {dados.TotalHoje.ToString("C", PtBr)}"));
        }
        catch (ConexaoException)
        {
            // Sem o script 006 ou sem internet: a janela mostra só o crachá e a troca de senha
        }
    }

    private FrameworkElement Detalhe(string rotulo, string valor)
    {
        var bloco = new StackPanel { Margin = new Thickness(0, 0, 28, 8) };
        bloco.Children.Add(new TextBlock { Text = rotulo, FontSize = 12, Style = (Style)FindResource("TextoSuave") });
        bloco.Children.Add(new TextBlock { Text = valor, FontSize = 14, FontWeight = FontWeights.SemiBold });
        return bloco;
    }

    private TextBlock ErroDe(PasswordBox campo) =>
        campo == SenhaAtual ? SenhaAtualErro : campo == SenhaNova ? SenhaNovaErro : SenhaRepetidaErro;

    private void Fechar_Click(object sender, RoutedEventArgs e) => _area.FecharJanela();

    private async void TrocarSenha_Click(object sender, RoutedEventArgs e)
    {
        AvisoErro.Visibility = Visibility.Collapsed;
        var nova = SenhaNova.Password;
        var ok = Formulario.Validar(SenhaAtual, SenhaAtualErro, SenhaAtual.Password.Length > 0, "Digite a senha atual.");
        ok &= Formulario.Validar(SenhaNova, SenhaNovaErro, nova.Length >= FuncionarioService.SenhaMinima,
            $"Use pelo menos {FuncionarioService.SenhaMinima} caracteres.");
        ok &= Formulario.Validar(SenhaRepetida, SenhaRepetidaErro, SenhaRepetida.Password == nova, "As duas senhas não são iguais.");
        if (!ok || _servico is null)
        {
            (SenhaAtual.Tag is not null ? SenhaAtual : SenhaNova.Tag is not null ? SenhaNova : SenhaRepetida).Focus();
            return;
        }

        BotaoSalvar.IsEnabled = false;
        try
        {
            await _servico.TrocarMinhaSenhaAsync(SenhaAtual.Password, nova);
            _area.FecharJanela();
            _area.MostrarToast("Senha trocada. Use a nova senha no próximo login.");
        }
        catch (SenhaAtualErradaException ex)
        {
            Formulario.Validar(SenhaAtual, SenhaAtualErro, false, ex.Message);
            SenhaAtual.Focus();
        }
        catch (ConexaoException ex)
        {
            AvisoErroTexto.Text = ex.Message;
            AvisoErro.Visibility = Visibility.Visible;
        }
        finally
        {
            BotaoSalvar.IsEnabled = true;
        }
    }
}
