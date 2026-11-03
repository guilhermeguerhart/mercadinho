// Edge Function enviar-email (semana 6): manda e-mails pela caixa de pedidos do mercado.
//   tipo "resumo_dia"        resumo do fechamento para os e-mails da configuração (gerente logado)
//   tipo "resumo_automatico" resumo das 23h quando ninguém fechou o dia (só o agendamento, com x-segredo)
//   tipo "pedido"            pedido para o fornecedor ("Pedido nº 42" no assunto) (gerente logado)
//   tipo "resposta"          resposta na conversa com o fornecedor (gerente logado)
// Publicar: npx supabase functions deploy enviar-email --no-verify-jwt   (o token é conferido aqui dentro)

import nodemailer from "npm:nodemailer@6.9.14";
import {
  cabecalhos, chamadaDoAgendamento, clienteDoServidor, clienteDoUsuario, EMAIL_SENHA, EMAIL_USUARIO, emailConfigurado, erro,
  escapar, moeda, resposta, SMTP,
} from "../_compartilhado/comum.ts";
import { resumoEmHtml, resumoEmTexto } from "./resumo.ts";

function transporte() {
  // Porta 465 (SSL): o Supabase bloqueia as portas 25 e 587 nas Edge Functions
  return nodemailer.createTransport({ host: SMTP, port: 465, secure: true, auth: { user: EMAIL_USUARIO, pass: EMAIL_SENHA } });
}

const remetente = () => `"Mercado" <${EMAIL_USUARIO}>`;

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: cabecalhos });
  if (req.method !== "POST") return erro("Use POST.", 405);
  if (!emailConfigurado()) return erro("A caixa de e-mail do mercado ainda não foi configurada no Supabase (EMAIL_USUARIO e EMAIL_SENHA_APP).", 503);

  let pedido: Record<string, unknown>;
  try {
    pedido = await req.json();
  } catch {
    return erro("Pedido inválido.");
  }
  const tipo = String(pedido.tipo ?? "");

  try {
    if (tipo === "resumo_automatico") {
      if (!chamadaDoAgendamento(req)) return erro("Sem permissão.", 401);
      return await resumoAutomatico();
    }

    const usuario = clienteDoUsuario(req);
    if (!usuario) return erro("Entre no sistema de novo.", 401);

    if (tipo === "resumo_dia") return await enviarResumo(usuario, String(pedido.dia ?? ""));
    if (tipo === "pedido" || tipo === "resposta") {
      const token = (req.headers.get("Authorization") ?? "").replace(/^Bearer\s+/i, "");
      return await enviarAoFornecedor(usuario, token, tipo, Number(pedido.fornecedor_id), pedido.pedido_id == null ? null : Number(pedido.pedido_id),
        String(pedido.assunto ?? ""), String(pedido.corpo ?? ""));
    }
    return erro("Tipo de e-mail desconhecido.");
  } catch (e) {
    console.error(e);
    const msg = e instanceof Error ? e.message : String(e);
    if (/Invalid login|Username and Password not accepted|EAUTH/i.test(msg))
      return erro("O Gmail recusou a senha da caixa de pedidos. Confira a senha de app nos segredos do Supabase.", 502);
    return erro("Não foi possível enviar o e-mail agora. Tente de novo em alguns minutos.", 502);
  }
});

// ---------------------------------------------------------------- Resumo do dia

async function enviarResumo(usuario: ReturnType<typeof clienteDoUsuario> & object, dia: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(dia)) return erro("Dia inválido.");
  // Com o token do gerente: o banco recusa quem não é gerente
  const { data: resumo, error } = await usuario.rpc("resumo_dia", { p_dia: dia });
  if (error) return erro(error.message, 403);
  const { data: config } = await usuario.from("config_mercado").select("emails_resumo").eq("id", 1).maybeSingle();
  const para = listaDeEmails(config?.emails_resumo);
  if (para.length === 0) return erro("Cadastre os e-mails que recebem o resumo (Fechamento do dia > Alterar).");

  const erroEnvio = await mandarResumo(resumo, para);
  await usuario.rpc("registrar_envio_resumo", { p_dia: dia, p_para: para.join(", "), p_erro: erroEnvio });
  if (erroEnvio) return erro(erroEnvio, 502);
  return resposta({ para: para.join(", ") });
}

async function resumoAutomatico() {
  const servidor = clienteDoServidor();
  const { data: config } = await servidor.from("config_mercado").select("emails_resumo,resumo_automatico").eq("id", 1).maybeSingle();
  if (!config?.resumo_automatico) return resposta({ ok: true, motivo: "resumo automático desligado" });
  const para = listaDeEmails(config.emails_resumo);
  if (para.length === 0) return resposta({ ok: true, motivo: "sem e-mails cadastrados" });

  // Grava a foto do dia só se ninguém fechou; se o gerente já fechou e o e-mail já saiu, não manda de novo
  const { data: resumo, error } = await servidor.rpc("fechamento_automatico", {});
  if (error) return erro(error.message, 500);
  if (resumo?.fechamento?.email_enviado_em) return resposta({ ok: true, motivo: "resumo já enviado" });

  const erroEnvio = await mandarResumo(resumo, para);
  await servidor.rpc("registrar_envio_resumo", { p_dia: resumo.dia, p_para: para.join(", "), p_erro: erroEnvio });
  return erroEnvio ? erro(erroEnvio, 502) : resposta({ para: para.join(", ") });
}

async function mandarResumo(resumo: Record<string, any>, para: string[]): Promise<string | null> {
  const [a, m, d] = String(resumo.dia).split("-");
  const automatico = resumo.fechamento?.automatico ? " (envio automático)" : "";
  try {
    await transporte().sendMail({
      from: remetente(),
      to: para.join(", "),
      subject: `Fechamento do dia ${d}/${m}/${a} - ${moeda(resumo.total)} em ${resumo.vendas} vendas${automatico}`,
      text: resumoEmTexto(resumo),
      html: resumoEmHtml(resumo),
    });
    return null;
  } catch (e) {
    console.error(e);
    return "O Gmail não aceitou o envio. Confira a senha de app da caixa de pedidos.";
  }
}

function listaDeEmails(texto: string | null | undefined): string[] {
  return (texto ?? "").split(/[,;\s]+/).map((e) => e.trim()).filter((e) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(e)).slice(0, 10);
}

// ---------------------------------------------------------------- Pedido e resposta ao fornecedor

async function enviarAoFornecedor(
  usuario: ReturnType<typeof clienteDoUsuario> & object, token: string, tipo: string, fornecedorId: number, pedidoId: number | null, assunto: string, corpo: string,
) {
  if (!Number.isFinite(fornecedorId)) return erro("Fornecedor inválido.");
  if (corpo.trim().length === 0 || corpo.length > 5000) return erro("Escreva a mensagem (até 5000 letras).");
  // Com o token do gerente: só o gerente lê os fornecedores (RLS); o destino vem do cadastro, não do programa
  const { data: fornecedor } = await usuario.from("fornecedores").select("id,nome,email").eq("id", fornecedorId).maybeSingle();
  if (!fornecedor) return erro("Fornecedor não encontrado ou sem permissão.", 403);
  if (!fornecedor.email) return erro(`${fornecedor.nome} não tem e-mail cadastrado.`);
  if (pedidoId != null) {
    const { data: p } = await usuario.from("pedidos").select("id").eq("id", pedidoId).eq("fornecedor_id", fornecedorId).maybeSingle();
    if (!p) return erro(`O pedido nº ${pedidoId} não é deste fornecedor.`);
  }
  const { data: quem } = await usuario.auth.getUser(token);
  const { data: perfil } = quem?.user ? await usuario.from("perfis").select("nome").eq("id", quem.user.id).maybeSingle() : { data: null };
  const nomeUsuario: string | null = perfil?.nome ?? null;

  const servidor = clienteDoServidor();
  // Resposta: liga à última mensagem recebida (o fornecedor vê na mesma conversa)
  let emResposta: string | null = null;
  if (tipo === "resposta") {
    let consulta = servidor.from("fornecedor_mensagens").select("message_id").eq("fornecedor_id", fornecedorId).eq("direcao", "recebida")
      .eq("canal", "email").not("message_id", "is", null).order("criada_em", { ascending: false }).limit(1);
    if (pedidoId != null) consulta = consulta.eq("pedido_id", pedidoId);
    const { data } = await consulta;
    emResposta = data?.[0]?.message_id ?? null;
  }
  const titulo = assunto.trim().slice(0, 250) || (pedidoId ? `Pedido nº ${pedidoId} - Mercado` : "Mercado");

  let messageId: string | null = null;
  let falha: string | null = null;
  try {
    const info = await transporte().sendMail({
      from: remetente(),
      to: fornecedor.email,
      subject: titulo,
      text: corpo,
      html: `<div style="font-family:Segoe UI,Arial,sans-serif;font-size:15px;white-space:pre-wrap">${escapar(corpo)}</div>`,
      ...(emResposta ? { inReplyTo: emResposta, references: [emResposta] } : {}),
    });
    messageId = info.messageId ?? null;
  } catch (e) {
    console.error(e);
    falha = "O Gmail não aceitou o envio.";
  }

  await servidor.from("fornecedor_mensagens").insert({
    fornecedor_id: fornecedorId, pedido_id: pedidoId, direcao: "enviada", canal: "email", de: EMAIL_USUARIO, para: fornecedor.email,
    assunto: titulo, corpo, message_id: messageId, em_resposta_a: emResposta, usuario_nome: nomeUsuario, lida_em: new Date().toISOString(), erro: falha,
  });
  if (falha) return erro(`${falha} Confira a senha de app da caixa de pedidos.`, 502);
  return resposta({ para: fornecedor.email });
}
