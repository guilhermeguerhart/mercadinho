-- =====================================================================================================================
-- 021 - Correção do laboratório da previsão (020). Rodar no SQL Editor do Supabase depois do 020. Pode rodar de novo.
--
-- O Supabase recusa UPDATE e DELETE sem WHERE quando a chamada vem do programa (extensão pg_safeupdate): o laboratório
-- dava "Erro no servidor (400)" com "UPDATE requires a WHERE clause". No SQL Editor e no agendamento isso não acontece,
-- por isso os testes locais passaram.
--   previsao_laboratorio   calcula o período (dia/semana/mês) já ao montar a tabela temporária, sem UPDATE
--   previsao_guardar_teste DELETE/UPDATE com "where true" (só por segurança: hoje só o cálculo da madrugada chama)
-- Não muda nenhum dado.
-- =====================================================================================================================

create or replace function public.previsao_guardar_teste(p_ini date, p_fim date)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
    delete from public.previsao_teste_dias where true;
    insert into public.previsao_teste_dias (produto_id, dia, vendido, previsto, desvio, ingenuo, media60)
    select produto_id, dia, round(vend, 3), round(previsto, 3), round(coalesce(desvio, 0), 4), round(ingenuo, 3), round(media60, 3)
    from _pv_real;

    delete from public.previsao_teste_loja where true;
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
    update public.previsao_teste_loja l set desvio = round(l.previsto * coalesce(g.desvio_rel, 0), 2) from _pv_geral g where true;
end;
$$;

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
        select 0::bigint as produto_id, dia, vendas::numeric as vendido, previsto, desvio, ingenuo::numeric as ingenuo, media60,
               case p_agrupar when 'dia' then dia when 'semana' then date_trunc('week', dia)::date
                              else date_trunc('month', dia)::date end as periodo
        from public.previsao_teste_loja;
    else
        create temp table _lab as
        select t.produto_id, t.dia, t.vendido, t.previsto, t.desvio, t.ingenuo, t.media60,
               case p_agrupar when 'dia' then t.dia when 'semana' then date_trunc('week', t.dia)::date
                              else date_trunc('month', t.dia)::date end as periodo
        from public.previsao_teste_dias t
        join public.produtos p on p.id = t.produto_id
        where (p_escopo = 'todos')
           or (p_escopo = 'categoria' and p.categoria = p_categoria)
           or (p_escopo = 'produto' and t.produto_id = p_produto);
    end if;

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

-- ---------------------------------------------------------------- Permissões (as mesmas do 020)
revoke execute on function public.previsao_guardar_teste(date, date) from public, anon, authenticated;
revoke execute on function public.previsao_laboratorio(text, text, bigint, text) from public, anon;
grant execute on function public.previsao_laboratorio(text, text, bigint, text) to authenticated, service_role;

select 'Laboratório corrigido' as resultado;
