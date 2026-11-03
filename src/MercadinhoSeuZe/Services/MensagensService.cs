using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MercadinhoSeuZe.Models;

namespace MercadinhoSeuZe.Services;

/// <summary>
/// Mensagens com os fornecedores (semana 6, script 009). Os e-mails chegam pela Edge Function ler-emails (lê a caixa
/// de pedidos a cada 5 minutos) e saem pela enviar-email; a senha do e-mail fica só nos segredos do Supabase.
/// Sem o script 009, TemMensagens = false e o ícone de mensagens some.
/// </summary>
public sealed class MensagensService(SupabaseApi api, Sessao sessao)
{
    public bool TemMensagens { get; private set; } = true;

    public string Usuario => sessao.Nome;

    /// <summary>Não lidas por fornecedor. Vazio sem o script 009 ou sem internet.</summary>
    public async Task<List<NaoLidas>> NaoLidasAsync()
    {
        if (!TemMensagens) return [];
        try
        {
            return await api.ChamarAsync<List<NaoLidas>>("mensagens_nao_lidas", new { }, sessao.Token) ?? [];
        }
        catch (TabelaInexistenteException)
        {
            TemMensagens = false;
            return [];
        }
    }

    /// <summary>Conversa com o fornecedor, da mais antiga para a mais nova (últimas 200).</summary>
    public async Task<List<MensagemFornecedor>> ConversaAsync(long fornecedorId)
    {
        var lista = await api.ListarAsync<MensagemFornecedor>(
            $"fornecedor_mensagens?fornecedor_id=eq.{fornecedorId}&select=*&order=criada_em.desc,id.desc&limit=200", sessao.Token);
        lista.Reverse();
        return lista;
    }

    public Task MarcarLidasAsync(long fornecedorId) =>
        api.ChamarAsync<object>("marcar_mensagens_lidas", new { p_fornecedor_id = fornecedorId }, sessao.Token);

    /// <summary>Resposta que chegou pelo WhatsApp (ou mensagem mandada por lá), colada para ficar na conversa.</summary>
    public async Task RegistrarWhatsappAsync(long fornecedorId, long? pedidoId, string texto, bool recebida) =>
        await api.ChamarAsync<long>("registrar_mensagem_whatsapp",
            new { p_fornecedor_id = fornecedorId, p_pedido_id = pedidoId, p_corpo = texto, p_direcao = recebida ? "recebida" : "enviada" }, sessao.Token);

    /// <summary>
    /// Manda um e-mail ao fornecedor pela caixa de pedidos do mercado (Edge Function enviar-email). tipo "pedido" (o
    /// assunto leva "Pedido nº") ou "resposta" (responde a última mensagem da conversa). Devolve o e-mail de destino.
    /// Sem a função publicada: FuncaoInexistenteException (a janela abre o e-mail no programa do computador).
    /// </summary>
    public async Task<string> EnviarEmailAsync(string tipo, long fornecedorId, long? pedidoId, string assunto, string texto)
    {
        var r = await api.FuncaoAsync<RespostaEnvio>("enviar-email",
            new { tipo, fornecedor_id = fornecedorId, pedido_id = pedidoId, assunto, corpo = texto }, sessao.Token);
        return r?.Para ?? "";
    }

    /// <summary>Link temporário (10 minutos) para baixar um anexo guardado no Storage. Só o gerente consegue.</summary>
    public async Task<string> LinkDoAnexoAsync(AnexoMensagem anexo)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"storage/v1/object/sign/anexos-fornecedores/{Uri.EscapeDataString(anexo.Caminho).Replace("%2F", "/")}")
        {
            Content = JsonContent.Create(new { expiresIn = 600 })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessao.Token);
        try
        {
            using var resposta = await api.Http.SendAsync(req);
            if (!resposta.IsSuccessStatusCode) throw new ConexaoException("Não foi possível abrir o anexo. Ele pode ter sido apagado.");
            var link = await resposta.Content.ReadFromJsonAsync<LinkAssinado>();
            return api.Http.BaseAddress + "storage/v1" + link?.Url;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ConexaoException("Não foi possível conectar ao servidor. Verifique a internet.");
        }
    }

    private sealed record RespostaEnvio([property: System.Text.Json.Serialization.JsonPropertyName("para")] string? Para);

    private sealed record LinkAssinado([property: System.Text.Json.Serialization.JsonPropertyName("signedURL")] string? Url);
}
