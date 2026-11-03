-- =====================================================================
-- Dados de exemplo extras (pedido do Guilherme em 03/11, preparando a semana 7): mais vendas nos feriados, vésperas
-- e datas festivas do calendário (eventos_calendario, script 012), puxando o que costuma vender nessas datas
-- (churrasco nos feriados, panetone e espumante no Natal, ovo de Páscoa, pipoca e canjica no São João...).
--
-- NÃO É PARA O USO REAL. Rodar depois do 011 já ter gerado os dados e do 012:
--   1. Rodar este arquivo inteiro (cria as funções).
--   2. select public.gerar_datas_festivas_exemplo();
-- Roda uma vez só (lembra que já rodou). O limpar_dados_exemplo('APAGAR TUDO') do 011 também apaga estes dados.
-- =====================================================================

create table if not exists public.dados_exemplo_execucoes (
    nome      text primary key,
    rodou_em  timestamptz not null default now()
);
alter table public.dados_exemplo_execucoes enable row level security;   -- sem regra: ninguém lê pelo programa

create or replace function public.gerar_datas_festivas_exemplo()
returns text
language plpgsql
security definer
set search_path = public
set statement_timeout = 0
as $$
declare
    v_hoje date := public.hoje_local();
    v_base bigint;
    v_vendas integer;
    v_itens integer;
    v_dias integer;
begin
    if exists (select 1 from public.dados_exemplo_execucoes where nome = 'datas_festivas') then
        raise exception 'As vendas das datas festivas já foram geradas.';
    end if;
    if not exists (select 1 from public.eventos_calendario) then
        raise exception 'Rode antes o 012_semana6_ajustes.sql (calendário de datas especiais).';
    end if;
    if (select count(*) from public.vendas) < 1000 then
        raise exception 'Rode antes o 011 (select public.gerar_dados_exemplo();).';
    end if;
    perform setseed(0.77);

    -- ------------------------------------------------------------ Produtos de época
    insert into public.produtos (nome, categoria, preco, unidades_por_caixa, dias_validade, unidade_medida, estoque_minimo, preco_custo)
    select nome, categoria, preco, caixa, dias, unidade, 0, round(preco * 0.62, 2)
    from (values
        ('Panetone 500g', 'Padaria', 24.90, 12, 120, 'un'),
        ('Colomba pascal 500g', 'Padaria', 26.90, 12, 120, 'un'),
        ('Ovo de Páscoa 240g', 'Mercearia', 49.90, 12, 180, 'un'),
        ('Chocolate ao leite 90g', 'Mercearia', 6.49, 20, 270, 'un'),
        ('Espumante 750ml', 'Bebidas', 39.90, 6, 730, 'un'),
        ('Carvão vegetal 3kg', 'Mercearia', 18.90, 10, null, 'un'),
        ('Bacalhau 1kg', 'Frios', 89.90, 10, 180, 'un'),
        ('Peru temperado (kg)', 'Açougue', 39.90, 10, 90, 'kg'),
        ('Chester (kg)', 'Açougue', 34.90, 10, 90, 'kg'),
        ('Milho de pipoca 500g', 'Grãos', 5.49, 20, 365, 'un'),
        ('Canjica 500g', 'Grãos', 6.29, 20, 365, 'un'),
        ('Paçoca 1kg', 'Mercearia', 19.90, 10, 180, 'un'),
        ('Amendoim 500g', 'Grãos', 11.90, 20, 180, 'un')
    ) as x(nome, categoria, preco, caixa, dias, unidade)
    on conflict ((lower(trim(nome)))) do nothing;
    update public.produtos p set fornecedor_id = (
        select f.id from public.fornecedores f
        where f.ativo and exists (select 1 from unnest(string_to_array(f.categorias, ',')) c where lower(trim(c)) = lower(p.categoria))
        order by f.nome limit 1)
    where p.fornecedor_id is null;

    -- ------------------------------------------------------------ Dias com evento (só os que já passaram e tiveram caixa)
    create temp table t_ev on commit drop as
    select e.dia, max(e.fator) as fator,
           string_agg(e.categorias, ',') as categorias, string_agg(e.produtos, ',') as produtos,
           (select count(*) from public.vendas v where v.dia = e.dia and v.situacao = 'paga') as vendas_do_dia
    from public.eventos_calendario e
    where e.dia < v_hoje and e.fator > 1 and exists (select 1 from public.caixas c where c.dia = e.dia)
    group by e.dia;
    -- Dias com fator > 1 que caíram fechados (sem caixa) ficam como estão
    create temp table t_cats on commit drop as
    select distinct e.dia, trim(c) as categoria from t_ev e cross join unnest(string_to_array(e.categorias, ',')) c where trim(c) <> '';
    create temp table t_tipicos on commit drop as
    select distinct e.dia, p.id as produto_id from t_ev e cross join unnest(string_to_array(e.produtos, ',')) n
    join public.produtos p on lower(p.nome) = lower(trim(n));

    create temp table t_prod on commit drop as
    select p.id, p.nome, p.categoria, p.preco, p.unidade_medida as unidade, coalesce(p.dias_validade, 365) as dias, p.preco_custo
    from public.produtos p;

    -- ------------------------------------------------------------ Vendas a mais em cada dia (fator − 1 do movimento do dia)
    create temp table t_caixas_dia on commit drop as
    select dia, array_agg(id order by aberto_em) as ids from public.caixas where dia in (select dia from t_ev) group by dia;
    select coalesce(max(id), 0) into v_base from public.vendas;
    create temp table t_vendas on commit drop as
    select v_base + row_number() over (order by e.dia, s.i) as id, e.dia,
           cd.ids[1 + (s.i % array_length(cd.ids, 1))] as caixa_id,
           (e.dia + (case when r < 0.45 then 9 + random() * 3.5 else 15 + random() * 5.5 end) * interval '1 hour') at time zone 'America/Sao_Paulo' as paga_em,
           (2 + floor(power(random(), 1.4) * 10))::int as n_itens, random() as sorteio_forma
    from t_ev e
    join t_caixas_dia cd on cd.dia = e.dia
    cross join lateral generate_series(1, greatest(1, round(e.vendas_do_dia * (e.fator - 1))::int)) s(i)
    cross join lateral (select random() as r) rr;

    -- Itens: metade dos produtos típicos da data, um terço das categorias da data, o resto de tudo
    create temp table t_itens on commit drop as
    select v.id as venda_id, v.dia, p.id as produto_id, p.nome, p.unidade,
           sum(case when p.unidade = 'kg' then round((0.6 + random() * 2.4)::numeric, 3)
                    else (case when random() < 0.55 then 1 when random() < 0.6 then 2 else 3 + floor(random() * 10) end)::numeric end) as quantidade,
           round(p.preco / power(1.055, (v_hoje - v.dia) / 365.0)::numeric, 2) as preco_unitario
    from t_vendas v
    cross join lateral generate_series(1, v.n_itens) j
    cross join lateral (select random() + j * 0 + v.id * 0 as r) rr
    join lateral (
        select x.id from (
            select t.produto_id as id, 1 as ordem from t_tipicos t where t.dia = v.dia and rr.r < 0.5
            union all
            select p2.id, 2 from t_prod p2 join t_cats c on c.dia = v.dia and c.categoria = p2.categoria where rr.r >= 0.5 and rr.r < 0.83
            union all
            select p3.id, 3 from t_prod p3 where rr.r >= 0.83 or (rr.r < 0.5 and not exists (select 1 from t_tipicos t2 where t2.dia = v.dia))
        ) x order by random() limit 1) escolhido on true
    join t_prod p on p.id = escolhido.id
    group by v.id, v.dia, p.id, p.nome, p.unidade, p.preco;
    alter table t_itens add column id bigint, add column subtotal numeric;
    select coalesce(max(id), 0) into v_base from public.venda_itens;
    update t_itens t set id = x.n + v_base, subtotal = round(t.quantidade * t.preco_unitario, 2)
    from (select venda_id, produto_id, row_number() over (order by venda_id, produto_id) as n from t_itens) x
    where x.venda_id = t.venda_id and x.produto_id = t.produto_id;

    alter table t_vendas add column total numeric, add column forma text, add column operador_id uuid, add column operador_nome text;
    update t_vendas v set total = x.soma from (select venda_id, sum(subtotal) as soma from t_itens group by venda_id) x where x.venda_id = v.id;
    update t_vendas set forma = case when sorteio_forma < 0.18 then 'dinheiro' when sorteio_forma < 0.55 then 'pix'
                                     when sorteio_forma < 0.75 then 'debito' else 'credito' end;
    update t_vendas v set operador_id = c.operador_id, operador_nome = c.operador_nome from public.caixas c where c.id = v.caixa_id;

    insert into public.vendas (id, caixa_id, operador_id, operador_nome, dia, situacao, forma_pagamento, total, criada_em, paga_em)
    overriding system value
    select id, caixa_id, operador_id, operador_nome, dia, 'paga', forma, total, paga_em - interval '4 minutes', paga_em from t_vendas;
    insert into public.venda_itens (id, venda_id, produto_id, nome_produto, unidade_medida, quantidade, preco_unitario, subtotal)
    overriding system value
    select id, venda_id, produto_id, nome, unidade, quantidade, preco_unitario, subtotal from t_itens;
    perform setval(pg_get_serial_sequence('public.vendas', 'id'), (select max(id) from public.vendas));
    perform setval(pg_get_serial_sequence('public.venda_itens', 'id'), (select max(id) from public.venda_itens));

    insert into public.venda_pagamentos (venda_id, forma, valor, recebido, troco, bandeira, nsu, autorizacao, parcelas, final_cartao, criado_em)
    select v.id, v.forma, v.total, r.recebido, case when v.forma = 'dinheiro' then r.recebido - v.total else 0 end,
           case when v.forma in ('debito', 'credito') then (array['Visa', 'Mastercard', 'Elo'])[1 + floor(random() * 3)::int] end,
           case when v.forma <> 'dinheiro' then lpad((floor(random() * 999999))::text, 6, '0') end,
           case when v.forma in ('debito', 'credito') then upper(substr(md5(random()::text), 1, 6)) end,
           case when v.forma = 'credito' then case when v.total > 250 then 3 else 1 end end,
           case when v.forma in ('debito', 'credito') then lpad((floor(random() * 9999))::text, 4, '0') end,
           v.paga_em
    from t_vendas v
    cross join lateral (select case when v.forma = 'dinheiro' then (case when v.total <= 50 then 50 when v.total <= 100 then 100 else ceil(v.total / 50) * 50 end)::numeric end as recebido) r;
    update public.vendas v set recebido = p.recebido, troco = p.troco
    from public.venda_pagamentos p where p.venda_id = v.id and p.forma = 'dinheiro' and v.id in (select id from t_vendas);

    -- ------------------------------------------------------------ Lotes: o lote do mês recebe o que saiu a mais (produto de época ganha lote)
    create temp table t_saida on commit drop as
    select i.produto_id, date_trunc('month', i.dia)::date as mes, sum(i.quantidade) as qtd from t_itens i group by 1, 2;
    update public.lotes l set quantidade_inicial = l.quantidade_inicial + s.qtd
    from t_saida s
    where l.produto_id = s.produto_id and l.data_compra = s.mes - 3
      and l.id = (select min(l2.id) from public.lotes l2 where l2.produto_id = s.produto_id and l2.data_compra = s.mes - 3);
    insert into public.lotes (produto_id, quantidade_inicial, quantidade_restante, data_compra, validade, preco_custo)
    select s.produto_id, s.qtd, 0, s.mes - 3, greatest(s.mes - 3 + p.dias, (s.mes + interval '1 month')::date),
           round(p.preco_custo / power(1.055, (v_hoje - s.mes) / 365.0)::numeric, 2)
    from t_saida s join t_prod p on p.id = s.produto_id
    where not exists (select 1 from public.lotes l where l.produto_id = s.produto_id and l.data_compra = s.mes - 3);
    insert into public.venda_item_lotes (item_id, lote_id, quantidade)
    select i.id, (select l.id from public.lotes l where l.produto_id = i.produto_id and l.data_compra = date_trunc('month', i.dia)::date - 3 order by l.id limit 1), i.quantidade
    from t_itens i;
    -- Estoque de hoje dos produtos de época (pouco, fora da época)
    insert into public.lotes (produto_id, quantidade_inicial, quantidade_restante, data_compra, validade, preco_custo)
    select p.id, q, q, v_hoje - 5, case when p.dias < 3650 then v_hoje - 5 + p.dias end, p.preco_custo
    from t_prod p cross join lateral (select case when p.unidade = 'kg' then 4.0 else 6 end::numeric as q) x
    where p.nome in ('Panetone 500g', 'Chocolate ao leite 90g', 'Espumante 750ml', 'Carvão vegetal 3kg', 'Amendoim 500g', 'Milho de pipoca 500g')
      and not exists (select 1 from public.lotes l where l.produto_id = p.id and l.quantidade_restante > 0);

    -- ------------------------------------------------------------ Gaveta e fechamentos desses dias
    update public.caixas c set
        fechado_esperado = public.dinheiro_na_gaveta(c.id),
        fechado_contado = public.dinheiro_na_gaveta(c.id) + coalesce(c.fechado_diferenca, 0),
        fechado_em = greatest(c.fechado_em, (select max(v.paga_em) from public.vendas v where v.caixa_id = c.id) + interval '15 minutes')
    where c.id in (select distinct caixa_id from t_vendas) and c.fechado_em is not null;

    perform set_config('request.jwt.claims', '{"role":"service_role"}', true);
    update public.fechamentos_dia f set resumo = public.resumo_dia(f.dia) where f.dia in (select dia from t_ev);

    insert into public.dados_exemplo_execucoes (nome) values ('datas_festivas');
    select count(*) into v_vendas from t_vendas;
    select count(*) into v_itens from t_itens;
    select count(*) into v_dias from t_ev;
    return format('Pronto: %s vendas a mais (%s itens) em %s datas especiais. Os fechamentos desses dias foram refeitos.', v_vendas, v_itens, v_dias);
end;
$$;

-- O limpar do 011 passa a apagar também o registro de que as datas festivas já rodaram
create or replace function public.limpar_dados_exemplo(p_confirmacao text)
returns text
language plpgsql
security definer
set search_path = public
as $$
begin
    if p_confirmacao is distinct from 'APAGAR TUDO' then
        raise exception 'Para apagar todos os dados, rode: select public.limpar_dados_exemplo(''APAGAR TUDO'');';
    end if;
    truncate public.fornecedor_mensagens, public.fechamentos_dia, public.caixa_movimentos, public.venda_itens_cancelados,
             public.autorizacoes, public.venda_pagamentos, public.venda_item_lotes, public.venda_itens, public.vendas, public.caixas,
             public.promocoes, public.promocoes_validade, public.produto_fornecedores, public.pedido_itens, public.pedidos,
             public.lotes, public.produtos, public.fornecedores, public.auditoria_login, public.contatos, public.pedidos_senha,
             public.dados_exemplo_execucoes
             restart identity cascade;
    return 'Tudo apagado. Os usuários e o calendário de datas especiais continuam. Anexos de e-mail já baixados ficam no Storage (apague pelo painel, em Storage > anexos-fornecedores).';
end;
$$;

revoke execute on function public.gerar_datas_festivas_exemplo() from public, anon, authenticated;
revoke execute on function public.limpar_dados_exemplo(text) from public, anon, authenticated;
