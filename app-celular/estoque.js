// Aba Estoque do app (pedido do Guilherme em 05/11): busca, situação de cada produto (sem estoque, baixo, perto do mínimo),
// validade, lotes, reservado no caixa e leitor de código de barras pela câmera. Mesmas regras do programa (Models/Estoque.cs).
// Os dados (produtos, fornecedores, pedidos pendentes) também servem para a aba Pedir (pedidos.js).
"use strict";

const CAMPOS_PRODUTO = "id,nome,categoria,preco,unidade_medida,estoque_minimo,estoque_aviso,unidades_por_caixa,codigo_barras_caixa," +
  "codigo_balanca,fornecedor_id,prazo_entrega_dias,lotes(id,quantidade_inicial,quantidade_restante,data_compra,validade)";
const DIAS_VALIDADE = 30;

const est = { produtos: null, fornecedores: [], vinculos: [], pendentes: new Set(), lidoEm: 0, filtro: "todos", busca: "", aberto: null };

/** Lê produtos, lotes, reservas, fornecedores e pedidos pendentes (no máximo uma vez por minuto, a não ser forçado). */
async function carregarEstoqueDados(forcar = false) {
  if (est.produtos && !forcar && Date.now() - est.lidoEm < 60000) return null;
  const [produtos, reservas, fornecedores, vinculos, pendentes] = await Promise.all([
    sb.from("produtos").select(CAMPOS_PRODUTO).order("nome"),
    sb.rpc("reservados_hoje"),
    sb.from("fornecedores").select("id,nome,telefone,whatsapp,contato,categorias,prazo_entrega_dias,dias_entrega,modelo_mensagem,ativo").order("nome"),
    sb.from("produto_fornecedores").select("produto_id,fornecedor_id"),
    sb.from("pedido_itens").select("produto_id,pedidos!inner(situacao)").eq("pedidos.situacao", "pendente"),
  ]);
  if (produtos.error) return produtos.error;
  const reservado = new Map((reservas.data ?? []).map((r) => [r.produto_id, Number(r.quantidade)]));
  const hoje = iso(hojeLocal());
  const limite = iso(somarDias(hojeLocal(), DIAS_VALIDADE));
  est.produtos = produtos.data.map((p) => {
    const kg = p.unidade_medida === "kg";
    const lotes = (p.lotes ?? []).filter((l) => Number(l.quantidade_restante) > 0)
      .sort((a, b) => (a.validade ?? "9999").localeCompare(b.validade ?? "9999"));
    const qtd = lotes.reduce((s, l) => s + Number(l.quantidade_restante), 0);
    const minimo = Number(p.estoque_minimo);
    const aviso = p.estoque_aviso != null ? Number(p.estoque_aviso) : kg ? Math.ceil(minimo * 15) / 10 : Math.ceil(minimo * 1.5);
    const situacao = qtd <= 0 ? "sem" : qtd < minimo ? "baixo" : qtd <= aviso && aviso > minimo ? "aviso" : "ok";
    const vencido = lotes.some((l) => l.validade && l.validade < hoje);
    const proxima = lotes.find((l) => l.validade && l.validade >= hoje)?.validade ?? null;
    return {
      ...p, kg, lotes, qtd, minimo, aviso, situacao, vencido, proxima,
      validadeProxima: proxima != null && proxima <= limite,
      reservado: reservado.get(p.id) ?? 0,
      porCaixa: Number(p.unidades_por_caixa ?? 1),
    };
  });
  est.fornecedores = fornecedores.data ?? [];
  est.vinculos = vinculos.data ?? [];
  est.pendentes = new Set((pendentes.data ?? []).map((i) => i.produto_id));
  est.lidoEm = Date.now();
  return null;
}

function formatarQtd(p, q) {
  return p.kg ? `${Number(q).toLocaleString("pt-BR", { maximumFractionDigits: 3 })} kg` : `${Number(q).toLocaleString("pt-BR")} un.`;
}

function dataBr(texto) { return texto ? deIso(texto).toLocaleDateString("pt-BR") : "—"; }

function diasAte(texto) { return Math.round((deIso(texto) - hojeLocal()) / 86400000); }

const SITUACOES = {
  sem: ["Sem estoque", "perigo"], baixo: ["Estoque baixo", "perigo"], aviso: ["Perto do mínimo", "atencao"], ok: ["Em dia", "ok"],
};

const FILTROS_ESTOQUE = [
  ["todos", "Todos", () => true],
  ["repor", "Repor", (p) => p.situacao !== "ok"],
  ["sem", "Sem estoque", (p) => p.situacao === "sem"],
  ["validade", "Validade próxima", (p) => p.validadeProxima],
  ["vencidos", "Vencidos", (p) => p.vencido],
];

function normalizar(t) { return String(t ?? "").toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, ""); }

async function carregarEstoque(forcar = false) {
  const alvo = $("abaEstoque");
  if (!est.produtos) alvo.innerHTML = `<p class="carregando">Carregando o estoque…</p>`;
  const erro = await carregarEstoqueDados(forcar);
  if (erro) { alvo.innerHTML = `<div class="aviso perigo">${esc(mensagemDeErro(erro))}</div>`; return; }
  mostrarEstoque();
}

function mostrarEstoque() {
  const todos = est.produtos ?? [];
  const contar = (f) => todos.filter(f).length;
  const filtro = FILTROS_ESTOQUE.find((f) => f[0] === est.filtro) ?? FILTROS_ESTOQUE[0];
  const termo = normalizar(est.busca);
  const lista = todos.filter(filtro[2]).filter((p) => !termo || normalizar(p.nome).includes(termo) || normalizar(p.categoria).includes(termo)
    || p.codigo_barras_caixa === est.busca.trim());
  const leitor = "BarcodeDetector" in window;

  $("abaEstoque").innerHTML = `
    <h2>Estoque</h2><p class="sub">${plural(todos.length, "produto", "produtos")} · atualizado às ${new Date(est.lidoEm).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" })}</p>
    <div class="busca-linha">
      <input id="buscaEstoque" type="search" placeholder="Buscar produto ou categoria" value="${esc(est.busca)}" aria-label="Buscar produto">
      <button class="fantasma" id="botaoCamera" type="button" aria-label="Ler código de barras com a câmera" title="${leitor ? "Ler código de barras" : "Este navegador não lê código de barras; use a busca"}" ${leitor ? "" : "disabled"}>
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 7V4h3M21 7V4h-3M3 17v3h3M21 17v3h-3M7 8v8M10 8v8M13 8v8M17 8v8"/></svg></button>
    </div>
    <div class="chips">${FILTROS_ESTOQUE.map(([id, nome, f]) => {
      const n = contar(f);
      return id !== "todos" && n === 0 ? "" : `<button class="chip ${id === est.filtro ? "ativo" : ""}" data-filtro="${id}">${nome} · ${n}</button>`;
    }).join("")}</div>
    ${lista.length ? lista.map(cartaoProduto).join("") : `<p class="vazio">Nenhum produto encontrado.</p>`}`;

  const busca = $("buscaEstoque");
  busca.addEventListener("input", () => {
    est.busca = busca.value;
    const pos = busca.selectionStart;
    mostrarEstoque();
    const nova = $("buscaEstoque");
    nova.focus();
    nova.setSelectionRange(pos, pos);
  });
  for (const b of document.querySelectorAll("[data-filtro]")) b.onclick = () => { est.filtro = b.dataset.filtro; mostrarEstoque(); };
  for (const d of document.querySelectorAll("#abaEstoque details"))
    d.addEventListener("toggle", () => { if (d.open) est.aberto = Number(d.dataset.id); });
  if (leitor) $("botaoCamera").onclick = abrirLeitor;
}

function cartaoProduto(p) {
  const [rotulo, classe] = SITUACOES[p.situacao];
  const disponivel = Math.max(0, p.qtd - p.reservado);
  const fornecedor = est.fornecedores.find((f) => f.id === p.fornecedor_id)?.nome;
  const validade = p.vencido ? `<span class="selo perigo">Lote vencido</span>`
    : p.validadeProxima ? `<span class="selo atencao">Vence em ${diasAte(p.proxima)} dia(s)</span>` : "";
  const lotes = p.lotes.length ? p.lotes.map((l) => {
    const venc = l.validade && l.validade < iso(hojeLocal());
    return `<tr><td>${formatarQtd(p, l.quantidade_restante)} <small>de ${formatarQtd(p, l.quantidade_inicial)}</small></td>
      <td>${dataBr(l.data_compra)}</td><td class="${venc ? "txt-perigo" : ""}">${l.validade ? dataBr(l.validade) : "sem validade"}</td></tr>`;
  }).join("") : `<tr><td colspan="3">Nenhum lote com unidades.</td></tr>`;
  return `<details class="cartao produto" data-id="${p.id}" ${est.aberto === p.id ? "open" : ""}>
    <summary>
      <span class="bolinha-sit ${classe}" aria-hidden="true"></span>
      <span class="produto-nome"><b>${esc(p.nome)}</b><small>${esc(p.categoria)} · ${rotulo}</small></span>
      <span class="produto-qtd"><b>${formatarQtd(p, p.qtd)}</b><small>${moedaReal(p.preco)}${p.kg ? "/kg" : ""}</small></span>
    </summary>
    <div class="produto-corpo">
      ${validade}
      <div class="dados">
        <div><small>Mínimo</small>${formatarQtd(p, p.minimo)}</div>
        <div><small>Avisar com</small>${formatarQtd(p, p.aviso)}</div>
        <div><small>Reservado no caixa</small>${formatarQtd(p, p.reservado)}</div>
        <div><small>Disponível</small>${formatarQtd(p, disponivel)}</div>
        <div><small>Fornecedor</small>${esc(fornecedor ?? "—")}</div>
        <div><small>Em pedido pendente</small>${est.pendentes.has(p.id) ? "Sim" : "Não"}</div>
      </div>
      <table class="lotes"><thead><tr><th>Restante</th><th>Compra</th><th>Validade</th></tr></thead><tbody>${lotes}</tbody></table>
    </div>
  </details>`;
}

// ---------------------------------------------------------------- Leitor de código de barras (câmera)

let leitorAtivo = null;

/** EAN-13 da unidade a partir do DUN-14 da caixa: tira o 1º dígito e refaz o verificador (regra do programa). */
function eanDaCaixa(dun) {
  if (!/^\d{14}$/.test(dun ?? "")) return null;
  const doze = dun.slice(1, 13);
  const soma = [...doze].reduce((s, c, i) => s + Number(c) * (i % 2 === 0 ? 1 : 3), 0);
  return doze + ((10 - (soma % 10)) % 10);
}

function produtoDoCodigo(codigo) {
  const ps = est.produtos ?? [];
  return ps.find((p) => p.codigo_barras_caixa === codigo)
    ?? ps.find((p) => eanDaCaixa(p.codigo_barras_caixa) === codigo)
    // Etiqueta da balança: 2 + código do produto (4 ou 5 dígitos) + preço
    ?? (/^2\d{12}$/.test(codigo) ? ps.find((p) => p.codigo_balanca != null && (Number(codigo.slice(1, 5)) === p.codigo_balanca || Number(codigo.slice(1, 6)) === p.codigo_balanca)) : null);
}

async function abrirLeitor() {
  const tela = $("telaLeitor");
  let stream;
  try {
    stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" }, audio: false });
  } catch {
    toast("Não foi possível abrir a câmera. Libere a câmera para o app nas configurações do celular.");
    return;
  }
  const video = $("videoLeitor");
  video.srcObject = stream;
  tela.hidden = false;
  await video.play().catch(() => {});
  const detector = new BarcodeDetector({ formats: ["ean_13", "ean_8", "code_128", "itf", "upc_a", "upc_e"] });
  const fechar = () => {
    clearInterval(leitorAtivo);
    leitorAtivo = null;
    stream.getTracks().forEach((t) => t.stop());
    tela.hidden = true;
  };
  $("fecharLeitor").onclick = fechar;
  leitorAtivo = setInterval(async () => {
    let codigos;
    try { codigos = await detector.detect(video); } catch { return; }
    if (!codigos.length) return;
    const codigo = codigos[0].rawValue;
    fechar();
    if (navigator.vibrate) navigator.vibrate(80);
    const p = produtoDoCodigo(codigo);
    if (!p) { toast(`Nenhum produto com o código ${codigo}.`); return; }
    est.busca = p.nome;
    est.filtro = "todos";
    est.aberto = p.id;
    mostrarEstoque();
    document.querySelector(`#abaEstoque details[data-id="${p.id}"]`)?.scrollIntoView({ block: "center" });
  }, 300);
}
