// Resumo do dia em e-mail (HTML simples, com as cores do programa) e em texto puro.
import { escapar, moeda } from "../_compartilhado/comum.ts";

const NOMES_FORMAS: Record<string, string> = {
  dinheiro: "Dinheiro", pix: "Pix", debito: "Cartão de débito", credito: "Cartão de crédito",
  vale_alimentacao: "Vale-alimentação", vale_refeicao: "Vale-refeição",
};

const hora = (iso: string | null) =>
  iso ? new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit", timeZone: "America/Sao_Paulo" }) : "—";

const dataBr = (dia: string) => {
  const [a, m, d] = String(dia).split("-");
  return `${d}/${m}/${a}`;
};

function gaveta(c: Record<string, any>): string {
  if (c.diferenca == null) return c.fechado_em ? "—" : "não fechou";
  const d = Number(c.diferenca);
  return d === 0 ? "bateu" : d > 0 ? `sobra de ${moeda(d)}` : `falta de ${moeda(-d)}`;
}

export function resumoEmTexto(r: Record<string, any>): string {
  const linhas: string[] = [];
  linhas.push(`Fechamento do dia ${dataBr(r.dia)}`, "");
  linhas.push(`Vendido: ${moeda(r.total)} em ${r.vendas} vendas (ticket médio ${moeda(r.ticket_medio)})`);
  if (r.semana_anterior) linhas.push(`Mesmo dia da semana passada: ${moeda(r.semana_anterior.total)}`);
  linhas.push(`Descontos: ${moeda(r.descontos)} · Promoções: ${moeda(r.promocoes)}`, "", "Formas de pagamento:");
  for (const [f, v] of Object.entries(r.formas ?? {}).sort((a, b) => Number(b[1]) - Number(a[1]))) linhas.push(`- ${NOMES_FORMAS[f] ?? f}: ${moeda(v as number)}`);
  linhas.push("", "Caixas:");
  for (const c of r.caixas ?? []) linhas.push(`- ${c.operador_nome}: ${moeda(c.total)} (${c.vendas} vendas), gaveta ${gaveta(c)}`);
  if ((r.mais_vendidos ?? []).length) {
    linhas.push("", "Mais vendidos:");
    for (const p of r.mais_vendidos.slice(0, 5)) linhas.push(`- ${p.nome}: ${p.quantidade} ${p.unidade}, ${moeda(p.valor)}`);
  }
  if (r.fechamento?.obs) linhas.push("", `Observação: ${r.fechamento.obs}`);
  return linhas.join("\n");
}

export function resumoEmHtml(r: Record<string, any>): string {
  const tinta = "#1F2633", suave = "#5A6372", linha = "#DDE1E7", marinho = "#1F3354", etiqueta = "#A8C8EC";
  const td = `padding:8px 10px;border-bottom:1px solid ${linha};font-size:14px`;
  const anterior = r.semana_anterior?.total > 0
    ? (() => {
      const v = (Number(r.total) - Number(r.semana_anterior.total)) / Number(r.semana_anterior.total);
      return `${v >= 0 ? "+" : "−"}${Math.abs(v * 100).toFixed(0)}% sobre a semana passada (${moeda(r.semana_anterior.total)})`;
    })()
    : "";
  const kpi = (rotulo: string, valor: string, sub = "") =>
    `<td style="padding:12px 14px;border-right:1px dashed ${linha};vertical-align:top">
       <div style="font-size:12px;color:${suave};font-weight:600">${rotulo}</div>
       <div style="font-size:22px;font-weight:800;color:${tinta};margin-top:2px">${valor}</div>
       <div style="font-size:12px;color:${suave}">${sub}</div></td>`;
  const formas = Object.entries(r.formas ?? {}).sort((a, b) => Number(b[1]) - Number(a[1]))
    .map(([f, v]) => `<tr><td style="${td}">${escapar(NOMES_FORMAS[f] ?? f)}</td><td style="${td};text-align:right;font-weight:600">${moeda(v as number)}</td></tr>`).join("");
  const caixas = (r.caixas ?? []).map((c: any) => {
    const d = c.diferenca == null ? null : Number(c.diferenca);
    const cor = d == null ? suave : d < 0 ? "#B5371C" : d > 0 ? "#9A6700" : "#2F6B3F";
    return `<tr><td style="${td}"><b>${escapar(c.operador_nome)}</b><br><span style="color:${suave};font-size:12px">${hora(c.aberto_em)} às ${hora(c.fechado_em)}</span></td>
      <td style="${td};text-align:right">${c.vendas}</td><td style="${td};text-align:right;font-weight:600">${moeda(c.total)}</td>
      <td style="${td};text-align:right;color:${cor};font-weight:600">${gaveta(c)}${c.obs ? `<br><span style="font-weight:400;font-size:12px">“${escapar(c.obs)}”</span>` : ""}</td></tr>`;
  }).join("");
  const produtos = (r.mais_vendidos ?? []).slice(0, 10).map((p: any, i: number) =>
    `<tr><td style="${td}">${i + 1}. ${escapar(p.nome)}</td><td style="${td};text-align:right">${Number(p.quantidade).toLocaleString("pt-BR")} ${p.unidade}</td><td style="${td};text-align:right;font-weight:600">${moeda(p.valor)}</td></tr>`).join("");
  const cancelados = (r.itens_cancelados ?? []).map((i: any) =>
    `<li>${hora(i.criado_em)} · ${escapar(i.nome)} (${moeda(i.valor)}) · caixa ${escapar(i.operador_nome)}${i.autorizado_por ? ` · autorizado por ${escapar(i.autorizado_por)}` : ""}</li>`).join("");
  const margem = Number(r.vendido_com_custo) > 0
    ? `${(((Number(r.vendido_com_custo) - Number(r.custo)) / Number(r.vendido_com_custo)) * 100).toFixed(0)}%` : "—";
  const titulo = (t: string) => `<h2 style="font-size:16px;color:${tinta};margin:26px 0 8px;border-bottom:2px solid ${tinta};padding-bottom:4px">${t}</h2>`;

  return `<!doctype html><html><body style="margin:0;background:#F1F2F4;font-family:'Segoe UI',Arial,sans-serif;color:${tinta}">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:24px 12px">
  <table role="presentation" width="640" cellpadding="0" cellspacing="0" style="max-width:640px;background:#FFFFFF;border:1px solid ${linha};border-radius:8px;border-top:6px solid ${marinho}">
  <tr><td style="padding:22px 26px">
    <div style="font-size:13px;color:${suave};font-weight:600">MERCADO · FECHAMENTO DO DIA</div>
    <div style="font-size:24px;font-weight:800;margin-top:4px">${dataBr(r.dia)}</div>
    ${r.fechamento ? `<div style="font-size:13px;color:${suave};margin-top:2px">${r.fechamento.automatico ? "Resumo automático das 23h (o dia ainda não foi fechado)" : `Fechado por ${escapar(r.fechamento.fechado_por)} às ${hora(r.fechamento.fechado_em)}`}</div>` : ""}
    <div style="margin-top:16px;display:inline-block;background:${etiqueta};border-radius:3px;padding:4px 12px;font-size:28px;font-weight:800">${moeda(r.total)}</div>
    <div style="font-size:13px;color:${suave};margin-top:6px">${anterior}</div>
    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:18px;border:1px solid ${linha};border-radius:8px"><tr>
      ${kpi("Vendas", String(r.vendas), `${r.itens_vendidos} itens`)}${kpi("Ticket médio", moeda(r.ticket_medio))}
      ${kpi("Descontos", moeda(Number(r.descontos) + Number(r.promocoes)), `promoções ${moeda(r.promocoes)}`)}${kpi("Margem", margem, "onde há custo")}
    </tr></table>
    ${titulo("Formas de pagamento")}<table width="100%" cellpadding="0" cellspacing="0">${formas || `<tr><td style="${td}">Nenhum pagamento.</td></tr>`}</table>
    ${titulo("Caixas")}<table width="100%" cellpadding="0" cellspacing="0">
      <tr><td style="${td};color:${suave};font-size:12px;font-weight:700">CAIXA</td><td style="${td};color:${suave};font-size:12px;font-weight:700;text-align:right">VENDAS</td>
      <td style="${td};color:${suave};font-size:12px;font-weight:700;text-align:right">TOTAL</td><td style="${td};color:${suave};font-size:12px;font-weight:700;text-align:right">GAVETA</td></tr>
      ${caixas || `<tr><td style="${td}" colspan="4">Nenhum caixa aberto.</td></tr>`}</table>
    ${produtos ? `${titulo("Mais vendidos")}<table width="100%" cellpadding="0" cellspacing="0">${produtos}</table>` : ""}
    ${cancelados || Number(r.canceladas) > 0 ? `${titulo("Cancelamentos")}<div style="font-size:14px">${Number(r.canceladas) > 0 ? `${r.canceladas} venda(s) paga(s) cancelada(s) (${moeda(r.canceladas_valor)}).` : ""}<ul style="padding-left:18px">${cancelados}</ul></div>` : ""}
    ${r.fechamento?.obs ? `${titulo("Observação")}<div style="font-size:14px">${escapar(r.fechamento.obs)}</div>` : ""}
    <div style="font-size:12px;color:${suave};margin-top:26px">E-mail enviado pelo sistema do Mercado. Os detalhes estão na tela Fechamento do dia.</div>
  </td></tr></table></td></tr></table></body></html>`;
}
