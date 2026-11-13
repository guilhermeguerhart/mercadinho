-- =====================================================================================================================
-- 015 - Análises rápidas: resumo de cada dia guardado pronto (rodar depois do 014; pode rodar de novo)
--
-- Com 5 anos de vendas, a análise de 12 meses (e às vezes a de 30 dias) passava do tempo limite do Supabase (8 s) e a
-- tela mostrava "Erro no servidor (500)" (achado pelo programa de testes). Agora cada dia que já passou tem um resumo
-- pronto em analise_cache_dia (total, vendas, descontos, promoções, custo, formas de pagamento, categorias e produtos).
-- A análise só soma até 800 linhas, em vez de ler dezenas de milhares de vendas e itens. O dia de hoje continua sendo
-- calculado na hora.
--
-- - O resumo de um dia é feito na primeira análise que precisa dele (normalmente só o dia de ontem) e apagado sozinho
--   quando uma venda daquele dia muda (gatilho em vendas), para ser refeito na próxima análise.
-- - O fim deste script já prepara todos os dias que têm venda (pode levar alguns minutos com anos de vendas).
-- - Depois de gerar dados de exemplo (011/013), rode de novo: select public.preencher_analise_cache();
-- - O resultado da análise é o mesmo de antes, campo por campo (conferido num Postgres local). As categorias são as
--   que os produtos tinham quando o resumo do dia foi feito.
-- =====================================================================================================================

create table if not exists public.analise_cache_dia (
    dia               date primary key,
    total             numeric not null default 0,
    vendas            integer not null default 0,
    descontos         numeric not null default 0,
    promocoes         numeric not null default 0,
    custo             numeric not null default 0,
    vendido_com_custo numeric not null default 0,
    formas            jsonb not null default '{}'::jsonb,   -- {"dinheiro": 350.00, ...}
    categorias        jsonb not null default '{}'::jsonb,   -- {"Bebidas": 120.50, ...}
    produtos          jsonb not null default '[]'::jsonb,   -- [{"nome", "unidade", "quantidade", "valor"}]
    calculado_em      timestamptz not null default now()
);

-- Só as funções abaixo (security definer) leem e gravam; ninguém lê a tabela direto.
alter table public.analise_cache_dia enable row level security;
revoke all on public.analise_cache_dia from anon, authenticated;

-- ---------------------------------------------------------------- Resumo de um dia (sem gravar)
create or replace function public.analise_dia_calcular(p_dia date)
returns public.analise_cache_dia
language sql
stable
security definer
set search_path = public
as $$
    with v as materialized (
        select id, total, desconto from public.vendas where situacao = 'paga' and dia = p_dia
    ), i as materialized (
        select it.id, it.produto_id, it.nome_produto, it.unidade_medida, it.quantidade, it.subtotal, it.desconto_promocao
        from public.venda_itens it join v on v.id = it.venda_id
    ), c as materialized (
        select l.item_id, sum(l.quantidade * lo.preco_custo) as custo
        from public.venda_item_lotes l join public.lotes lo on lo.id = l.lote_id
        where lo.preco_custo is not null and l.item_id in (select id from i)
        group by l.item_id
    )
    select p_dia,
        coalesce((select sum(total) from v), 0),
        (select count(*)::int from v),
        coalesce((select sum(desconto) from v), 0),
        coalesce((select sum(desconto_promocao) from i), 0),
        coalesce((select sum(custo) from c), 0),
        coalesce((select sum(i.subtotal) from i where exists (select 1 from c where c.item_id = i.id)), 0),
        coalesce((select jsonb_object_agg(forma, valor) from (
                     select pg.forma, sum(pg.valor) as valor from public.venda_pagamentos pg join v on v.id = pg.venda_id group by pg.forma) f), '{}'::jsonb),
        coalesce((select jsonb_object_agg(categoria, valor) from (
                     select coalesce(nullif(p.categoria, ''), 'Sem categoria') as categoria, sum(i.subtotal) as valor
                     from i left join public.produtos p on p.id = i.produto_id group by 1) k), '{}'::jsonb),
        coalesce((select jsonb_agg(jsonb_build_object('nome', nome, 'unidade', unidade, 'quantidade', quantidade, 'valor', valor)) from (
                     select i.nome_produto as nome, i.unidade_medida as unidade, sum(i.quantidade) as quantidade, sum(i.subtotal) as valor
                     from i group by 1, 2) m), '[]'::jsonb),
        now();
$$;

-- ---------------------------------------------------------------- Prepara os dias que faltam (até ontem)
create or replace function public.preencher_analise_cache(p_inicio date default null, p_fim date default null)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    v_inicio date;
    v_fim date := least(coalesce(p_fim, public.hoje_local() - 1), public.hoje_local() - 1);
    v_feitos integer := 0;
    v_dia date;
begin
    -- Gerente, agendamento do servidor ou o SQL Editor (sem usuário logado)
    if auth.uid() is not null and not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente vê as análises de vendas.' using errcode = '42501';
    end if;
    v_inicio := coalesce(p_inicio, (select min(dia) from public.vendas where situacao = 'paga'));
    if v_inicio is null or v_inicio > v_fim then
        return 0;
    end if;
    for v_dia in
        select d::date from generate_series(v_inicio, v_fim, interval '1 day') d
        where not exists (select 1 from public.analise_cache_dia a where a.dia = d::date)
    loop
        insert into public.analise_cache_dia select * from public.analise_dia_calcular(v_dia)
        on conflict (dia) do nothing;
        v_feitos := v_feitos + 1;
    end loop;
    return v_feitos;
end;
$$;

-- ---------------------------------------------------------------- Venda de um dia passado mudou: refaz o resumo dele
create or replace function public.analise_cache_invalidar()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
    if tg_op <> 'INSERT' then
        delete from public.analise_cache_dia where dia = old.dia;
    end if;
    if tg_op <> 'DELETE' and (tg_op = 'INSERT' or new.dia is distinct from old.dia) then
        delete from public.analise_cache_dia where dia = new.dia;
    end if;
    return null;
end;
$$;

drop trigger if exists vendas_analise_cache on public.vendas;
create trigger vendas_analise_cache after insert or update or delete on public.vendas
    for each row execute function public.analise_cache_invalidar();

-- ---------------------------------------------------------------- Análises a partir dos resumos de cada dia
-- Não é mais "stable": pode guardar o resumo dos dias que faltam. As telas chamam por POST (rpc), como antes.
drop function if exists public.analise_vendas(date, date, text);
create function public.analise_vendas(p_inicio date, p_fim date, p_agrupar text default 'dia')
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_dias integer := p_fim - p_inicio + 1;
    v_unidade text;
    v_hoje date := public.hoje_local();
    v_resultado jsonb;
begin
    if not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente vê as análises de vendas.' using errcode = '42501';
    end if;
    if p_inicio is null or p_fim is null or p_fim < p_inicio or v_dias > 800 then
        raise exception 'Período inválido.' using errcode = '22023';
    end if;
    if p_agrupar not in ('dia', 'semana', 'mes') then
        raise exception 'Agrupamento inválido.' using errcode = '22023';
    end if;
    v_unidade := case p_agrupar when 'dia' then 'day' when 'semana' then 'week' else 'month' end;
    perform public.preencher_analise_cache(p_inicio, p_fim);

    with d as materialized (
        -- Dias que já passaram (prontos) + hoje (calculado agora), só os que têm venda
        select * from public.analise_cache_dia where dia between p_inicio and least(p_fim, v_hoje - 1) and vendas > 0
        union all
        select * from public.analise_dia_calcular(v_hoje) where v_hoje between p_inicio and p_fim
    ), d1 as materialized (select * from d where vendas > 0)
    select jsonb_build_object(
        'inicio', p_inicio, 'fim', p_fim, 'agrupar', p_agrupar,
        'serie', coalesce((select jsonb_agg(jsonb_build_object('periodo', s.periodo, 'total', coalesce(t.total, 0),
                                                              'vendas', coalesce(t.vendas, 0), 'descontos', coalesce(t.descontos, 0)) order by s.periodo)
                 from (select distinct date_trunc(v_unidade, g)::date as periodo
                       from generate_series(p_inicio, p_fim, interval '1 day') g) s
                 left join (select date_trunc(v_unidade, dia)::date as periodo, sum(total) as total, sum(vendas) as vendas, sum(descontos) as descontos
                            from d1 group by 1) t
                   on t.periodo = s.periodo), '[]'::jsonb),
        'total', coalesce((select sum(total) from d1), 0),
        'vendas', coalesce((select sum(vendas) from d1), 0),
        'anterior', jsonb_build_object(
            'inicio', p_inicio - v_dias, 'fim', p_inicio - 1,
            'total', coalesce((select sum(total) from public.vendas where situacao = 'paga' and dia between p_inicio - v_dias and p_inicio - 1), 0),
            'vendas', (select count(*) from public.vendas where situacao = 'paga' and dia between p_inicio - v_dias and p_inicio - 1)),
        'categorias', coalesce((select jsonb_agg(c order by c.valor desc) from (
                       select k.key as categoria, sum(k.value::numeric) as valor
                       from d1, jsonb_each_text(d1.categorias) k group by 1) c), '[]'::jsonb),
        'formas', coalesce((select jsonb_object_agg(forma, valor) from (
                       select f.key as forma, sum(f.value::numeric) as valor
                       from d1, jsonb_each_text(d1.formas) f group by 1) x), '{}'::jsonb),
        -- 0 = domingo ... 6 = sábado
        'dias_semana', coalesce((select jsonb_agg(w order by w.dia_semana) from (
                       select extract(dow from dia)::int as dia_semana, sum(total) as total, sum(vendas) as vendas, count(*) as dias
                       from d1 group by 1) w), '[]'::jsonb),
        'mais_vendidos', coalesce((select jsonb_agg(m) from (
                       select p ->> 'nome' as nome, p ->> 'unidade' as unidade, sum((p ->> 'quantidade')::numeric) as quantidade,
                              sum((p ->> 'valor')::numeric) as valor
                       from d1, jsonb_array_elements(d1.produtos) p
                       group by 1, 2 order by sum((p ->> 'valor')::numeric) desc limit 10) m), '[]'::jsonb),
        'custo', coalesce((select sum(custo) from d1), 0),
        'vendido_com_custo', coalesce((select sum(vendido_com_custo) from d1), 0),
        'descontos', coalesce((select sum(descontos) from d1), 0),
        'promocoes', coalesce((select sum(promocoes) from d1), 0),
        -- Perdas: lotes que venceram no período ainda com unidades (valor pelo custo do lote ou do produto)
        'perdas', jsonb_build_object(
            'lotes', (select count(*) from public.lotes where quantidade_restante > 0 and validade between p_inicio and least(p_fim, v_hoje - 1)),
            'valor', coalesce((select sum(lo.quantidade_restante * coalesce(lo.preco_custo, p.preco_custo, 0))
                               from public.lotes lo join public.produtos p on p.id = lo.produto_id
                               where lo.quantidade_restante > 0 and lo.validade between p_inicio and least(p_fim, v_hoje - 1)), 0)),
        'cancelamentos', jsonb_build_object(
            'vendas', (select count(*) from public.vendas where situacao = 'cancelada' and paga_em is not null and dia between p_inicio and p_fim),
            'itens', (select count(*) from public.venda_itens_cancelados ic join public.vendas vv on vv.id = ic.venda_id where vv.dia between p_inicio and p_fim),
            'valor_itens', coalesce((select sum(ic.valor) from public.venda_itens_cancelados ic join public.vendas vv on vv.id = ic.venda_id
                                     where vv.dia between p_inicio and p_fim), 0)))
    into v_resultado;
    return v_resultado;
end;
$$;

revoke execute on function public.analise_dia_calcular(date) from public, anon, authenticated;
revoke execute on function public.preencher_analise_cache(date, date) from public, anon;
revoke execute on function public.analise_cache_invalidar() from public, anon, authenticated;
revoke execute on function public.analise_vendas(date, date, text) from public, anon;
grant execute on function public.preencher_analise_cache(date, date) to authenticated, service_role;
grant execute on function public.analise_vendas(date, date, text) to authenticated, service_role;

-- Prepara agora todos os dias que já têm venda (de uma vez, aqui no SQL Editor, sem o limite de 8 s das telas)
select public.preencher_analise_cache() as dias_preparados;
