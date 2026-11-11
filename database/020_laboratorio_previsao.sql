-- =====================================================================================================================
-- 020 - Laboratório da previsão (semana 7, parte 4). Rodar no SQL Editor do Supabase depois do 019. Pode rodar de novo.
--
-- Pedido do Guilherme em 11/11: na versão de desenvolvimento, uma parte só para comparar o teste da previsão com o que
-- vendeu de verdade e com o ano passado, podendo abrir por dia, semana e mês.
--
-- 1. Duas tabelas novas guardam o teste do último ano dia a dia (antes só ficava o resumo por semana):
--      previsao_teste_dias   produto × dia: vendido, previsto, desvio, ano passado (mesmo dia da semana) e média de 60 dias
--      previsao_teste_loja   dia: número de vendas da loja (movimento) com as mesmas comparações, datas especiais e
--                            períodos fora do normal
-- 2. previsao_calcular (do 019) passa a preencher as duas tabelas a cada cálculo da madrugada. Nada mais muda nele.
-- 3. previsao_laboratorio(escopo, categoria, produto, agrupar): tudo o que a tela do laboratório mostra, num jsonb.
-- Só o gerente lê. Não muda nenhuma venda nem a previsão em si.
-- =====================================================================================================================

-- ---------------------------------------------------------------- Tabelas
create table if not exists public.previsao_teste_dias (
    produto_id  bigint not null references public.produtos (id) on delete cascade,
    dia         date not null,
    vendido     numeric(12,3) not null,
    previsto    numeric(12,3) not null,
    desvio      numeric(12,4) not null default 0,
    ingenuo     numeric(12,3) not null,      -- ano passado: o mesmo dia da semana, 52 semanas antes
    media60     numeric(12,3) not null,      -- média dos 60 dias antes do teste
    primary key (produto_id, dia)
);
create table if not exists public.previsao_teste_loja (
    dia       date primary key,
    vendas    integer not null,              -- número de vendas pagas (movimento da loja)
    previsto  numeric(10,2) not null,
    desvio    numeric(10,2) not null default 0,
    ingenuo   integer not null,
    media60   numeric(10,2) not null,
    aberto    boolean not null,
    evento    text,                          -- data especial (Natal, Dia das Mães...)
    anomalia  text                           -- período fora do normal (pandemia, greve...)
);
alter table public.previsao_teste_dias enable row level security;
alter table public.previsao_teste_loja enable row level security;
drop policy if exists "gerente le o teste por dia" on public.previsao_teste_dias;
create policy "gerente le o teste por dia" on public.previsao_teste_dias for select to authenticated using ((select public.eh_gerente()));
drop policy if exists "gerente le o teste da loja" on public.previsao_teste_loja;
create policy "gerente le o teste da loja" on public.previsao_teste_loja for select to authenticated using ((select public.eh_gerente()));

-- ---------------------------------------------------------------- Guarda o teste dia a dia
-- Chamada por previsao_calcular logo depois de previsao_testar, enquanto as tabelas temporárias ainda são as do teste
-- (_pv_real: produto × dia; _pv_geral e _pv_dias: o modelo da loja treinado até a véspera do teste).
create or replace function public.previsao_guardar_teste(p_ini date, p_fim date)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
    delete from public.previsao_teste_dias;
    insert into public.previsao_teste_dias (produto_id, dia, vendido, previsto, desvio, ingenuo, media60)
    select produto_id, dia, round(vend, 3), round(previsto, 3), round(coalesce(desvio, 0), 4), round(ingenuo, 3), round(media60, 3)
    from _pv_real;

    delete from public.previsao_teste_loja;
    insert into public.previsao_teste_loja (dia, vendas, previsto, desvio, ingenuo, media60, aberto, evento, anomalia)
    with mov as (select dia, sum(vendas) as n from public.previsao_movimento_dia where dia between p_ini - 364 and p_fim group by dia),
         abertos as (select dia from public.previsao_dias_abertos where dia between p_ini - 60 and p_fim),
         m60 as (select sum(m.n)::numeric / nullif(count(*), 0) as m
                 from abertos a left join mov m on m.dia = a.dia where a.dia between p_ini - 60 and p_ini - 1),
         ev as (select dia, string_agg(distinct nome, ', ') filter (where tipo <> 'anomalia') as evento,
                       string_agg(distinct nome, ', ') filter (where tipo = 'anomalia') as anomalia,
                       max(fator) filter (where tipo <> 'anomalia') as fator
                from public.eventos_calendario where dia between p_ini and p_fim group by dia),
         fechado as (select to_char(dia, 'MM-DD') as md from _pv_dias group by 1 having count(*) >= 2 and not bool_or(aberto))
    select d::date, coalesce(m.n, 0),
           round(case when f.md is not null or coalesce(e.fator, 1) = 0 then 0
                      else greatest(g.intercepto + g.inclinacao * ((d::date - g.treino_ini) / 365.25), 0)
                           * g.idx_semana[extract(dow from d)::int + 1] * g.idx_mes[extract(month from d)::int] * coalesce(e.fator, 1)
                 end, 2),
           0, coalesce(a1.n, 0),
           round(case when ab.dia is not null then coalesce((select m from m60), 0) else 0 end, 2),
           ab.dia is not null, e.evento, e.anomalia
    from generate_series(p_ini, p_fim, interval '1 day') d
    cross join _pv_geral g
    left join mov m on m.dia = d::date
    left join mov a1 on a1.dia = d::date - 364
    left join abertos ab on ab.dia = d::date
    left join ev e on e.dia = d::date
    left join fechado f on f.md = to_char(d::date, 'MM-DD');
    update public.previsao_teste_loja l set desvio = round(l.previsto * coalesce(g.desvio_rel, 0), 2) from _pv_geral g;
end;
$$;

-- ---------------------------------------------------------------- Cálculo completo (o do 019 + guardar o teste dia a dia)
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
        -- Laboratório (020): guarda o teste dia a dia antes de o modelo de hoje substituir o do teste
        perform public.previsao_guardar_teste((v_teste ->> 'teste_ini')::date, (v_teste ->> 'teste_fim')::date);
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

-- ---------------------------------------------------------------- Laboratório: o teste aberto por dia, semana ou mês
-- p_escopo: 'todos' (todos os produtos), 'categoria' (p_categoria), 'produto' (p_produto) ou 'loja' (número de vendas).
-- p_agrupar: 'dia', 'semana' ou 'mes'.
-- Erro = soma de |previsto − vendido| ÷ soma do vendido, medido produto a produto em cada dia/semana/mês (o mesmo número
-- do topo da tela quando é "todos" por semana). erro_total = o mesmo, mas da soma do escopo (erros de produtos diferentes
-- se compensam). Faixa de 80%: ±1,07 desvio no dia; ±1,33 × raiz(soma dos desvios²) na semana e no mês (calibrado no 019).
create or replace function public.previsao_laboratorio(p_escopo text default 'todos', p_categoria text default null,
                                                       p_produto bigint default null, p_agrupar text default 'semana')
returns jsonb
language plpgsql
security definer
set search_path = public
set client_min_messages = warning
as $$
declare
    v_z numeric;
    v_res jsonb;
    v_ini date;
    v_fim date;
begin
    if auth.uid() is not null and not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente vê o laboratório da previsão.' using errcode = '42501';
    end if;
    if p_escopo not in ('todos', 'categoria', 'produto', 'loja') then
        raise exception 'Escopo inválido: use todos, categoria, produto ou loja.' using errcode = '22023';
    end if;
    if p_agrupar not in ('dia', 'semana', 'mes') then
        raise exception 'Agrupamento inválido: use dia, semana ou mes.' using errcode = '22023';
    end if;
    if p_escopo = 'categoria' and p_categoria is null then raise exception 'Escolha a categoria.' using errcode = '22023'; end if;
    if p_escopo = 'produto' and p_produto is null then raise exception 'Escolha o produto.' using errcode = '22023'; end if;
    v_z := case when p_agrupar = 'dia' then case when p_escopo = 'loja' then 1.28 else 1.07 end else 1.33 end;

    select min(dia), max(dia) into v_ini, v_fim from public.previsao_teste_loja;

    -- Dias do escopo (a loja entra como um "produto" 0)
    drop table if exists _lab;
    if p_escopo = 'loja' then
        create temp table _lab as
        select 0::bigint as produto_id, dia, vendas::numeric as vendido, previsto, desvio, ingenuo::numeric as ingenuo, media60
        from public.previsao_teste_loja;
    else
        create temp table _lab as
        select t.produto_id, t.dia, t.vendido, t.previsto, t.desvio, t.ingenuo, t.media60
        from public.previsao_teste_dias t
        join public.produtos p on p.id = t.produto_id
        where (p_escopo = 'todos')
           or (p_escopo = 'categoria' and p.categoria = p_categoria)
           or (p_escopo = 'produto' and t.produto_id = p_produto);
    end if;
    alter table _lab add column periodo date;
    update _lab set periodo = case p_agrupar when 'dia' then dia when 'semana' then date_trunc('week', dia)::date
                                             else date_trunc('month', dia)::date end;

    -- Produto × período
    drop table if exists _labp;
    create temp table _labp as
    select produto_id, periodo, sum(vendido) as vendido, sum(previsto) as previsto, sqrt(sum(desvio * desvio)) as desvio,
           sum(ingenuo) as ingenuo, sum(media60) as media60
    from _lab group by 1, 2;

    with serie as (
        select p.periodo, sum(vendido) as vendido, sum(previsto) as previsto, sum(ingenuo) as ingenuo, sum(media60) as media60,
               sqrt(sum(desvio * desvio)) as desvio,
               sum(abs(previsto - vendido)) as abs_m, sum(abs(ingenuo - vendido)) as abs_i
        from _labp p group by 1
    ),
    dias as (
        select periodo, min(l.dia) as ini, max(l.dia) as fim, count(*) filter (where l.aberto) as abertos,
               string_agg(distinct l.evento, ', ') as eventos, string_agg(distinct l.anomalia, ', ') as anomalias
        from (select distinct periodo, dia from _lab) x
        join public.previsao_teste_loja l using (dia)
        group by periodo
    ),
    sem as (    -- dia da semana (sempre do dado diário)
        select extract(dow from dia)::int as dow, sum(vendido) as vendido, sum(previsto) as previsto,
               sum(abs(previsto - vendido)) as abs_m, sum(abs(ingenuo - vendido)) as abs_i
        from _lab group by 1
    ),
    mes as (    -- mês a mês (sempre produto × mês)
        select m, sum(v) as vendido, sum(pr) as previsto, sum(abs(pr - v)) as abs_m, sum(abs(i - v)) as abs_i, sum(abs(m60 - v)) as abs_a
        from (select produto_id, date_trunc('month', dia)::date as m, sum(vendido) as v, sum(previsto) as pr, sum(ingenuo) as i, sum(media60) as m60
              from _lab group by 1, 2) x group by m
    ),
    prod as (
        select p.produto_id, sum(vendido) as vendido, sum(previsto) as previsto,
               sum(abs(previsto - vendido)) as abs_m, sum(abs(ingenuo - vendido)) as abs_i, sum(abs(media60 - vendido)) as abs_a
        from _labp p group by 1
    ),
    ev as (     -- datas especiais do período de teste: o dia inteiro do escopo
        select l.dia, l.evento, sum(x.vendido) as vendido, sum(x.previsto) as previsto, sum(x.ingenuo) as ingenuo
        from public.previsao_teste_loja l join _lab x on x.dia = l.dia
        where l.evento is not null group by 1, 2
    )
    select jsonb_build_object(
        'teste_ini', v_ini, 'teste_fim', v_fim, 'escopo', p_escopo, 'agrupar', p_agrupar, 'faixa_z', v_z,
        'nome', case p_escopo when 'loja' then 'Movimento da loja (número de vendas)' when 'todos' then 'Todos os produtos'
                              when 'categoria' then p_categoria
                              else (select nome from public.produtos where id = p_produto) end,
        'unidade', case p_escopo when 'loja' then 'vendas'
                                 when 'produto' then (select unidade_medida from public.produtos where id = p_produto)
                                 else 'mistura' end,
        'resumo', (select jsonb_build_object(
            'vendido', round(sum(vendido), 2), 'previsto', round(sum(previsto), 2), 'ingenuo', round(sum(ingenuo), 2),
            'erro', round(sum(abs(previsto - vendido)) / nullif(sum(vendido), 0), 4),
            'erro_ingenuo', round(sum(abs(ingenuo - vendido)) / nullif(sum(vendido), 0), 4),
            'erro_media', round(sum(abs(media60 - vendido)) / nullif(sum(vendido), 0), 4),
            'vies', round(sum(previsto) / nullif(sum(vendido), 0) - 1, 4),
            'dentro_faixa', round(avg(case when vendido between greatest(previsto - v_z * desvio, 0) and previsto + v_z * desvio then 1.0 else 0 end)
                                  filter (where previsto > 0), 4),
            'erro_total', (select round(sum(abs(previsto - vendido)) / nullif(sum(vendido), 0), 4) from serie),
            'erro_total_ingenuo', (select round(sum(abs(ingenuo - vendido)) / nullif(sum(vendido), 0), 4) from serie),
            'periodos', (select count(*) from serie),
            'periodos_ganhou', (select count(*) from serie where abs_m < abs_i),
            'produtos', count(distinct produto_id))
            from _labp),
        'serie', coalesce((select jsonb_agg(jsonb_build_object(
            'periodo', s.periodo, 'ini', d.ini, 'fim', d.fim, 'abertos', d.abertos,
            'vendido', round(s.vendido, 2), 'previsto', round(s.previsto, 2),
            'minimo', round(greatest(s.previsto - v_z * s.desvio, 0), 2), 'maximo', round(s.previsto + v_z * s.desvio, 2),
            'ingenuo', round(s.ingenuo, 2), 'media60', round(s.media60, 2),
            'erro', round(s.abs_m / nullif(s.vendido, 0), 4), 'erro_ingenuo', round(s.abs_i / nullif(s.vendido, 0), 4),
            'eventos', d.eventos, 'anomalias', d.anomalias) order by s.periodo)
            from serie s left join dias d using (periodo)), '[]'),
        'dia_semana', coalesce((select jsonb_agg(jsonb_build_object(
            'dow', dow, 'vendido', round(vendido, 2), 'previsto', round(previsto, 2),
            'erro', round(abs_m / nullif(vendido, 0), 4), 'erro_ingenuo', round(abs_i / nullif(vendido, 0), 4),
            'vies', round(previsto / nullif(vendido, 0) - 1, 4)) order by dow) from sem), '[]'),
        'meses', coalesce((select jsonb_agg(jsonb_build_object(
            'mes', m, 'vendido', round(vendido, 2), 'previsto', round(previsto, 2),
            'erro', round(abs_m / nullif(vendido, 0), 4), 'erro_ingenuo', round(abs_i / nullif(vendido, 0), 4),
            'erro_media', round(abs_a / nullif(vendido, 0), 4), 'vies', round(previsto / nullif(vendido, 0) - 1, 4)) order by m) from mes), '[]'),
        'categorias', case when p_escopo = 'todos' then coalesce((select jsonb_agg(jsonb_build_object(
            'categoria', c.categoria, 'produtos', c.n, 'vendido', round(c.vendido, 2),
            'erro', round(c.abs_m / nullif(c.vendido, 0), 4), 'erro_ingenuo', round(c.abs_i / nullif(c.vendido, 0), 4),
            'vies', round(c.previsto / nullif(c.vendido, 0) - 1, 4)) order by c.abs_m / nullif(c.vendido, 0) desc nulls last)
            from (select pr.categoria, count(*) as n, sum(x.vendido) as vendido, sum(x.previsto) as previsto, sum(x.abs_m) as abs_m, sum(x.abs_i) as abs_i
                  from prod x join public.produtos pr on pr.id = x.produto_id group by 1) c), '[]') else '[]'::jsonb end,
        'produtos', case when p_escopo in ('todos', 'categoria') then coalesce((select jsonb_agg(jsonb_build_object(
            'produto_id', x.produto_id, 'produto', pr.nome, 'categoria', pr.categoria, 'unidade', pr.unidade_medida,
            'vendido', round(x.vendido, 2), 'previsto', round(x.previsto, 2),
            'erro', round(x.abs_m / nullif(x.vendido, 0), 4), 'erro_ingenuo', round(x.abs_i / nullif(x.vendido, 0), 4),
            'erro_media', round(x.abs_a / nullif(x.vendido, 0), 4), 'vies', round(x.previsto / nullif(x.vendido, 0) - 1, 4))
            order by x.vendido desc)
            from prod x join public.produtos pr on pr.id = x.produto_id), '[]') else '[]'::jsonb end,
        'eventos', coalesce((select jsonb_agg(jsonb_build_object(
            'dia', dia, 'evento', evento, 'vendido', round(vendido, 2), 'previsto', round(previsto, 2), 'ingenuo', round(ingenuo, 2))
            order by dia) from ev), '[]'),
        'anos', coalesce((select jsonb_agg(jsonb_build_object(
            'ano', ano, 'erro', erro_semana, 'erro_normal', erro_semana_normal, 'erro_ingenuo', erro_ingenuo, 'erro_media', erro_media,
            'mov_erro', mov_erro, 'vies', vies, 'dentro_faixa', dentro_faixa, 'semanas_com_anomalia', semanas_com_anomalia) order by ano)
            from public.previsao_teste_anos), '[]'),
        'categorias_lista', coalesce((select jsonb_agg(categoria order by categoria) from (
            select distinct p.categoria from public.produtos p where exists (select 1 from public.previsao_teste_dias t where t.produto_id = p.id)) c), '[]')
    ) into v_res;
    return v_res;
end;
$$;

-- ---------------------------------------------------------------- Permissões
revoke execute on function public.previsao_guardar_teste(date, date) from public, anon, authenticated;
revoke execute on function public.previsao_laboratorio(text, text, bigint, text) from public, anon;
grant execute on function public.previsao_laboratorio(text, text, bigint, text) to authenticated, service_role;

-- Recalcula para já preencher o teste dia a dia
select public.previsao_calcular() as resultado;
