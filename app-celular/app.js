// Mercado Gerência (semana 6): app de celular do gerente (estoque.js, pedidos.js e trava.js completam). Usa o mesmo banco do programa
// (Supabase, chave pública) e as funções do script 009: resumo_dia, analise_vendas e mensagens_nao_lidas.
// As regras do banco (RLS) garantem que só o gerente vê as vendas.
"use strict";

const sb = supabase.createClient(MERCADO.supabaseUrl, MERCADO.chavePublica, {
  auth: { persistSession: true, autoRefreshToken: true, storageKey: "mercado-gerencia" },
});

const $ = (id) => document.getElementById(id);
const fmtMoeda = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
const NOMES_FORMAS = {
  dinheiro: "Dinheiro", pix: "Pix", debito: "Débito", credito: "Crédito", vale_alimentacao: "Vale-alimentação", vale_refeicao: "Vale-refeição",
};
const DIAS = ["domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado"];

const estado = {
  perfil: null,
  dia: hojeLocal(),
  agrupar: "dia",
  ocultar: lerLocal("ocultar") === "1",
  resumo: null,
  analise: null,
  aba: "Hoje",
};

// ---------------------------------------------------------------- Ajudantes

function lerLocal(chave) { try { return localStorage.getItem("mercado-" + chave); } catch { return null; } }
function gravarLocal(chave, valor) { try { localStorage.setItem("mercado-" + chave, valor); } catch { /* modo privado */ } }

function hojeLocal() {
  const d = new Date();
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}
function iso(d) { return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`; }
function deIso(texto) { const [a, m, d] = texto.split("-").map(Number); return new Date(a, m - 1, d); }
function somarDias(d, n) { const r = new Date(d); r.setDate(r.getDate() + n); return r; }
function mesmoDia(a, b) { return iso(a) === iso(b); }

function moeda(v) { return estado.ocultar ? "R$ •••••" : fmtMoeda.format(Number(v ?? 0)); }
function moedaReal(v) { return fmtMoeda.format(Number(v ?? 0)); }
function numero(v) { return estado.ocultar ? "•••" : Number(v ?? 0).toLocaleString("pt-BR"); }
function pct(v) { return `${v >= 0 ? "+" : "−"}${Math.abs(v * 100).toFixed(0)}%`; }
function plural(n, um, varios) { return `${Number(n).toLocaleString("pt-BR")} ${n === 1 ? um : varios}`; }
function hora(texto) { return texto ? new Date(texto).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" }) : "—"; }
function esc(t) { return String(t ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c])); }

function nomeDoDia(d) {
  const texto = d.toLocaleDateString("pt-BR", { weekday: "long", day: "numeric", month: "long" });
  const hoje = hojeLocal();
  return mesmoDia(d, hoje) ? `Hoje, ${texto}` : mesmoDia(d, somarDias(hoje, -1)) ? `Ontem, ${texto}` : texto[0].toUpperCase() + texto.slice(1);
}

let timerToast;
function toast(texto) {
  const t = $("toast");
  t.textContent = texto;
  t.hidden = false;
  clearTimeout(timerToast);
  timerToast = setTimeout(() => (t.hidden = true), 3200);
}

function mensagemDeErro(e) {
  const m = String(e?.message ?? e ?? "");
  if (/Failed to fetch|NetworkError|network/i.test(m)) return "Sem internet agora. Tente de novo em instantes.";
  if (/JWT|expired|token/i.test(m)) return "Sua sessão expirou. Entre de novo.";
  if (/does not exist|PGRST202/i.test(m)) return "O banco ainda não tem as funções da semana 6 (script 009).";
  return m || "Não foi possível carregar. Tente de novo.";
}

function kpi(rotulo, valor, sub = "") {
  return `<div class="kpi"><div class="kpi-rotulo">${esc(rotulo)}</div><div class="kpi-valor">${esc(valor)}</div><div class="kpi-sub">${esc(sub)}</div></div>`;
}

function barra(rotulo, valor, maximo, texto) {
  const largura = maximo > 0 ? Math.max(0, (valor / maximo) * 100) : 0;
  return `<div class="barra"><span title="${esc(rotulo)}">${esc(rotulo)}</span><span class="trilho-barra"><i style="width:${largura.toFixed(1)}%"></i></span><b>${esc(texto)}</b></div>`;
}

/** Colunas no tempo (esquerda → direita); o último ponto (hoje) em destaque. */
function colunas(pontos, destaque = pontos.length - 1) {
  if (!pontos.length) return "";
  const maximo = Math.max(...pontos.map((p) => p.valor), 0);
  const passo = Math.max(1, Math.ceil(pontos.length / 7));
  const barras = pontos.map((p, i) =>
    `<div class="${i === destaque ? "destaque" : ""}" style="height:${maximo > 0 ? ((p.valor / maximo) * 100).toFixed(1) : 0}%" title="${esc(p.dica)}"></div>`).join("");
  const rotulos = pontos.map((p, i) => `<span>${i % passo === 0 || i === pontos.length - 1 ? esc(p.rotulo) : ""}</span>`).join("");
  return `<div class="colunas" role="img" aria-label="Gráfico de colunas">${barras}</div><div class="rotulos">${rotulos}</div>`;
}

// ---------------------------------------------------------------- Entrar e sair

const BLOQUEIO_MIN = 5;

async function entrar(evento) {
  evento.preventDefault();
  const usuario = $("usuario").value.trim().toLowerCase();
  const senha = $("senha").value;
  const erro = $("erroLogin");
  erro.textContent = "";
  if (!usuario || !senha) { erro.textContent = "Digite o usuário e a senha."; return; }

  // Mesma regra do programa: 5 senhas erradas seguidas bloqueiam por 5 minutos (neste aparelho)
  const bloqueio = JSON.parse(lerLocal("bloqueio-" + usuario) ?? "{}");
  if (bloqueio.ate && Date.now() < bloqueio.ate) {
    erro.textContent = `Usuário bloqueado por senhas erradas. Tente de novo em ${Math.ceil((bloqueio.ate - Date.now()) / 60000)} minuto(s).`;
    return;
  }

  $("botaoEntrar").disabled = true;
  const email = usuario.includes("@") ? usuario : `${usuario}@mercado.local`;
  const { error } = await sb.auth.signInWithPassword({ email, password: senha });
  // Cada tentativa vai para a auditoria (a auditoria não impede o login)
  sb.from("auditoria_login").insert({ usuario, sucesso: !error }).then(() => {}, () => {});
  $("botaoEntrar").disabled = false;

  if (error) {
    const falhas = (bloqueio.falhas ?? 0) + 1;
    gravarLocal("bloqueio-" + usuario, JSON.stringify(falhas >= 5 ? { ate: Date.now() + BLOQUEIO_MIN * 60000 } : { falhas }));
    erro.textContent = /fetch|network/i.test(error.message) ? "Sem internet agora." : falhas >= 5
      ? `Usuário bloqueado por ${BLOQUEIO_MIN} minutos.` : "Usuário ou senha incorretos.";
    return;
  }
  gravarLocal("bloqueio-" + usuario, "{}");
  gravarLocal("usuario", usuario);
  $("senha").value = "";
  await abrir();
}

async function abrir() {
  const { data: { session } } = await sb.auth.getSession();
  if (!session) return mostrarLogin();
  const { data: perfil, error } = await sb.from("perfis").select("nome,usuario,perfil,ativo").eq("id", session.user.id).maybeSingle();
  if (error) { mostrarLogin(mensagemDeErro(error)); return; }
  if (!perfil || !perfil.ativo || perfil.perfil !== "gerente") {
    await sb.auth.signOut();
    mostrarLogin("Este app é só para o gerente. O operador usa o caixa no computador.");
    return;
  }
  estado.perfil = perfil;
  // Trava ligada: pede a digital, o rosto ou o PIN do celular antes de mostrar qualquer coisa
  if (travaLigada() && !(await desbloquear())) return;
  $("telaLogin").hidden = true;
  $("telaApp").hidden = false;
  $("ocultar").checked = estado.ocultar;
  trocarAba(estado.aba);
  carregarAvisos(true);
}

function mostrarLogin(mensagem = "") {
  $("telaApp").hidden = true;
  $("telaLogin").hidden = false;
  $("usuario").value = lerLocal("usuario") ?? "";
  $("erroLogin").textContent = mensagem;
  ($("usuario").value ? $("senha") : $("usuario")).focus();
}

async function sair() {
  desligarTrava();
  await sb.auth.signOut();
  estado.resumo = estado.analise = null;
  mostrarLogin();
}

// ---------------------------------------------------------------- Abas

function trocarAba(aba) {
  estado.aba = aba;
  for (const b of document.querySelectorAll(".abas button")) b.classList.toggle("ativo", b.dataset.aba === aba);
  for (const nome of ["Hoje", "Analises", "Estoque", "Pedir", "Avisos", "Conta"]) $("aba" + nome).hidden = nome !== aba;
  window.scrollTo(0, 0);
  if (aba === "Hoje") carregarDia();
  if (aba === "Analises") carregarAnalise();
  if (aba === "Estoque") carregarEstoque();
  if (aba === "Pedir") carregarPedir();
  if (aba === "Avisos") carregarAvisos();
  if (aba === "Conta") mostrarConta();
}

// ---------------------------------------------------------------- Hoje (resumo do dia)

async function carregarDia() {
  const alvo = $("abaHoje");
  if (!estado.resumo) alvo.innerHTML = `<p class="carregando">Carregando o dia…</p>`;
  const { data, error } = await sb.rpc("resumo_dia", { p_dia: iso(estado.dia) });
  if (error) { alvo.innerHTML = `<div class="aviso perigo">${esc(mensagemDeErro(error))}</div>`; return; }
  estado.resumo = data;
  mostrarDia();
}

function mostrarDia() {
  const r = estado.resumo;
  const hoje = mesmoDia(estado.dia, hojeLocal());
  const abertos = r.caixas.filter((c) => !c.fechado_em);
  const [selo, classe] = r.fechamento && !r.fechamento.automatico ? [`Dia fechado por ${r.fechamento.fechado_por} às ${hora(r.fechamento.fechado_em)}`, "ok"]
    : r.fechamento?.automatico ? ["Resumo automático enviado; falta fechar", "atencao"]
    : hoje ? ["Dia em andamento", "info"] : ["Dia não fechado", "atencao"];
  const anterior = Number(r.semana_anterior?.total ?? 0);
  const variacao = anterior > 0 ? (Number(r.total) - anterior) / anterior : null;
  const margem = Number(r.vendido_com_custo) > 0 ? (Number(r.vendido_com_custo) - Number(r.custo)) / Number(r.vendido_com_custo) : null;
  const dif = Number(r.diferenca_caixas ?? 0);
  const algumFechado = r.caixas.some((c) => c.diferenca != null);

  const formas = Object.entries(r.formas ?? {}).sort((a, b) => b[1] - a[1]);
  const maxForma = Math.max(0, ...formas.map((f) => Number(f[1])));
  const maxProduto = Math.max(0, ...(r.mais_vendidos ?? []).map((p) => Number(p.valor)));

  let porHora = "";
  if ((r.por_hora ?? []).length) {
    const de = Math.min(...r.por_hora.map((h) => h.hora)), ate = Math.max(...r.por_hora.map((h) => h.hora));
    const pontos = [];
    for (let h = de; h <= ate; h++) {
      const x = r.por_hora.find((p) => p.hora === h);
      pontos.push({ rotulo: `${h}h`, valor: Number(x?.total ?? 0), dica: `${h}h: ${plural(x?.vendas ?? 0, "venda", "vendas")} · ${moeda(x?.total ?? 0)}` });
    }
    const pico = r.por_hora.reduce((a, b) => (Number(b.total) > Number(a.total) ? b : a));
    porHora = `<div class="cartao"><h3>Vendas por hora</h3><p class="sub">Mais movimento: ${pico.hora}h às ${pico.hora + 1}h.</p>
      ${colunas(pontos, pontos.findIndex((p) => p.rotulo === `${pico.hora}h`))}</div>`;
  }

  $("abaHoje").innerHTML = `
    <div class="dia-nav">
      <button class="fantasma" id="diaAntes" aria-label="Dia anterior">‹</button>
      <strong>${esc(nomeDoDia(estado.dia))}</strong>
      <button class="fantasma" id="diaDepois" aria-label="Próximo dia" ${hoje ? "disabled" : ""}>›</button>
    </div>
    <span class="selo ${classe}">${esc(selo)}</span>
    <div class="cartao">
      <div class="kpi-rotulo">Vendido</div>
      <div class="etiqueta-preco">${esc(moeda(r.total))}</div>
      <div class="sub" style="margin:0">${variacao != null && !estado.ocultar ? `${pct(variacao)} sobre ${DIAS[deIso(r.semana_anterior.dia).getDay()]} passada (${moeda(anterior)})` : `${plural(r.caixas.length, "caixa", "caixas")} no dia${abertos.length ? ` · ${abertos.length} aberto(s)` : ""}`}</div>
    </div>
    <div class="kpis">
      ${kpi("Vendas", numero(r.vendas), `${r.itens_vendidos} itens`)}
      ${kpi("Ticket médio", moeda(r.ticket_medio))}
      ${kpi("Descontos", moeda(Number(r.descontos) + Number(r.promocoes)), `promoções ${moeda(r.promocoes)}`)}
      ${kpi("Gaveta", !algumFechado ? "—" : dif === 0 ? "Bateu" : (dif > 0 ? "+" : "−") + moedaReal(Math.abs(dif)), "sobra ou falta")}
      ${margem != null ? kpi("Margem", estado.ocultar ? "•••" : `${(margem * 100).toFixed(0)}%`, "onde há custo") : ""}
    </div>
    <div class="cartao"><h3>Caixas</h3>
      ${r.caixas.length ? r.caixas.map((c) => {
        const d = c.diferenca == null ? null : Number(c.diferenca);
        const gaveta = d == null ? (c.fechado_em ? "" : "ainda aberto") : d === 0 ? "gaveta bateu" : d > 0 ? `sobrou ${moedaReal(d)}` : `faltou ${moedaReal(-d)}`;
        const cor = d == null ? "" : d < 0 ? "perigo" : d > 0 ? "atencao" : "ok";
        return `<div class="linha-caixa"><div><b>${esc(c.operador_nome)}</b><small>${hora(c.aberto_em)} às ${c.fechado_em ? hora(c.fechado_em) : "—"} · ${plural(c.vendas, "venda", "vendas")}</small>
          ${gaveta ? `<span class="selo ${cor || "info"}" style="margin:6px 0 0">${esc(gaveta)}</span>` : ""}</div><b>${esc(moeda(c.total))}</b></div>`;
      }).join("") : `<p class="vazio">Nenhum caixa aberto neste dia.</p>`}
    </div>
    <div class="cartao"><h3>Formas de pagamento</h3>
      ${formas.length ? formas.map(([f, v]) => barra(NOMES_FORMAS[f] ?? f, Number(v), maxForma, moeda(v))).join("") : `<p class="vazio">Nenhum pagamento.</p>`}
    </div>
    <div class="cartao"><h3>Mais vendidos</h3>
      ${(r.mais_vendidos ?? []).length ? r.mais_vendidos.map((p, i) => barra(`${i + 1}. ${p.nome}`, Number(p.valor), maxProduto,
        estado.ocultar ? `${Number(p.quantidade).toLocaleString("pt-BR")} ${p.unidade}` : moeda(p.valor))).join("") : `<p class="vazio">Nenhum produto vendido.</p>`}
    </div>
    ${porHora}
    ${(r.itens_cancelados ?? []).length || r.canceladas ? `<div class="cartao"><h3>Cancelamentos</h3>
      ${r.canceladas ? `<p>${plural(r.canceladas, "venda paga cancelada", "vendas pagas canceladas")} (${moeda(r.canceladas_valor)}).</p>` : ""}
      ${r.itens_cancelados.map((i) => `<p class="sub" style="margin:4px 0">${hora(i.criado_em)} · ${esc(i.nome)} (${moeda(i.valor)}) · ${esc(i.operador_nome)}${i.autorizado_por ? ` · autorizado por ${esc(i.autorizado_por)}` : ""}</p>`).join("")}</div>` : ""}
    <p class="sub">Atualizado às ${new Date().toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" })}. Para fechar o dia, use o programa no computador.</p>`;
  $("diaAntes").onclick = () => { estado.dia = somarDias(estado.dia, -1); estado.resumo = null; carregarDia(); };
  $("diaDepois").onclick = () => { if (!hoje) { estado.dia = somarDias(estado.dia, 1); estado.resumo = null; carregarDia(); } };
}

// ---------------------------------------------------------------- Análises

async function carregarAnalise() {
  const alvo = $("abaAnalises");
  const hoje = hojeLocal();
  let inicio;
  if (estado.agrupar === "semana") { inicio = somarDias(hoje, -((hoje.getDay() + 6) % 7) - 7 * 11); }
  else if (estado.agrupar === "mes") { inicio = new Date(hoje.getFullYear(), hoje.getMonth() - 11, 1); }
  else inicio = somarDias(hoje, -29);
  if (!estado.analise) alvo.innerHTML = `<p class="carregando">Carregando as análises…</p>`;
  const { data, error } = await sb.rpc("analise_vendas", { p_inicio: iso(inicio), p_fim: iso(hoje), p_agrupar: estado.agrupar });
  if (error) { alvo.innerHTML = `<div class="aviso perigo">${esc(mensagemDeErro(error))}</div>`; return; }
  estado.analise = data;
  mostrarAnalise();
}

function mostrarAnalise() {
  const a = estado.analise;
  const nome = { dia: "Por dia", semana: "Por semana", mes: "Por mês" };
  const rotulo = (p) => {
    const d = deIso(p);
    return a.agrupar === "mes" ? d.toLocaleDateString("pt-BR", { month: "short" }).replace(".", "") : `${String(d.getDate()).padStart(2, "0")}/${String(d.getMonth() + 1).padStart(2, "0")}`;
  };
  const pontos = a.serie.map((p) => ({ rotulo: rotulo(p.periodo), valor: Number(p.total), dica: `${rotulo(p.periodo)}: ${moeda(p.total)} em ${plural(p.vendas, "venda", "vendas")}` }));
  const variacao = Number(a.anterior.total) > 0 ? (Number(a.total) - Number(a.anterior.total)) / Number(a.anterior.total) : null;
  const margem = Number(a.vendido_com_custo) > 0 ? (Number(a.vendido_com_custo) - Number(a.custo)) / Number(a.vendido_com_custo) : null;
  const maxCat = Math.max(0, ...a.categorias.map((c) => Number(c.valor)));
  const diasSemana = [1, 2, 3, 4, 5, 6, 0].map((d) => {
    const x = a.dias_semana.find((w) => w.dia_semana === d);
    return { d, media: x && x.dias > 0 ? Number(x.total) / x.dias : 0 };
  });
  const maxDia = Math.max(0, ...diasSemana.map((x) => x.media));

  $("abaAnalises").innerHTML = `
    <div class="chips">${["dia", "semana", "mes"].map((g) => `<button class="chip ${g === a.agrupar ? "ativo" : ""}" data-agrupar="${g}">${nome[g]}</button>`).join("")}</div>
    <div class="kpis">
      ${kpi("Vendido", moeda(a.total), variacao != null && !estado.ocultar ? `${pct(variacao)} sobre o período anterior` : "no período")}
      ${kpi("Vendas", numero(a.vendas), `ticket ${moeda(a.vendas ? a.total / a.vendas : 0)}`)}
      ${kpi("Margem", margem == null ? "—" : estado.ocultar ? "•••" : `${(margem * 100).toFixed(0)}%`, "onde há custo")}
      ${kpi("Perdas", moedaReal(a.perdas.valor), plural(a.perdas.lotes, "lote vencido", "lotes vencidos"))}
    </div>
    <div class="cartao"><h3>${nome[a.agrupar]}</h3>${colunas(pontos)}</div>
    <div class="cartao"><h3>Por categoria</h3>
      ${a.categorias.length ? a.categorias.slice(0, 10).map((c) => barra(c.categoria, Number(c.valor), maxCat, moeda(c.valor))).join("") : `<p class="vazio">Sem vendas no período.</p>`}</div>
    <div class="cartao"><h3>Dias da semana</h3><p class="sub">Média vendida em cada dia (só os dias com venda).</p>
      ${diasSemana.map((x) => barra(DIAS[x.d], x.media, maxDia, moeda(x.media))).join("")}</div>
    <div class="cartao"><h3>Cancelamentos</h3><p class="sub" style="margin:0">${plural(a.cancelamentos.vendas, "venda paga cancelada", "vendas pagas canceladas")} e
      ${plural(a.cancelamentos.itens, "item tirado", "itens tirados")} das vendas (${moeda(a.cancelamentos.valor_itens)}).</p></div>`;
  for (const b of document.querySelectorAll("[data-agrupar]"))
    b.onclick = () => { estado.agrupar = b.dataset.agrupar; carregarAnalise(); };
}

// ---------------------------------------------------------------- Avisos

async function carregarAvisos(soContar = false) {
  const hoje = iso(hojeLocal());
  const [mensagens, atrasados, produtos, dia] = await Promise.all([
    sb.rpc("mensagens_nao_lidas"),
    sb.from("pedidos").select("id,previsao_entrega,fornecedores(nome)").eq("situacao", "pendente").lt("previsao_entrega", hoje).order("previsao_entrega"),
    sb.from("produtos").select("nome,estoque_minimo,unidade_medida,lotes(quantidade_restante,validade)"),
    sb.rpc("resumo_dia", { p_dia: hoje }),
  ]);
  const avisos = [];
  for (const m of mensagens.data ?? [])
    avisos.push(["info", `${m.fornecedor_nome} ${m.pedido_id ? `respondeu sobre o pedido nº ${m.pedido_id}` : "mandou mensagem"}${m.quantidade > 1 ? ` (${m.quantidade} mensagens)` : ""}.`, `Chegou às ${hora(m.ultima_em)} · abra em Fornecedores no computador`]);
  for (const p of atrasados.data ?? [])
    avisos.push(["perigo", `Pedido nº ${p.id} de ${p.fornecedores?.nome ?? "fornecedor"} está atrasado.`, `Previsto para ${deIso(p.previsao_entrega).toLocaleDateString("pt-BR")}`]);
  for (const c of dia.data?.caixas ?? []) {
    const d = c.diferenca == null ? 0 : Number(c.diferenca);
    if (d !== 0) avisos.push([d < 0 ? "perigo" : "atencao", `Caixa de ${c.operador_nome} fechou com ${d < 0 ? "falta" : "sobra"} de ${moedaReal(Math.abs(d))}.`, c.obs ? `“${c.obs}”` : "Hoje"]);
  }
  const semEstoque = [], baixos = [], vencidos = [];
  for (const p of produtos.data ?? []) {
    const lotes = p.lotes ?? [];
    const qtd = lotes.reduce((s, l) => s + Number(l.quantidade_restante), 0);
    if (qtd <= 0) semEstoque.push(p.nome);
    else if (qtd < Number(p.estoque_minimo)) baixos.push(p.nome);
    if (lotes.some((l) => Number(l.quantidade_restante) > 0 && l.validade && l.validade < hoje)) vencidos.push(p.nome);
  }
  const lista = (nomes) => nomes.slice(0, 6).join(", ") + (nomes.length > 6 ? ` e mais ${nomes.length - 6}` : "");
  if (vencidos.length) avisos.push(["perigo", `${plural(vencidos.length, "produto tem", "produtos têm")} lote vencido.`, lista(vencidos)]);
  if (semEstoque.length) avisos.push(["perigo", `${plural(semEstoque.length, "produto está", "produtos estão")} sem estoque.`, lista(semEstoque)]);
  if (baixos.length) avisos.push(["atencao", `${plural(baixos.length, "produto está", "produtos estão")} abaixo do mínimo.`, lista(baixos)]);

  const conta = $("contaAvisos");
  conta.hidden = avisos.length === 0;
  conta.textContent = avisos.length > 9 ? "9+" : String(avisos.length);
  if (soContar) return;
  const erro = [mensagens, atrasados, produtos].find((r) => r.error)?.error;
  $("abaAvisos").innerHTML = `<h2>Avisos</h2><p class="sub">Estoque, pedidos, caixas e respostas dos fornecedores.</p>
    ${erro ? `<div class="aviso perigo">${esc(mensagemDeErro(erro))}</div>` : ""}
    ${avisos.length ? avisos.map(([c, t, s]) => `<div class="aviso ${c}"><div>${esc(t)}<small>${esc(s)}</small></div></div>`).join("") : `<p class="vazio">Nenhum aviso agora.</p>`}`;
}

// ---------------------------------------------------------------- Conta

let pedidoInstalar = null;
window.addEventListener("beforeinstallprompt", (e) => { e.preventDefault(); pedidoInstalar = e; if (estado.aba === "Conta") mostrarConta(); });

async function mostrarConta() {
  const p = estado.perfil;
  const podeTravar = await travaDisponivel();
  $("abaConta").innerHTML = `<h2>${esc(p?.nome ?? "")}</h2><p class="sub">Gerente · usuário ${esc(p?.usuario ?? "")}</p>
    <div class="cartao"><h3>Trava do app</h3>
      <p class="sub">O app fica conectado neste celular. Com a trava ligada, ao abrir (ou depois de ${TRAVA_MINUTOS} minutos fora) ele pede a digital,
        o rosto ou o PIN do celular, em vez da senha do sistema.</p>
      ${podeTravar ? `<label class="interruptor escuro"><input id="chaveTrava" type="checkbox" ${travaLigada() ? "checked" : ""}><span class="trilho" aria-hidden="true"></span>
        <span>Pedir digital, rosto ou PIN</span></label>` : `<p class="sub" style="margin:0">Este celular ou navegador não oferece desbloqueio por digital, rosto ou PIN.</p>`}
    </div>
    <div class="cartao"><h3>Sobre o app</h3><p class="sub" style="margin:0">Vendas, caixas, análises, estoque, avisos e pedidos de reposição pelo WhatsApp.
      Vender, fechar o caixa, fechar o dia e responder os fornecedores continuam no programa do computador. O interruptor "Ocultar" no topo esconde os valores.</p></div>
    ${pedidoInstalar ? `<button class="primario" id="instalar" style="margin-bottom:12px">Instalar o app neste celular</button>` : ""}
    <button class="fantasma" id="botaoSair" style="width:100%">Sair</button>`;
  $("botaoSair").onclick = sair;
  if (podeTravar)
    $("chaveTrava").onchange = async (e) => {
      if (e.target.checked) {
        const ok = await ligarTrava();
        e.target.checked = ok;
        toast(ok ? "Trava ligada. Na próxima vez o app pede o desbloqueio do celular." : "Não foi possível ligar a trava.");
      } else {
        desligarTrava();
        toast("Trava desligada.");
      }
    };
  if (pedidoInstalar) $("instalar").onclick = async () => { pedidoInstalar.prompt(); await pedidoInstalar.userChoice; pedidoInstalar = null; mostrarConta(); };
}

// ---------------------------------------------------------------- Início

$("formLogin").addEventListener("submit", entrar);
$("botaoConta").addEventListener("click", () => trocarAba("Conta"));
// Tocar no M da tela de entrada mostra os usuários de teste (como o Alt+1 do programa). Só o usuário: senha não vai no app.
$("logoTeste").addEventListener("click", () => {
  const lista = $("usuariosTeste");
  lista.hidden = !lista.hidden;
  $("logoTeste").setAttribute("aria-expanded", String(!lista.hidden));
});
for (const b of document.querySelectorAll("[data-usuario]"))
  b.addEventListener("click", () => {
    $("usuario").value = b.dataset.usuario;
    $("usuariosTeste").hidden = true;
    $("senha").focus();
  });
for (const b of document.querySelectorAll(".abas button")) b.addEventListener("click", () => trocarAba(b.dataset.aba));
$("ocultar").addEventListener("change", (e) => {
  estado.ocultar = e.target.checked;
  gravarLocal("ocultar", estado.ocultar ? "1" : "0");
  if (estado.resumo && estado.aba === "Hoje") mostrarDia();
  if (estado.analise && estado.aba === "Analises") mostrarAnalise();
  if (estado.aba === "Avisos") carregarAvisos();
});
sb.auth.onAuthStateChange((evento) => { if (evento === "SIGNED_OUT") mostrarLogin(); });

// Atualiza sozinho a cada minuto enquanto o app está aberto na tela
setInterval(() => {
  if (document.hidden || $("telaApp").hidden) return;
  if (estado.aba === "Hoje" && mesmoDia(estado.dia, hojeLocal())) carregarDia();
  carregarAvisos(estado.aba !== "Avisos");
}, 60000);
document.addEventListener("visibilitychange", () => { if (!document.hidden && !$("telaApp").hidden) trocarAba(estado.aba); });

if ("serviceWorker" in navigator) navigator.serviceWorker.register("sw.js").catch(() => {});
abrir();
