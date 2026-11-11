-- =====================================================================
-- 019 - Semana 7, parte 3: tela "Previsão de compras" + ajustes da previsão
-- =====================================================================
-- Depende de 016 (previsão). Usa o 018 (simulador) se ele existir; sem ele também funciona.
-- O que muda:
--   * previsao_ajustar: dias em que o produto estava em falta não entram no aprendizado
--     (venda perdida anotada em simulacao_faltas ou 3+ dias seguidos sem venda, improváveis pela média);
--   * previsao_calcular: faixa de 80% de verdade (±1,07 desvio no dia) e o desvio de cada dia em previsao_dias.desvio;
--   * pedido_sugerido(): a conta do pedido de cada fornecedor (a tela e o simulador usam a mesma);
--   * previsao_produto(), previsao_alertas(), previsao_tela(): o que a tela mostra, já pronto (abre rápido);
--   * simulacao_pedidos (do 018) passa a usar pedido_sugerido.
-- Rodar inteiro no SQL Editor. No fim ele recalcula a previsão (uns 20 segundos).
-- =====================================================================

alter table public.previsao_dias add column if not exists desvio numeric;   -- desvio do dia (unidades ou kg)

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

    -- Dias em que o produto estava em falta não ensinam a previsão (teria vendido mais se tivesse):
    --   1) venda perdida anotada (simulacao_faltas, do 018);
    --   2) sequência de 3 ou mais dias abertos sem nenhuma venda, muito improvável pela média do produto
    --      (média por dia × dias > 9: a chance de acontecer por acaso é menor que 1 em 8.000).
    -- Testado em 24 anos de exemplo (019): o erro fica igual e a previsão para de pedir pouco (viés de -4,2% para -2,8%).
    if to_regclass('public.simulacao_faltas') is not null then
        execute 'update _pv_pd pd set normal = false from public.simulacao_faltas f where f.produto_id = pd.produto_id and f.dia = pd.dia';
    end if;
    with m as (select produto_id, avg(y) as media from _pv_pd where normal group by produto_id),
    s as (select produto_id, dia, y, row_number() over (partition by produto_id order by dia)
                 - row_number() over (partition by produto_id, (y = 0) order by dia) as grp
          from _pv_pd),
    r as (select s.produto_id, s.grp, min(s.dia) as d1, max(s.dia) as d2, count(*) as n
          from s where s.y = 0 group by s.produto_id, s.grp)
    update _pv_pd pd set normal = false
    from r join m using (produto_id)
    where pd.produto_id = r.produto_id and pd.dia between r.d1 and r.d2 and r.n >= 3 and m.media * r.n > 9;

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
    -- Faixa de 80%: ±1,07 desvio no dia (medido nos 24 anos de teste: ±1,28 pegava 86% dos dias).
    -- Para uma semana ou um ciclo de pedido: ±1,33 × raiz(soma dos desvios²) (ver pedido_sugerido e previsao_produto).
    insert into public.previsao_dias (produto_id, dia, previsto, minimo, maximo, tendencia, sazonal, evento, desvio)
    select produto_id, dia, round(previsto, 3),
           round(case when previsto = 0 then 0 else greatest(previsto - 1.07 * desvio, 0) end, 3),
           round(case when previsto = 0 then 0 else previsto + 1.07 * desvio end, 3),
           round(tendencia, 3), round(sazonal, 4), evento, round(case when previsto = 0 then 0 else desvio end, 4)
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


-- ---------------------------------------------------------------- Pedido sugerido (tela Previsão e simulador usam a mesma conta)
-- Para cada fornecedor: o próximo dia de pedido (hoje + prazo cai num dia de entrega; sem dias de entrega = segunda),
-- quando chega, até quando precisa durar (a entrega seguinte, ou a validade do produto se for antes) e, por produto:
--   sugerido = previsão até lá + margem × desvio do período - estoque dentro da validade - o que já foi pedido,
-- arredondado para cima em caixas fechadas (produto por quilo: quilos inteiros).
-- Margem 1,28 = em ~9 de cada 10 ciclos não falta. Sem previsão para o dia: média dos últimos 28 dias.
create or replace function public.pedido_sugerido(p_dia date default null, p_fornecedor bigint default null,
                                                  p_margem numeric default 1.28)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
    v_hoje date := coalesce(p_dia, public.hoje_local());
    v_f record;
    v_dias int[];
    v_pedido date;
    v_chegada date;
    v_k integer;
    v_itens jsonb;
    v_res jsonb := '[]'::jsonb;
begin
    if auth.uid() is not null and not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente vê o pedido sugerido.' using errcode = '42501';
    end if;
    for v_f in select * from public.fornecedores where ativo and (p_fornecedor is null or id = p_fornecedor) order by nome loop
        v_dias := (select array_agg(trim(x)::int) from unnest(string_to_array(nullif(trim(coalesce(v_f.dias_entrega, '')), ''), ',')) x
                   where trim(x) ~ '^[0-6]$');
        -- Próximo dia de pedido (de hoje até 14 dias)
        select d::date into v_pedido from generate_series(v_hoje, v_hoje + 14, interval '1 day') d
        where (v_dias is null and extract(dow from d) = 1)
           or (v_dias is not null and extract(dow from d::date + coalesce(v_f.prazo_entrega_dias, 3))::int = any (v_dias))
        order by d limit 1;
        continue when v_pedido is null;
        v_chegada := v_pedido + coalesce(v_f.prazo_entrega_dias, 3);
        if v_dias is null then
            v_k := 7;
        else
            select min(k) into v_k from generate_series(1, 7) k where extract(dow from v_chegada + k)::int = any (v_dias);
        end if;

        with prods as (
            select p.id, p.nome, p.categoria, p.unidade_medida, greatest(coalesce(p.unidades_por_caixa, 1), 1) as caixa, p.preco_custo,
                   least(v_chegada + v_k, v_chegada + coalesce(p.dias_validade, 3650)) as ate
            from public.produtos p where p.fornecedor_id = v_f.id
        ), media as (
            select i.produto_id, sum(i.quantidade) / 28.0 as m
            from public.venda_itens i join public.vendas v on v.id = i.venda_id
            where v.situacao = 'paga' and v.dia between v_hoje - 28 and v_hoje - 1
              and i.produto_id in (select id from prods)
            group by i.produto_id
        ), demanda as (
            select pr.id,
                   sum(coalesce(pd.previsto, md.m, 0)) as qtd,
                   sqrt(sum(power(coalesce(pd.desvio, (pd.maximo - pd.previsto) / 1.07, sqrt(coalesce(pd.previsto, md.m, 0))), 2))) as desvio,
                   string_agg(distinct pd.evento, ', ') as eventos
            from prods pr
            cross join lateral generate_series(v_hoje, pr.ate - 1, interval '1 day') d
            left join public.previsao_dias pd on pd.produto_id = pr.id and pd.dia = d::date
            left join media md on md.produto_id = pr.id
            group by pr.id
        ), estoque as (
            select produto_id, sum(quantidade_restante) as q from public.lotes
            where quantidade_restante > 0 and (validade is null or validade > v_hoje) and produto_id in (select id from prods)
            group by produto_id
        ), pendente as (
            select i.produto_id, sum(i.quantidade) as q from public.pedido_itens i join public.pedidos pd on pd.id = i.pedido_id
            where pd.situacao = 'pendente' and i.produto_id in (select id from prods) group by i.produto_id
        ), conta as (
            select pr.*, d.qtd, d.desvio, d.eventos, coalesce(e.q, 0) as estoque, coalesce(pe.q, 0) as pendente,
                   d.qtd + p_margem * d.desvio - coalesce(e.q, 0) - coalesce(pe.q, 0) as falta
            from prods pr join demanda d on d.id = pr.id
            left join estoque e on e.produto_id = pr.id
            left join pendente pe on pe.produto_id = pr.id
        )
        select jsonb_agg(jsonb_build_object(
                   'produto_id', id, 'nome', nome, 'categoria', categoria, 'unidade', unidade_medida, 'caixa', caixa,
                   'estoque', round(estoque, 3), 'pendente', round(pendente, 3),
                   'previsto', round(qtd, 1), 'margem', round(p_margem * desvio, 1), 'ate', ate, 'eventos', eventos,
                   'sugerido', case when unidade_medida = 'kg' then ceil(falta) else ceil(falta / caixa) * caixa end,
                   'custo', preco_custo) order by nome)
        into v_itens
        from conta where falta > 0.05;

        v_res := v_res || jsonb_build_array(jsonb_build_object(
            'fornecedor_id', v_f.id, 'fornecedor', v_f.nome, 'envio_preferido', v_f.envio_preferido, 'pedido_minimo', v_f.pedido_minimo,
            'data_pedido', v_pedido, 'pedir_hoje', v_pedido = v_hoje, 'chegada', v_chegada, 'proxima_entrega', v_chegada + v_k,
            'tem_pendente', exists (select 1 from public.pedidos where fornecedor_id = v_f.id and situacao = 'pendente'),
            'total', coalesce((select round(sum((x ->> 'sugerido')::numeric * coalesce((x ->> 'custo')::numeric, 0)), 2)
                               from jsonb_array_elements(v_itens) x), 0),
            'itens', coalesce(v_itens, '[]'::jsonb)));
    end loop;
    return v_res;
end;
$$;

-- ---------------------------------------------------------------- Um produto: 12 semanas passadas e 8 à frente
create or replace function public.previsao_produto(p_produto bigint)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
    v_res jsonb;
begin
    if auth.uid() is not null and not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente vê a previsão.' using errcode = '42501';
    end if;
    select jsonb_build_object(
        'produto_id', p.id, 'nome', p.nome, 'categoria', p.categoria, 'unidade', p.unidade_medida,
        'situacao', coalesce(m.situacao, 'sem_vendas'),
        'media_dia', m.media_dia, 'crescimento_ano', m.crescimento_ano, 'parte_datas', m.parte_datas,
        'idx_semana', m.idx_semana, 'idx_mes', m.idx_mes,
        'erro_teste', m.teste_erro_semana, 'erro_ingenuo', m.teste_erro_ingenuo,
        'calculado_em', m.calculado_em,
        'passado', coalesce((select jsonb_agg(jsonb_build_object('semana', x.semana, 'vendido', x.vendido, 'previsto', x.ajuste,
                                                                  'tendencia', x.tendencia, 'datas', x.datas) order by x.semana)
                             from (select * from public.previsao_decomposicao(p.id) order by semana desc limit 12) x), '[]'::jsonb),
        'futuro', coalesce((select jsonb_agg(jsonb_build_object('semana', w.semana, 'previsto', w.previsto,
                                                                 'minimo', greatest(w.previsto - 1.33 * w.desvio, 0),
                                                                 'maximo', w.previsto + 1.33 * w.desvio, 'eventos', w.eventos) order by w.semana)
                            from (select date_trunc('week', dia)::date as semana, round(sum(previsto), 1) as previsto,
                                         round(sqrt(sum(power(coalesce(desvio, (maximo - previsto) / 1.07), 2))), 1) as desvio,
                                         string_agg(distinct evento, ', ') as eventos
                                  from public.previsao_dias where produto_id = p.id
                                  group by 1 order by 1 limit 8) w), '[]'::jsonb),
        'datas', coalesce((select jsonb_agg(jsonb_build_object('evento', e.evento, 'a_mais', round(e.extra_hoje, 1)) order by e.extra_hoje desc)
                           from public.previsao_eventos e where e.produto_id = p.id and e.extra_hoje >= 0.5), '[]'::jsonb)
    )
    into v_res
    from public.produtos p left join public.previsao_modelos m on m.produto_id = p.id
    where p.id = p_produto;
    return v_res;
end;
$$;

-- ---------------------------------------------------------------- Alertas da previsão
-- parado: produto que costuma vender ficou dias sem venda (falta na prateleira?);
-- queda / alta: últimos 14 dias muito abaixo ou acima do que o modelo espera (mais de 3 desvios e 25%);
-- falta: clientes que ficaram sem o produto nos últimos 7 dias (simulacao_faltas, se existir);
-- data: data especial nos próximos 21 dias com os produtos que vendem a mais e até quando pedir.
create or replace function public.previsao_alertas()
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
    v_hoje date := public.hoje_local();
    v_res jsonb := '[]'::jsonb;
    v_faltas jsonb;
begin
    if auth.uid() is not null and not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente vê os alertas.' using errcode = '42501';
    end if;

    -- Parado e queda/alta
    with abertos as (
        select dia, row_number() over (order by dia desc) as n from public.previsao_dias_abertos
        where dia between v_hoje - 30 and v_hoje - 1
    ), modelo as (
        select m.*, p.nome, p.unidade_medida from public.previsao_modelos m join public.produtos p on p.id = m.produto_id
        where m.situacao = 'ok' and m.treino_ini is not null
    ), dia_a_dia as (
        select m.produto_id, a.dia, a.n,
               greatest(m.intercepto + m.inclinacao * ((a.dia - m.treino_ini) / 365.25), 0)
                 * m.idx_semana[extract(dow from a.dia)::int + 1] * m.idx_mes[extract(month from a.dia)::int] as esperado,
               coalesce((select sum(quantidade) from public.previsao_vendas_produto v where v.produto_id = m.produto_id and v.dia = a.dia), 0) as y
        from modelo m cross join abertos a
        where a.n <= 14 and not exists (select 1 from public.eventos_calendario e where e.dia = a.dia)
    ), parado as (    -- dias seguidos sem venda, contando de ontem para trás
        select d.produto_id, count(*) as dias, sum(d.esperado) as esperado
        from dia_a_dia d
        where d.n < coalesce((select min(x.n) from dia_a_dia x where x.produto_id = d.produto_id and x.y > 0), 99)
        group by d.produto_id
    ), soma as (
        select produto_id, sum(y) as y, sum(esperado) as esperado, count(*) as dias from dia_a_dia group by produto_id
    )
    select v_res || coalesce(jsonb_agg(a order by (a ->> 'ordem')::int, a ->> 'produto'), '[]'::jsonb) into v_res
    from (
        select jsonb_build_object('tipo', 'parado', 'ordem', 1, 'produto_id', m.produto_id, 'produto', m.nome,
               'texto', format('Sem venda há %s dias (o normal seria vender uns %s). Está na prateleira? Está em falta?',
                               pa.dias, round(pa.esperado))) as a
        from parado pa join modelo m on m.produto_id = pa.produto_id
        where pa.dias >= 2 and pa.esperado >= 5
        union all
        select jsonb_build_object('tipo', case when s.y < s.esperado then 'queda' else 'alta' end, 'ordem', 2,
               'produto_id', m.produto_id, 'produto', m.nome,
               'texto', format('Últimos 14 dias: vendeu %s, o esperado era %s (%s%%).', round(s.y, 1), round(s.esperado, 1),
                               to_char(round((s.y / s.esperado - 1) * 100), 'SG999')))
        from soma s join modelo m on m.produto_id = s.produto_id
        where s.esperado > 0 and abs(s.y - s.esperado) > 3 * m.desvio * sqrt(s.dias) and abs(s.y / s.esperado - 1) > 0.25
          and not exists (select 1 from parado pa where pa.produto_id = s.produto_id and pa.dias >= 2 and pa.esperado >= 5)
    ) x;

    -- Clientes que ficaram sem o produto (simulador; no futuro, o caixa)
    if to_regclass('public.simulacao_faltas') is not null then
        execute $q$
            select coalesce(jsonb_agg(jsonb_build_object('tipo', 'falta', 'ordem', 0, 'produto_id', p.id, 'produto', p.nome,
                       'texto', format('%s clientes ficaram sem nos últimos 7 dias (%s %s).', f.clientes,
                                       replace(round(f.qtd, 1)::text, '.', ','), case when p.unidade_medida = 'kg' then 'kg' else 'un.' end))
                   order by f.clientes desc), '[]'::jsonb)
            from (select produto_id, sum(clientes) as clientes, sum(quantidade) as qtd from public.simulacao_faltas
                  where dia between $1 - 7 and $1 - 1 group by produto_id having sum(clientes) >= 4
                  order by sum(clientes) desc limit 10) f
            join public.produtos p on p.id = f.produto_id
        $q$ into v_faltas using v_hoje;
        v_res := v_faltas || v_res;
    end if;

    -- Datas especiais chegando
    select v_res || coalesce(jsonb_agg(jsonb_build_object('tipo', 'data', 'ordem', 3, 'produto_id', null, 'produto', e.nome,
               'dia', e.dia, 'pedir_ate', e.pedir_ate,
               'texto', format('%s em %s dias (%s). Vende a mais: %s. Pedir até %s.', e.nome, e.dia - v_hoje, to_char(e.dia, 'DD/MM'),
                               e.produtos, to_char(e.pedir_ate, 'DD/MM'))) order by e.dia), '[]'::jsonb)
    into v_res
    from (
        select ev.dia, ev.nome,
               string_agg(p.nome || ' (+' || replace(round(pe.extra_hoje)::text, '.', ',') || ')', ', ' order by pe.extra_hoje desc) as produtos,
               ev.dia - max(coalesce(f.prazo_entrega_dias, p.prazo_entrega_dias, 3)) - 1 as pedir_ate
        from public.eventos_calendario ev
        join lateral (select * from public.previsao_eventos x where x.evento = ev.nome and x.extra_hoje >= 1
                      order by x.extra_hoje desc limit 5) pe on true
        join public.produtos p on p.id = pe.produto_id
        left join public.fornecedores f on f.id = p.fornecedor_id
        where ev.tipo <> 'anomalia' and ev.fator > 1 and ev.dia between v_hoje + 1 and v_hoje + 21
        group by ev.dia, ev.nome
    ) e;
    return v_res;
end;
$$;

-- ---------------------------------------------------------------- Topo da tela e aba "Teste da previsão"
create or replace function public.previsao_tela()
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
    v_hoje date := public.hoje_local();
    v_perdidas jsonb := null;
begin
    if auth.uid() is not null and not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente vê a previsão.' using errcode = '42501';
    end if;
    if to_regclass('public.simulacao_faltas') is not null then
        execute 'select jsonb_build_object(''clientes'', coalesce(sum(clientes), 0), ''produtos'', count(distinct produto_id))
                 from public.simulacao_faltas where dia between $1 - 7 and $1 - 1' into v_perdidas using v_hoje;
    end if;
    return jsonb_build_object(
        'hoje', v_hoje,
        'geral', (select to_jsonb(g) - 'idx_semana' - 'idx_mes' - 'parametros' from public.previsao_geral g limit 1),
        'perdidas_7d', v_perdidas,
        'anos', coalesce((select jsonb_agg(jsonb_build_object('ano', ano, 'erro', erro_semana, 'erro_ingenuo', erro_ingenuo,
                                                              'erro_media', erro_media, 'vies', vies) order by ano)
                          from public.previsao_teste_anos), '[]'::jsonb),
        'piores', coalesce((select jsonb_agg(x order by (x ->> 'erro')::numeric desc) from (
                               select jsonb_build_object('produto_id', m.produto_id, 'produto', p.nome, 'erro', m.teste_erro_semana,
                                                         'erro_ingenuo', m.teste_erro_ingenuo, 'vendido', m.teste_vendido) as x
                               from public.previsao_modelos m join public.produtos p on p.id = m.produto_id
                               where m.teste_erro_semana is not null and m.teste_vendido >= 52
                               order by m.teste_erro_semana desc limit 10) y), '[]'::jsonb),
        'melhores', coalesce((select jsonb_agg(x order by (x ->> 'erro')::numeric) from (
                               select jsonb_build_object('produto_id', m.produto_id, 'produto', p.nome, 'erro', m.teste_erro_semana,
                                                         'erro_ingenuo', m.teste_erro_ingenuo, 'vendido', m.teste_vendido) as x
                               from public.previsao_modelos m join public.produtos p on p.id = m.produto_id
                               where m.teste_erro_semana is not null and m.teste_vendido >= 52
                               order by m.teste_erro_semana limit 5) y), '[]'::jsonb)
    );
end;
$$;

-- ---------------------------------------------------------------- Simulador (018) passa a usar o mesmo pedido sugerido
create or replace function public.simulacao_pedidos(p_dia date)
returns integer
language plpgsql
set search_path = public
as $$
declare
    v_f jsonb;
    v_itens jsonb;
    v_pedido bigint;
    v_n integer := 0;
    v_margem numeric := coalesce((select margem from public.simulacao), 1.28);
begin
    perform public.simulacao_como(null);
    for v_f in select * from jsonb_array_elements(public.pedido_sugerido(p_dia, null, v_margem)) loop
        continue when not (v_f ->> 'pedir_hoje')::boolean or (v_f ->> 'tem_pendente')::boolean
                      or jsonb_array_length(v_f -> 'itens') = 0;
        select jsonb_agg(jsonb_build_object('produto_id', x ->> 'produto_id', 'quantidade', x ->> 'sugerido', 'custo', x ->> 'custo'))
        into v_itens from jsonb_array_elements(v_f -> 'itens') x;
        perform public.simulacao_como(public.simulacao_gerente());
        v_pedido := public.criar_pedido((v_f ->> 'fornecedor_id')::bigint, (v_f ->> 'chegada')::date, 'Pedido automático (simulação)', v_itens);
        update public.pedidos set data_pedido = p_dia,
               criado_em = (p_dia + time '07:30' + random() * interval '60 minutes') at time zone 'America/Sao_Paulo'
        where id = v_pedido;
        v_n := v_n + 1;
        perform public.simulacao_anotar(p_dia, 'pedido', (v_f ->> 'fornecedor') || ' (pedido ' || v_pedido || ', '
                                        || jsonb_array_length(v_itens) || ' itens, chega ' || to_char((v_f ->> 'chegada')::date, 'DD/MM') || ')');
    end loop;
    return v_n;
end;
$$;

-- ---------------------------------------------------------------- Permissões
revoke execute on function public.pedido_sugerido(date, bigint, numeric) from public, anon;
revoke execute on function public.previsao_produto(bigint) from public, anon;
revoke execute on function public.previsao_alertas() from public, anon;
revoke execute on function public.previsao_tela() from public, anon;
revoke execute on function public.simulacao_pedidos(date) from public, anon, authenticated;
grant execute on function public.pedido_sugerido(date, bigint, numeric) to authenticated, service_role;
grant execute on function public.previsao_produto(bigint) to authenticated, service_role;
grant execute on function public.previsao_alertas() to authenticated, service_role;
grant execute on function public.previsao_tela() to authenticated, service_role;

-- Recalcula já com as regras novas
select public.previsao_calcular() as resultado;
