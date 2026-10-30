using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MercadinhoSeuZe.Services;

namespace MercadinhoSeuZe.Views;

/// <summary>Janela da tela Funcionários: cadastrar, editar nome e função, ou redefinir a senha.</summary>
public partial class FuncionarioJanela : UserControl
{
    public enum Modo { Novo, Editar, Senha }

    private readonly AreaInternaView _area;
    private readonly FuncionariosView _tela;
    private readonly FuncionarioService _servico;
    private readonly Funcionario? _funcionario;
    private readonly Modo _modo;
    private readonly IReadOnlyList<Funcionario> _todos;
    private bool _usuarioDigitado;

    public FuncionarioJanela(AreaInternaView area, FuncionariosView tela, FuncionarioService servico, Funcionario? funcionario, Modo modo,
        IReadOnlyList<Funcionario> todos)
    {
        InitializeComponent();
        _area = area;
        _tela = tela;
        _servico = servico;
        _funcionario = funcionario;
        _modo = modo;
        _todos = todos;

        switch (modo)
        {
            case Modo.Novo:
                Explicacao.Text = "O funcionário entra com o usuário e a senha inicial, e pode trocar a senha depois em “Meu perfil”.";
                Funcao.SelectedIndex = 0;
                break;
            case Modo.Editar:
                Titulo.Text = "Editar funcionário";
                Explicacao.Text = "O usuário do login não muda. Para trocar a senha, use “Redefinir senha”.";
                BotaoSalvar.Content = "Salvar alterações";
                Nome.Text = funcionario!.Nome;
                Usuario.Text = funcionario.Usuario;
                Usuario.IsEnabled = false;
                Funcao.SelectedIndex = funcionario.Gerente ? 1 : 0;
                CamposSenha.Visibility = Visibility.Collapsed;
                if (funcionario.Id == servico.Sessao.Id)
                {
                    Funcao.IsEnabled = false;
                    DicaFuncao.Text = "Você não pode tirar o seu próprio acesso de gerente. Peça para outro gerente.";
                }
                break;
            case Modo.Senha:
                Titulo.Text = "Redefinir senha";
                Explicacao.Text = $"Nova senha para {funcionario!.Nome} ({funcionario.Usuario}). Passe a senha pessoalmente; ele pode trocar depois em “Meu perfil”.";
                BotaoSalvar.Content = "Redefinir senha";
                RotuloSenha.Text = "Nova senha";
                CamposDados.Visibility = Visibility.Collapsed;
                CamposSenha.Margin = new Thickness(0);
                break;
        }

        Formulario.LimparAoDigitar(Nome, NomeErro);
        Formulario.LimparAoDigitar(Usuario, UsuarioErro);
        Senha.PasswordChanged += (_, _) => { Senha.Tag = null; SenhaErro.Text = ""; };
        SenhaRepetida.PasswordChanged += (_, _) => { SenhaRepetida.Tag = null; SenhaRepetidaErro.Text = ""; };
        // Sugere o usuário pelo nome: "Ana Paula" → "ana.paula"
        Nome.TextChanged += (_, _) =>
        {
            if (_modo == Modo.Novo && !_usuarioDigitado) Usuario.Text = SugerirUsuario(Nome.Text);
        };
        Usuario.PreviewTextInput += (_, _) => _usuarioDigitado = true;
        Loaded += (_, _) => { if (modo == Modo.Senha) Senha.Focus(); else Nome.Focus(); };
    }

    private static string SugerirUsuario(string nome)
    {
        var semAcento = new string(nome.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        var partes = Regex.Split(semAcento, @"[^a-z0-9]+").Where(p => p.Length > 0).Take(2);
        return string.Join(".", partes);
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => _area.FecharJanela();

    private async void Salvar_Click(object sender, RoutedEventArgs e)
    {
        AvisoErro.Visibility = Visibility.Collapsed;
        var erros = new List<Control>();
        void Checar(Control campo, TextBlock erro, bool ok, string msg) { if (!Formulario.Validar(campo, erro, ok, msg)) erros.Add(campo); }

        var nome = Nome.Text.Trim();
        var usuario = Usuario.Text.Trim().ToLowerInvariant();
        var perfil = (Funcao.SelectedItem as ComboBoxItem)?.Tag as string ?? "operador";
        if (_modo != Modo.Senha)
        {
            Checar(Nome, NomeErro, nome.Length > 0, "Digite o nome do funcionário.");
            if (_modo == Modo.Novo)
            {
                var repetido = _todos.Any(f => f.Usuario == usuario);
                Checar(Usuario, UsuarioErro, Regex.IsMatch(usuario, @"^[a-z0-9][a-z0-9._-]{2,39}$") && !repetido,
                    repetido ? "Já existe um funcionário com este usuário." : "Use de 3 a 40 letras sem acento, números, ponto ou traço (ex.: ana.caixa).");
            }
        }
        if (_modo != Modo.Editar)
        {
            Checar(Senha, SenhaErro, Senha.Password.Length >= FuncionarioService.SenhaMinima, $"Use pelo menos {FuncionarioService.SenhaMinima} caracteres.");
            Checar(SenhaRepetida, SenhaRepetidaErro, SenhaRepetida.Password == Senha.Password, "As duas senhas não são iguais.");
        }
        if (erros.Count > 0)
        {
            erros[0].Focus();
            return;
        }

        BotaoSalvar.IsEnabled = false;
        try
        {
            switch (_modo)
            {
                case Modo.Novo:
                    await _servico.CriarAsync(usuario, nome, perfil, Senha.Password);
                    await _tela.RecarregarAsync($"{nome} foi cadastrado. Usuário: {usuario}.");
                    break;
                case Modo.Editar:
                    await _servico.EditarAsync(_funcionario!.Id, nome, perfil, _funcionario.Ativo);
                    await _tela.RecarregarAsync($"{nome} foi atualizado.");
                    break;
                case Modo.Senha:
                    await _servico.RedefinirSenhaAsync(_funcionario!.Id, Senha.Password);
                    await _tela.RecarregarAsync($"Senha de {_funcionario.Nome} redefinida.");
                    break;
            }
        }
        catch (ConexaoException ex)
        {
            AvisoErroTexto.Text = ex.Message;
            AvisoErro.Visibility = Visibility.Visible;
            BotaoSalvar.IsEnabled = true;
        }
    }
}
