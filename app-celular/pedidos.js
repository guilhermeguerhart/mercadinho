// Aba Pedir do app (pedido do Guilherme em 05/11): os produtos que precisam de reposição, separados por fornecedor,
// com a quantidade sugerida. "Criar pedido" grava no sistema (função criar_pedido, a mesma do programa) e o WhatsApp
// abre com a mensagem pronta, com o "Pedido nº" para a resposta voltar ligada ao pedido. Regras de Models/Fornecedores.cs.
"use strict";

const MODELO_PADRAO = "Olá{, contato}! Aqui é {usuario}, do Mercado.\nGostaria de fazer um pedido:\n{itens}\nEntrega prevista: {previsao}.\nPode confirmar, por favor? Obrigado!";
const PRAZO_PADRAO = 3;

const ped = { quantidades: new Map(), desmarcados: new Set(), criados: new Map(), sugeridos: null, sugeridosEm: 0, modo: null };

/** "previsao" (padrão) ou "minimo" (a regra antiga); lembrado no aparelho. Sem a previsão no banco, fica no mínimo. */
function trocarModoPedir(modo) {
  ped.modo = modo;
  gravarLocal("modo-pedir", modo);
  mostrarPedir();
}
function emModoPrevisao() { return (ped.modo ??= lerLocal("modo-pedir") === "minimo" ? "minimo" : "previsao") === "previsao" && ped.sugeridos != null; }

/** Pedido sugerido pela previsão (scripts 019 e 022, o mesmo do programa). null = sem o script ou sem internet: regra do mínimo. */
async function carregarSugeridos(forcar = false) {
  if (ped.sugeridos && !forcar && Date.now() - ped.sugeridosEm < 300000) return ped.sugeridos;
  const { data, error } = await sb.rpc("pedido_sugerido");
  if (!error && Array.isArray(data)) { ped.sugeridos = data; ped.sugeridosEm = Date.now(); }
  return ped.sugeridos;
}

function diaCurto(texto) {
  const d = deIso(texto), hoje = hojeLocal();
  if (mesmoDia(d, hoje)) return "hoje";
  if (mesmoDia(d, somarDias(hoje, 1))) return "amanhã";
  return d.toLocaleDateString("pt-BR", { weekday: "short", day: "2-digit", month: "2-digit" }).replace(".", "");
}

function categoriasDe(f) { return String(f.categorias ?? "").split(",").map((c) => normalizar(c.trim())).filter(Boolean); }

/** Fornecedor do produto: o do cadastro, um dos "também compro de" ou o primeiro (A-Z) que entrega a categoria. */
function fornecedorDe(p) {
  const ativos = est.fornecedores.filter((f) => f.ativo);
  return ativos.find((f) => f.id === p.fornecedor_id)
    ?? ativos.find((f) => est.vinculos.some((v) => v.produto_id === p.id && v.fornecedor_id === f.id))
    ?? ativos.find((f) => categoriasDe(f).includes(normalizar(p.categoria)))
    ?? null;
}

/** Quanto pedir: o da previsão (quando houver) ou o dobro do mínimo menos o que tem (pelo menos o mínimo), em caixas inteiras. */
function quantidadeSugerida(p) {
  if (p.sugeridoPrevisao != null) return Number(p.sugeridoPrevisao);
  const q = Math.max(p.minimo * 2 - p.qtd, p.minimo, p.kg ? 1 : p.porCaixa);
  if (p.kg) return Math.ceil(q * 10) / 10;
  return p.porCaixa > 1 ? Math.ceil(q / p.porCaixa) * p.porCaixa : Math.ceil(q);
}

function proximaEntrega(f, data) {
  const dias = String(f.dias_entrega ?? "").split(",").filter((d) => d !== "").map(Number);
  let d = data;
  if (!dias.length) return d;
  while (!dias.includes(d.getDay())) d = somarDias(d, 1);
  return d;
}

function previsaoDoPedido(f, itens, s = null) {
  if (s?.chegada) return deIso(s.chegada);
  const prazo = Math.max(...itens.map((p) => p.prazo_entrega_dias ?? f.prazo_entrega_dias ?? PRAZO_PADRAO));
  return proximaEntrega(f, somarDias(hojeLocal(), prazo));
}

function mensagemDoPedido(f, linhas, previsao, numero) {
  const contato = String(f.contato ?? "").trim();
  const itens = linhas.map(({ p, q }) => {
    const qtd = p.kg ? `${Number(q).toLocaleString("pt-BR", { maximumFractionDigits: 3 })} kg` : `${q} un.`;
    const caixas = !p.kg && p.porCaixa > 1 && q % p.porCaixa === 0 ? ` (${q / p.porCaixa} ${q / p.porCaixa === 1 ? "caixa" : "caixas"})` : "";
    return `- ${qtd} ${p.nome}${caixas}`;
  }).join("\n");
  const texto = (String(f.modelo_mensagem ?? "").trim() || MODELO_PADRAO)
    .replaceAll("{, contato}", contato ? ", " + contato : "")
    .replaceAll("{contato}", contato)
    .replaceAll("{fornecedor}", f.nome)
    .replaceAll("{itens}", itens)
    .replaceAll("{previsao}", previsao.toLocaleDateString("pt-BR"))
    .replaceAll("{usuario}", estado.perfil?.nome ?? "");
  return `Pedido nº ${numero}\n\n${texto}`;
}

function numeroWhatsapp(f) {
  const d = String(f.whatsapp || f.telefone || "").replace(/\D/g, "");
  return d.length === 10 || d.length === 11 ? "55" + d : d;
}

async function carregarPedir(forcar = false) {
  const alvo = $("abaPedir");
  if (!est.produtos) alvo.innerHTML = `<p class="carregando">Carregando o que precisa repor…</p>`;
  const [erro] = await Promise.all([carregarEstoqueDados(forcar), carregarSugeridos(forcar)]);
  if (erro) { alvo.innerHTML = `<div class="aviso perigo">${esc(mensagemDeErro(erro))}</div>`; return; }
  mostrarPedir();
}

function gruposDeReposicao() {
  // Com a previsão: um grupo por fornecedor com produtos para pedir (sem pedido pendente dele); hoje primeiro
  if (emModoPrevisao()) {
    return ped.sugeridos
      .filter((s) => s.itens.length && !s.tem_pendente)
      .sort((a, b) => (b.pedir_hoje - a.pedir_hoje) || a.data_pedido.localeCompare(b.data_pedido) || a.fornecedor.localeCompare(b.fornecedor))
      .map((s) => ({
        f: est.fornecedores.find((f) => f.id === s.fornecedor_id) ?? null, s,
        itens: s.itens.map((i) => {
          const p = (est.produtos ?? []).find((x) => x.id === i.produto_id);
          return p ? { ...p, sugeridoPrevisao: i.sugerido, item: i } : null;
        }).filter(Boolean),
      }))
      .filter((g) => g.f && g.itens.length);
  }
  const grupos = new Map();
  for (const p of est.produtos ?? []) {
    if (p.situacao === "ok" || est.pendentes.has(p.id)) continue;
    const f = fornecedorDe(p);
    const chave = f?.id ?? 0;
    if (!grupos.has(chave)) grupos.set(chave, { f, itens: [] });
    grupos.get(chave).itens.push(p);
  }
  const ordem = { sem: 0, baixo: 1, aviso: 2 };
  for (const g of grupos.values()) g.itens.sort((a, b) => ordem[a.situacao] - ordem[b.situacao] || a.nome.localeCompare(b.nome));
  return [...grupos.values()].sort((a, b) => (a.f ? 0 : 1) - (b.f ? 0 : 1) || (a.f?.nome ?? "").localeCompare(b.f?.nome ?? ""));
}

function mostrarPedir() {
  const grupos = gruposDeReposicao();
  const criados = [...ped.criados.values()];
  $("abaPedir").innerHTML = `
    <h2>Pedir</h2>
    ${ped.sugeridos ? `<div class="chips">
      <button class="chip ${emModoPrevisao() ? "ativo" : ""}" data-modo="previsao" aria-pressed="${emModoPrevisao()}">Pela previsão</button>
      <button class="chip ${emModoPrevisao() ? "" : "ativo"}" data-modo="minimo" aria-pressed="${!emModoPrevisao()}">Abaixo do mínimo</button></div>` : ""}
    <p class="sub">${emModoPrevisao()
      ? "Pela previsão de vendas: quanto pedir de cada fornecedor para durar até a entrega seguinte. Produto sem histórico de vendas vem pelo estoque mínimo."
      : "Produtos sem estoque, abaixo do mínimo ou perto dele, que ainda não estão em pedido pendente."}</p>
    ${criados.map((c) => `<div class="aviso ok-borda"><div>Pedido nº ${c.id} para <b>${esc(c.f.nome)}</b> criado.
      <small>Envie a mensagem: o WhatsApp abre com o pedido pronto.</small>
      <div class="botoes-linha"><a class="primario link-botao" href="https://wa.me/${numeroWhatsapp(c.f)}?text=${encodeURIComponent(c.texto)}" target="_blank" rel="noopener">Enviar pelo WhatsApp</a>
      <button class="fantasma" data-copiar="${c.id}">Copiar</button></div></div></div>`).join("")}
    ${grupos.length ? grupos.map(cartaoFornecedor).join("") : `<p class="vazio">Nada para repor agora.</p>`}`;

  for (const b of document.querySelectorAll("[data-modo]")) b.onclick = () => trocarModoPedir(b.dataset.modo);
  for (const c of document.querySelectorAll("[data-qtd]"))
    c.addEventListener("input", () => ped.quantidades.set(Number(c.dataset.qtd), c.value));
  for (const c of document.querySelectorAll("[data-marcar]"))
    c.addEventListener("change", () => { const id = Number(c.dataset.marcar); c.checked ? ped.desmarcados.delete(id) : ped.desmarcados.add(id); });
  for (const b of document.querySelectorAll("[data-criar]")) b.onclick = () => criarPedido(Number(b.dataset.criar), b);
  for (const b of document.querySelectorAll("[data-copiar]"))
    b.onclick = async () => {
      try { await navigator.clipboard.writeText(ped.criados.get(Number(b.dataset.copiar)).texto); toast("Mensagem copiada."); }
      catch { toast("Não foi possível copiar."); }
    };
}

function cartaoFornecedor(g) {
  const f = g.f;
  const linhas = g.itens.map((p) => {
    const [rotulo, classe] = SITUACOES[p.situacao] ?? ["em dia", "ok"];
    const q = ped.quantidades.get(p.id) ?? String(quantidadeSugerida(p)).replace(".", ",");
    const porque = p.item ? (p.item.regra === "minimo" ? " · sem histórico: pelo mínimo" : ` · vende uns ${formatarQtd(p, p.item.previsto)} até ${diaCurto(p.item.ate)}`) : "";
    return `<label class="item-pedido">
      <input type="checkbox" data-marcar="${p.id}" ${ped.desmarcados.has(p.id) ? "" : "checked"} ${f ? "" : "disabled"}>
      <span class="produto-nome"><b>${esc(p.nome)}</b><small><span class="txt-${classe}">${rotulo}</span> · tem ${formatarQtd(p, p.qtd)}, mínimo ${formatarQtd(p, p.minimo)}${esc(porque)}</small></span>
      <span class="qtd-pedido"><input inputmode="decimal" data-qtd="${p.id}" value="${esc(q)}" aria-label="Quantidade de ${esc(p.nome)}" ${f ? "" : "disabled"}><small>${p.kg ? "kg" : "un."}</small></span>
    </label>`;
  }).join("");
  if (!f) return `<div class="cartao"><h3>Sem fornecedor</h3><p class="sub">Ligue estes produtos a um fornecedor no programa (Estoque > Editar).</p>${linhas}</div>`;
  const previsao = previsaoDoPedido(f, g.itens, g.s);
  const selo = g.s ? (g.s.pedir_hoje ? `<span class="selo atencao">Pedir hoje</span>` : `<span class="selo info">Próximo pedido: ${esc(diaCurto(g.s.data_pedido))}</span>`) : "";
  return `<div class="cartao"><h3>${esc(f.nome)}</h3>${selo}
    <p class="sub">${plural(g.itens.length, "produto", "produtos")}${g.s?.total > 0 && !estado.ocultar ? ` · ≈ ${moedaReal(g.s.total)}` : ""} · chega ${previsao.toLocaleDateString("pt-BR", { weekday: "short", day: "2-digit", month: "2-digit" })}${g.s ? ` e precisa durar até ${esc(diaCurto(g.s.proxima_entrega))}` : ""}</p>
    ${linhas}
    <button class="primario" data-criar="${f.id}" style="margin-top:12px">Criar pedido e abrir o WhatsApp</button></div>`;
}

function lerQuantidade(texto, kg) {
  const v = Number(String(texto ?? "").replace(/\./g, "").replace(",", "."));
  if (!Number.isFinite(v) || v <= 0) return null;
  return kg ? Math.round(v * 1000) / 1000 : Number.isInteger(v) ? v : null;
}

async function criarPedido(fornecedorId, botao) {
  const g = gruposDeReposicao().find((x) => x.f?.id === fornecedorId);
  if (!g) return;
  const linhas = [];
  for (const p of g.itens) {
    if (ped.desmarcados.has(p.id)) continue;
    const q = lerQuantidade(ped.quantidades.get(p.id) ?? String(quantidadeSugerida(p)), p.kg);
    if (q == null) { toast(`Quantidade inválida em ${p.nome}${p.kg ? "" : " (use número inteiro)"}.`); return; }
    linhas.push({ p, q });
  }
  if (!linhas.length) { toast("Marque pelo menos um produto."); return; }
  const previsao = previsaoDoPedido(g.f, linhas.map((l) => l.p), g.s);
  botao.disabled = true;
  const { data: id, error } = await sb.rpc("criar_pedido", {
    p_fornecedor_id: g.f.id, p_previsao: iso(previsao), p_observacao: null,
    p_itens: linhas.map(({ p, q }) => ({ produto_id: p.id, quantidade: q, custo: null })),
  });
  if (error) { botao.disabled = false; toast(mensagemDeErro(error)); return; }
  const texto = mensagemDoPedido(g.f, linhas, previsao, id);
  ped.criados.set(id, { id, f: g.f, texto });
  for (const { p } of linhas) { ped.quantidades.delete(p.id); est.pendentes.add(p.id); }
  if (ped.sugeridos) await carregarSugeridos(true);   // o pedido novo já sai da sugestão
  mostrarPedir();
  window.scrollTo(0, 0);
  toast(`Pedido nº ${id} criado. Toque em "Enviar pelo WhatsApp".`);
}
