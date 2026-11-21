-- =====================================================================
-- 023 - Semana 8, parte 2: tela Calendário (junta Previsão de compras e Fechamento do dia)
-- =====================================================================
-- Depende de 009 (fechamento), 012 (calendário de datas), 016/019 (previsão) e 022 (fornecedor_proximo_pedido).
-- Pode ser rodado de novo. Não muda dados.
--   calendario_mes(p_mes): para cada dia do mês, o vendido (vendas pagas), se o dia foi fechado, a previsão de vendas
--   em R$ (soma da previsão de cada produto × preço; só do dia de hoje em diante, nos dias que a previsão cobre), as
--   datas especiais e os dias de pedido de cada fornecedor (dias em que pedir pega uma entrega dele), e as datas
--   festivas com "pedir até" e os produtos que vendem a mais.
-- =====================================================================

create or replace function public.calendario_mes(p_mes date default null)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
    v_hoje date := public.hoje_local();
    v_ini date := date_trunc('month', coalesce(p_mes, v_hoje))::date;
    v_fim date := (date_trunc('month', coalesce(p_mes, v_hoje)) + interval '1 month - 1 day')::date;
    v_res jsonb;
begin
    if auth.uid() is not null and not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente vê o calendário.' using errcode = '42501';
    end if;

    with dias as (
        select d::date as dia from generate_series(v_ini, v_fim, interval '1 day') d
    ), vendido as (
        select v.dia, sum(v.total) as total, count(*) as vendas
        from public.vendas v where v.situacao = 'paga' and v.dia between v_ini and least(v_fim, v_hoje)
        group by v.dia
    ), previsto as (     -- previsão em R$: quantidade prevista × preço de hoje
        select pd.dia, sum(pd.previsto * p.preco) as total, sum(pd.minimo * p.preco) as minimo, sum(pd.maximo * p.preco) as maximo
        from public.previsao_dias pd join public.produtos p on p.id = pd.produto_id
        where pd.dia between greatest(v_ini, v_hoje) and v_fim
        group by pd.dia
    ), mesmo_dia_ano_passado as (     -- mesmo dia da semana, 52 semanas antes
        select v.dia + 364 as dia, sum(v.total) as total
        from public.vendas v where v.situacao = 'paga' and v.dia between v_ini - 364 and v_fim - 364
        group by v.dia
    ), eventos as (
        select e.dia, string_agg(e.nome, ', ' order by e.nome) as nomes, max(e.fator) as fator, bool_or(e.fator = 0) as fechado
        from public.eventos_calendario e where e.dia between v_ini and v_fim and e.tipo <> 'anomalia'
        group by e.dia
    )
    select jsonb_build_object(
        'hoje', v_hoje, 'inicio', v_ini, 'fim', v_fim,
        'previsao_ate', (select max(dia) from public.previsao_dias),
        'dias', (select jsonb_agg(jsonb_build_object(
                     'dia', d.dia,
                     'vendido', case when d.dia <= v_hoje then round(coalesce(vd.total, 0), 2) end,
                     'vendas', case when d.dia <= v_hoje then coalesce(vd.vendas, 0) end,
                     'fechado', exists (select 1 from public.fechamentos_dia f where f.dia = d.dia and not f.automatico),
                     'previsto', round(pv.total, 2), 'previsto_min', round(pv.minimo, 2), 'previsto_max', round(pv.maximo, 2),
                     'ano_passado', round(ap.total, 2),
                     'evento', ev.nomes, 'fechado_evento', coalesce(ev.fechado, false)) order by d.dia)
                 from dias d
                 left join vendido vd on vd.dia = d.dia
                 left join previsto pv on pv.dia = d.dia
                 left join mesmo_dia_ano_passado ap on ap.dia = d.dia
                 left join eventos ev on ev.dia = d.dia),
        -- Dias de pedido de cada fornecedor ativo no mês (pedir nesse dia pega uma entrega dele; sem dias certos: segunda)
        'fornecedores', coalesce((select jsonb_agg(jsonb_build_object(
                     'fornecedor_id', f.id, 'fornecedor', f.nome,
                     'prazo', coalesce(f.prazo_entrega_dias, 3),
                     'dias', (select jsonb_agg(d.dia order by d.dia) from dias d
                              where (nullif(trim(coalesce(f.dias_entrega, '')), '') is null and extract(dow from d.dia) = 1)
                                 or extract(dow from d.dia + coalesce(f.prazo_entrega_dias, 3))::text
                                    = any (string_to_array(replace(coalesce(f.dias_entrega, ''), ' ', ''), ',')))) order by f.nome)
                 from public.fornecedores f where f.ativo), '[]'::jsonb),
        -- Datas festivas do mês e do começo do próximo (até 21 dias depois do fim): último dia para pedir e o que vende a mais
        'datas', coalesce((select jsonb_agg(x order by (x ->> 'dia')) from (
                     select jsonb_build_object('dia', ev.dia, 'nome', ev.nome, 'fator', ev.fator,
                            'pedir_ate', ev.dia - coalesce(max(coalesce(f.prazo_entrega_dias, p.prazo_entrega_dias, 3)), 3) - 1,
                            'produtos', string_agg(p.nome, ', ' order by pe.extra_hoje desc)) as x
                     from public.eventos_calendario ev
                     left join lateral (select * from public.previsao_eventos e2 where e2.evento = ev.nome and e2.extra_hoje >= 1
                                        order by e2.extra_hoje desc limit 5) pe on true
                     left join public.produtos p on p.id = pe.produto_id
                     left join public.fornecedores f on f.id = p.fornecedor_id
                     where ev.tipo <> 'anomalia' and ev.fator > 1 and ev.dia between v_ini and v_fim + 21
                     group by ev.dia, ev.nome, ev.fator) y), '[]'::jsonb)
    ) into v_res;
    return v_res;
end;
$$;

revoke execute on function public.calendario_mes(date) from public, anon;
grant execute on function public.calendario_mes(date) to authenticated;

notify pgrst, 'reload schema';
