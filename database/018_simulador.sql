-- =====================================================================
-- 018 - Simulador da loja (só para o período de desenvolvimento)
-- =====================================================================
-- Enquanto o sistema está em desenvolvimento, faz a loja "funcionar sozinha", com as mesmas regras do app:
--   * de manhã: recebe os pedidos que chegam (às vezes 1 dia atrasado, às vezes faltando item)
--     e faz os pedidos automáticos de cada fornecedor (previsão até a próxima entrega + margem - estoque - pendente);
--   * durante o dia (a cada 30 minutos): clientes compram pelo caixa de verdade (reservar_item + finalizar_venda),
--     saindo dos lotes que vencem primeiro, com as promoções de validade; produto sem estoque = venda perdida
--     (fica anotada em simulacao_faltas); sangria quando a gaveta enche; de vez em quando uma venda cancelada;
--   * no fim do dia: fecha os caixas (com pequenas diferenças na gaveta) e faz o Fechamento do dia.
-- Lote vencido fica no estoque com a sobra (é a perda, como na vida real até alguém dar baixa).
--
-- Ligar:      select public.simulacao_ligar(true);
-- Desligar:   select public.simulacao_ligar(false);
-- Rodar já:   select public.simular();          (o agendamento roda sozinho a cada 30 minutos)
-- Ver:        select * from public.simulacao_log order by id desc limit 30;
--
-- ANTES DE A LOJA USAR DE VERDADE: desligue a simulação e rode limpar_dados_exemplo, como sempre.
-- Depende de: 001 a 016 (usa a previsão do 016 para os pedidos automáticos; sem ela usa a média dos últimos 28 dias).
-- =====================================================================

-- ---------------------------------------------------------------- "Hoje" pode ser simulado (só dentro da simulação)
-- O simulador recupera dias em que ficou parado; para as regras do app (validade, caixa do dia, fechamento) aquele
-- dia é "hoje". A variável mercado.hoje só existe dentro da transação do simulador; o app nunca consegue mudá-la.
create or replace function public.hoje_local()
returns date
language sql
stable
as $$
    select coalesce(nullif(current_setting('mercado.hoje', true), '')::date,
                    (now() at time zone 'America/Sao_Paulo')::date);
$$;

-- Datas padrão pelo dia de Brasília (CURRENT_DATE é o dia do servidor, em UTC: depois das 21h já era "amanhã")
alter table public.pedidos alter column data_pedido set default public.hoje_local();
alter table public.lotes alter column data_compra set default public.hoje_local();

-- ---------------------------------------------------------------- Tabelas
create table if not exists public.simulacao (
    id               integer primary key default 1 check (id = 1),
    ligada           boolean not null default false,
    abre             time not null default '07:00',
    fecha            time not null default '20:30',
    nivel            numeric,                       -- vendas por dia (sem dia da semana / mês) na data nivel_em
    nivel_em         date,
    crescimento_ano  numeric not null default 0.07,
    margem           numeric not null default 1.28, -- margem de segurança dos pedidos, em desvios (1,28 = ~90% sem faltar)
    dias_recuperar   integer not null default 14,   -- parado mais que isso: pula para os últimos 14 dias
    ultimo_momento   timestamptz,                   -- até onde já foi simulado
    atualizado_em    timestamptz not null default now()
);
insert into public.simulacao (id) values (1) on conflict do nothing;

create table if not exists public.simulacao_produtos (       -- quanto cada produto pesa nas compras dos clientes, em cada mês
    produto_id bigint not null references public.produtos (id) on delete cascade,
    mes        integer not null check (mes between 1 and 12),
    peso       numeric not null,
    primary key (produto_id, mes)
);

create table if not exists public.simulacao_caixas (         -- caixas abertos pelo simulador
    caixa_id bigint primary key references public.caixas (id) on delete cascade,
    dia      date not null
);

create table if not exists public.simulacao_faltas (         -- venda perdida porque não tinha o produto
    dia        date not null,
    produto_id bigint not null references public.produtos (id) on delete cascade,
    quantidade numeric not null default 0,
    clientes   integer not null default 0,
    primary key (dia, produto_id)
);

create table if not exists public.simulacao_atrasos (        -- pedido que já atrasou uma vez (não atrasa de novo)
    pedido_id bigint primary key references public.pedidos (id) on delete cascade
);

create table if not exists public.simulacao_log (
    id        bigint generated always as identity primary key,
    momento   timestamptz not null default now(),
    dia       date,
    acao      text not null,
    detalhe   text
);

alter table public.simulacao enable row level security;
alter table public.simulacao_produtos enable row level security;
alter table public.simulacao_caixas enable row level security;
alter table public.simulacao_faltas enable row level security;
alter table public.simulacao_atrasos enable row level security;
alter table public.simulacao_log enable row level security;

-- Todo mundo logado vê se a simulação está ligada (faixa "Modo simulação" no app); o resto só o gerente
drop policy if exists simulacao_ver on public.simulacao;
create policy simulacao_ver on public.simulacao for select to authenticated using (true);
drop policy if exists simulacao_faltas_ver on public.simulacao_faltas;
create policy simulacao_faltas_ver on public.simulacao_faltas for select to authenticated using ((select public.eh_gerente()));
drop policy if exists simulacao_log_ver on public.simulacao_log;
create policy simulacao_log_ver on public.simulacao_log for select to authenticated using ((select public.eh_gerente()));
grant select on public.simulacao, public.simulacao_faltas, public.simulacao_log to authenticated;

-- ---------------------------------------------------------------- Ajudantes
-- Age como um usuário do app (para as funções do app aceitarem a chamada)
create or replace function public.simulacao_como(p_usuario uuid)
returns void
language plpgsql
set search_path = public
as $$
begin
    perform set_config('request.jwt.claim.sub', coalesce(p_usuario::text, ''), true);
    perform set_config('request.jwt.claims',
                       case when p_usuario is null then '' else json_build_object('sub', p_usuario, 'role', 'authenticated')::text end, true);
end;
$$;

-- Gerente que a simulação usa (pedidos, entregas, fechamento): de preferência um usuário de teste (*.gerente)
create or replace function public.simulacao_gerente()
returns uuid
language sql
stable
set search_path = public
as $$
    select id from public.perfis where perfil = 'gerente' and ativo order by (usuario like '%.gerente') desc, criado_em limit 1;
$$;

-- Parte das vendas do dia que já aconteceu até a hora h (mesmo desenho dos dados de exemplo:
-- 20% espalhado das 7h30 às 20h30, 35% de manhã das 9h30 às 12h30, 45% no fim da tarde das 16h às 20h)
create or replace function public.simulacao_parte_do_dia(p_hora numeric)
returns numeric
language sql
immutable
as $$
    select 0.20 * least(greatest((p_hora - 7.5) / 13, 0), 1)
         + 0.35 * least(greatest((p_hora - 9.5) / 3, 0), 1)
         + 0.45 * least(greatest((p_hora - 16) / 4, 0), 1);
$$;

-- Vendas esperadas no dia (sem as datas especiais)
create or replace function public.simulacao_vendas_dia(p_dia date)
returns numeric
language sql
stable
set search_path = public
as $$
    select s.nivel * power(1 + s.crescimento_ano, (p_dia - s.nivel_em) / 365.0)
           * (array[0.75, 0.85, 0.9, 0.95, 1.0, 1.25, 1.35])[extract(dow from p_dia)::int + 1]
           * (array[0.9, 0.88, 0.95, 0.97, 1.0, 0.98, 1.02, 1.0, 0.97, 1.0, 1.05, 1.3])[extract(month from p_dia)::int]
           -- sorte do dia: fixa para cada data (as rodadas de 30 minutos do mesmo dia concordam)
           * (0.85 + 0.3 * (('x' || substr(md5('mercado' || p_dia::text), 1, 7))::bit(28)::int / 268435455.0))
    from public.simulacao s;
$$;

-- Pesos dos produtos em cada mês: tirados dos últimos 3 anos de vendas (sem as datas especiais).
-- Produto de época (Ovo de Páscoa, Panetone...) só tem peso nos meses em que vendia; produto que só vendia
-- em data especial fica com peso zero no dia a dia; produto novo, sem venda nenhuma, entra com o peso mediano.
create or replace function public.simulacao_preparar_pesos()
returns void
language plpgsql
set search_path = public
as $$
begin
    if not exists (select 1 from public.simulacao_produtos) then
        insert into public.simulacao_produtos (produto_id, mes, peso)
        select x.produto_id, x.mes, x.n::numeric / sum(x.n) over (partition by x.mes)
        from (select i.produto_id, extract(month from v.dia)::int as mes, count(*) as n
              from public.venda_itens i join public.vendas v on v.id = i.venda_id
              where v.situacao = 'paga' and v.dia >= public.hoje_local() - 3 * 365 and i.produto_id is not null
                and not exists (select 1 from public.eventos_calendario e where e.dia = v.dia)
              group by 1, 2) x;
    end if;
    insert into public.simulacao_produtos (produto_id, mes, peso)
    select p.id, m.mes,
           case when exists (select 1 from public.venda_itens i where i.produto_id = p.id) then 0
                else coalesce((select percentile_cont(0.5) within group (order by peso) from public.simulacao_produtos s
                               where s.mes = m.mes and s.peso > 0), 0.005) end
    from public.produtos p cross join generate_series(1, 12) m(mes)
    where not exists (select 1 from public.simulacao_produtos s where s.produto_id = p.id and s.mes = m.mes);
end;
$$;

create or replace function public.simulacao_anotar(p_dia date, p_acao text, p_detalhe text)
returns void
language sql
set search_path = public
as $$
    insert into public.simulacao_log (dia, acao, detalhe) values (p_dia, p_acao, p_detalhe);
$$;

-- ---------------------------------------------------------------- Manhã: entregas
create or replace function public.simulacao_entregas(p_dia date)
returns integer
language plpgsql
set search_path = public
as $$
declare
    v_ped record;
    v_itens jsonb;
    v_n integer := 0;
    v_gerente uuid;
begin
    v_gerente := public.simulacao_gerente();
    for v_ped in
        select pd.*, f.nome as fornecedor from public.pedidos pd join public.fornecedores f on f.id = pd.fornecedor_id
        where pd.situacao = 'pendente'
          and coalesce(pd.previsao_entrega, pd.data_pedido + coalesce(f.prazo_entrega_dias, 3)) <= p_dia
        order by pd.id
    loop
        -- 8% chegam um dia depois (uma vez só)
        if random() < 0.08 and not exists (select 1 from public.simulacao_atrasos where pedido_id = v_ped.id) then
            insert into public.simulacao_atrasos values (v_ped.id);
            update public.pedidos set previsao_entrega = p_dia + 1 where id = v_ped.id;
            perform public.simulacao_anotar(p_dia, 'entrega atrasada', v_ped.fornecedor || ' (pedido ' || v_ped.id || ') chega amanhã');
            continue;
        end if;
        -- O que chegou: quase tudo; às vezes faltou no fornecedor ou veio avariado
        select jsonb_agg(jsonb_build_object(
                   'item_id', i.id,
                   'recebido', case when x.r < 0.04 then 0
                                    when x.r < 0.06 then case when i.unidade_medida = 'kg' then round(i.quantidade * 0.8, 3) else floor(i.quantidade * 0.8) end
                                    else i.quantidade end,
                   'motivo', case when x.r < 0.04 then 'faltou' when x.r < 0.06 then 'avariado' end,
                   'custo', coalesce(i.preco_custo, p.preco_custo),
                   'validade', case when p.dias_validade is not null and p.dias_validade < 3650 then p_dia + p.dias_validade end))
        into v_itens
        from public.pedido_itens i
        left join public.produtos p on p.id = i.produto_id
        cross join lateral (select random() + i.id * 0 as r) x
        where i.pedido_id = v_ped.id;
        if v_itens is null then continue; end if;

        perform public.simulacao_como(v_gerente);
        perform public.registrar_recebimento(v_ped.id, p_dia, (100000 + floor(random() * 899999))::text, v_itens, false, null);
        v_n := v_n + 1;
        perform public.simulacao_anotar(p_dia, 'entrega', v_ped.fornecedor || ' (pedido ' || v_ped.id || ', '
                                        || jsonb_array_length(v_itens) || ' itens)');
    end loop;
    return v_n;
end;
$$;

-- ---------------------------------------------------------------- Manhã: pedidos automáticos
-- Pede no dia em que (hoje + prazo) cai num dia de entrega do fornecedor. Fornecedor sem dias de entrega: pede às segundas.
-- Quantidade = previsão de hoje até a entrega seguinte (limitada pela validade) + margem × desvio - estoque bom - já pedido.
create or replace function public.simulacao_pedidos(p_dia date)
returns integer
language plpgsql
set search_path = public
as $$
declare
    v_f record;
    v_chegada date;
    v_fim date;
    v_dias int[];
    v_k integer;
    v_itens jsonb;
    v_pedido bigint;
    v_n integer := 0;
    v_margem numeric;
    v_gerente uuid;
begin
    v_gerente := public.simulacao_gerente();
    select margem into v_margem from public.simulacao;
    for v_f in select * from public.fornecedores where ativo order by id loop
        v_chegada := p_dia + coalesce(v_f.prazo_entrega_dias, 3);
        v_dias := (select array_agg(trim(x)::int) from unnest(string_to_array(nullif(trim(coalesce(v_f.dias_entrega, '')), ''), ',')) x
                   where trim(x) ~ '^[0-6]$');
        if v_dias is null then
            continue when extract(dow from p_dia)::int <> 1;
            v_k := 7;
        else
            continue when not (extract(dow from v_chegada)::int = any (v_dias));
            select min(k) into v_k from generate_series(1, 7) k where extract(dow from v_chegada + k)::int = any (v_dias);
        end if;
        -- Já tem pedido a caminho que chega até lá: espera
        continue when exists (select 1 from public.pedidos where fornecedor_id = v_f.id and situacao = 'pendente'
                              and coalesce(previsao_entrega, v_chegada) >= p_dia);
        v_fim := v_chegada + v_k;     -- a entrega seguinte chega neste dia (de manhã)

        with prods as (
            select p.id, p.unidade_medida, greatest(coalesce(p.unidades_por_caixa, 1), 1) as caixa, p.preco_custo,
                   least(v_fim, v_chegada + coalesce(p.dias_validade, 3650)) as ate
            from public.produtos p where p.fornecedor_id = v_f.id
        ), media as (    -- sem previsão: média dos últimos 28 dias
            select i.produto_id, sum(i.quantidade) / 28.0 as m
            from public.venda_itens i join public.vendas v on v.id = i.venda_id
            where v.situacao = 'paga' and v.dia between p_dia - 28 and p_dia - 1
            group by i.produto_id
        ), demanda as (
            select pr.id,
                   sum(coalesce(pd.previsto, md.m, 0)) as qtd,
                   sqrt(sum(power(coalesce((pd.maximo - pd.previsto) / 1.2816, sqrt(coalesce(pd.previsto, md.m, 0))), 2))) as desvio
            from prods pr
            cross join lateral generate_series(p_dia, pr.ate - 1, interval '1 day') d
            left join public.previsao_dias pd on pd.produto_id = pr.id and pd.dia = d::date
            left join media md on md.produto_id = pr.id
            group by pr.id
        ), estoque as (
            select produto_id, sum(quantidade_restante) as q from public.lotes
            where quantidade_restante > 0 and (validade is null or validade > p_dia) group by produto_id
        ), pendente as (
            select i.produto_id, sum(i.quantidade) as q from public.pedido_itens i join public.pedidos pd on pd.id = i.pedido_id
            where pd.situacao = 'pendente' group by i.produto_id
        ), conta as (
            select pr.id, pr.unidade_medida, pr.caixa, pr.preco_custo,
                   d.qtd + v_margem * d.desvio - coalesce(e.q, 0) - coalesce(pe.q, 0) as falta
            from prods pr join demanda d on d.id = pr.id
            left join estoque e on e.produto_id = pr.id
            left join pendente pe on pe.produto_id = pr.id
        )
        select jsonb_agg(jsonb_build_object(
                   'produto_id', id,
                   'quantidade', case when unidade_medida = 'kg' then ceil(falta) else ceil(falta / caixa) * caixa end,
                   'custo', preco_custo) order by id)
        into v_itens
        from conta where falta > 0.05;
        continue when v_itens is null;

        perform public.simulacao_como(v_gerente);
        v_pedido := public.criar_pedido(v_f.id, v_chegada, 'Pedido automático (simulação)', v_itens);
        update public.pedidos set data_pedido = p_dia,
               criado_em = (p_dia + time '07:30' + random() * interval '60 minutes') at time zone 'America/Sao_Paulo'
        where id = v_pedido;
        v_n := v_n + 1;
        perform public.simulacao_anotar(p_dia, 'pedido', v_f.nome || ' (pedido ' || v_pedido || ', ' || jsonb_array_length(v_itens)
                                        || ' itens, chega ' || to_char(v_chegada, 'DD/MM') || ')');
    end loop;
    return v_n;
end;
$$;

-- ---------------------------------------------------------------- Caixas do dia
create or replace function public.simulacao_abrir_caixas(p_dia date)
returns void
language plpgsql
set search_path = public
as $$
declare
    v_p record;
    v_caixa bigint;
    v_qtd integer := case when extract(dow from p_dia) in (5, 6) then 3 else 2 end;
    v_abre time;
begin
    if exists (select 1 from public.simulacao_caixas where dia = p_dia) then return; end if;
    select abre into v_abre from public.simulacao;
    -- Revezamento entre os operadores ativos (gerente não abre caixa na simulação)
    for v_p in
        select id, nome from (
            select id, nome, row_number() over (order by nome) - 1 as n, count(*) over () as total
            from public.perfis where ativo and perfil = 'operador') x
        order by (n - extract(doy from p_dia)::int % total + total) % total
        limit v_qtd
    loop
        perform public.simulacao_como(v_p.id);
        v_caixa := public.abrir_caixa((array[100, 150, 150, 200])[1 + floor(random() * 4)::int]);
        update public.caixas set aberto_em = (p_dia + v_abre + random() * interval '20 minutes') at time zone 'America/Sao_Paulo'
        where id = v_caixa and not exists (select 1 from public.simulacao_caixas where caixa_id = v_caixa)
          and not exists (select 1 from public.vendas where caixa_id = v_caixa);
        insert into public.simulacao_caixas values (v_caixa, p_dia) on conflict do nothing;
    end loop;
end;
$$;

-- ---------------------------------------------------------------- Vendas entre dois momentos do mesmo dia
create or replace function public.simulacao_vendas(p_dia date, p_de timestamptz, p_ate timestamptz)
returns integer
language plpgsql
set search_path = public
as $$
declare
    v_esperado numeric;
    v_qtd integer;
    v_i integer;
    v_j integer;
    v_momento timestamptz;
    v_caixa record;
    v_venda bigint;
    v_total numeric;
    v_pid bigint;
    v_pun text;
    v_q numeric;
    v_disp numeric;
    v_forma text;
    v_pag jsonb;
    v_rec numeric;
    v_feitas integer := 0;
    v_ev record;
    v_tipo text;
    v_gerente uuid;
    v_aut bigint;
    v_gaveta numeric;
    v_h1 numeric := extract(epoch from (p_de at time zone 'America/Sao_Paulo')::time) / 3600;
    v_h2 numeric := extract(epoch from (p_ate at time zone 'America/Sao_Paulo')::time) / 3600;
begin
    if (p_ate at time zone 'America/Sao_Paulo')::date > p_dia then v_h2 := 24; end if;
    v_gerente := public.simulacao_gerente();
    -- Data especial: fator > 1 = clientes a mais comprando as coisas da data; fator < 1 = menos clientes
    select max(fator) filter (where tipo <> 'anomalia') as fator,
           exp(sum(ln(greatest(fator, 0.0001))) filter (where tipo = 'anomalia')) as anomalia,
           string_agg(categorias, ',') as categorias, string_agg(produtos, ',') as produtos
    into v_ev
    from public.eventos_calendario where dia = p_dia;

    v_esperado := public.simulacao_vendas_dia(p_dia) * coalesce(v_ev.anomalia, 1)
                  * greatest(coalesce(v_ev.fator, 1), 0)
                  * (public.simulacao_parte_do_dia(v_h2) - public.simulacao_parte_do_dia(v_h1));
    v_qtd := floor(v_esperado + random())::int;

    drop table if exists _sim_faixa;
    create temp table _sim_faixa on commit drop as
    select produto_id, sum(peso) over (order by produto_id) - peso as ini, sum(peso) over (order by produto_id) as fim
    from public.simulacao_produtos s
    where s.mes = extract(month from p_dia)::int and s.peso > 0 and exists (select 1 from public.produtos p where p.id = s.produto_id);
    drop table if exists _sim_data;     -- produtos da data especial (típicos e das categorias)
    create temp table _sim_data on commit drop as
    select p.id, 1 as grupo from public.produtos p
    where lower(p.nome) in (select lower(trim(x)) from unnest(string_to_array(coalesce(v_ev.produtos, ''), ',')) x)
    union all
    select p.id, 2 from public.produtos p
    where lower(p.categoria) in (select lower(trim(x)) from unnest(string_to_array(coalesce(v_ev.categorias, ''), ',')) x);

    for v_i in 1..v_qtd loop
        v_momento := p_de + random() * (p_ate - p_de);
        -- Caixa: um dos abertos pela simulação, sem venda de outra pessoa em andamento
        select c.id, c.operador_id, c.operador_nome into v_caixa
        from public.caixas c join public.simulacao_caixas s on s.caixa_id = c.id
        where s.dia = p_dia and c.fechado_em is null
          and not exists (select 1 from public.vendas v where v.caixa_id = c.id and v.situacao in ('aberta', 'espera'))
        order by random() limit 1;
        exit when v_caixa.id is null;
        perform public.simulacao_como(v_caixa.operador_id);

        v_tipo := case when coalesce(v_ev.fator, 1) > 1 and random() < (v_ev.fator - 1) / v_ev.fator then 'data' else 'normal' end;
        for v_j in 1..(case when v_tipo = 'data' then 2 + floor(power(random(), 1.4) * 10) else 1 + floor(power(random(), 1.7) * 9) end)::int loop
            -- Produto: na data especial metade das coisas típicas, um terço das categorias da data; senão pelo peso
            v_pid := null;
            if v_tipo = 'data' then
                select p.id, p.unidade_medida into v_pid, v_pun from (select random() + v_j * 0 as r) rr
                join lateral (select d.id from _sim_data d
                              where (rr.r < 0.5 and d.grupo = 1) or (rr.r >= 0.5 and rr.r < 0.83 and d.grupo = 2)
                              order by random() limit 1) x on true
                join public.produtos p on p.id = x.id;
            end if;
            if v_pid is null then
                select p.id, p.unidade_medida into v_pid, v_pun
                from (select random() * (select max(fim) from _sim_faixa) + v_j * 0 as r) rr
                join _sim_faixa f on rr.r >= f.ini and rr.r < f.fim
                join public.produtos p on p.id = f.produto_id;
            end if;
            continue when v_pid is null;
            v_q := case when v_pun = 'kg' then round((0.25 + random() * 1.6)::numeric, 3)
                        when random() < 0.72 then 1 when random() < 0.75 then 2 else 3 + floor(random() * 4) end;

            -- O cliente leva o que tem; o resto é venda perdida
            v_disp := public.disponivel_para_venda(v_pid);
            if v_pun = 'un' then v_disp := floor(v_disp); end if;
            if v_disp < v_q then
                insert into public.simulacao_faltas (dia, produto_id, quantidade, clientes)
                values (p_dia, v_pid, v_q - greatest(v_disp, 0), 1)
                on conflict (dia, produto_id) do update
                set quantidade = simulacao_faltas.quantidade + excluded.quantidade, clientes = simulacao_faltas.clientes + 1;
                v_q := greatest(v_disp, 0);
            end if;
            continue when v_q <= 0;
            begin
                perform public.reservar_item(v_pid, v_q, true);
            exception when others then
                insert into public.simulacao_faltas (dia, produto_id, quantidade, clientes) values (p_dia, v_pid, v_q, 1)
                on conflict (dia, produto_id) do update
                set quantidade = simulacao_faltas.quantidade + excluded.quantidade, clientes = simulacao_faltas.clientes + 1;
            end;
        end loop;

        select id, total into v_venda, v_total from public.vendas where caixa_id = v_caixa.id and situacao = 'aberta';
        continue when v_venda is null;
        if v_total <= 0 then
            delete from public.venda_itens where venda_id = v_venda;
            delete from public.vendas where id = v_venda;
            continue;
        end if;

        -- Pagamento (mesma mistura dos dados de exemplo)
        v_rec := random();
        v_forma := case when v_rec < 0.06 and v_total > 40 then 'varias' when v_rec < 0.26 then 'dinheiro' when v_rec < 0.61 then 'pix'
                        when v_rec < 0.83 then 'debito' when v_rec < 0.95 then 'credito' when v_rec < 0.99 then 'vale_alimentacao'
                        else 'vale_refeicao' end;
        v_pag := case
            when v_forma = 'dinheiro' then jsonb_build_array(jsonb_build_object('forma', 'dinheiro', 'valor', v_total,
                     'recebido', case when v_total <= 10 then 10 when v_total <= 20 then 20 when v_total <= 50 then 50
                                      when v_total <= 100 then 100 else ceil(v_total / 50) * 50 end))
            when v_forma = 'varias' then jsonb_build_array(
                     jsonb_build_object('forma', 'dinheiro', 'valor', least(50, floor(v_total / 2)), 'recebido', 50),
                     jsonb_build_object('forma', 'credito', 'valor', v_total - least(50, floor(v_total / 2)), 'parcelas', 1,
                                        'bandeira', (array['Visa', 'Mastercard', 'Elo'])[1 + floor(random() * 3)::int],
                                        'nsu', lpad(floor(random() * 999999)::text, 6, '0'), 'final_cartao', lpad(floor(random() * 9999)::text, 4, '0')))
            else jsonb_build_array(jsonb_build_object('forma', v_forma, 'valor', v_total,
                     'bandeira', case when v_forma not in ('pix') then (array['Visa', 'Mastercard', 'Elo', 'Hipercard'])[1 + floor(random() * 4)::int] end,
                     'nsu', lpad(floor(random() * 999999)::text, 6, '0'),
                     'parcelas', case when v_forma = 'credito' then 1 end,
                     'final_cartao', case when v_forma <> 'pix' then lpad(floor(random() * 9999)::text, 4, '0') end))
            end;
        begin
            perform public.finalizar_venda(v_venda, v_pag,
                case when random() < 0.1 then (array['52601815906', '08301661305', '18609139034', '99603082430'])[1 + floor(random() * 4)::int] end);
        exception when others then
            perform public.simulacao_anotar(p_dia, 'erro na venda', sqlerrm);
            delete from public.venda_itens where venda_id = v_venda;
            update public.vendas set situacao = 'cancelada', total = 0, cancelada_em = v_momento, cancelada_por = 'Simulação' where id = v_venda;
            continue;
        end;
        update public.vendas set criada_em = v_momento - interval '3 minutes', paga_em = v_momento where id = v_venda;
        update public.venda_pagamentos set criado_em = v_momento where venda_id = v_venda;
        v_feitas := v_feitas + 1;

        -- De vez em quando o gerente cancela uma venda paga (devolve aos lotes)
        if random() < 0.004 then
            perform public.simulacao_como(v_gerente);
            perform public.cancelar_venda(v_venda);
            update public.vendas set cancelada_em = v_momento + interval '15 minutes' where id = v_venda;
            perform public.simulacao_como(v_caixa.operador_id);
        end if;

        -- Gaveta cheia: sangria com autorização do gerente
        v_gaveta := public.dinheiro_na_gaveta(v_caixa.id);
        if v_gaveta > 700 then
            insert into public.autorizacoes (venda_id, acao, operador_id, gerente_usuario, gerente_nome, aprovada, usada, criada_em)
            select null, 'sangria', v_caixa.operador_id, usuario, nome, true, false, now() from public.perfis where id = v_gerente
            returning id into v_aut;
            perform public.registrar_movimento('sangria', floor((v_gaveta - 200) / 50) * 50, 'Sangria (simulação)', v_aut);
            update public.caixa_movimentos set criado_em = v_momento + interval '1 minute'
            where id = (select max(id) from public.caixa_movimentos where caixa_id = v_caixa.id);
            update public.autorizacoes set criada_em = v_momento where id = v_aut;
        end if;
    end loop;
    return v_feitas;
end;
$$;

-- ---------------------------------------------------------------- Fim do dia
create or replace function public.simulacao_fechar(p_dia date)
returns void
language plpgsql
set search_path = public
as $$
declare
    v_c record;
    v_esperado numeric;
    v_dif numeric;
    v_r numeric;
    v_gerente uuid;
    v_fecha time;
begin
    v_gerente := public.simulacao_gerente();
    select fecha into v_fecha from public.simulacao;
    for v_c in
        select c.* from public.caixas c join public.simulacao_caixas s on s.caixa_id = c.id
        where s.dia = p_dia and c.fechado_em is null
    loop
        perform public.simulacao_como(v_c.operador_id);
        v_esperado := public.dinheiro_na_gaveta(v_c.id);
        v_r := random();
        v_dif := case when v_r < 0.8 then 0 when v_r < 0.95 then round((random() * 4 - 2)::numeric, 2) else -round((2 + random() * 8)::numeric, 2) end;
        begin
            perform public.fechar_caixa(v_c.id, greatest(v_esperado + v_dif, 0),
                                        case when v_dif < 0 then 'Faltou troco (simulação)' when v_dif > 0 then 'Sobrou na gaveta (simulação)' end);
            update public.caixas set fechado_em = (p_dia + v_fecha + random() * interval '15 minutes') at time zone 'America/Sao_Paulo'
            where id = v_c.id;
        exception when others then
            perform public.simulacao_anotar(p_dia, 'caixa não fechou', v_c.operador_nome || ': ' || sqlerrm);
        end;
    end loop;

    perform public.simulacao_como(v_gerente);
    if not exists (select 1 from public.fechamentos_dia where dia = p_dia and not automatico) then
        begin
            perform public.fechar_dia(p_dia, 'Fechado pela simulação');
            update public.fechamentos_dia set fechado_em = (p_dia + v_fecha + interval '25 minutes') at time zone 'America/Sao_Paulo'
            where dia = p_dia;
            perform public.simulacao_anotar(p_dia, 'dia fechado',
                (select count(*) || ' vendas, R$ ' || coalesce(sum(total), 0) from public.vendas where dia = p_dia and situacao = 'paga')
                || coalesce('; vendas perdidas por falta: ' || (select sum(clientes) from public.simulacao_faltas where dia = p_dia), ''));
        exception when others then
            -- ex.: alguém deixou um caixa aberto no app
            perform public.simulacao_anotar(p_dia, 'dia não fechado', sqlerrm);
        end;
    end if;
end;
$$;

-- ---------------------------------------------------------------- Rodada (agendada a cada 30 minutos)
create or replace function public.simular(p_ate timestamptz default null)
returns text
language plpgsql
security definer
set search_path = public
set statement_timeout = 0
set client_min_messages = warning
as $$
declare
    s public.simulacao%rowtype;
    v_ate timestamptz := coalesce(p_ate, now());
    v_dia date;
    v_abre timestamptz;
    v_fecha timestamptz;
    v_fim timestamptz;
    v_vendas integer := 0;
    v_dias integer := 0;
    v_sub text := current_setting('request.jwt.claim.sub', true);
    v_claims text := current_setting('request.jwt.claims', true);
begin
    if auth.uid() is not null and not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente roda a simulação.' using errcode = '42501';
    end if;
    select * into s from public.simulacao where id = 1 for update skip locked;
    if not found then
        return 'A simulação já está rodando.';
    end if;
    if not s.ligada then
        return 'Simulação desligada.';
    end if;
    perform public.simulacao_preparar_pesos();

    -- Parado há muito tempo: recupera só os últimos dias
    if s.ultimo_momento is null or s.ultimo_momento < v_ate - make_interval(days => s.dias_recuperar) then
        s.ultimo_momento := ((v_ate at time zone 'America/Sao_Paulo')::date - s.dias_recuperar)::timestamp at time zone 'America/Sao_Paulo';
        perform public.simulacao_anotar(null, 'recuperação', 'Ficou parada muito tempo: simulando a partir de '
                                        || to_char(s.ultimo_momento at time zone 'America/Sao_Paulo', 'DD/MM/YYYY'));
    end if;

    while s.ultimo_momento < v_ate loop
        v_dia := (s.ultimo_momento at time zone 'America/Sao_Paulo')::date;
        v_abre := (v_dia + s.abre) at time zone 'America/Sao_Paulo';
        v_fecha := (v_dia + s.fecha) at time zone 'America/Sao_Paulo';
        perform set_config('mercado.hoje', v_dia::text, true);

        -- Loja fechada (Natal, Ano-Novo, data com fator 0): ninguém compra nem entrega, mas o gerente faz os pedidos
        if to_char(v_dia, 'MM-DD') in ('12-25', '01-01')
           or exists (select 1 from public.eventos_calendario where dia = v_dia and fator = 0 and tipo <> 'anomalia') then
            exit when v_ate < v_abre;
            perform public.simulacao_pedidos(v_dia);
            s.ultimo_momento := (v_dia + 1)::timestamp at time zone 'America/Sao_Paulo';
            continue;
        end if;
        exit when v_ate < v_abre;

        if s.ultimo_momento < v_abre then
            perform public.simulacao_entregas(v_dia);
            perform public.simulacao_pedidos(v_dia);
            s.ultimo_momento := v_abre;
        end if;
        perform public.simulacao_abrir_caixas(v_dia);

        v_fim := least(v_ate, v_fecha);
        if v_fim > s.ultimo_momento then
            v_vendas := v_vendas + public.simulacao_vendas(v_dia, s.ultimo_momento, v_fim);
            s.ultimo_momento := v_fim;
        end if;
        if s.ultimo_momento >= v_fecha then
            perform public.simulacao_fechar(v_dia);
            s.ultimo_momento := (v_dia + 1)::timestamp at time zone 'America/Sao_Paulo';
            v_dias := v_dias + 1;
        end if;
    end loop;

    update public.simulacao set ultimo_momento = s.ultimo_momento, atualizado_em = now() where id = 1;
    delete from public.simulacao_log where momento < now() - interval '60 days';
    perform set_config('mercado.hoje', '', true);
    perform set_config('request.jwt.claim.sub', coalesce(v_sub, ''), true);
    perform set_config('request.jwt.claims', coalesce(v_claims, ''), true);
    return format('Simulação: %s vendas, %s dias fechados.', v_vendas, v_dias);
end;
$$;

-- ---------------------------------------------------------------- Ligar / desligar
create or replace function public.simulacao_ligar(p_ligada boolean)
returns text
language plpgsql
security definer
set search_path = public
set client_min_messages = warning
as $$
declare
    v_nivel numeric;
    v_ate date := public.hoje_local() - 1;
begin
    if auth.uid() is not null and not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente liga a simulação.' using errcode = '42501';
    end if;
    if not p_ligada then
        update public.simulacao set ligada = false, atualizado_em = now() where id = 1;
        perform public.simulacao_anotar(null, 'desligada', null);
        return 'Simulação desligada.';
    end if;
    -- Nível de vendas: média dos últimos 8 semanas sem data especial, tirando o dia da semana e o mês
    select avg(n / ((array[0.75, 0.85, 0.9, 0.95, 1.0, 1.25, 1.35])[extract(dow from dia)::int + 1]
                  * (array[0.9, 0.88, 0.95, 0.97, 1.0, 0.98, 1.02, 1.0, 0.97, 1.0, 1.05, 1.3])[extract(month from dia)::int]))
    into v_nivel
    from (select dia, count(*) as n from public.vendas where situacao = 'paga' and dia between v_ate - 55 and v_ate group by dia) x
    where not exists (select 1 from public.eventos_calendario e where e.dia = x.dia);
    update public.simulacao
    set ligada = true, nivel = coalesce(v_nivel, 45), nivel_em = v_ate - 28,
        -- Continua de onde as vendas pararam (no máximo dias_recuperar atrás)
        ultimo_momento = greatest(
            coalesce((select (max(dia) + 1)::timestamp at time zone 'America/Sao_Paulo' from public.vendas where situacao = 'paga'), now()),
            ((public.hoje_local() - dias_recuperar)::timestamp at time zone 'America/Sao_Paulo')),
        atualizado_em = now()
    where id = 1;
    perform public.simulacao_preparar_pesos();
    perform public.simulacao_anotar(null, 'ligada', 'Nível: ' || round(coalesce(v_nivel, 45), 1) || ' vendas por dia');
    return 'Simulação ligada. Rode select public.simular(); ou espere o agendamento (a cada 30 minutos).';
end;
$$;

-- ---------------------------------------------------------------- Permissões
revoke execute on function public.simulacao_como(uuid) from public, anon, authenticated;
revoke execute on function public.simulacao_entregas(date) from public, anon, authenticated;
revoke execute on function public.simulacao_pedidos(date) from public, anon, authenticated;
revoke execute on function public.simulacao_abrir_caixas(date) from public, anon, authenticated;
revoke execute on function public.simulacao_vendas(date, timestamptz, timestamptz) from public, anon, authenticated;
revoke execute on function public.simulacao_fechar(date) from public, anon, authenticated;
revoke execute on function public.simulacao_preparar_pesos() from public, anon, authenticated;
revoke execute on function public.simulacao_anotar(date, text, text) from public, anon, authenticated;
revoke execute on function public.simulacao_gerente() from public, anon, authenticated;
revoke execute on function public.simular(timestamptz) from public, anon;
revoke execute on function public.simulacao_ligar(boolean) from public, anon;
grant execute on function public.simular(timestamptz) to authenticated, service_role;
grant execute on function public.simulacao_ligar(boolean) to authenticated, service_role;

-- limpar_dados_exemplo também zera a simulação e a desliga (as outras tabelas dela já saem junto com produtos e caixas)
do $$
declare
    v_def text;
begin
    select pg_get_functiondef('public.limpar_dados_exemplo(text)'::regprocedure) into v_def;
    if position('simulacao' in v_def) = 0 then
        v_def := replace(v_def, $t$    delete from public.eventos_calendario where tipo = 'anomalia';$t$,
            $t$    if to_regclass('public.simulacao') is not null then
        truncate public.simulacao_log, public.simulacao_faltas, public.simulacao_caixas, public.simulacao_atrasos, public.simulacao_produtos;
        update public.simulacao set ligada = false, ultimo_momento = null, nivel = null, nivel_em = null;
    end if;
    delete from public.eventos_calendario where tipo = 'anomalia';$t$);
        execute v_def;
    end if;
end;
$$;

-- ---------------------------------------------------------------- Agendamento: a cada 30 minutos
do $$
begin
    if exists (select 1 from pg_extension where extname = 'pg_cron') then
        perform cron.unschedule(jobid) from cron.job where jobname = 'mercado-simulacao';
        perform cron.schedule('mercado-simulacao', '*/30 * * * *', 'select public.simular()');
    else
        raise notice 'pg_cron não está ligado: rode select public.simular(); para a loja andar.';
    end if;
end;
$$;

select 'Simulador instalado (desligado). Para ligar: select public.simulacao_ligar(true);' as resultado;
