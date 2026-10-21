using System.Diagnostics;
using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

/// <summary>
/// Contato rápido com o fornecedor: abre o WhatsApp (app ou WhatsApp Web) ou o programa de e-mail já com a
/// mensagem escrita. A pessoa só confere e aperta enviar; o programa não manda nada sozinho.
/// </summary>
public static class ContatoRapido
{
    /// <summary>Abre a conversa do fornecedor no WhatsApp com o texto preenchido. false se não abriu.</summary>
    public static bool AbrirWhatsapp(Fornecedor f, string mensagem) =>
        Abrir($"https://wa.me/{f.NumeroWhatsapp}?text={Uri.EscapeDataString(mensagem)}");

    /// <summary>Abre um e-mail novo para o fornecedor com assunto e texto preenchidos. false se não abriu.</summary>
    public static bool AbrirEmail(Fornecedor f, string assunto, string mensagem) =>
        !string.IsNullOrWhiteSpace(f.Email) &&
        Abrir($"mailto:{f.Email!.Trim()}?subject={Uri.EscapeDataString(assunto)}&body={Uri.EscapeDataString(mensagem.Replace("\n", "\r\n"))}");

    public static bool Copiar(string texto)
    {
        try
        {
            System.Windows.Clipboard.SetText(texto);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;   // área de transferência ocupada por outro programa
        }
    }

    private static bool Abrir(string endereco)
    {
        try
        {
            Process.Start(new ProcessStartInfo(endereco) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;   // nenhum navegador ou programa de e-mail configurado
        }
    }
}
