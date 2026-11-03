// Edge Function ler-emails (semana 6): lê a caixa de pedidos (IMAP) e grava as respostas dos fornecedores em
// fornecedor_mensagens, ligadas ao pedido pelo "Pedido nº 42" do assunto ou do texto. Chamada a cada 5 minutos pelo
// agendamento do banco (pg_cron + pg_net, script 010) com o cabeçalho x-segredo.
// Só grava e-mails de remetentes cadastrados como fornecedor (ou que citam um pedido existente); o resto fica na caixa.
// Anexos (nota fiscal em PDF ou XML) vão para o Storage, bucket privado "anexos-fornecedores".
// Publicar: npx supabase functions deploy ler-emails --no-verify-jwt

import { ImapFlow } from "npm:imapflow@1.0.164";
import { simpleParser } from "npm:mailparser@3.7.1";
import {
  cabecalhos, chamadaDoAgendamento, clienteDoServidor, EMAIL_SENHA, EMAIL_USUARIO, emailConfigurado, erro, IMAP, numeroDoPedido, resposta,
} from "../_compartilhado/comum.ts";

const MAXIMO_POR_VEZ = 30;
const MAXIMO_ANEXO = 10 * 1024 * 1024;

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: cabecalhos });
  if (!chamadaDoAgendamento(req)) return erro("Sem permissão.", 401);
  if (!emailConfigurado()) return erro("A caixa de e-mail do mercado ainda não foi configurada (EMAIL_USUARIO e EMAIL_SENHA_APP).", 503);

  const servidor = clienteDoServidor();
  const { data: fornecedores, error } = await servidor.from("fornecedores").select("id,nome,email").not("email", "is", null);
  if (error) return erro(error.message, 500);
  const porEmail = new Map<string, number>();
  for (const f of fornecedores ?? []) porEmail.set(String(f.email).trim().toLowerCase(), f.id);

  const cliente = new ImapFlow({ host: IMAP, port: 993, secure: true, auth: { user: EMAIL_USUARIO, pass: EMAIL_SENHA }, logger: false });
  let gravadas = 0, ignoradas = 0;
  try {
    await cliente.connect();
    const trava = await cliente.getMailboxLock("INBOX");
    try {
      const desde = new Date(Date.now() - 14 * 24 * 3600 * 1000);
      const uids = ((await cliente.search({ seen: false, since: desde }, { uid: true })) || []).slice(-MAXIMO_POR_VEZ);
      for (const uid of uids) {
        const msg = await cliente.fetchOne(String(uid), { source: true }, { uid: true });
        if (!msg?.source) continue;
        const email = await simpleParser(msg.source);
        const de = (email.from?.value?.[0]?.address ?? "").toLowerCase();
        if (de === EMAIL_USUARIO.toLowerCase()) continue;   // cópia do que o próprio mercado mandou

        const assunto = email.subject ?? "";
        const texto = semCitacao(email.text ?? "");
        let fornecedorId = porEmail.get(de) ?? null;
        let pedidoId = numeroDoPedido(assunto) ?? numeroDoPedido(texto);
        if (pedidoId != null) {
          const { data: p } = await servidor.from("pedidos").select("id,fornecedor_id").eq("id", pedidoId).maybeSingle();
          if (!p || (fornecedorId != null && p.fornecedor_id !== fornecedorId)) pedidoId = null;
          else fornecedorId ??= p.fornecedor_id;
        }
        if (fornecedorId == null) {
          ignoradas++;
          continue;   // não é de fornecedor: fica sem ler na caixa para alguém olhar
        }

        const messageId = email.messageId ?? `<sem-id-${uid}@${IMAP}>`;
        const anexos = [];
        for (const a of email.attachments ?? []) {
          if (!a.filename || a.size > MAXIMO_ANEXO || a.contentDisposition === "inline") continue;
          const caminho = `${fornecedorId}/${Date.now()}-${uid}/${a.filename.replace(/[^\w.\-]+/g, "_")}`;
          const { error: erroAnexo } = await servidor.storage.from("anexos-fornecedores")
            .upload(caminho, a.content, { contentType: a.contentType, upsert: true });
          if (!erroAnexo) anexos.push({ nome: a.filename, caminho, tipo: a.contentType, tamanho: a.size });
        }

        const { error: erroGravar } = await servidor.from("fornecedor_mensagens").upsert({
          fornecedor_id: fornecedorId, pedido_id: pedidoId, direcao: "recebida", canal: "email", de, para: EMAIL_USUARIO,
          assunto: assunto.slice(0, 300), corpo: (texto || "(e-mail sem texto)").slice(0, 20000), message_id: messageId.slice(0, 300),
          em_resposta_a: (typeof email.inReplyTo === "string" ? email.inReplyTo : null)?.slice(0, 300) ?? null,
          anexos, criada_em: (email.date ?? new Date()).toISOString(),
        }, { onConflict: "message_id", ignoreDuplicates: true });
        if (erroGravar) {
          console.error(erroGravar);
          continue;   // tenta de novo na próxima leitura
        }
        await cliente.messageFlagsAdd(String(uid), ["\\Seen"], { uid: true });
        gravadas++;
      }
    } finally {
      trava.release();
    }
    await cliente.logout();
  } catch (e) {
    console.error(e);
    const msg = e instanceof Error ? e.message : String(e);
    return erro(/auth|credentials|login/i.test(msg) ? "O Gmail recusou a senha de app da caixa de pedidos." : "Não foi possível ler a caixa de e-mail agora.", 502);
  }
  return resposta({ gravadas, ignoradas });
});

/** Tira a mensagem antiga citada embaixo da resposta ("Em ... escreveu:", "On ... wrote:", linhas com ">"). */
function semCitacao(texto: string): string {
  const linhas = texto.replace(/\r\n/g, "\n").split("\n");
  const saida: string[] = [];
  for (const l of linhas) {
    if (/^\s*(Em|On)\s.+(escreveu|wrote)\s*:\s*$/i.test(l) || /^-{2,}\s*(Mensagem original|Original Message)/i.test(l) || /^De:\s.+/i.test(l) && saida.length > 0) break;
    if (/^\s*>/.test(l)) continue;
    saida.push(l);
  }
  return saida.join("\n").trim();
}
