-- =====================================================================
-- Semana 7: Previsão de compras (pedido do Guilherme em 09/11: "focar totalmente nas previsões").
--
-- Separa a venda de cada produto em quatro partes e usa isso para prever os próximos dias:
--   tendência      para onde a venda vai (reta: nível + crescimento por ano)
--   sazonalidade   o que se repete: dia da semana, mês e as datas do calendário (eventos_calendario, script 012)
--   resíduo        o que sobra (a parte que não dá para explicar); o desvio padrão dele vira a faixa da previsão
--                  e, nas próximas partes, o estoque de segurança
--   treino e teste o modelo aprende com o passado até um dia de corte e é conferido no ano seguinte, que ele não viu
--
-- Modelo (multiplicativo, decomposição clássica feita em SQL):
--   venda prevista do dia = tendência(dia) × índice do dia da semana × índice do mês  (+ extra da data especial)
--   1. Loja: com o movimento (número de vendas pagas por dia) acha tendência, índices da semana e do mês e o
--      desvio do resíduo. Dias com data especial ficam de fora dessa conta (são medidos à parte no passo 3).
--   2. Produto: os mesmos passos com a quantidade vendida. Produto que vende pouco tem índices "puxados" para os
--      da categoria, e a categoria para os da loja (encolhimento: peso = unidades / (unidades + K)). O mesmo vale
--      para o crescimento por ano, puxado para o da loja.
--   3. Datas especiais: para cada produto e data (pelo nome: "Véspera de Natal", "Páscoa"...), a média do que
--      vendeu a mais do que o normal nos anos anteriores, medida em relação ao movimento esperado da loja naquele
--      dia (assim a mesma data num sábado rende mais que numa segunda, e o crescimento da loja entra junto). Data que nunca
--      aconteceu antes (ex.: jogo da Copa) usa o fator do calendário nos produtos e categorias da data.
--   4. Dias em que a loja nunca abre (Natal, 1º de janeiro, ou um dia da semana sem caixa) ficam com zero.
--
-- Como usar (SQL Editor do Supabase, depois do 001 a 015):
--   1. Rodar este arquivo inteiro (cria tabelas e funções e já faz o primeiro cálculo).
--   2. Recalcular quando quiser:  select public.previsao_calcular();     (o agendamento faz isso toda madrugada)
--   Resultados: previsao_geral (loja e teste), previsao_modelos (um por produto), previsao_eventos,
--   previsao_dias (próximos 60 dias por produto) e previsao_teste_semanas (teste semana a semana).
-- Pode rodar de novo: não apaga dados de vendas, só refaz as tabelas de previsão.
-- =====================================================================

-- Versão antiga deste script (sem anomalias e sem dia de corte): apaga as funções com a assinatura velha para não
-- ficarem duas versões com o mesmo nome (o banco não saberia qual chamar).
drop function if exists public.previsao_prever(date, date);
drop function if exists public.previsao_testar(integer, integer, numeric, numeric, boolean, numeric);

-- ---------------------------------------------------------------- Tabelas
create table if not exists public.previsao_geral (
    id                   smallint primary key default 1 check (id = 1),
    calculado_em         timestamptz not null default now(),
    segundos             numeric(8,2),
    treino_ini           date not null,
    treino_fim           date not null,
    dias_usados          integer not null,           -- dias abertos sem data especial
    intercepto           numeric not null,           -- vendas por dia no início do treino (sem sazonalidade)
    inclinacao           numeric not null,           -- quanto o movimento cresce por ano (vendas por dia)
    nivel                numeric not null,           -- vendas por dia no fim do treino (sem sazonalidade)
    crescimento_ano      numeric not null,           -- inclinação / intercepto (ex.: 0,07 = 7% do início por ano)
    idx_semana           numeric[] not null,         -- 7 índices, 1 = domingo ... 7 = sábado (média 1)
    idx_mes              numeric[] not null,         -- 12 índices, 1 = janeiro (média 1)
    desvio               numeric not null,           -- desvio padrão do resíduo, em vendas por dia
    desvio_rel           numeric not null,           -- o mesmo em % do previsto (0,087 = 8,7%)
    -- Teste (treino até teste_ini − 1, conferido de teste_ini a teste_fim)
    teste_ini            date,
    teste_fim            date,
    teste_mov_erro       numeric,                    -- erro médio do movimento da loja (vendas por dia), em %
    teste_mov_desvio_rel numeric,                    -- desvio do resíduo da loja no ano de teste
    teste_erro_dia       numeric,                    -- erro dos produtos dia a dia (soma |prev − real| / soma real)
    teste_erro_semana    numeric,                    -- erro dos produtos somando a semana (o que importa no pedido)
    teste_erro_ingenuo   numeric,                    -- mesmo cálculo semanal repetindo o ano passado
    teste_erro_media     numeric,                    -- mesmo cálculo semanal com a média dos últimos 60 dias
    teste_vies           numeric,                    -- soma prevista / soma vendida − 1 (positivo = previu a mais)
    teste_dentro_faixa   numeric,                    -- % dos dias em que a venda real ficou dentro da faixa
    parametros           jsonb
);

create table if not exists public.previsao_modelos (
    produto_id         bigint primary key references public.produtos (id) on delete cascade,
    calculado_em       timestamptz not null default now(),
    situacao           text not null check (situacao in ('ok', 'pouco_historico', 'de_epoca', 'sem_vendas')),
    treino_ini         date,
    treino_fim         date,
    dias_usados        integer not null default 0,
    vendido_treino     numeric not null default 0,
    media_dia          numeric not null default 0,   -- média vendida por dia aberto no último ano do treino
    intercepto         numeric not null default 0,
    inclinacao         numeric not null default 0,   -- por ano
    nivel              numeric not null default 0,   -- tendência no fim do treino (por dia, sem sazonalidade)
    crescimento_ano    numeric not null default 0,   -- inclinação / nível no meio do treino
    idx_semana         numeric[],
    idx_mes            numeric[],
    peso_proprio       numeric not null default 0,   -- 0 a 1: quanto dos índices veio do próprio produto
    desvio             numeric not null default 0,   -- desvio padrão do resíduo (unidades ou kg por dia)
    desvio_rel         numeric,
    parte_datas        numeric not null default 0,   -- fração do vendido que caiu em datas especiais
    teste_vendido      numeric,
    teste_previsto     numeric,
    teste_erro_semana  numeric,
    teste_erro_ingenuo numeric,
    teste_erro_media   numeric
);

create table if not exists public.previsao_eventos (
    produto_id   bigint not null references public.produtos (id) on delete cascade,
    evento       text not null,
    extra_rel    numeric not null,    -- a mais no dia, por venda esperada da loja nesse dia (vezes o movimento = unidades)
    extra_hoje   numeric not null,    -- o mesmo em unidades, num dia médio do fim do treino
    desvio_rel   numeric not null default 0,   -- quanto esse a mais variou entre os anos (mesma medida do extra_rel)
    ocorrencias  integer not null,
    primary key (produto_id, evento)
);

create table if not exists public.previsao_dias (
    produto_id  bigint not null references public.produtos (id) on delete cascade,
    dia         date not null,
    previsto    numeric(12,3) not null,
    minimo      numeric(12,3) not null,   -- faixa de 80%: a venda fica entre mínimo e máximo em 8 de cada 10 dias
    maximo      numeric(12,3) not null,
    tendencia   numeric(12,3) not null,
    sazonal     numeric(8,4) not null,    -- índice semana × mês do dia
    evento      text,
    primary key (produto_id, dia)
);

create table if not exists public.previsao_teste_semanas (
    produto_id  bigint not null references public.produtos (id) on delete cascade,
    semana      date not null,            -- segunda-feira
    vendido     numeric(12,3) not null,
    previsto    numeric(12,3) not null,
    ingenuo     numeric(12,3) not null,   -- mesma semana do ano passado
    media60     numeric(12,3) not null,   -- média dos últimos 60 dias do treino × dias abertos
    primary key (produto_id, semana)
);

alter table public.previsao_geral enable row level security;
alter table public.previsao_modelos enable row level security;
alter table public.previsao_eventos enable row level security;
alter table public.previsao_dias enable row level security;
alter table public.previsao_teste_semanas enable row level security;

drop policy if exists "gerente le a previsao geral" on public.previsao_geral;
create policy "gerente le a previsao geral" on public.previsao_geral for select to authenticated using ((select public.eh_gerente()));
drop policy if exists "gerente le os modelos" on public.previsao_modelos;
create policy "gerente le os modelos" on public.previsao_modelos for select to authenticated using ((select public.eh_gerente()));
drop policy if exists "gerente le os eventos da previsao" on public.previsao_eventos;
create policy "gerente le os eventos da previsao" on public.previsao_eventos for select to authenticated using ((select public.eh_gerente()));
drop policy if exists "gerente le a previsao por dia" on public.previsao_dias;
create policy "gerente le a previsao por dia" on public.previsao_dias for select to authenticated using ((select public.eh_gerente()));
drop policy if exists "gerente le o teste da previsao" on public.previsao_teste_semanas;
create policy "gerente le o teste da previsao" on public.previsao_teste_semanas for select to authenticated using ((select public.eh_gerente()));

-- ---------------------------------------------------------------- De onde vêm as vendas
-- Venda por venda (vendas, venda_itens) e, para anos antigos, o resumo por dia (dados de exemplo do 017; vazio no uso real)
create table if not exists public.vendas_resumo_dia (
    dia      date primary key,
    vendas   integer not null check (vendas >= 0),
    total    numeric(12,2) not null default 0,
    itens    numeric(14,3) not null default 0
);
create table if not exists public.venda_itens_resumo_dia (
    dia         date not null,
    produto_id  bigint not null references public.produtos (id) on delete cascade,
    quantidade  numeric(12,3) not null check (quantidade > 0),
    valor       numeric(12,2) not null default 0,
    primary key (dia, produto_id)
);
create index if not exists venda_itens_resumo_dia_produto on public.venda_itens_resumo_dia (produto_id, dia);
alter table public.vendas_resumo_dia enable row level security;
alter table public.venda_itens_resumo_dia enable row level security;
drop policy if exists "gerente le o resumo de vendas antigas" on public.vendas_resumo_dia;
create policy "gerente le o resumo de vendas antigas" on public.vendas_resumo_dia for select to authenticated using ((select public.eh_gerente()));
drop policy if exists "gerente le o resumo de itens antigos" on public.venda_itens_resumo_dia;
create policy "gerente le o resumo de itens antigos" on public.venda_itens_resumo_dia for select to authenticated using ((select public.eh_gerente()));

-- security_invoker: quem consulta pela tela continua sujeito às regras (RLS) das tabelas
create or replace view public.previsao_movimento_dia with (security_invoker = true) as
    select dia, count(*)::numeric as vendas from public.vendas where situacao = 'paga' group by dia
    union all
    select dia, vendas::numeric from public.vendas_resumo_dia;
create or replace view public.previsao_dias_abertos with (security_invoker = true) as
    select dia from public.caixas
    union
    select dia from public.vendas_resumo_dia;
create or replace view public.previsao_vendas_produto as
    select v.dia, i.produto_id, sum(i.quantidade) as quantidade
    from public.venda_itens i join public.vendas v on v.id = i.venda_id
    where v.situacao = 'paga' and i.produto_id is not null
    group by v.dia, i.produto_id
    union all
    select dia, produto_id, quantidade from public.venda_itens_resumo_dia;
alter view public.previsao_vendas_produto set (security_invoker = true);

-- ---------------------------------------------------------------- Ajudante: deixa a média dos índices em 1
-- Índices nulos (sem dado) viram p_vazio; zeros (loja fechada nesse dia da semana) continuam zero.
create or replace function public.previsao_normalizar(p numeric[], p_vazio numeric default 1)
returns numeric[]
language sql
immutable
as $$
    select array_agg(case when x is null then p_vazio when x <= 0 then 0
                          else round(x / nullif(m.media, 0), 5) end order by n)
    from unnest(p) with ordinality u(x, n)
    cross join (select avg(y) as media from unnest(p) y where y > 0) m;
$$;

-- ---------------------------------------------------------------- 1 a 3: aprende com as vendas até p_fim
-- Deixa o resultado em tabelas temporárias (_pv_dias, _pv_geral, _pv_modelo, _pv_idx, _pv_ev) para
-- previsao_prever. p_k = encolhimento dos índices, p_kt = encolhimento do crescimento (em unidades vendidas).
create or replace function public.previsao_ajustar(p_fim date, p_anos integer default 4,
                                                   p_k numeric default 1000, p_kt numeric default 3000,
                                                   p_ke numeric default 30)
returns void
language plpgsql
security definer
set search_path = public
set statement_timeout = 0
set client_min_messages = warning
as $$
declare
    v_ini date;
    v_a numeric;
    v_b numeric;
    v_dow numeric[] := array_fill(1::numeric, array[7]);
    v_mes numeric[] := array_fill(1::numeric, array[12]);
    v_volta integer;
begin
    select greatest(p_fim - p_anos * 365 + 1, min(dia)) into v_ini from public.previsao_movimento_dia where dia <= p_fim;
    if v_ini is null or v_ini > p_fim - 55 then
        raise exception 'Pouco histórico de vendas para prever (precisa de pelo menos 8 semanas).';
    end if;

    -- ------------------------------------------------------------ Dias do treino
    drop table if exists _pv_dias;
    create temp table _pv_dias as
    with mov as (select dia, sum(vendas) as vendas from public.previsao_movimento_dia where dia between v_ini and p_fim group by dia),
         abertos as (select dia from public.previsao_dias_abertos where dia between v_ini and p_fim),
         ev as (select distinct on (dia) dia, nome, fator, categorias, produtos
                from public.eventos_calendario where tipo <> 'anomalia' and dia between v_ini and p_fim order by dia, fator desc, nome),
         -- Período fora do normal (pandemia, greve...): fica fora de tudo, nem normal nem data especial
         an as (select distinct dia from public.eventos_calendario where tipo = 'anomalia' and dia between v_ini and p_fim)
    select d::date as dia, round((d::date - v_ini) / 365.25, 6) as t,
           extract(dow from d)::int + 1 as sem, extract(month from d)::int as mes,
           (a.dia is not null) as aberto, coalesce(m.vendas, 0)::numeric as vendas,
           case when an.dia is null then ev.nome end as evento, ev.fator, ev.categorias, ev.produtos,
           (a.dia is not null and ev.nome is null and an.dia is null) as normal,
           (an.dia is not null) as anomalia
    from generate_series(v_ini, p_fim, interval '1 day') d
    left join abertos a on a.dia = d::date
    left join mov m on m.dia = d::date
    left join ev on ev.dia = d::date
    left join an on an.dia = d::date;

    -- ------------------------------------------------------------ 1. Loja (movimento = vendas por dia)
    for v_volta in 1..4 loop
        select regr_intercept(vendas / (v_dow[sem] * v_mes[mes]), t)::numeric, regr_slope(vendas / (v_dow[sem] * v_mes[mes]), t)::numeric
        into v_a, v_b
        from _pv_dias where normal and v_dow[sem] > 0;

        select public.previsao_normalizar(array_agg(x.r order by s.n), 0) into v_dow
        from generate_series(1, 7) s(n)
        left join (select sem, avg(vendas / greatest(v_a + v_b * t, 0.01) / v_mes[mes]) as r
                   from _pv_dias where normal group by sem) x on x.sem = s.n;

        select public.previsao_normalizar(array_agg(x.r order by s.n), 1) into v_mes
        from generate_series(1, 12) s(n)
        left join (select mes, avg(vendas / greatest(v_a + v_b * t, 0.01) / nullif(v_dow[sem], 0)) as r
                   from _pv_dias where normal and v_dow[sem] > 0 group by mes) x on x.mes = s.n;
    end loop;
    select regr_intercept(vendas / (v_dow[sem] * v_mes[mes]), t)::numeric, regr_slope(vendas / (v_dow[sem] * v_mes[mes]), t)::numeric
    into v_a, v_b
    from _pv_dias where normal and v_dow[sem] > 0;

    drop table if exists _pv_geral;
    create temp table _pv_geral as
    select v_ini as treino_ini, p_fim as treino_fim, count(*)::int as dias_usados,
           v_a as intercepto, v_b as inclinacao,
           v_a + v_b * max(t) as nivel, v_b / nullif(v_a, 0) as crescimento_ano,
           v_dow as idx_semana, v_mes as idx_mes,
           stddev_samp(vendas - (v_a + v_b * t) * v_dow[sem] * v_mes[mes]) as desvio,
           stddev_samp(vendas / nullif((v_a + v_b * t) * v_dow[sem] * v_mes[mes], 0) - 1) as desvio_rel,
           p_k as k, p_kt as kt, p_ke as ke, p_anos as anos
    from _pv_dias where normal and v_dow[sem] > 0;

    -- ------------------------------------------------------------ 2. Produtos
    -- Grade produto × dia aberto (desde a primeira venda do produto no período), com zero onde não vendeu
    drop table if exists _pv_pd;
    create temp table _pv_pd as
    with q as (select produto_id, dia, sum(quantidade) as y
               from public.previsao_vendas_produto where dia between v_ini and p_fim
               group by produto_id, dia),
         primeiro as (select produto_id, min(dia) as d0 from q group by produto_id)
    select pr.produto_id, p.categoria, d.dia, d.t, d.sem, d.mes, d.normal, d.evento, d.fator,
           coalesce(q.y, 0)::numeric as y,
           v_dow[d.sem] * v_mes[d.mes] as s_loja,
           greatest(v_a + v_b * d.t, 0.01) as tend_loja
    from primeiro pr
    join public.produtos p on p.id = pr.produto_id
    join _pv_dias d on d.dia >= pr.d0 and d.aberto
    left join q on q.produto_id = pr.produto_id and q.dia = d.dia;
    create index on _pv_pd (produto_id);

    -- 2a. Tendência inicial de cada produto, já tirando a sazonalidade da loja
    drop table if exists _pv_t1;
    create temp table _pv_t1 as
    select produto_id, regr_intercept(y / s_loja, t)::numeric as a, regr_slope(y / s_loja, t)::numeric as b
    from _pv_pd where normal and s_loja > 0 group by produto_id;

    -- 2b. Índices do dia da semana: produto → categoria → loja (encolhimento)
    drop table if exists _pv_idx;
    create temp table _pv_idx (produto_id bigint, tipo char(1), k int, valor numeric, peso numeric);

    with base as (
        select pd.produto_id, pd.categoria, pd.sem, pd.y,
               greatest(t1.a + t1.b * pd.t, 0.001) * v_mes[pd.mes] as esperado
        from _pv_pd pd join _pv_t1 t1 using (produto_id)
        where pd.normal and v_dow[pd.sem] > 0
    ), cat as (
        select categoria, sem, sum(y) as n, sum(y) / nullif(sum(esperado), 0) as r from base group by categoria, sem
    ), cat_norm as (       -- categoria puxada para a loja e com média 1
        select categoria, sem, valor / avg(valor) over (partition by categoria) as valor
        from (select categoria, sem, (n * coalesce(r, 0) + p_k * v_dow[sem]) / (n + p_k) as valor from cat) x
    ), prod as (
        select produto_id, categoria, sem, sum(y) as n, sum(y) / nullif(sum(esperado), 0) as r from base group by produto_id, categoria, sem
    )
    insert into _pv_idx
    select produto_id, 's', sem, valor / avg(valor) over (partition by produto_id), peso
    from (select p.produto_id, p.sem, (p.n * coalesce(p.r, 0) + p_k * c.valor) / (p.n + p_k) as valor, p.n / (p.n + p_k) as peso
          from prod p join cat_norm c on c.categoria = p.categoria and c.sem = p.sem) x;
    -- Dias da semana em que a loja não abre: índice zero
    insert into _pv_idx
    select t1.produto_id, 's', s.n, 0, 0 from _pv_t1 t1 cross join generate_series(1, 7) s(n) where v_dow[s.n] = 0;

    -- 2c. Índices do mês, do mesmo jeito (sem dado no mês = índice da categoria/loja)
    with base as (
        select pd.produto_id, pd.categoria, pd.mes, pd.y,
               greatest(t1.a + t1.b * pd.t, 0.001) * i.valor as esperado
        from _pv_pd pd join _pv_t1 t1 using (produto_id)
        join _pv_idx i on i.produto_id = pd.produto_id and i.tipo = 's' and i.k = pd.sem
        where pd.normal and i.valor > 0
    ), cat as (
        select categoria, mes, sum(y) as n, sum(y) / nullif(sum(esperado), 0) as r from base group by categoria, mes
    ), cat_todos as (
        select c.categoria, m.n as mes, coalesce(x.n, 0) as n, x.r
        from (select distinct categoria from base) c cross join generate_series(1, 12) m(n)
        left join cat x on x.categoria = c.categoria and x.mes = m.n
    ), cat_norm as (
        select categoria, mes, valor / avg(valor) over (partition by categoria) as valor
        from (select categoria, mes, (n * coalesce(r, 0) + p_k * v_mes[mes]) / (n + p_k) as valor from cat_todos) x
    ), prod as (
        select produto_id, categoria, mes, sum(y) as n, sum(y) / nullif(sum(esperado), 0) as r from base group by produto_id, categoria, mes
    ), prod_todos as (
        select p.produto_id, p.categoria, m.n as mes, coalesce(x.n, 0) as n, x.r
        from (select distinct produto_id, categoria from base) p cross join generate_series(1, 12) m(n)
        left join prod x on x.produto_id = p.produto_id and x.mes = m.n
    )
    insert into _pv_idx
    select produto_id, 'm', mes, valor / avg(valor) over (partition by produto_id), peso
    from (select p.produto_id, p.mes, (p.n * coalesce(p.r, 0) + p_k * c.valor) / (p.n + p_k) as valor, p.n / (p.n + p_k) as peso
          from prod_todos p join cat_norm c on c.categoria = p.categoria and c.mes = p.mes) x;
    create index on _pv_idx (produto_id, tipo, k);

    -- 2d. Tendência final (sem a sazonalidade do produto), com o crescimento puxado para o da loja
    drop table if exists _pv_modelo;
    create temp table _pv_modelo as
    with ds as (
        select pd.produto_id, pd.t, pd.y, pd.y / (s.valor * m.valor) as yd
        from _pv_pd pd
        join _pv_idx s on s.produto_id = pd.produto_id and s.tipo = 's' and s.k = pd.sem
        join _pv_idx m on m.produto_id = pd.produto_id and m.tipo = 'm' and m.k = pd.mes
        where pd.normal and s.valor > 0 and m.valor > 0
    ), r as (
        select produto_id, count(*) as dias, sum(y) as n, avg(t) as tm, avg(yd) as media,
               coalesce(regr_slope(yd, t)::numeric, 0) as b
        from ds group by produto_id
    )
    select r.produto_id, r.dias::int as dias_usados, r.n as vendido_normal, r.tm, r.media,
           r.b as b_proprio,
           -- crescimento relativo: (n × do produto + kt × da loja) / (n + kt)
           r.media * ((r.n * coalesce(r.b / nullif(r.media, 0), 0) + p_kt * (v_b / greatest(v_a + v_b * r.tm, 0.01))) / (r.n + p_kt)) as b
    from r;
    alter table _pv_modelo add column a numeric, add column nivel numeric, add column desvio numeric,
        add column desvio_rel numeric, add column media_dia numeric, add column parte_datas numeric,
        add column vendido_total numeric, add column peso_proprio numeric;
    update _pv_modelo set a = media - b * tm;
    update _pv_modelo m set nivel = greatest(m.a + m.b * g.t_fim, 0)
    from (select max(t) as t_fim from _pv_dias) g;

    -- Valor do modelo em cada dia do treino (sem as datas especiais)
    drop table if exists _pv_ajuste;
    create temp table _pv_ajuste as
    select pd.produto_id, pd.dia, pd.t, pd.normal, pd.evento, pd.fator, pd.y, pd.tend_loja, pd.s_loja,
           greatest(m.a + m.b * pd.t, 0) * s.valor * mm.valor as ajuste
    from _pv_pd pd
    join _pv_modelo m on m.produto_id = pd.produto_id
    join _pv_idx s on s.produto_id = pd.produto_id and s.tipo = 's' and s.k = pd.sem
    join _pv_idx mm on mm.produto_id = pd.produto_id and mm.tipo = 'm' and mm.k = pd.mes;
    create index on _pv_ajuste (produto_id);

    -- Resíduo (último ano do treino, que é o mais parecido com o que vem pela frente)
    update _pv_modelo m set desvio = coalesce(x.desvio, 0), desvio_rel = x.desvio / nullif(x.media_ajuste, 0), media_dia = coalesce(x.media_y, 0)
    from (select produto_id, stddev_samp(y - ajuste) as desvio, avg(ajuste) as media_ajuste, avg(y) as media_y
          from _pv_ajuste where normal and dia > p_fim - 365 group by produto_id) x
    where x.produto_id = m.produto_id;
    update _pv_modelo m set vendido_total = x.total, parte_datas = x.datas / nullif(x.total, 0)
    from (select produto_id, sum(y) as total, sum(y) filter (where evento is not null) as datas from _pv_ajuste group by produto_id) x
    where x.produto_id = m.produto_id;
    update _pv_modelo m set peso_proprio = x.peso
    from (select produto_id, avg(peso) as peso from _pv_idx group by produto_id) x where x.produto_id = m.produto_id;

    -- ------------------------------------------------------------ 3. Datas especiais
    drop table if exists _pv_ev;
    create temp table _pv_ev as
    with occ as (     -- cada vez que a data aconteceu: a mais que o normal, por venda esperada da loja
        select a.produto_id, p.categoria, a.evento, a.y, a.ajuste,
               (a.y - a.ajuste) / (a.tend_loja * a.s_loja) as r, a.ajuste / (a.tend_loja * a.s_loja) as base_rel,
               coalesce(m.parte_datas, 0) >= 0.5 as de_epoca
        from _pv_ajuste a
        join public.produtos p on p.id = a.produto_id
        join _pv_modelo m on m.produto_id = a.produto_id
        where a.evento is not null and a.fator > 0 and a.s_loja > 0
    ), cat as (       -- quanto a categoria vende a mais nessa data, em proporção do normal (sem os produtos de época)
        select categoria, evento, sum(y - ajuste) / nullif(sum(ajuste), 0) as lift
        from occ where not de_epoca group by categoria, evento
    ), prod as (
        select o.produto_id, o.categoria, o.evento, count(*)::int as ocorrencias, sum(o.y) as n, bool_or(o.de_epoca) as de_epoca,
               avg(o.r) as proprio, coalesce(stddev_samp(o.r), 0) as desvio_rel, avg(o.base_rel) as base_rel
        from occ o group by o.produto_id, o.categoria, o.evento
    )
    -- Produto com pouca venda na data fica perto do que a categoria faz (encolhimento: n / (n + p_ke)).
    -- Produto de época não: a venda dele É a data, então vale só o que ele mesmo vendeu.
    select pr.produto_id, pr.evento, pr.ocorrencias,
           case when pr.de_epoca then pr.proprio
                else (pr.n * pr.proprio + p_ke * coalesce(c.lift, 0) * pr.base_rel) / (pr.n + p_ke) end as extra_rel,
           case when pr.ocorrencias >= 2 then pr.desvio_rel else abs(pr.proprio) * 0.5 end as desvio_rel
    from prod pr left join cat c on c.categoria = pr.categoria and c.evento = pr.evento;
end;
$$;

-- ---------------------------------------------------------------- 4. Prevê de p_ini a p_fim com o que previsao_ajustar aprendeu
create or replace function public.previsao_prever(p_ini date, p_fim date, p_anomalias boolean default true)
returns void
language plpgsql
security definer
set search_path = public
set client_min_messages = warning
as $$
declare
    g record;
begin
    select * into g from _pv_geral;

    drop table if exists _pv_prev;
    create temp table _pv_prev as
    with dias as (
        select d::date as dia, round((d::date - g.treino_ini) / 365.25, 6) as t,
               extract(dow from d)::int + 1 as sem, extract(month from d)::int as mes
        from generate_series(p_ini, p_fim, interval '1 day') d
    ), fechados as (     -- dia e mês em que a loja nunca abriu (visto em pelo menos 2 anos)
        select to_char(dia, 'MM-DD') as md from _pv_dias group by 1 having count(*) >= 2 and not bool_or(aberto)
    ), ev as (
        select distinct on (dia) dia, nome, fator, categorias, produtos
        from public.eventos_calendario where tipo <> 'anomalia' and dia between p_ini and p_fim order by dia, fator desc, nome
    ), an as (     -- período fora do normal marcado à frente (ex.: reforma): multiplica tudo pelo fator
        select dia, exp(sum(ln(greatest(fator, 0.0001)))) as fator
        from public.eventos_calendario where p_anomalias and tipo = 'anomalia' and dia between p_ini and p_fim group by dia
    ), base as (
        select m.produto_id, p.categoria, lower(p.nome) as nome_produto, d.dia, d.t,
               greatest(m.a + m.b * d.t, 0) as tendencia, s.valor * mm.valor as sazonal,
               greatest(g.intercepto + g.inclinacao * d.t, 0.01) * g.idx_semana[d.sem] * g.idx_mes[d.mes] as tend_loja,
               ev.nome as evento, ev.fator, ev.categorias, ev.produtos,
               (f.md is not null or coalesce(ev.fator, 1) = 0) as fechado,
               coalesce(an.fator, 1) as f_anomalia,
               m.desvio
        from _pv_modelo m
        join public.produtos p on p.id = m.produto_id
        cross join dias d
        join _pv_idx s on s.produto_id = m.produto_id and s.tipo = 's' and s.k = d.sem
        join _pv_idx mm on mm.produto_id = m.produto_id and mm.tipo = 'm' and mm.k = d.mes
        left join fechados f on f.md = to_char(d.dia, 'MM-DD')
        left join ev on ev.dia = d.dia
        left join an on an.dia = d.dia
    )
    select b.produto_id, b.dia, b.tendencia, b.sazonal, b.evento,
           case when b.fechado then 0
                when e.produto_id is not null then sqrt(b.desvio ^ 2 + (e.desvio_rel * b.tend_loja) ^ 2)
                else b.desvio end as desvio,
           b.f_anomalia * case when b.fechado then 0
                when b.evento is null then b.tendencia * b.sazonal
                when e.produto_id is not null then greatest(b.tendencia * b.sazonal + e.extra_rel * b.tend_loja, 0)
                -- data que nunca aconteceu no treino: fator do calendário nos produtos e categorias da data
                when b.fator < 1
                  or exists (select 1 from unnest(string_to_array(coalesce(b.categorias, ''), ',')) c where lower(trim(c)) = lower(b.categoria))
                  or exists (select 1 from unnest(string_to_array(coalesce(b.produtos, ''), ',')) x where lower(trim(x)) = b.nome_produto)
                  then b.tendencia * b.sazonal * b.fator
                else b.tendencia * b.sazonal
           end as previsto
    from base b
    left join _pv_ev e on e.produto_id = b.produto_id and e.evento = b.evento;
    create index on _pv_prev (produto_id, dia);
end;
$$;

-- ---------------------------------------------------------------- Teste: treina até um ano atrás e confere o último ano
create or replace function public.previsao_testar(p_dias integer default 365, p_anos integer default 4,
                                                  p_k numeric default 1000, p_kt numeric default 3000,
                                                  p_gravar boolean default true, p_ke numeric default 30,
                                                  p_corte date default null)
returns jsonb
language plpgsql
security definer
set search_path = public
set statement_timeout = 0
set client_min_messages = warning
as $$
declare
    v_hoje date := public.hoje_local();
    v_ini date := coalesce(p_corte, public.hoje_local() - p_dias);    -- primeiro dia do teste
    v_fim date := least(coalesce(p_corte, public.hoje_local() - p_dias) + p_dias - 1, public.hoje_local() - 1);
    v_res jsonb;
begin
    if auth.uid() is not null and not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente calcula a previsão.' using errcode = '42501';
    end if;
    perform public.previsao_ajustar(v_ini - 1, p_anos, p_k, p_kt, p_ke);
    -- No teste o modelo não sabe dos períodos fora do normal que ainda vão acontecer (seria olhar o futuro)
    perform public.previsao_prever(v_ini, v_fim, false);

    -- Venda vend do período de teste e as duas comparações "sem modelo"
    drop table if exists _pv_real;
    create temp table _pv_real as
    with q as (select produto_id, dia, sum(quantidade) as y
               from public.previsao_vendas_produto where dia between v_ini - 364 and v_fim
               group by 1, 2),
         abertos as (select dia from public.previsao_dias_abertos where dia between v_ini and v_fim),
         media60 as (select produto_id, sum(y) / nullif((select count(*) from public.previsao_dias_abertos where dia between v_ini - 60 and v_ini - 1), 0) as m
                     from q where dia between v_ini - 60 and v_ini - 1 group by 1)
    select pv.produto_id, pv.dia, pv.previsto, pv.desvio,
           coalesce(r.y, 0) as vend,
           coalesce(a.y, 0) as ingenuo,
           case when ab.dia is not null then coalesce(m60.m, 0) else 0 end as media60
    from _pv_prev pv
    left join q r on r.produto_id = pv.produto_id and r.dia = pv.dia
    left join q a on a.produto_id = pv.produto_id and a.dia = pv.dia - 364
    left join abertos ab on ab.dia = pv.dia
    left join media60 m60 on m60.produto_id = pv.produto_id;

    drop table if exists _pv_sem;
    create temp table _pv_sem as
    select produto_id, date_trunc('week', dia)::date as semana,
           sum(vend) as vendido, sum(previsto) as previsto, sum(ingenuo) as ingenuo, sum(media60) as media60
    from _pv_real group by 1, 2;

    with mov as (    -- movimento da loja: previsto pela parte 1 x vendas reais
        select d::date as dia, coalesce(m.n, 0) as vend,
               case when f.md is not null or coalesce(e.fator, 1) = 0 then 0
                    else greatest(g.intercepto + g.inclinacao * ((d::date - g.treino_ini) / 365.25), 0)
                         * g.idx_semana[extract(dow from d)::int + 1] * g.idx_mes[extract(month from d)::int] * coalesce(e.fator, 1)
               end as prev,
               e.dia is null as normal
        from generate_series(v_ini, v_fim, interval '1 day') d
        cross join _pv_geral g
        left join (select dia, sum(vendas) as n from public.previsao_movimento_dia where dia between v_ini and v_fim group by dia) m on m.dia = d::date
        left join (select to_char(dia, 'MM-DD') as md from _pv_dias group by 1 having count(*) >= 2 and not bool_or(aberto)) f on f.md = to_char(d::date, 'MM-DD')
        left join (select distinct on (dia) dia, fator from public.eventos_calendario
                   where tipo <> 'anomalia' and dia between v_ini and v_fim order by dia, fator desc) e on e.dia = d::date
    )
    select jsonb_build_object(
        'teste_ini', v_ini, 'teste_fim', v_fim, 'k', p_k, 'kt', p_kt, 'ke', p_ke, 'anos', p_anos,
        'mov_erro', (select round(sum(abs(prev - vend)) / nullif(sum(vend), 0), 4) from mov),
        'mov_desvio_rel', (select round(stddev_samp(vend / nullif(prev, 0) - 1), 4) from mov where normal and prev > 0 and vend > 0),
        'erro_dia', (select round(sum(abs(previsto - vend)) / nullif(sum(vend), 0), 4) from _pv_real),
        'erro_semana', (select round(sum(abs(previsto - vendido)) / nullif(sum(vendido), 0), 4) from _pv_sem),
        'erro_semana_sem_anomalia', (select round(sum(abs(previsto - vendido)) / nullif(sum(vendido), 0), 4) from _pv_sem s
                                     where not exists (select 1 from public.eventos_calendario e
                                                       where e.tipo = 'anomalia' and e.dia between s.semana and s.semana + 6)),
        'semanas_com_anomalia', (select count(distinct semana) from _pv_sem s
                                 where exists (select 1 from public.eventos_calendario e
                                               where e.tipo = 'anomalia' and e.dia between s.semana and s.semana + 6)),
        'erro_ingenuo', (select round(sum(abs(ingenuo - vendido)) / nullif(sum(vendido), 0), 4) from _pv_sem),
        'erro_media', (select round(sum(abs(media60 - vendido)) / nullif(sum(vendido), 0), 4) from _pv_sem),
        'vies', (select round(sum(previsto) / nullif(sum(vend), 0) - 1, 4) from _pv_real),
        'dentro_faixa', (select round(avg(case when vend between greatest(previsto - 1.2816 * desvio, 0) and previsto + 1.2816 * desvio then 1.0 else 0 end), 4)
                         from _pv_real where previsto > 0)
    ) into v_res;

    if p_gravar then
        delete from public.previsao_teste_semanas;
        insert into public.previsao_teste_semanas (produto_id, semana, vendido, previsto, ingenuo, media60)
        select produto_id, semana, round(vendido, 3), round(previsto, 3), round(ingenuo, 3), round(media60, 3) from _pv_sem;
        drop table if exists _pv_teste_prod;
        create temp table _pv_teste_prod as
        select produto_id, sum(vendido) as vendido, sum(previsto) as previsto,
               sum(abs(previsto - vendido)) / nullif(sum(vendido), 0) as erro,
               sum(abs(ingenuo - vendido)) / nullif(sum(vendido), 0) as erro_ingenuo,
               sum(abs(media60 - vendido)) / nullif(sum(vendido), 0) as erro_media
        from _pv_sem group by produto_id;
    end if;
    return v_res;
end;
$$;

-- ---------------------------------------------------------------- Cálculo completo: teste + modelo atual + próximos dias
create or replace function public.previsao_calcular(p_dias integer default 60)
returns text
language plpgsql
security definer
set search_path = public
set statement_timeout = 0
set client_min_messages = warning
as $$
declare
    v_inicio timestamptz := clock_timestamp();
    v_hoje date := public.hoje_local();
    v_teste jsonb;
    v_produtos integer;
begin
    if auth.uid() is not null and not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente calcula a previsão.' using errcode = '42501';
    end if;
    if p_dias not between 7 and 120 then
        raise exception 'Use de 7 a 120 dias.';
    end if;

    -- Teste só quando há pelo menos 1 ano + 8 semanas de vendas
    if (select min(dia) from public.previsao_movimento_dia) <= v_hoje - 365 - 56 then
        v_teste := public.previsao_testar();
    end if;

    -- Modelo com tudo até ontem
    perform public.previsao_ajustar(v_hoje - 1);
    perform public.previsao_prever(v_hoje, v_hoje + p_dias - 1);

    delete from public.previsao_geral;
    insert into public.previsao_geral (calculado_em, treino_ini, treino_fim, dias_usados, intercepto, inclinacao, nivel,
        crescimento_ano, idx_semana, idx_mes, desvio, desvio_rel, teste_ini, teste_fim, teste_mov_erro, teste_mov_desvio_rel,
        teste_erro_dia, teste_erro_semana, teste_erro_ingenuo, teste_erro_media, teste_vies, teste_dentro_faixa, parametros)
    select now(), treino_ini, treino_fim, dias_usados, round(intercepto, 4), round(inclinacao, 4), round(nivel, 4),
           round(crescimento_ano, 5), idx_semana, idx_mes, round(desvio, 4), round(desvio_rel, 5),
           (v_teste ->> 'teste_ini')::date, (v_teste ->> 'teste_fim')::date, (v_teste ->> 'mov_erro')::numeric,
           (v_teste ->> 'mov_desvio_rel')::numeric, (v_teste ->> 'erro_dia')::numeric, (v_teste ->> 'erro_semana')::numeric,
           (v_teste ->> 'erro_ingenuo')::numeric, (v_teste ->> 'erro_media')::numeric, (v_teste ->> 'vies')::numeric,
           (v_teste ->> 'dentro_faixa')::numeric,
           jsonb_build_object('anos', anos, 'k', k, 'kt', kt, 'ke', ke, 'dias_previstos', p_dias)
    from _pv_geral;

    delete from public.previsao_modelos;
    insert into public.previsao_modelos (produto_id, calculado_em, situacao, treino_ini, treino_fim, dias_usados, vendido_treino,
        media_dia, intercepto, inclinacao, nivel, crescimento_ano, idx_semana, idx_mes, peso_proprio, desvio, desvio_rel, parte_datas)
    select m.produto_id, now(),
           case when coalesce(m.parte_datas, 0) >= 0.5 then 'de_epoca'
                when m.dias_usados < 56 or m.vendido_total < 10 then 'pouco_historico' else 'ok' end,
           g.treino_ini, g.treino_fim, m.dias_usados, round(m.vendido_total, 3), round(coalesce(m.media_dia, 0), 4),
           round(m.a, 4), round(m.b, 4), round(m.nivel, 4), round(coalesce(m.b / nullif(m.media, 0), 0), 5),
           (select array_agg(round(valor, 4) order by k) from _pv_idx i where i.produto_id = m.produto_id and i.tipo = 's'),
           (select array_agg(round(valor, 4) order by k) from _pv_idx i where i.produto_id = m.produto_id and i.tipo = 'm'),
           round(coalesce(m.peso_proprio, 0), 4), round(coalesce(m.desvio, 0), 4), round(m.desvio_rel, 5), round(coalesce(m.parte_datas, 0), 4)
    from _pv_modelo m cross join _pv_geral g;
    insert into public.previsao_modelos (produto_id, situacao)
    select p.id, 'sem_vendas' from public.produtos p where not exists (select 1 from _pv_modelo m where m.produto_id = p.id);
    if v_teste is not null then
        update public.previsao_modelos m set teste_vendido = round(t.vendido, 3), teste_previsto = round(t.previsto, 3),
            teste_erro_semana = round(t.erro, 4), teste_erro_ingenuo = round(t.erro_ingenuo, 4), teste_erro_media = round(t.erro_media, 4)
        from _pv_teste_prod t where t.produto_id = m.produto_id;
    end if;

    delete from public.previsao_eventos;
    insert into public.previsao_eventos (produto_id, evento, extra_rel, extra_hoje, desvio_rel, ocorrencias)
    select e.produto_id, e.evento, round(e.extra_rel, 6), round(e.extra_rel * g.nivel, 3), round(e.desvio_rel, 6), e.ocorrencias
    from _pv_ev e cross join _pv_geral g;

    delete from public.previsao_dias;
    insert into public.previsao_dias (produto_id, dia, previsto, minimo, maximo, tendencia, sazonal, evento)
    select produto_id, dia, round(previsto, 3),
           round(case when previsto = 0 then 0 else greatest(previsto - 1.2816 * desvio, 0) end, 3),
           round(case when previsto = 0 then 0 else previsto + 1.2816 * desvio end, 3),
           round(tendencia, 3), round(sazonal, 4), evento
    from _pv_prev;
    get diagnostics v_produtos = row_count;

    update public.previsao_geral set segundos = round(extract(epoch from clock_timestamp() - v_inicio)::numeric, 2);
    select count(*) into v_produtos from public.previsao_modelos where situacao <> 'sem_vendas';
    return format('Previsão pronta: %s produtos, %s dias à frente, em %s s.%s', v_produtos, p_dias,
                  round(extract(epoch from clock_timestamp() - v_inicio)::numeric, 1),
                  case when v_teste is null then ' (Sem teste: precisa de mais de 1 ano de vendas.)'
                       else format(' Teste do último ano: erro semanal de %s%% (repetir o ano passado: %s%%; média dos últimos 60 dias: %s%%).',
                                   round((v_teste ->> 'erro_semana')::numeric * 100, 1), round((v_teste ->> 'erro_ingenuo')::numeric * 100, 1),
                                   round((v_teste ->> 'erro_media')::numeric * 100, 1)) end);
end;
$$;

-- ---------------------------------------------------------------- Decomposição de um produto, semana a semana (para os gráficos)
-- vendido = tendência × sazonal (+ datas) + resíduo. Lê o modelo gravado; produto_id nulo = movimento da loja.
create or replace function public.previsao_decomposicao(p_produto bigint default null)
returns table (semana date, vendido numeric, tendencia numeric, sazonal numeric, datas numeric, ajuste numeric, residuo numeric)
language plpgsql
stable
security definer
set search_path = public
as $$
declare
    g public.previsao_geral;
    m public.previsao_modelos;
begin
    if auth.uid() is not null and not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente vê a previsão.' using errcode = '42501';
    end if;
    select * into g from public.previsao_geral;
    if g.id is null then
        return;
    end if;
    if p_produto is not null then
        select * into m from public.previsao_modelos where produto_id = p_produto and situacao <> 'sem_vendas';
        if m.produto_id is null then
            return;
        end if;
    end if;
    return query
    with dias as (
        select d::date as dia, (d::date - g.treino_ini) / 365.25 as t,
               extract(dow from d)::int + 1 as sem, extract(month from d)::int as mes
        from generate_series(g.treino_ini, g.treino_fim, interval '1 day') d
        where exists (select 1 from public.previsao_dias_abertos a where a.dia = d::date)
    ), reais as (
        select dia, sum(vendas) as y from public.previsao_movimento_dia
        where p_produto is null and dia between g.treino_ini and g.treino_fim group by dia
        union all
        select dia, sum(quantidade) from public.previsao_vendas_produto
        where produto_id = p_produto and dia between g.treino_ini and g.treino_fim group by dia
    ), ev as (
        select distinct on (dia) dia, nome, fator from public.eventos_calendario
        where tipo <> 'anomalia' and dia between g.treino_ini and g.treino_fim order by dia, fator desc, nome
    ), dia_a_dia as (
        select d.dia, coalesce(r.y, 0) as y,
               case when p_produto is null then greatest(g.intercepto + g.inclinacao * d.t, 0)
                    else greatest(m.intercepto + m.inclinacao * d.t, 0) end as tend,
               case when p_produto is null then g.idx_semana[d.sem] * g.idx_mes[d.mes]
                    else m.idx_semana[d.sem] * m.idx_mes[d.mes] end as saz,
               case when ev.nome is null then 0
                    when p_produto is null then greatest(g.intercepto + g.inclinacao * d.t, 0) * g.idx_semana[d.sem] * g.idx_mes[d.mes] * (ev.fator - 1)
                    else coalesce(e.extra_rel, 0) * greatest(g.intercepto + g.inclinacao * d.t, 0.01) * g.idx_semana[d.sem] * g.idx_mes[d.mes] end as extra
        from dias d
        left join reais r on r.dia = d.dia
        left join ev on ev.dia = d.dia
        left join public.previsao_eventos e on e.produto_id = p_produto and e.evento = ev.nome
    )
    select date_trunc('week', dia)::date, round(sum(y), 3), round(avg(tend) * count(*), 3), round(avg(saz), 4),
           round(sum(extra), 3), round(sum(tend * saz + extra), 3), round(sum(y - tend * saz - extra), 3)
    from dia_a_dia group by 1 order by 1;
end;
$$;

-- ---------------------------------------------------------------- Teste ano a ano (verificação de precisão em vários anos)
-- Para cada ano: treina com os 4 anos anteriores e prevê o ano inteiro sem ver nenhuma venda dele.
create table if not exists public.previsao_teste_anos (
    ano                  integer primary key,
    calculado_em         timestamptz not null default now(),
    erro_semana          numeric,      -- erro semanal por produto do modelo
    erro_semana_normal   numeric,      -- o mesmo sem as semanas com período fora do normal
    erro_ingenuo         numeric,      -- repetir o ano passado
    erro_media           numeric,      -- média dos últimos 60 dias
    mov_erro             numeric,      -- erro do movimento da loja (vendas por dia)
    vies                 numeric,
    dentro_faixa         numeric,
    semanas_com_anomalia integer,
    resultado            jsonb
);
alter table public.previsao_teste_anos enable row level security;
drop policy if exists "gerente le o teste por ano" on public.previsao_teste_anos;
create policy "gerente le o teste por ano" on public.previsao_teste_anos for select to authenticated using ((select public.eh_gerente()));

create or replace function public.previsao_testar_anos(p_de integer default null, p_ate integer default null)
returns text
language plpgsql
security definer
set search_path = public
set statement_timeout = 0
set client_min_messages = warning
as $$
declare
    v_primeiro date;
    v_ano integer;
    v_r jsonb;
    v_n integer := 0;
begin
    if auth.uid() is not null and not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente calcula a previsão.' using errcode = '42501';
    end if;
    select min(dia) into v_primeiro from public.previsao_movimento_dia;
    -- Precisa de pelo menos 1 ano de treino antes do ano testado; o último ano completo é o passado
    for v_ano in
        select a from generate_series(greatest(coalesce(p_de, 0), extract(year from v_primeiro)::int + 2),
                                      least(coalesce(p_ate, 9999), extract(year from public.hoje_local())::int - 1)) a
    loop
        v_r := public.previsao_testar(365, 4, 1000, 3000, false, 30, make_date(v_ano, 1, 1));
        insert into public.previsao_teste_anos (ano, calculado_em, erro_semana, erro_semana_normal, erro_ingenuo, erro_media, mov_erro,
                                               vies, dentro_faixa, semanas_com_anomalia, resultado)
        values (v_ano, now(), (v_r ->> 'erro_semana')::numeric, (v_r ->> 'erro_semana_sem_anomalia')::numeric, (v_r ->> 'erro_ingenuo')::numeric,
                (v_r ->> 'erro_media')::numeric, (v_r ->> 'mov_erro')::numeric, (v_r ->> 'vies')::numeric, (v_r ->> 'dentro_faixa')::numeric,
                (v_r ->> 'semanas_com_anomalia')::int, v_r)
        on conflict (ano) do update set calculado_em = excluded.calculado_em, erro_semana = excluded.erro_semana,
            erro_semana_normal = excluded.erro_semana_normal, erro_ingenuo = excluded.erro_ingenuo, erro_media = excluded.erro_media,
            mov_erro = excluded.mov_erro, vies = excluded.vies, dentro_faixa = excluded.dentro_faixa,
            semanas_com_anomalia = excluded.semanas_com_anomalia, resultado = excluded.resultado;
        v_n := v_n + 1;
    end loop;
    return format('Pronto: %s anos testados. Veja: select * from previsao_teste_anos order by ano;', v_n);
end;
$$;

-- ---------------------------------------------------------------- Permissões
revoke execute on function public.previsao_ajustar(date, integer, numeric, numeric, numeric) from public, anon, authenticated;
revoke execute on function public.previsao_prever(date, date, boolean) from public, anon, authenticated;
revoke execute on function public.previsao_testar(integer, integer, numeric, numeric, boolean, numeric, date) from public, anon;
revoke execute on function public.previsao_calcular(integer) from public, anon;
revoke execute on function public.previsao_decomposicao(bigint) from public, anon;
grant execute on function public.previsao_testar(integer, integer, numeric, numeric, boolean, numeric, date) to authenticated, service_role;
grant execute on function public.previsao_calcular(integer) to authenticated, service_role;
revoke execute on function public.previsao_testar_anos(integer, integer) from public, anon;
grant execute on function public.previsao_testar_anos(integer, integer) to authenticated, service_role;
grant execute on function public.previsao_decomposicao(bigint) to authenticated, service_role;

-- ---------------------------------------------------------------- Agendamento: toda madrugada às 3h de Brasília (6h UTC)
do $$
begin
    if exists (select 1 from pg_extension where extname = 'pg_cron') then
        perform cron.unschedule(jobid) from cron.job where jobname = 'mercado-previsao';
        perform cron.schedule('mercado-previsao', '0 6 * * *', 'select public.previsao_calcular()');
    else
        raise notice 'pg_cron não está ligado: a previsão só é recalculada quando alguém rodar select public.previsao_calcular();';
    end if;
end;
$$;

-- Primeiro cálculo
select public.previsao_calcular() as resultado;
