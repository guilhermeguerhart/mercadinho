// Peças comuns das Edge Functions do Mercado (semana 6).
// Os segredos ficam nos "Secrets" das Edge Functions no painel do Supabase (nunca no .exe nem no git):
//   EMAIL_USUARIO         e-mail da caixa de pedidos (ex.: pedidos.mercado@gmail.com)
//   EMAIL_SENHA_APP       senha de app do Gmail (16 letras, criada em myaccount.google.com/apppasswords)
//   SEGREDO_AGENDAMENTO   texto qualquer e comprido; o mesmo vai no Vault do banco (script 010) para o pg_cron
//   EMAIL_SMTP / EMAIL_IMAP  opcionais (padrão smtp.gmail.com e imap.gmail.com)
// SUPABASE_URL, SUPABASE_ANON_KEY e SUPABASE_SERVICE_ROLE_KEY já vêm do próprio Supabase.

import { createClient, type SupabaseClient } from "npm:@supabase/supabase-js@2.45.4";

export const URL_SUPABASE = Deno.env.get("SUPABASE_URL")!;
const CHAVE_PUBLICA = Deno.env.get("SUPABASE_ANON_KEY") ?? Deno.env.get("CHAVE_PUBLICA") ?? "";
const CHAVE_SERVICO = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? Deno.env.get("CHAVE_SERVICO") ?? "";

export const EMAIL_USUARIO = Deno.env.get("EMAIL_USUARIO") ?? "";
export const EMAIL_SENHA = (Deno.env.get("EMAIL_SENHA_APP") ?? "").replace(/\s+/g, "");
export const SMTP = Deno.env.get("EMAIL_SMTP") ?? "smtp.gmail.com";
export const IMAP = Deno.env.get("EMAIL_IMAP") ?? "imap.gmail.com";
const SEGREDO = Deno.env.get("SEGREDO_AGENDAMENTO") ?? "";

export const cabecalhos = {
  "Content-Type": "application/json; charset=utf-8",
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type, x-segredo",
};

export function resposta(corpo: unknown, status = 200) {
  return new Response(JSON.stringify(corpo), { status, headers: cabecalhos });
}

export function erro(mensagem: string, status = 400) {
  return resposta({ erro: mensagem }, status);
}

/** Cliente com o token de quem está logado no programa: o banco aplica as regras do perfil (RLS). */
export function clienteDoUsuario(req: Request): SupabaseClient | null {
  const auth = req.headers.get("Authorization") ?? "";
  if (!auth.startsWith("Bearer ")) return null;
  return createClient(URL_SUPABASE, CHAVE_PUBLICA, {
    global: { headers: { Authorization: auth } },
    auth: { persistSession: false },
  });
}

/** Cliente do servidor (grava os e-mails que chegam e o envio). Só existe dentro da Edge Function. */
export function clienteDoServidor(): SupabaseClient {
  return createClient(URL_SUPABASE, CHAVE_SERVICO, { auth: { persistSession: false } });
}

/** Chamada do agendamento (pg_cron + pg_net, script 010): traz o segredo combinado no cabeçalho x-segredo. */
export function chamadaDoAgendamento(req: Request): boolean {
  return SEGREDO.length >= 16 && req.headers.get("x-segredo") === SEGREDO;
}

export function emailConfigurado(): boolean {
  return EMAIL_USUARIO.includes("@") && EMAIL_SENHA.length >= 8;
}

/** Valor em reais, como no programa. */
export function moeda(v: number | string | null | undefined): string {
  return Number(v ?? 0).toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

export function escapar(texto: string): string {
  return texto.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

/** "Pedido nº 42" (também "Pedido n° 42", "pedido no 42", "Pedido 42") no assunto ou no texto. */
export function numeroDoPedido(texto: string): number | null {
  const m = texto.match(/pedido\s*(?:n\s*[º°o.]?\s*)?#?\s*(\d{1,9})/i);
  return m ? Number(m[1]) : null;
}
