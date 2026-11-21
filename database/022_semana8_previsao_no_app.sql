-- =====================================================================
-- 022 - Semana 8, parte 1: a previsão de compras no dia a dia do app
-- =====================================================================
-- Depende de 016 e 019 (previsão). Pode ser rodado de novo sem perder dados.
-- O que muda:
--   * pedido_sugerido(): agora também sugere
--       - produto SEM histórico de vendas (loja nova, produto novo): pela regra do estoque mínimo, a mesma do
--         "Pedir o que falta" do app (sem estoque, abaixo do mínimo ou no aviso; pedir o dobro do mínimo menos o que
--         tem, pelo menos o mínimo, em caixas fechadas). O item vem com regra = 'minimo'; os outros, 'previsao';
--       - produto SEM fornecedor principal: entra no primeiro fornecedor ativo (A-Z) que entrega a categoria dele
--         (mesma regra das telas de Fornecedores e Estoque);
--     cada item traz também o estoque mínimo ('minimo').
--   * fornecedor_proximo_pedido(): próximo dia de pedido, chegada e entrega seguinte de um fornecedor (a conta que
--     estava dentro do pedido_sugerido, agora separada para a coluna "Dura até" do Estoque usar a mesma).
--   * previsao_duracao(): para cada produto, até quando o estoque dura pela previsão e se acaba antes da próxima
--     entrega possível (coluna "Dura até" e indicador "Acabam antes da entrega" do Estoque).
--   * vendas_perdidas + registrar_venda_perdida(): botão "Faltou produto" (F10) do caixa. Cada registro também soma
--     em simulacao_faltas (faltas por dia), que a previsão já usa para não aprender com dias em falta e para os
--     alertas e o indicador "Vendas perdidas". Antes só o simulador escrevia lá; agora o caixa de verdade também.
-- Rodar inteiro no SQL Editor. Depois, no app, sair e entrar de novo.
-- =====================================================================

-- ---------------------------------------------------------------- Faltas por dia (já existe se o 018 rodou)
create table if not exists public.simulacao_faltas (         -- venda perdida porque não tinha o produto (caixa e simulador)
    dia        date not null,
    produto_id bigint not null references public.produtos (id) on delete cascade,
    quantidade numeric not null default 0,
    clientes   integer not null default 0,
    primary key (dia, produto_id)
);
alter table public.simulacao_faltas enable row level security;
drop policy if exists simulacao_faltas_ver on public.simulacao_faltas;
create policy simulacao_faltas_ver on public.simulacao_faltas for select to authenticated using ((select public.eh_gerente()));
grant select on public.simulacao_faltas to authenticated;

-- ---------------------------------------------------------------- Vendas perdidas registradas no caixa
-- motivo: 'nao_tinha' (acabou), 'levou_outro' (levou um parecido no lugar), 'prateleira_vazia' (o sistema diz que tem,
-- mas não está na gôndola: o gerente recebe aviso para repor).
create table if not exists public.vendas_perdidas (
    id             bigint generated always as identity primary key,
    dia            date not null default public.hoje_local(),
    momento        timestamptz not null default now(),
    produto_id     bigint not null references public.produtos (id) on delete cascade,
    quantidade     numeric(12, 3) not null check (quantidade > 0 and quantidade <= 9999),
    motivo         text not null default 'nao_tinha' check (motivo in ('nao_tinha', 'levou_outro', 'prateleira_vazia')),
    operador_id    uuid references public.perfis (id) on delete set null,
    operador_nome  text not null default '',
    estoque_no_sistema numeric(12, 3)                    -- quanto o sistema dizia ter na hora (para conferir a prateleira)
);
create index if not exists vendas_perdidas_dia on public.vendas_perdidas (dia);
create index if not exists vendas_perdidas_produto on public.vendas_perdidas (produto_id);

alter table public.vendas_perdidas enable row level security;
-- Gerente lê tudo; o operador lê só o que ele mesmo registrou. Gravar só pela função registrar_venda_perdida.
drop policy if exists vendas_perdidas_ver on public.vendas_perdidas;
create policy vendas_perdidas_ver on public.vendas_perdidas for select to authenticated
    using ((select public.eh_gerente()) or operador_id = (select auth.uid()));
grant select on public.vendas_perdidas to authenticated;

create or replace function public.registrar_venda_perdida(p_produto bigint, p_quantidade numeric, p_motivo text default 'nao_tinha')
returns text
language plpgsql
volatile
security definer
set search_path = public
as $$
declare
    v_perfil public.perfis%rowtype;
    v_prod record;
    v_estoque numeric;
    v_dia date := public.hoje_local();
begin
    select * into v_perfil from public.perfis where id = auth.uid() and ativo;
    if not found then
        raise exception 'Faça login de novo para registrar.' using errcode = '42501';
    end if;
    if p_quantidade is null or p_quantidade <= 0 or p_quantidade > 9999 then
        raise exception 'Informe quanto o cliente queria (mais que zero).' using errcode = '22023';
    end if;
    if p_motivo is null or p_motivo not in ('nao_tinha', 'levou_outro', 'prateleira_vazia') then
        raise exception 'Motivo inválido.' using errcode = '22023';
    end if;
    select id, nome, unidade_medida into v_prod from public.produtos where id = p_produto;
    if not found then
        raise exception 'Produto não encontrado.' using errcode = 'P0002';
    end if;
    select coalesce(sum(quantidade_restante), 0) into v_estoque from public.lotes
    where produto_id = p_produto and quantidade_restante > 0 and (validade is null or validade >= v_dia);

    insert into public.vendas_perdidas (dia, produto_id, quantidade, motivo, operador_id, operador_nome, estoque_no_sistema)
    values (v_dia, p_produto, case when v_prod.unidade_medida = 'kg' then round(p_quantidade, 3) else ceil(p_quantidade) end,
            p_motivo, v_perfil.id, v_perfil.nome, v_estoque);

    -- Soma nas faltas do dia (a previsão não aprende com o dia em falta; alertas e "Vendas perdidas" contam)
    insert into public.simulacao_faltas (dia, produto_id, quantidade, clientes) values (v_dia, p_produto, p_quantidade, 1)
    on conflict (dia, produto_id) do update
    set quantidade = simulacao_faltas.quantidade + excluded.quantidade, clientes = simulacao_faltas.clientes + 1;

    return case p_motivo
        when 'prateleira_vazia' then format('Anotado: %s não está na prateleira. O gerente foi avisado para repor.', v_prod.nome)
        else format('Anotado: faltou %s. Isso ajuda a pedir mais da próxima vez.', v_prod.nome) end;
end;
$$;

-- ---------------------------------------------------------------- Próximo pedido de um fornecedor
-- Próximo dia de pedido (hoje + prazo cai num dia de entrega; sem dias de entrega = segunda), quando chega e a entrega
-- seguinte (até quando o pedido precisa durar). Mesma conta que o 019 fazia dentro do pedido_sugerido.
create or replace function public.fornecedor_proximo_pedido(p_fornecedor bigint, p_hoje date default null)
returns table (data_pedido date, chegada date, proxima_entrega date)
language plpgsql
stable
security definer
set search_path = public
as $$
declare
    v_hoje date := coalesce(p_hoje, public.hoje_local());
    v_f record;
    v_dias int[];
    v_k integer;
begin
    select * into v_f from public.fornecedores where id = p_fornecedor;
    if not found then return; end if;
    v_dias := (select array_agg(trim(x)::int) from unnest(string_to_array(nullif(trim(coalesce(v_f.dias_entrega, '')), ''), ',')) x
               where trim(x) ~ '^[0-6]$');
    select d::date into data_pedido from generate_series(v_hoje, v_hoje + 14, interval '1 day') d
    where (v_dias is null and extract(dow from d) = 1)
       or (v_dias is not null and extract(dow from d::date + coalesce(v_f.prazo_entrega_dias, 3))::int = any (v_dias))
    order by d limit 1;
    if data_pedido is null then return; end if;
    chegada := data_pedido + coalesce(v_f.prazo_entrega_dias, 3);
    if v_dias is null then
        v_k := 7;
    else
        select min(k) into v_k from generate_series(1, 7) k where extract(dow from chegada + k)::int = any (v_dias);
    end if;
    proxima_entrega := chegada + v_k;
    return next;
end;
$$;

-- ---------------------------------------------------------------- Fornecedor de cada produto
-- O principal (se estiver ativo) ou, sem principal, o primeiro fornecedor ativo (A-Z) que entrega a categoria.
create or replace function public.fornecedor_do_produto(p_produto bigint)
returns bigint
language sql
stable
security definer
set search_path = public
as $$
    select coalesce(
        (select f.id from public.produtos p join public.fornecedores f on f.id = p.fornecedor_id and f.ativo where p.id = p_produto),
        (select f.id from public.produtos p join public.fornecedores f on f.ativo
           and exists (select 1 from unnest(string_to_array(f.categorias, ',')) c where lower(trim(c)) = lower(trim(p.categoria)))
         where p.id = p_produto and p.fornecedor_id is null
         order by lower(f.nome) limit 1));
$$;

-- ---------------------------------------------------------------- Pedido sugerido (troca o do 019)
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
    v_pedido date;
    v_chegada date;
    v_seguinte date;
    v_itens jsonb;
    v_res jsonb := '[]'::jsonb;
begin
    if auth.uid() is not null and not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente vê o pedido sugerido.' using errcode = '42501';
    end if;

    for v_f in select * from public.fornecedores where ativo and (p_fornecedor is null or id = p_fornecedor) order by nome loop
        select x.data_pedido, x.chegada, x.proxima_entrega into v_pedido, v_chegada, v_seguinte
        from public.fornecedor_proximo_pedido(v_f.id, v_hoje) x;
        continue when v_pedido is null;

        with prods as (     -- do fornecedor: o principal é ele, ou não tem principal e ele é o primeiro (A-Z) da categoria
            select p.id, p.nome, p.categoria, p.unidade_medida, greatest(coalesce(p.unidades_por_caixa, 1), 1) as caixa, p.preco_custo,
                   p.estoque_minimo as minimo, p.estoque_aviso,
                   least(v_seguinte, v_chegada + coalesce(p.dias_validade, 3650)) as ate
            from public.produtos p
            where p.fornecedor_id = v_f.id
               or (p.fornecedor_id is null
                   and v_f.id = (select f.id from public.fornecedores f
                                 where f.ativo and exists (select 1 from unnest(string_to_array(f.categorias, ',')) c
                                                           where lower(trim(c)) = lower(trim(p.categoria)))
                                 order by lower(f.nome) limit 1))
        ), media as (
            select i.produto_id, sum(i.quantidade) / 28.0 as m
            from public.venda_itens i join public.vendas v on v.id = i.venda_id
            where v.situacao = 'paga' and v.dia between v_hoje - 28 and v_hoje - 1
              and i.produto_id in (select id from prods)
            group by i.produto_id
        ), historico as (     -- tem como prever: o modelo aprendeu algo ou vendeu nos últimos 28 dias
            select pr.id from prods pr
            where exists (select 1 from public.previsao_modelos m where m.produto_id = pr.id and m.situacao <> 'sem_vendas')
               or exists (select 1 from media md where md.produto_id = pr.id)
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
            select produto_id, sum(quantidade_restante) filter (where validade is null or validade > v_hoje) as q,
                   sum(quantidade_restante) as total
            from public.lotes
            where quantidade_restante > 0 and produto_id in (select id from prods)
            group by produto_id
        ), pendente as (
            select i.produto_id, sum(i.quantidade) as q from public.pedido_itens i join public.pedidos pd on pd.id = i.pedido_id
            where pd.situacao = 'pendente' and i.produto_id in (select id from prods) group by i.produto_id
        ), conta as (
            select pr.*, d.qtd, d.desvio, d.eventos, coalesce(e.q, 0) as estoque, coalesce(e.total, 0) as total,
                   coalesce(pe.q, 0) as pendente, h.id is not null as tem_historico,
                   case when pr.unidade_medida = 'kg' then ceil(pr.minimo * 15) / 10 else ceil(pr.minimo * 1.5) end as aviso_padrao
            from prods pr join demanda d on d.id = pr.id
            left join estoque e on e.produto_id = pr.id
            left join pendente pe on pe.produto_id = pr.id
            left join historico h on h.id = pr.id
        ), regra as (
            select c.*,
                   case when c.tem_historico then c.qtd + p_margem * c.desvio - c.estoque - c.pendente
                        -- Sem histórico: regra do estoque mínimo (igual a Produto.PrecisaRepor/QuantidadeSugerida do app)
                        when c.pendente = 0 and (c.total = 0 or c.total < c.minimo
                                                 or (c.total <= coalesce(c.estoque_aviso, c.aviso_padrao)
                                                     and coalesce(c.estoque_aviso, c.aviso_padrao) > c.minimo))
                             then greatest(c.minimo * 2 - c.total, c.minimo, case when c.unidade_medida = 'kg' then 1 else c.caixa end)
                        else 0 end as falta
            from conta c
        )
        select jsonb_agg(jsonb_build_object(
                   'produto_id', id, 'nome', nome, 'categoria', categoria, 'unidade', unidade_medida, 'caixa', caixa,
                   'estoque', round(estoque, 3), 'pendente', round(pendente, 3), 'minimo', minimo,
                   'previsto', round(qtd, 1), 'margem', round(p_margem * desvio, 1), 'ate', ate, 'eventos', eventos,
                   'regra', case when tem_historico then 'previsao' else 'minimo' end,
                   'sugerido', case when unidade_medida = 'kg' then
                                        case when tem_historico then ceil(falta) else ceil(falta * 10) / 10 end
                                    else ceil(falta / caixa) * caixa end,
                   'custo', preco_custo) order by nome)
        into v_itens
        from regra where falta > 0.05;

        v_res := v_res || jsonb_build_array(jsonb_build_object(
            'fornecedor_id', v_f.id, 'fornecedor', v_f.nome, 'envio_preferido', v_f.envio_preferido, 'pedido_minimo', v_f.pedido_minimo,
            'data_pedido', v_pedido, 'pedir_hoje', v_pedido = v_hoje, 'chegada', v_chegada, 'proxima_entrega', v_seguinte,
            'tem_pendente', exists (select 1 from public.pedidos where fornecedor_id = v_f.id and situacao = 'pendente'),
            'total', coalesce((select round(sum((x ->> 'sugerido')::numeric * coalesce((x ->> 'custo')::numeric, 0)), 2)
                               from jsonb_array_elements(v_itens) x), 0),
            'itens', coalesce(v_itens, '[]'::jsonb)));
    end loop;
    return v_res;
end;
$$;

-- ---------------------------------------------------------------- Até quando o estoque dura (coluna do Estoque)
-- Para cada produto: estoque dentro da validade, dia em que a previsão diz que acaba (null = não acaba nos dias
-- previstos), a próxima entrega possível do fornecedor dele e se acaba antes dela, se já tem pedido pendente,
-- se ainda não tem histórico e há quantos dias não vende (últimos 120 dias).
create or replace function public.previsao_duracao()
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
    v_hoje date := public.hoje_local();
    v_res jsonb;
begin
    if auth.uid() is not null and not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente vê a previsão.' using errcode = '42501';
    end if;

    with prods as (
        select p.id, public.fornecedor_do_produto(p.id) as fornecedor_id from public.produtos p
    ), entregas as (
        select f.id as fornecedor_id, x.chegada
        from public.fornecedores f cross join lateral public.fornecedor_proximo_pedido(f.id, v_hoje) x
        where f.ativo
    ), estoque as (
        select produto_id, sum(quantidade_restante) as q from public.lotes
        where quantidade_restante > 0 and (validade is null or validade > v_hoje) group by produto_id
    ), acumulado as (
        select pd.produto_id, pd.dia, sum(pd.previsto) over (partition by pd.produto_id order by pd.dia) as soma
        from public.previsao_dias pd where pd.dia >= v_hoje
    ), acaba as (
        select a.produto_id, min(a.dia) as dia
        from acumulado a left join estoque e on e.produto_id = a.produto_id
        where a.soma >= coalesce(e.q, 0)
        group by a.produto_id
    ), horizonte as (
        select max(dia) as ultimo from public.previsao_dias
    ), ultima_venda as (
        select i.produto_id, max(v.dia) as dia
        from public.venda_itens i join public.vendas v on v.id = i.venda_id
        where v.situacao = 'paga' and v.dia between v_hoje - 120 and v_hoje
        group by i.produto_id
    ), pendente as (
        select distinct i.produto_id from public.pedido_itens i join public.pedidos pd on pd.id = i.pedido_id
        where pd.situacao = 'pendente'
    )
    select coalesce(jsonb_agg(jsonb_build_object(
               'produto_id', pr.id,
               'estoque', round(coalesce(e.q, 0), 3),
               'tem_historico', coalesce(m.situacao, 'sem_vendas') <> 'sem_vendas',
               'acaba_em', case when coalesce(m.situacao, 'sem_vendas') = 'sem_vendas' then null else ac.dia end,
               'previsto_ate', h.ultimo,
               'chegada', en.chegada,
               'antes_da_entrega', coalesce(m.situacao, 'sem_vendas') <> 'sem_vendas' and ac.dia is not null and en.chegada is not null
                                   and ac.dia < en.chegada and pe.produto_id is null,
               'ja_pedido', pe.produto_id is not null,
               'dias_sem_venda', case when uv.dia is null then null else v_hoje - uv.dia end)), '[]'::jsonb)
    into v_res
    from prods pr
    left join public.previsao_modelos m on m.produto_id = pr.id
    left join estoque e on e.produto_id = pr.id
    left join acaba ac on ac.produto_id = pr.id
    left join entregas en on en.fornecedor_id = pr.fornecedor_id
    left join ultima_venda uv on uv.produto_id = pr.id
    left join pendente pe on pe.produto_id = pr.id
    cross join horizonte h;
    return v_res;
end;
$$;

-- ---------------------------------------------------------------- Permissões
revoke execute on function public.registrar_venda_perdida(bigint, numeric, text) from public, anon;
revoke execute on function public.fornecedor_proximo_pedido(bigint, date) from public, anon;
revoke execute on function public.fornecedor_do_produto(bigint) from public, anon;
revoke execute on function public.pedido_sugerido(date, bigint, numeric) from public, anon;
revoke execute on function public.previsao_duracao() from public, anon;
grant execute on function public.registrar_venda_perdida(bigint, numeric, text) to authenticated;
grant execute on function public.fornecedor_proximo_pedido(bigint, date) to authenticated;
grant execute on function public.fornecedor_do_produto(bigint) to authenticated;
grant execute on function public.pedido_sugerido(date, bigint, numeric) to authenticated;
grant execute on function public.previsao_duracao() to authenticated;

notify pgrst, 'reload schema';
