-- =====================================================================
-- Dados de exemplo: histórico de 26 anos (pedido do Guilherme em 09/11, semana 7), para testar a previsão em
-- anos bons e ruins. NÃO É PARA O USO REAL (o limpar_dados_exemplo('APAGAR TUDO') do 011 também apaga tudo isto).
--
-- O que entra, antes dos 5 anos do 011 (que continuam como estão):
--   2016 até o início do 011   venda por venda, nas mesmas tabelas de hoje (caixas, vendas, itens, pagamentos)
--   2000 a 2015                resumo por produto e dia (venda_itens_resumo_dia) e por dia (vendas_resumo_dia),
--                              para caber no plano grátis do Supabase (500 MB)
--   Calendário 2000-2020       feriados e datas festivas (preencher_calendario do 012), Copas de 2002 a 2018 e os
--                              períodos fora do normal (tipo 'anomalia'): pandemia, greve dos caminhoneiros e apagão
--   A loja                     começa menor (umas 19 vendas num dia comum em 2000) e cresce até as 45 do 011, com a
--                              crise de 2008-2009 e a recessão de 2015-2016 na tendência; preços pelo IPCA de cada ano
--                              (sem PIX antes de 16/11/2020); produtos que só chegaram depois (long neck em 2006...)
--   Instabilidades escondidas  o que acontece numa loja e ninguém marca no calendário: produto em falta por alguns
--                              dias, chuva forte, queda de energia, festa no bairro, um concorrente que abriu, obra na
--                              rua, compras grandes. Ficam anotadas em dados_exemplo_instabilidades (o "gabarito"),
--                              que a previsão NÃO lê: serve para conferir se o resíduo percebeu cada uma.
--
-- Como usar (SQL Editor do Supabase, depois do 011 e do 013 terem gerado os 5 anos):
--   1. Rodar este arquivo inteiro (cria as tabelas e as funções e preenche o calendário).
--   2. Gerar em 3 partes (cada uma leva de 30 s a 2 min):
--        select public.gerar_historico_exemplo('2016-01-01');   -- venda por venda até o início do 011
--        select public.gerar_historico_exemplo('2008-01-01');   -- resumo 2008 a 2015
--        select public.gerar_historico_exemplo('2000-01-01');   -- resumo 2000 a 2007
--   3. Resumos das Análises dos anos novos:  select public.preencher_analise_cache();
--   4. Previsão de novo:  select public.previsao_calcular();
-- Cada parte gera do dia pedido até o dia anterior ao que já existe; rodar de novo a mesma parte não duplica.
-- =====================================================================

-- ---------------------------------------------------------------- Tabelas do resumo (2000 a 2015)
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

-- Gabarito das instabilidades e parâmetros do gerador (só o SQL Editor lê)
create table if not exists public.dados_exemplo_instabilidades (
    id          bigint generated always as identity primary key,
    tipo        text not null,          -- falta_produto, dia_ruim, pico_local, mudanca_nivel, loja_fechada, compra_grande, lancamento, preco
    inicio      date not null,
    fim         date not null,
    produto_id  bigint references public.produtos (id) on delete cascade,
    categorias  text,
    fator       numeric not null,       -- multiplica o movimento (ou a venda do produto/categoria); 0 = some
    descricao   text not null
);
alter table public.dados_exemplo_instabilidades enable row level security;
create table if not exists public.dados_exemplo_historico (
    id      smallint primary key default 1 check (id = 1),
    limite  date not null               -- primeiro dia do 011: o histórico vai até o dia anterior
);
alter table public.dados_exemplo_historico enable row level security;
create table if not exists public.dados_exemplo_partes (
    de   date primary key,
    ate  date not null,
    feito_em timestamptz not null default now()
);
alter table public.dados_exemplo_partes enable row level security;
create table if not exists public.dados_exemplo_pesos (
    produto_id  bigint primary key references public.produtos (id) on delete cascade,
    peso        numeric not null        -- itens por venda num dia comum, medidos no primeiro ano do 011
);
alter table public.dados_exemplo_pesos enable row level security;

-- ---------------------------------------------------------------- Calendário 2000 a 2020
-- Período fora do normal: a previsão deixa esses dias fora da conta do normal (016)
alter table public.eventos_calendario drop constraint if exists eventos_calendario_tipo_check;
alter table public.eventos_calendario add constraint eventos_calendario_tipo_check
    check (tipo in ('feriado', 'vespera', 'festivo', 'evento', 'anomalia'));

select public.preencher_calendario(a) from generate_series(2000, 2020) a;
-- Black Friday só pegou no Brasil a partir de 2010
delete from public.eventos_calendario where nome = 'Black Friday' and dia < '2010-01-01';

-- Jogos do Brasil nas Copas de 2002 a 2018
insert into public.eventos_calendario (dia, nome, tipo, fator, categorias, produtos)
select j.dia::date, 'Copa do Mundo: ' || j.jogo, 'evento', j.fator, 'Bebidas, Açougue, Congelados', 'Carvão vegetal 3kg'
from (values
    ('2002-06-03', 'Brasil x Turquia', 1.30), ('2002-06-08', 'Brasil x China', 1.30), ('2002-06-13', 'Costa Rica x Brasil', 1.30),
    ('2002-06-17', 'Brasil x Bélgica', 1.35), ('2002-06-21', 'Inglaterra x Brasil', 1.35), ('2002-06-26', 'Brasil x Turquia (semifinal)', 1.40),
    ('2002-06-30', 'Alemanha x Brasil (final)', 1.50),
    ('2006-06-13', 'Brasil x Croácia', 1.30), ('2006-06-18', 'Brasil x Austrália', 1.30), ('2006-06-22', 'Japão x Brasil', 1.30),
    ('2006-06-27', 'Brasil x Gana', 1.35), ('2006-07-01', 'Brasil x França', 1.40),
    ('2010-06-15', 'Brasil x Coreia do Norte', 1.30), ('2010-06-20', 'Brasil x Costa do Marfim', 1.30), ('2010-06-25', 'Portugal x Brasil', 1.30),
    ('2010-06-28', 'Brasil x Chile', 1.35), ('2010-07-02', 'Holanda x Brasil', 1.40),
    ('2014-06-12', 'Brasil x Croácia (abertura)', 1.45), ('2014-06-17', 'Brasil x México', 1.35), ('2014-06-23', 'Camarões x Brasil', 1.35),
    ('2014-06-28', 'Brasil x Chile', 1.40), ('2014-07-04', 'Brasil x Colômbia', 1.40), ('2014-07-08', 'Brasil x Alemanha (semifinal)', 1.45),
    ('2014-07-12', 'Brasil x Holanda', 1.20),
    ('2018-06-17', 'Brasil x Suíça', 1.30), ('2018-06-22', 'Brasil x Costa Rica', 1.30), ('2018-06-27', 'Sérvia x Brasil', 1.30),
    ('2018-07-02', 'Brasil x México', 1.35), ('2018-07-06', 'Brasil x Bélgica', 1.40)
) as j(dia, jogo, fator)
on conflict (dia, nome) do nothing;

-- Períodos fora do normal (todo mundo lembra deles; ficam no calendário com o efeito no movimento)
insert into public.eventos_calendario (dia, nome, tipo, fator, categorias, produtos)
select d::date, a.nome, 'anomalia', a.fator, a.categorias, a.produtos
from (values
    ('2001-06-01'::date, '2002-02-28'::date, 'Apagão: racionamento de energia', 0.97, 'Congelados', null::text),
    ('2018-05-21', '2018-05-23', 'Greve dos caminhoneiros: corrida às compras', 1.35, 'Grãos, Mercearia', null),
    ('2018-05-24', '2018-05-31', 'Greve dos caminhoneiros: falta de mercadoria', 0.80, 'Hortifrúti, Açougue, Laticínios', null),
    ('2020-03-13', '2020-03-21', 'Pandemia: corrida às compras', 1.60, 'Grãos, Mercearia, Limpeza, Higiene',
        'Arroz branco 5kg, Feijão carioca 1kg, Papel higiênico (12 rolos), Água sanitária 2L, Sabonete em barra 85g, Macarrão espaguete 500g'),
    ('2020-03-22', '2020-12-31', 'Pandemia: quarentena', 1.15, 'Limpeza, Higiene, Bebidas, Padaria', null),
    ('2021-03-01', '2021-04-30', 'Pandemia: segunda onda', 1.08, 'Limpeza, Higiene', null)
) as a(ini, fim, nome, fator, categorias, produtos)
cross join generate_series(a.ini, a.fim, interval '1 day') d
on conflict (dia, nome) do nothing;

-- ---------------------------------------------------------------- IPCA de cada ano (IBGE), para os preços antigos
create or replace function public.ipca_ano(p_ano integer)
returns numeric
language sql
immutable
as $$
    select coalesce((array[5.97, 7.67, 12.53, 9.30, 7.60, 5.69, 3.14, 4.46, 5.90, 4.31, 5.91, 6.50, 5.84, 5.91, 6.41,
                           10.67, 6.29, 2.95, 3.75, 4.31, 4.52, 10.06, 5.79, 4.62, 4.83, 4.50, 4.50])[p_ano - 1999], 4.5) / 100;
$$;
-- Índice de preços no dia (1,00 em 01/01/2000), crescendo dentro do ano pelo IPCA daquele ano
create or replace function public.indice_precos(p_dia date)
returns numeric
language sql
immutable
as $$
    select coalesce((select exp(sum(ln(1 + public.ipca_ano(a)))) from generate_series(2000, extract(year from p_dia)::int - 1) a), 1)
           * power(1 + public.ipca_ano(extract(year from p_dia)::int), (extract(doy from p_dia) - 1) / 365.0);
$$;

-- ---------------------------------------------------------------- Gerador
create or replace function public.gerar_historico_exemplo(p_de date, p_detalhe_desde date default '2016-01-01')
returns text
language plpgsql
security definer
set search_path = public
set statement_timeout = 0
set client_min_messages = warning
as $$
declare
    v_hoje date := public.hoje_local();
    v_limite date;
    v_ate date;
    v_detalhe boolean;
    v_preco_limite numeric;      -- fator de preço do 011 no primeiro dia dele
    v_gerente record;
    v_base bigint;
    v_vendas integer;
    v_itens integer;
begin
    if auth.uid() is not null then
        raise exception 'Rode pelo SQL Editor do Supabase.';
    end if;

    -- Limite = primeiro dia do 011 (guardado na primeira vez, porque depois as vendas antigas mudam o min)
    select limite into v_limite from public.dados_exemplo_historico;
    if v_limite is null then
        select min(dia) into v_limite from public.vendas where situacao = 'paga';
        if v_limite is null or (select count(*) from public.vendas) < 1000 then
            raise exception 'Rode antes o 011 (select public.gerar_dados_exemplo();) e o 013.';
        end if;
        insert into public.dados_exemplo_historico (limite) values (v_limite);
    end if;
    if p_de < '2000-01-01' or p_de >= v_limite then
        raise exception 'Use uma data entre 01/01/2000 e %.', to_char(v_limite - 1, 'DD/MM/YYYY');
    end if;
    v_detalhe := p_de >= p_detalhe_desde;
    -- Vai até o dia anterior à próxima parte já gerada (ou ao 011)
    if exists (select 1 from public.dados_exemplo_partes where p_de between de and ate) then
        return 'Nada a gerar: esse período já existe.';
    end if;
    select least(v_limite, coalesce((select min(de) from public.dados_exemplo_partes where de > p_de), v_limite)) - 1 into v_ate;
    if not v_detalhe then
        v_ate := least(v_ate, p_detalhe_desde - 1);
    end if;
    if v_ate < p_de then
        return 'Nada a gerar: esse período já existe.';
    end if;
    select id, nome into v_gerente from public.perfis where perfil = 'gerente' and ativo order by criado_em limit 1;
    v_preco_limite := 1 / power(1.055, (v_hoje - v_limite) / 365.0);
    perform setseed(0.26 + extract(year from p_de) / 100000.0);

    -- ------------------------------------------------------------ Pesos dos produtos (uma vez): itens por venda
    if not exists (select 1 from public.dados_exemplo_pesos) then
        insert into public.dados_exemplo_pesos (produto_id, peso)
        select p.id, coalesce(x.n, 0)::numeric / greatest((select count(*) from public.vendas v
                                                          where v.situacao = 'paga' and v.dia between v_limite and v_limite + 364
                                                            and not exists (select 1 from public.eventos_calendario e where e.dia = v.dia)), 1)
        from public.produtos p
        left join (select i.produto_id, count(*) as n from public.venda_itens i join public.vendas v on v.id = i.venda_id
                   where v.situacao = 'paga' and v.dia between v_limite and v_limite + 364
                     and not exists (select 1 from public.eventos_calendario e where e.dia = v.dia)
                   group by i.produto_id) x on x.produto_id = p.id;
    end if;

    -- ------------------------------------------------------------ Instabilidades escondidas (uma vez, de 2000 ao limite)
    if not exists (select 1 from public.dados_exemplo_instabilidades) then
        perform setseed(0.2026);
        -- Fatos da loja (datas fixas)
        insert into public.dados_exemplo_instabilidades (tipo, inicio, fim, produto_id, categorias, fator, descricao) values
            ('loja_fechada', '2005-02-14', '2005-02-18', null, null, 0, 'Reforma da loja: fechada por 5 dias'),
            ('dia_ruim', '2009-11-10', '2009-11-10', null, null, 0.30, 'Apagão em vários estados à noite'),
            ('mudanca_nivel', '2011-03-01', '2011-11-30', null, null, 0.88, 'Concorrente abriu na rua de cima (movimento volta aos poucos)'),
            ('dia_ruim', '2013-07-09', '2013-07-09', null, null, 0.50, 'Assalto: loja fechou à tarde'),
            ('mudanca_nivel', '2017-08-07', '2017-09-15', null, null, 0.78, 'Obra na rua: calçada fechada'),
            ('preco', '2020-09-01', '2021-03-31', null, 'Grãos', 0.80, 'Arroz e feijão caros: compras menores'),
            ('preco', '2019-11-01', '2021-11-30', null, 'Açougue', 0.88, 'Carne bovina cara: menos açougue');
        -- Produtos que chegaram depois (não vendem antes)
        insert into public.dados_exemplo_instabilidades (tipo, inicio, fim, produto_id, fator, descricao)
        select 'lancamento', '2000-01-01', x.desde::date - 1, p.id, 0, 'Produto ainda não vendido na loja: ' || p.nome
        from (values ('Energético 250ml', '2003-06-01'), ('Azeite extravirgem 500ml', '2004-05-01'), ('Cerveja long neck 330ml', '2006-03-01'),
                     ('Água de coco 1L', '2008-01-01'), ('Chá gelado limão 1,5L', '2010-09-01'), ('Grão-de-bico 500g', '2012-04-01'),
                     ('Sabão líquido 3L', '2014-02-01'), ('Suco de uva integral 1,5L', '2015-07-01')) as x(nome, desde)
        join public.produtos p on p.nome = x.nome;
        -- Produto em falta: cada produto comum, 0 a 2 vezes por ano, por 3 a 12 dias
        insert into public.dados_exemplo_instabilidades (tipo, inicio, fim, produto_id, fator, descricao)
        select 'falta_produto', i.d, i.d + i.dur, p.id, 0, 'Faltou no estoque: ' || p.nome
        from public.produtos p
        join public.dados_exemplo_pesos w on w.produto_id = p.id and w.peso > 0.002
        cross join generate_series(2000, extract(year from v_limite)::int) a
        cross join lateral generate_series(1, 2) k
        cross join lateral (select (make_date(a, 1, 1) + floor(random() * 360 + p.id * 0 + k * 0)::int) as d, 3 + floor(random() * 10 + p.id * 0)::int as dur,
                                   random() + p.id * 0 + k * 0 as s) i
        where i.s < 0.45 and i.d < v_limite - 15;
        -- Dias ruins (6 a 9 por ano) e picos locais (3 a 4 por ano)
        insert into public.dados_exemplo_instabilidades (tipo, inicio, fim, fator, descricao)
        select 'dia_ruim', d, d, round((0.4 + random() * 0.35)::numeric, 2),
               (array['Chuva forte', 'Queda de energia', 'Rua alagada', 'Temporal no fim da tarde'])[1 + floor(random() * 4)::int]
        from generate_series(2000, extract(year from v_limite)::int) a
        cross join lateral generate_series(1, 6 + floor(random() * 4)::int) k
        cross join lateral (select make_date(a, 1, 1) + floor(random() * 364 + k * 0)::int as d) x
        where d < v_limite;
        insert into public.dados_exemplo_instabilidades (tipo, inicio, fim, categorias, fator, descricao)
        select 'pico_local', d, d, 'Bebidas, Açougue', round((1.2 + random() * 0.3)::numeric, 2),
               (array['Festa no bairro', 'Jogo no estádio perto', 'Quermesse da igreja', 'Feira de rua'])[1 + floor(random() * 4)::int]
        from generate_series(2000, extract(year from v_limite)::int) a
        cross join lateral generate_series(1, 3 + floor(random() * 2)::int) k
        cross join lateral (select make_date(a, 1, 1) + floor(random() * 364 + k * 0)::int as d) x
        where d < v_limite;
        -- Compras grandes (festa, obra, igreja): uma por mês, em média
        insert into public.dados_exemplo_instabilidades (tipo, inicio, fim, fator, descricao)
        select 'compra_grande', d, d, 1, (array['Compra para festa de aniversário', 'Compra para a obra do vizinho', 'Compra da igreja para o bazar',
                                                   'Compra para churrasco da firma'])[1 + floor(random() * 4)::int]
        from generate_series('2000-01-01'::date, v_limite - 1, interval '1 day') s(d)
        where random() < 1 / 30.0;
        perform setseed(0.26 + extract(year from p_de) / 100000.0);
    end if;

    -- ------------------------------------------------------------ Dias: movimento esperado
    -- Nível da loja num dia comum (vendas por dia), ligando os pontos; termina nas 45 do 011 no limite
    create temp table t_nivel on commit drop as
    select * from (values ('2000-01-01'::date, 19.0), ('2002-06-30', 21.0), ('2004-01-01', 23.0), ('2006-01-01', 26.0), ('2008-09-01', 30.0),
                          ('2009-06-01', 28.5), ('2011-01-01', 32.0), ('2013-01-01', 35.0), ('2014-09-01', 37.0), ('2015-06-01', 35.5),
                          ('2016-09-01', 33.5), ('2017-12-01', 35.0), ('2019-12-01', 40.0)) as n(dia, nivel)
    union all select v_limite, 45.0;

    create temp table t_dias on commit drop as
    with d as (select s::date as dia from generate_series(p_de, v_ate, interval '1 day') s),
         ev as (select distinct on (dia) dia, nome, fator, categorias, produtos from public.eventos_calendario
                where tipo <> 'anomalia' and dia between p_de and v_ate order by dia, fator desc, nome),
         an as (select distinct on (dia) dia, nome, fator, categorias, produtos from public.eventos_calendario
                where tipo = 'anomalia' and dia between p_de and v_ate order by dia, fator desc, nome),
         inst as (select d.dia, exp(sum(ln(greatest(case when i.tipo = 'mudanca_nivel'
                                                      then i.fator + (1 - i.fator) * (d.dia - i.inicio) / greatest(i.fim - i.inicio, 1)
                                                      else i.fator end, 0.0001)))) as f,
                         bool_or(i.tipo = 'loja_fechada') as fechada
                  from d join public.dados_exemplo_instabilidades i on d.dia between i.inicio and i.fim
                  where i.tipo in ('dia_ruim', 'pico_local', 'mudanca_nivel', 'loja_fechada') group by d.dia)
    select d.dia, extract(year from d.dia)::int as ano,
           (select n1.nivel + (n2.nivel - n1.nivel) * (d.dia - n1.dia) / greatest(n2.dia - n1.dia, 1)
            from t_nivel n1, t_nivel n2
            where n1.dia = (select max(dia) from t_nivel where dia <= d.dia) and n2.dia = (select min(dia) from t_nivel where dia > d.dia)) as nivel,
           ev.nome as evento, ev.fator as ev_fator, ev.categorias as ev_cats, ev.produtos as ev_prods,
           an.nome as anomalia, an.fator as an_fator, an.categorias as an_cats, an.produtos as an_prods,
           coalesce(inst.f, 1) as f_inst,
           (to_char(d.dia, 'MM-DD') in ('12-25', '01-01') or coalesce(inst.fechada, false)) as fechado
    from d
    left join ev on ev.dia = d.dia
    left join an on an.dia = d.dia
    left join inst on inst.dia = d.dia;
    alter table t_dias add column n_vendas integer, add column f_cesta numeric default 1, add column f_preco numeric;
    -- Preço do dia = preço do 011 no limite, corrigido para trás pelo IPCA
    update t_dias set f_preco = v_preco_limite * public.indice_precos(dia) / public.indice_precos(v_limite);
    -- Anomalias: a quarentena é menos visitas e compras maiores; a corrida é muita gente comprando muito
    update t_dias set f_cesta = case when anomalia like 'Pandemia: corrida%' then 1.6 when anomalia = 'Pandemia: quarentena' then 1.3
                                     when anomalia = 'Pandemia: segunda onda' then 1.15 when anomalia like 'Greve%corrida%' then 1.3 else 1 end;
    update t_dias set n_vendas = case when fechado then 0 else greatest(3, round(nivel
        * (array[0.75, 0.85, 0.9, 0.95, 1.0, 1.25, 1.35])[extract(dow from dia)::int + 1]
        * (array[0.9, 0.88, 0.95, 0.97, 1.0, 0.98, 1.02, 1.0, 0.97, 1.0, 1.05, 1.3])[extract(month from dia)::int]
        * case when anomalia like 'Pandemia: corrida%' then 1.25 when anomalia = 'Pandemia: quarentena' then 0.9
               when anomalia = 'Pandemia: segunda onda' then 0.95 when anomalia like 'Greve%corrida%' then 1.1
               when anomalia like 'Greve%falta%' then 0.85 else 1 end
        * f_inst
        * (0.85 + random() * 0.3)))::int end;

    -- ------------------------------------------------------------ Peso de cada produto em cada dia
    create temp table t_prod on commit drop as
    select p.id, p.nome, p.categoria, p.preco, p.unidade_medida as unidade, p.preco_custo, w.peso
    from public.produtos p join public.dados_exemplo_pesos w on w.produto_id = p.id;

    -- Instabilidades por produto ou categoria (falta, lançamento, preço caro, pico local), dia a dia
    create temp table t_inst_pd on commit drop as
    select g.d::date as dia, i.produto_id, i.categorias, i.fator
    from public.dados_exemplo_instabilidades i
    cross join lateral generate_series(greatest(i.inicio, p_de), least(i.fim, v_ate), interval '1 day') g(d)
    where i.tipo in ('falta_produto', 'lancamento', 'preco', 'pico_local') and i.fim >= p_de and i.inicio <= v_ate
      and (i.produto_id is not null or i.categorias is not null);
    create temp table t_mult on commit drop as
    select x.dia, p.id as produto_id, exp(sum(ln(greatest(x.fator, 0.0001)))) as f
    from t_inst_pd x
    join t_prod p on x.produto_id = p.id
                  or (x.produto_id is null and exists (select 1 from unnest(string_to_array(x.categorias, ',')) c where trim(c) = p.categoria))
    group by x.dia, p.id;
    create index on t_mult (dia, produto_id);

    create temp table t_peso_dia on commit drop as
    select d.dia, p.id as produto_id,
           p.peso * coalesce(m.f, 1)
           -- anomalia por categoria/produto (o calendário diz quais)
           * case when d.anomalia is null then 1
                  when exists (select 1 from unnest(string_to_array(coalesce(d.an_prods, ''), ',')) x where trim(x) = p.nome) then d.an_fator * 1.6
                  when exists (select 1 from unnest(string_to_array(coalesce(d.an_cats, ''), ',')) c where trim(c) = p.categoria)
                       then case when d.an_fator < 1 then 0.35 else d.an_fator end
                  else 1 end as peso
    from t_dias d cross join t_prod p
    left join t_mult m on m.dia = d.dia and m.produto_id = p.id
    where not d.fechado;
    -- Sem ln(0): quem tem fator 0 fica com peso quase zero; zera de vez
    update t_peso_dia set peso = 0 where peso < 0.00001;
    create temp table t_faixa on commit drop as
    select dia, produto_id, sum(peso) over (partition by dia order by produto_id) - peso as ini,
           sum(peso) over (partition by dia order by produto_id) as fim
    from t_peso_dia where peso > 0;
    create index on t_faixa (dia, ini);
    create temp table t_total_dia on commit drop as select dia, max(fim) as total from t_faixa group by dia;

    -- ------------------------------------------------------------ Vendas comuns (mesma receita do 011)
    select coalesce(max(id), 0) into v_base from public.vendas;
    create temp table t_vendas on commit drop as
    select v_base + row_number() over (order by d.dia, s.i) as id, d.dia,
           (d.dia + (case when r < 0.35 then 9.5 + random() * 3 when r < 0.8 then 16 + random() * 4 else 7.5 + random() * 13 end) * interval '1 hour')
             at time zone 'America/Sao_Paulo' as paga_em,
           greatest(1, round((1 + floor(power(random(), 1.7) * 9)) * d.f_cesta))::int as n_itens,
           random() as sorteio_forma, random() as sorteio_extra, 'comum'::text as tipo
    from t_dias d
    cross join lateral generate_series(1, d.n_vendas) s(i)
    cross join lateral (select random() as r) rr
    where d.n_vendas > 0;

    create temp table t_picks on commit drop as
    select v.id as venda_id, v.dia, random() * t.total as r
    from t_vendas v join t_total_dia t on t.dia = v.dia
    cross join lateral generate_series(1, v.n_itens) j;
    create temp table t_itens on commit drop as
    select k.venda_id, k.dia, f.produto_id
    from t_picks k
    cross join lateral (select produto_id from t_faixa f where f.dia = k.dia and f.ini <= k.r order by f.ini desc limit 1) f;

    -- ------------------------------------------------------------ Vendas a mais nas datas especiais (mesma receita do 013)
    create temp table t_cats on commit drop as
    select distinct d.dia, trim(c) as categoria from t_dias d cross join unnest(string_to_array(d.ev_cats, ',')) c
    where d.ev_fator > 1 and trim(c) <> '' and not d.fechado;
    create temp table t_tipicos on commit drop as
    select distinct d.dia, p.id as produto_id from t_dias d cross join unnest(string_to_array(d.ev_prods, ',')) n
    join t_prod p on lower(p.nome) = lower(trim(n))
    where d.ev_fator > 1 and not d.fechado;
    select coalesce(max(id), 0) into v_base from t_vendas;
    insert into t_vendas (id, dia, paga_em, n_itens, sorteio_forma, sorteio_extra, tipo)
    select v_base + row_number() over (order by d.dia, s.i), d.dia,
           (d.dia + (case when r < 0.45 then 9 + random() * 3.5 else 15 + random() * 5.5 end) * interval '1 hour') at time zone 'America/Sao_Paulo',
           (2 + floor(power(random(), 1.4) * 10))::int, random(), random(), 'data'
    from t_dias d
    cross join lateral generate_series(1, greatest(1, round(d.n_vendas * (d.ev_fator - 1))::int)) s(i)
    cross join lateral (select random() as r) rr
    where d.ev_fator > 1 and d.n_vendas > 0;
    insert into t_itens (venda_id, dia, produto_id)
    select v.id, v.dia, escolhido.id
    from t_vendas v
    cross join lateral generate_series(1, v.n_itens) j
    cross join lateral (select random() + j * 0 + v.id * 0 as r) rr
    join lateral (
        select x.id from (
            select t.produto_id as id from t_tipicos t where t.dia = v.dia and rr.r < 0.5
            union all
            select p2.id from t_prod p2 join t_cats c on c.dia = v.dia and c.categoria = p2.categoria where rr.r >= 0.5 and rr.r < 0.83
            union all
            select p3.id from t_prod p3 where rr.r >= 0.83 or (rr.r < 0.5 and not exists (select 1 from t_tipicos t2 where t2.dia = v.dia))
        ) x order by random() limit 1) escolhido on true
    where v.tipo = 'data';
    -- Produto em falta ou ainda não lançado não sai nem na data especial
    delete from t_itens i using t_peso_dia w where w.dia = i.dia and w.produto_id = i.produto_id and w.peso = 0;

    -- ------------------------------------------------------------ Compras grandes (gabarito): uma venda com muita coisa
    select coalesce(max(id), 0) into v_base from t_vendas;
    insert into t_vendas (id, dia, paga_em, n_itens, sorteio_forma, sorteio_extra, tipo)
    select v_base + row_number() over (order by i.inicio, i.id), i.inicio,
           (i.inicio + time '10:30') at time zone 'America/Sao_Paulo', 3, random(), 0.7, 'grande'
    from public.dados_exemplo_instabilidades i
    join t_dias d on d.dia = i.inicio and d.n_vendas > 0
    where i.tipo = 'compra_grande';

    -- Quantidade e preço
    create temp table t_qtd on commit drop as
    select i.venda_id, i.dia, i.produto_id, p.nome, p.unidade,
           sum(case when v.tipo = 'data' then
                         case when p.unidade = 'kg' then round((0.6 + random() * 2.4)::numeric, 3)
                              else (case when random() < 0.55 then 1 when random() < 0.6 then 2 else 3 + floor(random() * 10) end)::numeric end
                    else case when p.unidade = 'kg' then round((0.25 + random() * 1.6)::numeric, 3)
                              else (case when random() < 0.72 then 1 when random() < 0.75 then 2 else 3 + floor(random() * 4) end)::numeric end
               end) as quantidade,
           round(p.preco * d.f_preco, 2) as preco_unitario,
           round(coalesce(p.preco_custo, p.preco * 0.64) * d.f_preco, 2) as custo
    from t_itens i join t_vendas v on v.id = i.venda_id join t_prod p on p.id = i.produto_id join t_dias d on d.dia = i.dia
    group by i.venda_id, i.dia, i.produto_id, p.nome, p.unidade, p.preco, p.preco_custo, d.f_preco;
    insert into t_qtd (venda_id, dia, produto_id, nome, unidade, quantidade, preco_unitario, custo)
    select v.id, v.dia, p.id, p.nome, p.unidade, g.q,
           round(p.preco * d.f_preco, 2), round(coalesce(p.preco_custo, p.preco * 0.64) * d.f_preco, 2)
    from t_vendas v
    join t_dias d on d.dia = v.dia
    cross join lateral (values ('Cerveja lata 350ml', 24 + 12 * floor(random() * 3)), ('Refrigerante cola 2L', 6 + floor(random() * 7)),
                               ('Carvão vegetal 3kg', 2 + floor(random() * 4))) as g(nome, q)
    join t_prod p on p.nome = g.nome
    where v.tipo = 'grande'
      and not exists (select 1 from t_peso_dia w where w.dia = v.dia and w.produto_id = p.id and w.peso = 0);
    alter table t_qtd add column subtotal numeric, add column desconto_promocao numeric default 0, add column promocao text;
    update t_qtd set subtotal = round(quantidade * preco_unitario, 2);
    delete from t_vendas v where not exists (select 1 from t_qtd q where q.venda_id = v.id);

    insert into public.dados_exemplo_partes (de, ate) values (p_de, v_ate);

    -- ------------------------------------------------------------ Gravar: resumo (antes de p_detalhe_desde)
    if not v_detalhe then
        insert into public.venda_itens_resumo_dia (dia, produto_id, quantidade, valor)
        select dia, produto_id, sum(quantidade), sum(subtotal) from t_qtd group by dia, produto_id
        on conflict do nothing;
        insert into public.vendas_resumo_dia (dia, vendas, total, itens)
        select v.dia, count(distinct v.id), coalesce(sum(q.subtotal), 0), coalesce(sum(q.quantidade), 0)
        from t_vendas v join t_qtd q on q.venda_id = v.id group by v.dia
        on conflict do nothing;
        -- Resumo das Análises desses dias (formas de pagamento da época: dinheiro e cartão; sem PIX)
        create temp table t_cache_cat on commit drop as
        select q.dia, jsonb_object_agg(k.categoria, k.valor) as categorias
        from (select q.dia, p.categoria, sum(q.subtotal) as valor from t_qtd q join t_prod p on p.id = q.produto_id group by 1, 2) k
        join (select distinct dia from t_qtd) q on q.dia = k.dia group by q.dia;
        create temp table t_cache_prod on commit drop as
        select dia, jsonb_agg(jsonb_build_object('nome', nome, 'unidade', unidade, 'quantidade', quantidade, 'valor', valor)) as produtos,
               sum(custo) as custo
        from (select dia, nome, unidade, sum(quantidade) as quantidade, sum(subtotal) as valor, sum(quantidade * custo) as custo
              from t_qtd group by 1, 2, 3) m group by dia;
        insert into public.analise_cache_dia (dia, total, vendas, descontos, promocoes, custo, vendido_com_custo, formas, categorias, produtos)
        select r.dia, r.total, r.vendas, 0, 0, coalesce(cp.custo, 0), r.total,
               jsonb_build_object('dinheiro', f.dinheiro, 'debito', f.debito, 'credito', f.credito,
                                  'vale_alimentacao', r.total - f.dinheiro - f.debito - f.credito),
               coalesce(cc.categorias, '{}'::jsonb), coalesce(cp.produtos, '[]'::jsonb)
        from (select dia, extract(year from dia)::int as ano, total, vendas from public.vendas_resumo_dia where dia between p_de and v_ate) r
        cross join lateral (select round(r.total * (0.62 - (r.ano - 2000) * 0.015), 2) as dinheiro,
                                   round(r.total * (0.18 + (r.ano - 2000) * 0.008), 2) as debito,
                                   round(r.total * (0.14 + (r.ano - 2000) * 0.006), 2) as credito) f
        left join t_cache_cat cc on cc.dia = r.dia
        left join t_cache_prod cp on cp.dia = r.dia
        on conflict (dia) do nothing;
        select count(*), coalesce(sum(vendas), 0) into v_itens, v_vendas from public.vendas_resumo_dia where dia between p_de and v_ate;
        return format('Pronto: resumo de %s a %s, %s dias abertos, %s vendas (agrupadas por produto e dia).',
                      to_char(p_de, 'DD/MM/YYYY'), to_char(v_ate, 'DD/MM/YYYY'), v_itens, v_vendas);
    end if;

    -- ------------------------------------------------------------ Gravar: venda por venda (de p_detalhe_desde em diante)
    -- Promoções (6% dos itens), como no 011
    update t_qtd set desconto_promocao = round(subtotal * (0.1 + random() * 0.2)::numeric, 2),
                     promocao = case when random() < 0.5 then 'Promoção' else 'Validade −' || (10 + floor(random() * 4) * 5) || '%' end
    where random() < 0.06;

    -- Caixas: 2 por dia (3 na sexta e no sábado), revezando entre todos os usuários
    create temp table t_pessoas on commit drop as
    select id, nome, row_number() over (order by perfil desc, nome) - 1 as n from public.perfis;
    select coalesce(max(id), 0) into v_base from public.caixas;
    create temp table t_caixas on commit drop as
    select v_base + row_number() over (order by d.dia, k) as id, d.dia, k, pe.id as operador_id, pe.nome,
           (d.dia + time '07:00' + (random() * interval '20 minutes')) at time zone 'America/Sao_Paulo' as aberto_em,
           (array[100, 150, 150, 200])[1 + floor(random() * 4)::int]::numeric as troco
    from t_dias d
    cross join generate_series(0, 2) k
    join t_pessoas pe on pe.n = (k + extract(doy from d.dia)::int) % (select count(*) from t_pessoas)
    where d.n_vendas > 0 and k < case when extract(dow from d.dia) in (5, 6) then 3 else 2 end;
    delete from t_caixas c where exists (select 1 from public.caixas x where x.operador_id = c.operador_id and x.dia = c.dia);
    create temp table t_caixas_dia on commit drop as
    select dia, array_agg(id order by k) as ids from t_caixas group by dia;

    alter table t_vendas add column caixa_id bigint, add column total numeric, add column desconto numeric default 0, add column forma text,
        add column situacao text default 'paga', add column cpf text, add column operador_id uuid, add column operador_nome text;
    update t_vendas v set caixa_id = cd.ids[1 + (v.id % array_length(cd.ids, 1))] from t_caixas_dia cd where cd.dia = v.dia;
    update t_vendas v set total = x.soma from (select venda_id, sum(subtotal - desconto_promocao) as soma from t_qtd group by venda_id) x where x.venda_id = v.id;
    update t_vendas set desconto = round(total * 0.05, 2) where sorteio_extra < 0.03 and total > 30;
    update t_vendas set total = total - desconto;
    update t_vendas set cpf = (array['52601815906', '08301661305', '18609139034', '99603082430', '62819482112', '99351819019',
                                     '93786579741', '54323194897', '75749118606', '25276018987', '55597971115', '47104974601'])[1 + floor(random() * 12)::int]
    where sorteio_extra between 0.5 and 0.6;
    update t_vendas set situacao = 'cancelada' where sorteio_extra > 0.996;
    -- Formas da época: PIX só a partir de 16/11/2020 (e cresce aos poucos)
    update t_vendas set forma = case
        when sorteio_forma < 0.06 and total > 40 then 'varias'
        when dia >= '2020-11-16' and sorteio_forma < 0.06 + 0.35 * least(1, (dia - '2020-11-16'::date) / 365.0) then 'pix'
        when sorteio_forma < 0.50 then 'dinheiro'
        when sorteio_forma < 0.75 then 'debito'
        when sorteio_forma < 0.94 then 'credito'
        when sorteio_forma < 0.985 then 'vale_alimentacao'
        else 'vale_refeicao' end;
    update t_vendas v set operador_id = c.operador_id, operador_nome = c.nome from t_caixas c where c.id = v.caixa_id;

    insert into public.caixas (id, operador_id, operador_nome, dia, aberto_em, troco_inicial)
    overriding system value
    select id, operador_id, nome, dia, aberto_em, troco from t_caixas;
    insert into public.vendas (id, caixa_id, operador_id, operador_nome, dia, situacao, forma_pagamento, total, criada_em, paga_em,
                               cancelada_em, cancelada_por, cpf_nota, desconto, desconto_autorizado_por)
    overriding system value
    select id, caixa_id, operador_id, operador_nome, dia, situacao, forma, total, paga_em - interval '3 minutes', paga_em,
           case when situacao = 'cancelada' then paga_em + interval '20 minutes' end,
           case when situacao = 'cancelada' then v_gerente.nome end,
           cpf, desconto, case when desconto > 0 then v_gerente.nome end
    from t_vendas;
    select coalesce(max(id), 0) into v_base from public.venda_itens;
    insert into public.venda_itens (id, venda_id, produto_id, nome_produto, unidade_medida, quantidade, preco_unitario, subtotal,
                                    subtotal_normal, desconto_promocao, promocao)
    overriding system value
    select v_base + row_number() over (order by venda_id, produto_id), venda_id, produto_id, nome, unidade, quantidade, preco_unitario,
           subtotal - desconto_promocao, case when desconto_promocao > 0 then subtotal end, desconto_promocao, promocao
    from t_qtd;
    insert into public.venda_pagamentos (venda_id, forma, valor, recebido, troco, bandeira, nsu, autorizacao, parcelas, final_cartao, criado_em)
    select v.id, f.forma, f.valor,
           case when f.forma = 'dinheiro' then f.recebido end,
           case when f.forma = 'dinheiro' then f.recebido - f.valor else 0 end,
           case when f.forma not in ('dinheiro', 'pix') then (array['Visa', 'Mastercard', 'Elo', 'Hipercard'])[1 + floor(random() * 4)::int] end,
           case when f.forma <> 'dinheiro' then lpad((floor(random() * 999999))::text, 6, '0') end,
           case when f.forma not in ('dinheiro', 'pix') then upper(substr(md5(random()::text), 1, 6)) end,
           case when f.forma = 'credito' then case when v.total > 200 and random() < 0.4 then 2 + floor(random() * 4)::int else 1 end end,
           case when f.forma not in ('dinheiro', 'pix') then lpad((floor(random() * 9999))::text, 4, '0') end,
           v.paga_em
    from t_vendas v
    cross join lateral (
        select x.forma, x.valor,
               case when x.valor <= 10 then 10 when x.valor <= 20 then 20 when x.valor <= 50 then 50 when x.valor <= 100 then 100
                    else ceil(x.valor / 50) * 50 end::numeric as recebido
        from (select v.forma as forma, v.total as valor where v.forma <> 'varias'
              union all select 'dinheiro', least(50, floor(v.total / 2)) where v.forma = 'varias'
              union all select 'credito', v.total - least(50, floor(v.total / 2)) where v.forma = 'varias') x
        where x.valor > 0) f
    where v.total > 0;
    update public.vendas v set recebido = p.recebido, troco = p.troco
    from public.venda_pagamentos p where p.venda_id = v.id and p.forma = 'dinheiro' and v.forma_pagamento = 'dinheiro' and v.dia between p_de and v_ate;

    -- Fechamento de cada caixa (com sobra ou falta de vez em quando)
    update public.caixas c set
        fechado_em = x.ultima + (10 + random() * 25) * interval '1 minute',
        fechado_esperado = public.dinheiro_na_gaveta(c.id),
        fechado_por = c.operador_nome
    from (select caixa_id, max(paga_em) as ultima from public.vendas where dia between p_de and v_ate group by caixa_id) x
    where x.caixa_id = c.id and c.id in (select id from t_caixas);
    update public.caixas c set fechado_em = (c.dia + time '20:00') at time zone 'America/Sao_Paulo', fechado_esperado = c.troco_inicial,
        fechado_por = c.operador_nome
    where c.id in (select id from t_caixas) and c.fechado_em is null;
    update public.caixas c set
        fechado_diferenca = d.dif,
        fechado_contado = c.fechado_esperado + d.dif,
        fechado_obs = case when d.dif <> 0 then (array['Troco errado no fim da tarde', 'Moedas a mais', 'Faltou conferir uma sangria',
                                                       'Cliente pagou a mais e não quis o troco', 'Nota de R$ 10 a menos'])[1 + floor(random() * 5)::int] end
    from (select id, case when random() < 0.86 then 0 else round(((random() - 0.6) * 30)::numeric, 2) end as dif from t_caixas) d
    where d.id = c.id;

    perform setval(pg_get_serial_sequence('public.vendas', 'id'), (select max(id) from public.vendas));
    perform setval(pg_get_serial_sequence('public.venda_itens', 'id'), (select max(id) from public.venda_itens));
    perform setval(pg_get_serial_sequence('public.caixas', 'id'), (select max(id) from public.caixas));

    select count(*) into v_vendas from t_vendas;
    select count(*) into v_itens from t_qtd;
    return format('Pronto: venda por venda de %s a %s, %s vendas e %s itens. Depois rode: select public.preencher_analise_cache();',
                  to_char(p_de, 'DD/MM/YYYY'), to_char(v_ate, 'DD/MM/YYYY'), v_vendas, v_itens);
end;
$$;

-- ---------------------------------------------------------------- Limpar: agora também apaga o histórico, a previsão e o cache
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
             public.vendas_resumo_dia, public.venda_itens_resumo_dia, public.dados_exemplo_instabilidades,
             public.dados_exemplo_historico, public.dados_exemplo_partes, public.dados_exemplo_pesos, public.dados_exemplo_execucoes, public.analise_cache_dia
             restart identity cascade;
    -- Previsão (se o 016 já rodou) e os períodos fora do normal inventados para o teste
    if to_regclass('public.previsao_geral') is not null then
        truncate public.previsao_geral, public.previsao_modelos, public.previsao_eventos, public.previsao_dias, public.previsao_teste_semanas,
                 public.previsao_teste_anos;
    end if;
    delete from public.eventos_calendario where tipo = 'anomalia';
    return 'Tudo apagado. Os usuários e o calendário de feriados continuam. Anexos de e-mail já baixados ficam no Storage (apague pelo painel, em Storage > anexos-fornecedores).';
end;
$$;

revoke execute on function public.gerar_historico_exemplo(date, date) from public, anon, authenticated;
revoke execute on function public.limpar_dados_exemplo(text) from public, anon, authenticated;
