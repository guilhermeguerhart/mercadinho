-- =====================================================================================================================
-- 014 - Desempenho (achados do programa de testes em 06/11/2026)
-- Rodar no SQL Editor do Supabase depois do 013. Pode rodar de novo. Não muda nenhum dado nem quem pode ver o quê:
-- só deixa as consultas mais rápidas.
--
-- 1. Índice que faltava em venda_itens(venda_id): toda leitura "venda → itens" (análises, resumo do dia, caixa, regras
--    de acesso) percorria a tabela inteira de itens.
-- 2. Regras de acesso (RLS) com (select public.eh_gerente()): o banco confere "é gerente?" uma vez por consulta, e não
--    uma vez por linha. Antes, o operador lendo venda_item_lotes passava do tempo limite de 8 s (erro 57014).
-- 3. analise_vendas lê as vendas e os itens do período uma vez só (antes eram mais de cinco leituras da tabela de
--    itens). A análise de 12 meses com 5 anos de vendas passava de 8 s e a tela mostrava "Erro no servidor (500)".
--    O resultado é o mesmo, campo por campo.
-- =====================================================================================================================

-- ---------------------------------------------------------------- 1. Índices
create index if not exists venda_itens_venda on public.venda_itens (venda_id);
create index if not exists vendas_pagas_dia on public.vendas (dia) where situacao = 'paga';
create index if not exists caixas_dia on public.caixas (dia);

-- ---------------------------------------------------------------- 2. Regras de acesso (mesmo efeito, conferidas uma vez)
drop policy if exists "gerente le baixas por lote" on public.venda_item_lotes;
create policy "gerente le baixas por lote"
    on public.venda_item_lotes for select
    to authenticated
    using ((select public.eh_gerente()));

drop policy if exists "gerente le autorizacoes" on public.autorizacoes;
create policy "gerente le autorizacoes"
    on public.autorizacoes for select
    to authenticated
    using ((select public.eh_gerente()));

drop policy if exists "gerente le itens cancelados" on public.venda_itens_cancelados;
create policy "gerente le itens cancelados"
    on public.venda_itens_cancelados for select
    to authenticated
    using ((select public.eh_gerente()));

drop policy if exists "le caixas de hoje" on public.caixas;
create policy "le caixas de hoje"
    on public.caixas for select
    to authenticated
    using ((select public.eh_gerente())
           or ((select public.usuario_ativo()) and dia = (select public.hoje_local()) and operador_id = (select auth.uid())));

drop policy if exists "le vendas de hoje" on public.vendas;
create policy "le vendas de hoje"
    on public.vendas for select
    to authenticated
    using ((select public.eh_gerente())
           or ((select public.usuario_ativo()) and dia = (select public.hoje_local()) and operador_id = (select auth.uid())));

drop policy if exists "le itens das vendas de hoje" on public.venda_itens;
create policy "le itens das vendas de hoje"
    on public.venda_itens for select
    to authenticated
    using ((select public.eh_gerente())
           or exists (select 1 from public.vendas v where v.id = venda_id and (select public.usuario_ativo())
                      and v.dia = (select public.hoje_local()) and v.operador_id = (select auth.uid())));

drop policy if exists "le pagamentos das vendas de hoje" on public.venda_pagamentos;
create policy "le pagamentos das vendas de hoje"
    on public.venda_pagamentos for select
    to authenticated
    using ((select public.eh_gerente())
           or exists (select 1 from public.vendas v where v.id = venda_id and (select public.usuario_ativo())
                      and v.dia = (select public.hoje_local()) and v.operador_id = (select auth.uid())));

drop policy if exists "le movimentos dos caixas de hoje" on public.caixa_movimentos;
create policy "le movimentos dos caixas de hoje"
    on public.caixa_movimentos for select
    to authenticated
    using ((select public.eh_gerente())
           or ((select public.usuario_ativo())
               and exists (select 1 from public.caixas c where c.id = caixa_id and c.dia = (select public.hoje_local()))));

-- ---------------------------------------------------------------- 3. Análises: vendas e itens do período lidos uma vez
create or replace function public.analise_vendas(p_inicio date, p_fim date, p_agrupar text default 'dia')
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
    v_dias integer := p_fim - p_inicio + 1;
    v_unidade text;
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

    with v as materialized (
        select id, dia, total, desconto from public.vendas
        where situacao = 'paga' and dia between p_inicio and p_fim
    ), i as materialized (
        select it.id, it.produto_id, it.nome_produto, it.unidade_medida, it.quantidade, it.subtotal, it.desconto_promocao
        from public.venda_itens it join v on v.id = it.venda_id
    ), custo as materialized (
        -- Custo de cada item que saiu de lote com preço de custo
        select l.item_id, sum(l.quantidade * lo.preco_custo) as custo
        from public.venda_item_lotes l join public.lotes lo on lo.id = l.lote_id
        where lo.preco_custo is not null and l.item_id in (select id from i)
        group by l.item_id
    )
    select jsonb_build_object(
        'inicio', p_inicio, 'fim', p_fim, 'agrupar', p_agrupar,
        -- Uma linha por período, inclusive os sem venda (o gráfico não pula dias)
        'serie', coalesce((select jsonb_agg(jsonb_build_object('periodo', s.periodo, 'total', coalesce(t.total, 0),
                                                              'vendas', coalesce(t.vendas, 0), 'descontos', coalesce(t.descontos, 0)) order by s.periodo)
                 from (select distinct date_trunc(v_unidade, d)::date as periodo
                       from generate_series(p_inicio, p_fim, interval '1 day') d) s
                 left join (select date_trunc(v_unidade, dia)::date as periodo, sum(total) as total, count(*) as vendas, sum(desconto) as descontos
                            from v group by 1) t
                   on t.periodo = s.periodo), '[]'::jsonb),
        'total', coalesce((select sum(total) from v), 0),
        'vendas', (select count(*) from v),
        'anterior', jsonb_build_object(
            'inicio', p_inicio - v_dias, 'fim', p_inicio - 1,
            'total', coalesce((select sum(total) from public.vendas where situacao = 'paga' and dia between p_inicio - v_dias and p_inicio - 1), 0),
            'vendas', (select count(*) from public.vendas where situacao = 'paga' and dia between p_inicio - v_dias and p_inicio - 1)),
        'categorias', coalesce((select jsonb_agg(c order by c.valor desc) from (
                       select coalesce(nullif(p.categoria, ''), 'Sem categoria') as categoria, sum(i.subtotal) as valor
                       from i left join public.produtos p on p.id = i.produto_id
                       group by 1) c), '[]'::jsonb),
        'formas', coalesce((select jsonb_object_agg(forma, valor) from (
                       select pg.forma, sum(pg.valor) as valor from public.venda_pagamentos pg join v on v.id = pg.venda_id
                       group by pg.forma) f), '{}'::jsonb),
        -- 0 = domingo ... 6 = sábado
        'dias_semana', coalesce((select jsonb_agg(w order by w.dia_semana) from (
                       select extract(dow from dia)::int as dia_semana, sum(total) as total, count(*) as vendas, count(distinct dia) as dias
                       from v group by 1) w), '[]'::jsonb),
        'mais_vendidos', coalesce((select jsonb_agg(m) from (
                       select i.nome_produto as nome, i.unidade_medida as unidade, sum(i.quantidade) as quantidade, sum(i.subtotal) as valor
                       from i group by i.nome_produto, i.unidade_medida order by sum(i.subtotal) desc limit 10) m), '[]'::jsonb),
        'custo', coalesce((select sum(custo) from custo), 0),
        'vendido_com_custo', coalesce((select sum(i.subtotal) from i where exists (select 1 from custo c where c.item_id = i.id)), 0),
        'descontos', coalesce((select sum(desconto) from v), 0),
        'promocoes', coalesce((select sum(i.desconto_promocao) from i), 0),
        -- Perdas: lotes que venceram no período ainda com unidades (valor pelo custo do lote ou do produto)
        'perdas', jsonb_build_object(
            'lotes', (select count(*) from public.lotes where quantidade_restante > 0 and validade between p_inicio and least(p_fim, public.hoje_local() - 1)),
            'valor', coalesce((select sum(lo.quantidade_restante * coalesce(lo.preco_custo, p.preco_custo, 0))
                               from public.lotes lo join public.produtos p on p.id = lo.produto_id
                               where lo.quantidade_restante > 0 and lo.validade between p_inicio and least(p_fim, public.hoje_local() - 1)), 0)),
        'cancelamentos', jsonb_build_object(
            'vendas', (select count(*) from public.vendas where situacao = 'cancelada' and paga_em is not null and dia between p_inicio and p_fim),
            'itens', (select count(*) from public.venda_itens_cancelados ic join public.vendas vv on vv.id = ic.venda_id where vv.dia between p_inicio and p_fim),
            'valor_itens', coalesce((select sum(ic.valor) from public.venda_itens_cancelados ic join public.vendas vv on vv.id = ic.venda_id
                                     where vv.dia between p_inicio and p_fim), 0)))
    into v_resultado;
    return v_resultado;
end;
$$;

-- As estatísticas novas ajudam o banco a escolher o caminho rápido
analyze public.vendas;
analyze public.venda_itens;
analyze public.venda_item_lotes;
