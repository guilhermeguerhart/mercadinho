-- =====================================================================
-- Dados de exemplo para testar o sistema (pedido do Guilherme em 03/11): enche todas as tabelas com um mercado
-- fictício e 5 anos de vendas, caixas e fechamentos do dia.
--
-- NÃO É PARA O USO REAL. Antes de usar o sistema de verdade, rode:  select public.limpar_dados_exemplo('APAGAR TUDO');
--
-- Como usar (SQL Editor do Supabase, depois do 001 a 009 e do 002_usuarios_teste.sql):
--   1. Rodar este arquivo inteiro (só cria as funções).
--   2. select public.gerar_dados_exemplo();          -- produtos, fornecedores, pedidos, 5 anos de vendas e caixas
--   3. Fechamentos do dia, um ano por vez (cada um leva alguns segundos):
--        select public.gerar_fechamentos_exemplo(2021);  ... até o ano atual
--      ou tudo de uma vez: select public.gerar_fechamentos_exemplo(null);
-- Parâmetros: gerar_dados_exemplo(anos, vendas_por_dia). Padrão 5 anos e ~45 vendas num dia comum
-- (dá umas 85 mil vendas e 300 mil itens; cabe no plano grátis do Supabase).
-- Pode rodar de novo: as funções recusam gerar vendas em dias que já têm vendas.
-- =====================================================================

create or replace function public.gerar_dados_exemplo(p_anos integer default 5, p_vendas_dia integer default 45)
returns text
language plpgsql
security definer
set search_path = public
set statement_timeout = 0
as $$
declare
    v_hoje date := public.hoje_local();
    v_inicio date := (public.hoje_local() - make_interval(years => p_anos))::date;
    v_fim date := public.hoje_local() - 1;
    v_gerente record;
    v_operadores integer;
    v_base bigint;
    v_vendas integer;
    v_itens integer;
begin
    if p_anos not between 1 and 10 or p_vendas_dia not between 5 and 300 then
        raise exception 'Use de 1 a 10 anos e de 5 a 300 vendas por dia.';
    end if;
    select id, nome into v_gerente from public.perfis where perfil = 'gerente' and ativo order by criado_em limit 1;
    select count(*) into v_operadores from public.perfis;
    if v_gerente.id is null or v_operadores < 2 then
        raise exception 'Rode antes o 002_usuarios_teste.sql (precisa de um gerente e de operadores).';
    end if;
    if exists (select 1 from public.vendas where dia between v_inicio and v_fim) then
        raise exception 'Já existem vendas entre % e %. Para gerar de novo, rode antes: select public.limpar_dados_exemplo(''APAGAR TUDO'');',
            to_char(v_inicio, 'DD/MM/YYYY'), to_char(v_fim, 'DD/MM/YYYY');
    end if;
    perform setseed(0.42);

    -- ------------------------------------------------------------ Catálogo (o mesmo do leitor simulado, Alt+2 no Estoque)
    insert into public.produtos (nome, categoria, preco, unidades_por_caixa, dias_validade, unidade_medida, estoque_minimo, preco_custo)
    select c.nome, c.categoria, c.preco, c.caixa, c.dias, c.unidade,
           case when c.unidade = 'kg' then 5 else greatest(c.caixa, 6) end,
           round(c.preco * case when c.unidade = 'kg' then 0.70 else 0.64 end, 2)
    from (values
        ('Arroz branco 5kg', 'Grãos', 24.90, 6, 365, 'un'),
        ('Arroz integral 1kg', 'Grãos', 8.99, 10, 300, 'un'),
        ('Feijão carioca 1kg', 'Grãos', 8.50, 10, 240, 'un'),
        ('Feijão preto 1kg', 'Grãos', 9.20, 10, 240, 'un'),
        ('Lentilha 500g', 'Grãos', 7.90, 12, 365, 'un'),
        ('Grão-de-bico 500g', 'Grãos', 9.90, 12, 365, 'un'),
        ('Açúcar refinado 1kg', 'Mercearia', 4.79, 10, 720, 'un'),
        ('Açúcar cristal 5kg', 'Mercearia', 19.90, 4, 720, 'un'),
        ('Sal refinado 1kg', 'Mercearia', 2.49, 20, 1095, 'un'),
        ('Óleo de soja 900ml', 'Mercearia', 7.20, 20, 365, 'un'),
        ('Azeite extravirgem 500ml', 'Mercearia', 34.90, 12, 540, 'un'),
        ('Café torrado 500g', 'Mercearia', 18.90, 10, 270, 'un'),
        ('Café solúvel 200g', 'Mercearia', 16.50, 12, 540, 'un'),
        ('Farinha de trigo 1kg', 'Mercearia', 5.49, 10, 180, 'un'),
        ('Farinha de mandioca 500g', 'Mercearia', 6.20, 12, 240, 'un'),
        ('Macarrão espaguete 500g', 'Mercearia', 4.59, 20, 540, 'un'),
        ('Macarrão parafuso 500g', 'Mercearia', 4.79, 20, 540, 'un'),
        ('Molho de tomate 340g', 'Mercearia', 3.29, 24, 365, 'un'),
        ('Extrato de tomate 140g', 'Mercearia', 2.99, 24, 540, 'un'),
        ('Milho verde em lata 170g', 'Mercearia', 4.39, 24, 730, 'un'),
        ('Ervilha em lata 170g', 'Mercearia', 4.19, 24, 730, 'un'),
        ('Sardinha em lata 125g', 'Mercearia', 6.90, 50, 1095, 'un'),
        ('Atum ralado 170g', 'Mercearia', 9.49, 48, 1095, 'un'),
        ('Biscoito cream cracker 400g', 'Mercearia', 5.99, 20, 240, 'un'),
        ('Biscoito recheado 140g', 'Mercearia', 2.99, 48, 270, 'un'),
        ('Achocolatado em pó 400g', 'Mercearia', 8.79, 24, 365, 'un'),
        ('Fermento em pó 100g', 'Mercearia', 4.49, 24, 365, 'un'),
        ('Maionese 500g', 'Mercearia', 9.90, 12, 180, 'un'),
        ('Ketchup 400g', 'Mercearia', 8.49, 12, 300, 'un'),
        ('Vinagre de álcool 750ml', 'Mercearia', 3.19, 12, 1095, 'un'),
        ('Gelatina sabor morango 25g', 'Mercearia', 1.99, 72, 540, 'un'),
        ('Leite condensado 395g', 'Mercearia', 6.99, 27, 300, 'un'),
        ('Creme de leite 200g', 'Mercearia', 3.79, 27, 300, 'un'),
        ('Aveia em flocos 250g', 'Mercearia', 5.29, 24, 300, 'un'),
        ('Leite integral 1L', 'Laticínios', 5.80, 12, 120, 'un'),
        ('Leite desnatado 1L', 'Laticínios', 5.99, 12, 120, 'un'),
        ('Iogurte natural 170g', 'Laticínios', 3.49, 24, 30, 'un'),
        ('Iogurte de morango 900g', 'Laticínios', 11.90, 6, 35, 'un'),
        ('Queijo muçarela fatiado 150g', 'Laticínios', 9.90, 20, 45, 'un'),
        ('Requeijão cremoso 200g', 'Laticínios', 8.49, 24, 60, 'un'),
        ('Manteiga com sal 200g', 'Laticínios', 12.90, 20, 120, 'un'),
        ('Margarina 500g', 'Laticínios', 8.79, 12, 150, 'un'),
        ('Presunto fatiado 200g', 'Frios', 8.99, 20, 30, 'un'),
        ('Salsicha hot dog 500g', 'Frios', 9.49, 12, 60, 'un'),
        ('Mortadela fatiada 200g', 'Frios', 5.99, 20, 30, 'un'),
        ('Ovos brancos dúzia', 'Hortifrúti', 11.90, 30, 30, 'un'),
        ('Água mineral 500ml', 'Bebidas', 1.99, 12, 365, 'un'),
        ('Água mineral 1,5L', 'Bebidas', 3.49, 6, 365, 'un'),
        ('Refrigerante cola 2L', 'Bebidas', 9.99, 6, 180, 'un'),
        ('Refrigerante guaraná 2L', 'Bebidas', 8.49, 6, 180, 'un'),
        ('Refrigerante lata 350ml', 'Bebidas', 4.29, 12, 270, 'un'),
        ('Suco de laranja 1L', 'Bebidas', 7.99, 12, 240, 'un'),
        ('Suco de uva integral 1,5L', 'Bebidas', 17.90, 6, 365, 'un'),
        ('Cerveja lata 350ml', 'Bebidas', 3.99, 12, 180, 'un'),
        ('Cerveja long neck 330ml', 'Bebidas', 5.49, 24, 180, 'un'),
        ('Energético 250ml', 'Bebidas', 8.99, 24, 365, 'un'),
        ('Chá gelado limão 1,5L', 'Bebidas', 6.49, 6, 180, 'un'),
        ('Água de coco 1L', 'Bebidas', 9.90, 12, 240, 'un'),
        ('Sabão em pó 1,6kg', 'Limpeza', 21.90, 9, null, 'un'),
        ('Sabão líquido 3L', 'Limpeza', 34.90, 4, null, 'un'),
        ('Detergente líquido 500ml', 'Limpeza', 2.79, 24, null, 'un'),
        ('Água sanitária 2L', 'Limpeza', 6.49, 6, 180, 'un'),
        ('Desinfetante lavanda 2L', 'Limpeza', 9.99, 6, null, 'un'),
        ('Amaciante 2L', 'Limpeza', 14.90, 6, null, 'un'),
        ('Esponja multiuso (3 un.)', 'Limpeza', 4.99, 40, null, 'un'),
        ('Limpador multiuso 500ml', 'Limpeza', 5.49, 12, null, 'un'),
        ('Saco de lixo 50L (10 un.)', 'Limpeza', 7.90, 30, null, 'un'),
        ('Palha de aço (8 un.)', 'Limpeza', 3.49, 30, null, 'un'),
        ('Papel higiênico (12 rolos)', 'Higiene', 19.90, 4, null, 'un'),
        ('Creme dental 90g', 'Higiene', 4.99, 72, 730, 'un'),
        ('Sabonete em barra 85g', 'Higiene', 2.49, 72, 900, 'un'),
        ('Shampoo 350ml', 'Higiene', 14.90, 12, 900, 'un'),
        ('Condicionador 350ml', 'Higiene', 15.90, 12, 900, 'un'),
        ('Desodorante aerosol 150ml', 'Higiene', 13.90, 12, 730, 'un'),
        ('Escova de dentes', 'Higiene', 6.99, 72, null, 'un'),
        ('Absorvente (8 un.)', 'Higiene', 5.49, 48, 1095, 'un'),
        ('Fralda descartável M (30 un.)', 'Higiene', 49.90, 4, null, 'un'),
        ('Papel toalha (2 rolos)', 'Higiene', 6.99, 12, null, 'un'),
        ('Pão de forma 500g', 'Padaria', 8.49, 12, 10, 'un'),
        ('Bisnaguinha 300g', 'Padaria', 7.99, 12, 12, 'un'),
        ('Bolo pronto de laranja 300g', 'Padaria', 9.90, 12, 15, 'un'),
        ('Torrada 160g', 'Padaria', 5.79, 20, 180, 'un'),
        ('Pizza congelada 460g', 'Congelados', 18.90, 8, 120, 'un'),
        ('Lasanha congelada 600g', 'Congelados', 19.90, 8, 150, 'un'),
        ('Hambúrguer congelado (12 un.)', 'Congelados', 22.90, 8, 180, 'un'),
        ('Batata pré-frita 1kg', 'Congelados', 16.90, 10, 365, 'un'),
        ('Sorvete 2L', 'Congelados', 24.90, 6, 365, 'un'),
        ('Peito de frango congelado 1kg', 'Congelados', 17.90, 10, 365, 'un'),
        ('Picanha bovina', 'Açougue', 79.90, 10, 10, 'kg'),
        ('Alcatra bovina', 'Açougue', 49.90, 15, 10, 'kg'),
        ('Contrafilé bovino', 'Açougue', 54.90, 15, 10, 'kg'),
        ('Fraldinha bovina', 'Açougue', 44.90, 12, 10, 'kg'),
        ('Patinho bovino', 'Açougue', 42.90, 15, 10, 'kg'),
        ('Acém bovino', 'Açougue', 32.90, 20, 10, 'kg'),
        ('Costela bovina', 'Açougue', 29.90, 20, 10, 'kg'),
        ('Carne moída de primeira', 'Açougue', 39.90, 10, 3, 'kg'),
        ('Músculo bovino', 'Açougue', 34.90, 15, 10, 'kg'),
        ('Cupim bovino', 'Açougue', 42.90, 12, 10, 'kg'),
        ('Lombo suíno', 'Açougue', 26.90, 15, 10, 'kg'),
        ('Costelinha suína', 'Açougue', 27.90, 15, 10, 'kg'),
        ('Pernil suíno', 'Açougue', 21.90, 20, 10, 'kg'),
        ('Bisteca suína', 'Açougue', 22.90, 15, 8, 'kg'),
        ('Linguiça toscana', 'Açougue', 21.90, 10, 15, 'kg'),
        ('Linguiça calabresa', 'Açougue', 29.90, 10, 30, 'kg'),
        ('Frango inteiro', 'Açougue', 12.90, 20, 7, 'kg'),
        ('Coxa e sobrecoxa de frango', 'Açougue', 14.90, 20, 7, 'kg'),
        ('Filé de peito de frango', 'Açougue', 24.90, 15, 7, 'kg'),
        ('Asa de frango', 'Açougue', 18.90, 15, 7, 'kg'),
        ('Coração de frango', 'Açougue', 29.90, 5, 5, 'kg'),
        ('Filé de tilápia', 'Açougue', 54.90, 10, 5, 'kg'),
        ('Ração para cães 3kg', 'Pet', 39.90, 4, 365, 'un'),
        ('Ração para gatos 1kg', 'Pet', 19.90, 10, 365, 'un'),
        ('Areia sanitária 4kg', 'Pet', 14.90, 5, null, 'un')
    ) as c(nome, categoria, preco, caixa, dias, unidade)
    on conflict ((lower(trim(nome)))) do nothing;
    update public.produtos set preco_custo = round(preco * case when unidade_medida = 'kg' then 0.70 else 0.64 end, 2) where preco_custo is null;
    -- PLU da balança para os produtos por quilo que ainda não têm
    update public.produtos p set codigo_balanca = x.plu
    from (select id, 100 + row_number() over (order by nome) as plu from public.produtos where unidade_medida = 'kg' and codigo_balanca is null) x
    where p.id = x.id and not exists (select 1 from public.produtos o where o.codigo_balanca = x.plu);

    -- ------------------------------------------------------------ Fornecedores (os mesmos do Alt+2 em Fornecedores)
    insert into public.fornecedores (nome, telefone, email, contato, categorias, prazo_entrega_dias, observacao, envio_preferido, dias_entrega, pedido_minimo)
    values
        ('Distribuidora Boa Safra', '(11) 4002-1122', 'pedidos@boasafra.example', 'Carla', 'Grãos, Mercearia', 3, 'Pedido mínimo R$ 300.', 'email', '1,4', 300),
        ('Laticínios Real', '(11) 4002-5588', 'vendas@laticiniosreal.example', 'Rodrigo', 'Laticínios, Frios', 2, null, 'email', '1,3,5', null),
        ('Limp Distribuidora', '(11) 4002-3344', null, null, 'Limpeza, Higiene', 5, 'Entrega só às terças.', 'whatsapp', '2', 200),
        ('Frigorífico Bom Corte', '(11) 4002-7799', 'pedidos@bomcorte.example', 'Seu Antônio', 'Açougue', 1, 'Peso das caixas varia: conferir a etiqueta.', 'whatsapp', '1,2,3,4,5,6', null),
        ('Bebidas Express', '(11) 4002-6611', 'comercial@bebidasexpress.example', null, 'Bebidas', 2, null, 'email', '3,6', 250),
        ('Atacadão Central', '(11) 4002-9090', 'atendimento@atacadaocentral.example', 'Júlia', 'Padaria, Congelados, Hortifrúti, Pet', 4, null, 'email', null, null)
    on conflict ((lower(trim(nome)))) do nothing;
    -- Fornecedor principal de cada produto: o primeiro (A-Z) que entrega a categoria
    update public.produtos p set fornecedor_id = (
        select f.id from public.fornecedores f
        where f.ativo and exists (select 1 from unnest(string_to_array(f.categorias, ',')) c where lower(trim(c)) = lower(p.categoria))
        order by f.nome limit 1)
    where p.fornecedor_id is null;

    -- ------------------------------------------------------------ Dias, caixas e vendas
    create temp table t_prod on commit drop as
    select p.id, p.nome, p.categoria, p.preco, p.unidade_medida as unidade, coalesce(p.dias_validade, 365) as dias,
           p.unidades_por_caixa as caixa, p.preco_custo,
           -- Popularidade: básicos vendem mais
           (case when p.categoria in ('Padaria', 'Laticínios', 'Bebidas', 'Grãos') then 3.0 when p.categoria = 'Açougue' then 1.6 else 1.0 end)
             * (0.4 + random() * 1.6) as peso
    from public.produtos p;
    create temp table t_faixa on commit drop as
    select id, sum(peso) over (order by id) - peso as ini, sum(peso) over (order by id) as fim from t_prod;

    create temp table t_dias on commit drop as
    select d::date as dia,
           case when to_char(d, 'MM-DD') in ('12-25', '01-01') then 0
                else greatest(5, round(p_vendas_dia
                    * (array[0.75, 0.85, 0.9, 0.95, 1.0, 1.25, 1.35])[extract(dow from d)::int + 1]
                    * (array[0.9, 0.88, 0.95, 0.97, 1.0, 0.98, 1.02, 1.0, 0.97, 1.0, 1.05, 1.3])[extract(month from d)::int]
                    * (1 + 0.07 * (d::date - v_inicio) / 365.0)
                    * (0.85 + random() * 0.3)))::int end as n_vendas
    from generate_series(v_inicio, v_fim, interval '1 day') d;

    -- Quem trabalha: os ativos (operadores e gerentes) e, nos 2 primeiros anos, também quem foi desativado depois
    create temp table t_pessoas on commit drop as
    select id, nome, perfil, ativo, row_number() over (order by perfil desc, nome) - 1 as n from public.perfis;

    select coalesce(max(id), 0) into v_base from public.caixas;
    create temp table t_caixas on commit drop as
    select v_base + row_number() over (order by d.dia, k) as id, d.dia, k,
           (case when d.dia < v_inicio + 730 or pe.ativo then pe.id end) as operador_id, pe.nome,
           (d.dia + time '07:00' + (random() * interval '20 minutes')) at time zone 'America/Sao_Paulo' as aberto_em,
           (array[100, 150, 150, 200])[1 + floor(random() * 4)::int]::numeric as troco
    from t_dias d
    cross join generate_series(0, 2) k
    join t_pessoas pe on pe.n = (k + extract(doy from d.dia)::int) % (select count(*) from t_pessoas)
    where d.n_vendas > 0 and k < case when extract(dow from d.dia) in (5, 6) then 3 else 2 end;
    delete from t_caixas c where c.operador_id is null
        or exists (select 1 from public.caixas x where x.operador_id = c.operador_id and x.dia = c.dia);
    create temp table t_caixas_dia on commit drop as
    select dia, array_agg(id order by k) as ids from t_caixas group by dia;

    select coalesce(max(id), 0) into v_base from public.vendas;
    create temp table t_vendas on commit drop as
    select v_base + row_number() over (order by d.dia, s.i) as id, d.dia,
           cd.ids[1 + (s.i % array_length(cd.ids, 1))] as caixa_id,
           (d.dia + (case when r < 0.35 then 9.5 + random() * 3 when r < 0.8 then 16 + random() * 4 else 7.5 + random() * 13 end) * interval '1 hour')
             at time zone 'America/Sao_Paulo' as paga_em,
           (1 + floor(power(random(), 1.7) * 9))::int as n_itens,
           random() as sorteio_forma, random() as sorteio_extra
    from t_dias d
    join t_caixas_dia cd on cd.dia = d.dia
    cross join lateral generate_series(1, d.n_vendas) s(i)
    cross join lateral (select random() as r) rr;

    -- Itens: produtos sorteados pela popularidade; preço do dia com a inflação (~5,5% ao ano)
    create temp table t_itens on commit drop as
    select v.id as venda_id, v.dia, p.id as produto_id, p.nome, p.unidade,
           sum(case when p.unidade = 'kg' then round((0.25 + random() * 1.6)::numeric, 3)
                    else (case when random() < 0.72 then 1 when random() < 0.75 then 2 else 3 + floor(random() * 4) end)::numeric end) as quantidade,
           round(p.preco / power(1.055, (v_hoje - v.dia) / 365.0)::numeric, 2) as preco_unitario
    from t_vendas v
    cross join lateral generate_series(1, v.n_itens) j
    cross join lateral (select random() * (select max(fim) from t_faixa) + j * 0 + v.id * 0 as r) rr
    join t_faixa f on rr.r >= f.ini and rr.r < f.fim
    join t_prod p on p.id = f.id
    group by v.id, v.dia, p.id, p.nome, p.unidade, p.preco;
    alter table t_itens add column id bigint, add column subtotal_normal numeric, add column desconto_promocao numeric default 0,
        add column promocao text, add column subtotal numeric;
    select coalesce(max(id), 0) into v_base from public.venda_itens;
    update t_itens t set id = x.n + v_base from (select venda_id, produto_id, row_number() over (order by venda_id, produto_id) as n from t_itens) x
    where x.venda_id = t.venda_id and x.produto_id = t.produto_id;
    update t_itens set subtotal_normal = round(quantidade * preco_unitario, 2);
    -- ~6% dos itens com promoção (do produto ou de validade)
    update t_itens set desconto_promocao = round(subtotal_normal * (0.1 + random() * 0.2)::numeric, 2),
                       promocao = case when random() < 0.5 then 'Promoção' else 'Validade −' || (10 + floor(random() * 4) * 5) || '%' end
    where random() < 0.06;
    update t_itens set subtotal = subtotal_normal - desconto_promocao;

    -- Totais, desconto (3%), CPF na nota (10%) e cancelamentos (0,4%)
    alter table t_vendas add column total numeric, add column desconto numeric default 0, add column forma text, add column situacao text default 'paga',
        add column cpf text, add column operador_id uuid, add column operador_nome text;
    update t_vendas v set total = x.soma from (select venda_id, sum(subtotal) as soma from t_itens group by venda_id) x where x.venda_id = v.id;
    update t_vendas set desconto = round(total * 0.05, 2) where sorteio_extra < 0.03 and total > 30;
    update t_vendas set total = total - desconto;
    update t_vendas set cpf = (array['52601815906', '08301661305', '18609139034', '99603082430', '62819482112', '99351819019',
                                     '93786579741', '54323194897', '75749118606', '25276018987', '55597971115', '47104974601'])[1 + floor(random() * 12)::int]
    where sorteio_extra between 0.5 and 0.6;
    update t_vendas set situacao = 'cancelada' where sorteio_extra > 0.996;
    update t_vendas set forma = case
        when sorteio_forma < 0.06 and total > 40 then 'varias'
        when sorteio_forma < 0.26 then 'dinheiro'
        when sorteio_forma < 0.61 then 'pix'
        when sorteio_forma < 0.83 then 'debito'
        when sorteio_forma < 0.95 then 'credito'
        when sorteio_forma < 0.99 then 'vale_alimentacao'
        else 'vale_refeicao' end;
    update t_vendas v set operador_id = c.operador_id, operador_nome = c.nome from t_caixas c where c.id = v.caixa_id;

    -- Gravação: caixas (o fechamento entra depois), vendas e itens
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

    insert into public.venda_itens (id, venda_id, produto_id, nome_produto, unidade_medida, quantidade, preco_unitario, subtotal,
                                    subtotal_normal, desconto_promocao, promocao)
    overriding system value
    select id, venda_id, produto_id, nome, unidade, quantidade, preco_unitario, subtotal,
           case when desconto_promocao > 0 then subtotal_normal end, desconto_promocao, promocao
    from t_itens;

    -- Pagamentos: uma forma (ou dinheiro + crédito em "varias"); dinheiro arredondado para cima, com troco
    insert into public.venda_pagamentos (venda_id, forma, valor, recebido, troco, bandeira, nsu, autorizacao, parcelas, final_cartao, criado_em)
    select v.id, f.forma, f.valor,
           case when f.forma = 'dinheiro' then f.recebido end,
           case when f.forma = 'dinheiro' then f.recebido - f.valor else 0 end,
           case when f.forma <> 'dinheiro' and f.forma <> 'pix' then (array['Visa', 'Mastercard', 'Elo', 'Hipercard'])[1 + floor(random() * 4)::int] end,
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
    from public.venda_pagamentos p where p.venda_id = v.id and p.forma = 'dinheiro' and v.forma_pagamento = 'dinheiro' and v.dia between v_inicio and v_fim;

    -- ------------------------------------------------------------ Lotes: um por produto e por mês, comprado no começo do mês
    create temp table t_lotes on commit drop as
    select row_number() over (order by i.produto_id, date_trunc('month', i.dia)) as n, i.produto_id,
           date_trunc('month', i.dia)::date as mes, sum(i.quantidade) as vendido
    from t_itens i join t_vendas v on v.id = i.venda_id and v.situacao = 'paga'
    group by i.produto_id, date_trunc('month', i.dia);
    select coalesce(max(id), 0) into v_base from public.lotes;
    insert into public.lotes (id, produto_id, quantidade_inicial, quantidade_restante, data_compra, validade, preco_custo, criado_em)
    overriding system value
    select v_base + l.n, l.produto_id, l.vendido, 0, l.mes - 3,
           greatest(l.mes - 3 + p.dias, (l.mes + interval '1 month')::date),
           round(p.preco_custo / power(1.055, (v_hoje - l.mes) / 365.0)::numeric, 2),
           (l.mes - 3)::timestamp at time zone 'America/Sao_Paulo'
    from t_lotes l join t_prod p on p.id = l.produto_id;
    perform setval(pg_get_serial_sequence('public.lotes', 'id'), (select max(id) from public.lotes));
    -- De qual lote saiu cada item (a venda cancelada devolveu ao lote; fica sem baixa)
    insert into public.venda_item_lotes (item_id, lote_id, quantidade)
    select i.id, v_base + l.n, i.quantidade
    from t_itens i join t_vendas v on v.id = i.venda_id and v.situacao = 'paga'
    join t_lotes l on l.produto_id = i.produto_id and l.mes = date_trunc('month', i.dia)::date;

    -- Estoque de hoje: um lote novo por produto (alguns sem estoque, alguns abaixo do mínimo, alguns perto de vencer)
    insert into public.lotes (produto_id, quantidade_inicial, quantidade_restante, data_compra, validade, preco_custo)
    select p.id, q, q, c, case when p.dias < 3650 then c + p.dias end, p.preco_custo
    from t_prod p
    cross join lateral (select v_hoje - floor(random() * least(p.dias, 12))::int as c, random() as s) x
    cross join lateral (select case when p.unidade = 'kg' then round((case when x.s < 0.15 then 2 + random() * 2 else 8 + random() * 25 end)::numeric, 3)
                                    when x.s < 0.15 then 1 + floor(random() * 4)
                                    else p.caixa * (1 + floor(random() * 4)) end as q) y
    where x.s > 0.07;
    -- Perdas: alguns lotes dos últimos meses venceram com sobra
    update public.lotes set quantidade_inicial = quantidade_inicial + 3, quantidade_restante = 3
    where id in (select lo.id from public.lotes lo join t_prod p on p.id = lo.produto_id
                 where lo.validade between v_hoje - 120 and v_hoje - 1 and lo.quantidade_restante = 0 and p.dias <= 60
                 order by random() limit 8);

    -- ------------------------------------------------------------ Gaveta: sangrias, suprimentos e fechamento de cada caixa
    insert into public.caixa_movimentos (caixa_id, tipo, valor, motivo, operador_nome, autorizado_por, criado_em)
    select c.id, 'sangria', floor((x.dinheiro - 150) / 100) * 100, 'Cofre', c.nome, v_gerente.nome,
           (c.dia + time '15:30') at time zone 'America/Sao_Paulo'
    from t_caixas c
    cross join lateral (select coalesce(sum(p.valor), 0) as dinheiro from public.venda_pagamentos p join public.vendas v on v.id = p.venda_id
                        where v.caixa_id = c.id and v.situacao = 'paga' and p.forma = 'dinheiro') x
    where x.dinheiro > 600;
    insert into public.caixa_movimentos (caixa_id, tipo, valor, motivo, operador_nome, criado_em)
    select c.id, 'suprimento', 50, 'Troco em moedas', c.nome, c.aberto_em + interval '3 hours'
    from t_caixas c where random() < 0.05;

    update public.caixas c set
        fechado_em = x.ultima + (10 + random() * 25) * interval '1 minute',
        fechado_esperado = public.dinheiro_na_gaveta(c.id),
        fechado_por = c.operador_nome
    from (select caixa_id, max(paga_em) as ultima from public.vendas group by caixa_id) x
    where x.caixa_id = c.id and c.id in (select id from t_caixas);
    update public.caixas c set
        fechado_diferenca = d.dif,
        fechado_contado = c.fechado_esperado + d.dif,
        fechado_obs = case when d.dif <> 0 then (array['Troco errado no fim da tarde', 'Moedas a mais', 'Faltou conferir uma sangria',
                                                       'Cliente pagou a mais e não quis o troco', 'Nota de R$ 10 a menos'])[1 + floor(random() * 5)::int] end
    from (select id, case when random() < 0.86 then 0 else round(((random() - 0.6) * 30)::numeric, 2) end as dif from t_caixas) d
    where d.id = c.id;

    -- Itens cancelados com a senha do gerente (uns 2 por dia)
    insert into public.venda_itens_cancelados (venda_id, nome_produto, unidade_medida, quantidade, valor, operador_nome, autorizado_por, criado_em)
    select v.id, p.nome, p.unidade, 1, round(p.preco / power(1.055, (v_hoje - v.dia) / 365.0)::numeric, 2), v.operador_nome, v_gerente.nome,
           v.paga_em - interval '1 minute'
    from t_vendas v
    join t_prod p on p.id = (select id from t_prod order by id offset (v.id % (select count(*) from t_prod)) limit 1)
    where v.sorteio_extra between 0.3 and 0.315 and p.unidade = 'un';
    insert into public.autorizacoes (venda_id, acao, operador_id, gerente_usuario, gerente_nome, aprovada, usada, criada_em)
    select ic.venda_id, 'cancelar_item', v.operador_id, (select usuario from public.perfis where id = v_gerente.id), v_gerente.nome, true, true, ic.criado_em
    from public.venda_itens_cancelados ic join public.vendas v on v.id = ic.venda_id
    where v.dia between v_inicio and v_fim;

    -- Entradas no sistema (auditoria): cada caixa aberto é um login; de vez em quando uma senha errada
    insert into public.auditoria_login (usuario, sucesso, data_hora)
    select pe.usuario, true, c.aberto_em - interval '2 minutes' from t_caixas c join public.perfis pe on pe.id = c.operador_id
    union all
    select pe.usuario, false, c.aberto_em - interval '3 minutes' from t_caixas c join public.perfis pe on pe.id = c.operador_id where random() < 0.04;

    -- ------------------------------------------------------------ Pedidos aos fornecedores (toda semana) e entregas
    create temp table t_pedidos on commit drop as
    select row_number() over (order by s.d, f.id) as n, f.id as fornecedor_id, s.d::date as data_pedido, coalesce(f.prazo_entrega_dias, 3) as prazo
    from public.fornecedores f
    cross join generate_series(v_inicio + 3, v_hoje, interval '7 days') s(d)
    where f.ativo;
    select coalesce(max(id), 0) into v_base from public.pedidos;
    insert into public.pedidos (id, fornecedor_id, data_pedido, previsao_entrega, situacao, entregue_em, numero_nota, criado_por, criado_em)
    overriding system value
    select v_base + n, fornecedor_id, data_pedido, data_pedido + prazo,
           case when data_pedido + prazo >= v_hoje - 1 then 'pendente' else 'entregue' end,
           case when data_pedido + prazo >= v_hoje - 1 then null else data_pedido + prazo end,
           case when data_pedido + prazo >= v_hoje - 1 then null else (10000 + v_base + n)::text end,
           v_gerente.id, (data_pedido + time '10:00') at time zone 'America/Sao_Paulo'
    from t_pedidos;
    perform setval(pg_get_serial_sequence('public.pedidos', 'id'), (select max(id) from public.pedidos));
    -- Um pedido atrasado para o Painel avisar
    update public.pedidos set previsao_entrega = v_hoje - 2, data_pedido = v_hoje - 6
    where id = (select max(id) from public.pedidos where situacao = 'pendente' and fornecedor_id = (select min(fornecedor_id) from t_pedidos));
    insert into public.pedido_itens (pedido_id, produto_id, nome_produto, unidade_medida, quantidade, quantidade_recebida, preco_custo, motivo_diferenca)
    select v_base + pd.n, p.id, p.nome, p.unidade, q.qtd,
           case when pd.data_pedido + pd.prazo >= v_hoje - 1 then null when q.falta then 0 else q.qtd end,
           round(p.preco_custo / power(1.055, (v_hoje - pd.data_pedido) / 365.0)::numeric, 2),
           case when pd.data_pedido + pd.prazo < v_hoje - 1 and q.falta then 'faltou' end
    from t_pedidos pd
    join public.fornecedores f on f.id = pd.fornecedor_id
    join t_prod p on exists (select 1 from unnest(string_to_array(f.categorias, ',')) c where lower(trim(c)) = lower(p.categoria))
    cross join lateral (select case when p.unidade = 'kg' then 20 + floor(random() * 30) else p.caixa * (1 + floor(random() * 3)) end as qtd,
                               random() < 0.04 as falta) q
    where random() < 0.35;
    delete from public.pedidos pd where pd.id > v_base and not exists (select 1 from public.pedido_itens i where i.pedido_id = pd.id);
    -- Últimos custo e compra de cada produto com cada fornecedor
    insert into public.produto_fornecedores (produto_id, fornecedor_id, ultimo_custo, ultima_compra)
    select distinct on (i.produto_id, pd.fornecedor_id) i.produto_id, pd.fornecedor_id, i.preco_custo, pd.data_pedido
    from public.pedido_itens i join public.pedidos pd on pd.id = i.pedido_id
    where i.produto_id is not null and pd.id > v_base
    order by i.produto_id, pd.fornecedor_id, pd.data_pedido desc
    on conflict (produto_id, fornecedor_id) do update set ultimo_custo = excluded.ultimo_custo, ultima_compra = excluded.ultima_compra;

    -- Conversa por e-mail dos últimos pedidos (algumas respostas sem ler)
    insert into public.fornecedor_mensagens (fornecedor_id, pedido_id, direcao, canal, de, para, assunto, corpo, message_id, usuario_nome, criada_em, lida_em)
    select pd.fornecedor_id, pd.id, m.direcao, 'email',
           case when m.direcao = 'enviada' then 'pedidos.mercado@exemplo.com' else f.email end,
           case when m.direcao = 'enviada' then f.email else 'pedidos.mercado@exemplo.com' end,
           case when m.direcao = 'enviada' then 'Pedido nº ' || pd.id || ' - Mercado para ' || f.nome else 'Re: Pedido nº ' || pd.id || ' - Mercado para ' || f.nome end,
           case when m.direcao = 'enviada' then 'Pedido nº ' || pd.id || E'\n\nOlá' || coalesce(', ' || f.contato, '') || E'! Aqui é ' || v_gerente.nome
                    || E', do Mercado.\nGostaria de fazer um pedido:\n' || (select string_agg('- ' || trim(to_char(i.quantidade, 'FM999990.###')) || ' ' || i.unidade_medida || '. ' || i.nome_produto, E'\n') from public.pedido_itens i where i.pedido_id = pd.id)
                    || E'\nEntrega prevista: ' || to_char(pd.previsao_entrega, 'DD/MM/YYYY') || E'.\nPode confirmar, por favor? Obrigado!'
                else (array['Bom dia! Pedido confirmado, entregamos na data combinada.', 'Recebido! Só o último item vai na próxima entrega, tudo bem?',
                            'Confirmado. A nota vai junto com a mercadoria.'])[1 + floor(random() * 3)::int] end,
           '<exemplo-' || pd.id || '-' || m.direcao || '@mercado.example>',
           case when m.direcao = 'enviada' then v_gerente.nome end,
           pd.criado_em + case when m.direcao = 'enviada' then interval '1 minute' else interval '3 hours' end,
           case when m.direcao = 'enviada' or pd.situacao = 'entregue' then pd.criado_em + interval '4 hours' end
    from public.pedidos pd
    join public.fornecedores f on f.id = pd.fornecedor_id and f.email is not null
    cross join (values ('enviada'), ('recebida')) m(direcao)
    where pd.id > v_base and pd.data_pedido >= v_hoje - 21
    on conflict (message_id) do nothing;

    -- ------------------------------------------------------------ Promoções, regras de validade, configuração e site
    insert into public.promocoes_validade (categoria, dias_antes, desconto_min, desconto_max)
    values ('Padaria', 2, 20, 50), ('Laticínios', 5, 10, 40), ('Frios', 4, 10, 35), ('Hortifrúti', 3, 15, 50), ('Açougue', 3, 10, 30)
    on conflict ((lower(trim(categoria)))) do nothing;
    insert into public.promocoes (produto_id, tipo, valor, inicio, fim, criada_por, encerrada_em)
    select x.id, x.tipo, x.valor, x.inicio, x.fim, v_gerente.nome, null
    from (
        select p.id, 'percentual' as tipo, 15::numeric as valor, v_hoje - 3 as inicio, v_hoje + 4 as fim from t_prod p where p.categoria = 'Bebidas' and p.unidade = 'un' order by p.nome limit 2
    ) x
    where not exists (select 1 from public.promocoes pr where pr.produto_id = x.id);
    insert into public.promocoes (produto_id, tipo, valor, inicio, fim, criada_por)
    select p.id, 'preco', round(p.preco * 0.85, 2), v_hoje + 2, v_hoje + 9, v_gerente.nome
    from t_prod p where p.categoria = 'Mercearia' and not exists (select 1 from public.promocoes pr where pr.produto_id = p.id) order by p.nome limit 1;
    insert into public.promocoes (produto_id, tipo, valor, inicio, fim, criada_por)
    select p.id, 'percentual', 20, v_hoje - 40, v_hoje - 30, v_gerente.nome
    from t_prod p where p.categoria = 'Limpeza' and not exists (select 1 from public.promocoes pr where pr.produto_id = p.id) order by p.nome limit 2;

    update public.config_mercado set emails_resumo = coalesce(emails_resumo, 'gerente@mercado.example'),
                                     email_pedidos = coalesce(email_pedidos, 'pedidos.mercado@exemplo.com') where id = 1;
    insert into public.contatos (nome, email, mensagem, data_hora) values
        ('Roberta Lima', 'roberta@exemplo.com', 'Vocês fazem entrega no bairro?', now() - interval '20 days'),
        ('Padaria Pão Quente', 'contato@paoquente.example', 'Gostaria de oferecer nossos pães para revenda.', now() - interval '6 days');
    insert into public.pedidos_senha (usuario, resolvido, data_hora)
    select usuario, true, now() - interval '90 days' from public.perfis where perfil = 'operador' order by usuario limit 1;

    -- ------------------------------------------------------------ Contadores dos ids (as próximas gravações seguem depois)
    perform setval(pg_get_serial_sequence('public.caixas', 'id'), (select max(id) from public.caixas));
    perform setval(pg_get_serial_sequence('public.vendas', 'id'), (select max(id) from public.vendas));
    perform setval(pg_get_serial_sequence('public.venda_itens', 'id'), (select max(id) from public.venda_itens));
    perform setval(pg_get_serial_sequence('public.lotes', 'id'), (select max(id) from public.lotes));
    perform setval(pg_get_serial_sequence('public.pedidos', 'id'), (select max(id) from public.pedidos));

    select count(*) into v_vendas from t_vendas;
    select count(*) into v_itens from t_itens;
    return format('Pronto: %s vendas, %s itens, %s caixas, de %s a %s. Agora gere os fechamentos: select public.gerar_fechamentos_exemplo(null);',
                  v_vendas, v_itens, (select count(*) from t_caixas), to_char(v_inicio, 'DD/MM/YYYY'), to_char(v_fim, 'DD/MM/YYYY'));
end;
$$;

-- Fechamentos do dia (foto do resumo_dia de cada dia com caixas fechados). p_ano null = todos os anos.
create or replace function public.gerar_fechamentos_exemplo(p_ano integer default null)
returns text
language plpgsql
security definer
set search_path = public
set statement_timeout = 0
as $$
declare
    v_gerente text;
    v_dia date;
    v_n integer := 0;
begin
    select nome into v_gerente from public.perfis where perfil = 'gerente' and ativo order by criado_em limit 1;
    -- resumo_dia só responde ao gerente ou ao servidor: aqui o SQL Editor passa como servidor
    perform set_config('request.jwt.claims', '{"role":"service_role"}', true);
    for v_dia in
        select distinct c.dia from public.caixas c
        where c.dia < public.hoje_local() and (p_ano is null or extract(year from c.dia) = p_ano)
          and not exists (select 1 from public.fechamentos_dia f where f.dia = c.dia)
          and not exists (select 1 from public.caixas a where a.dia = c.dia and a.fechado_em is null)
        order by 1
    loop
        insert into public.fechamentos_dia (dia, fechado_em, fechado_por, obs, resumo, email_enviado_em, email_para)
        values (v_dia, (v_dia + time '22:05') at time zone 'America/Sao_Paulo', coalesce(v_gerente, 'Gerente'),
                case when extract(dow from v_dia) = 6 and random() < 0.2 then 'Movimento forte no sábado.' end,
                public.resumo_dia(v_dia), (v_dia + time '22:06') at time zone 'America/Sao_Paulo', 'gerente@mercado.example');
        v_n := v_n + 1;
    end loop;
    return format('Pronto: %s fechamentos gravados.', v_n);
end;
$$;

-- Apaga TODO o movimento (vendas, caixas, estoque, fornecedores, pedidos, promoções, fechamentos, mensagens e auditoria)
-- para começar o uso real. Os usuários (perfis) ficam. Só roda com a frase de confirmação.
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
             public.lotes, public.produtos, public.fornecedores, public.auditoria_login, public.contatos, public.pedidos_senha
             restart identity cascade;
    return 'Tudo apagado. Os usuários continuam. Anexos de e-mail já baixados ficam no Storage (apague pelo painel, em Storage > anexos-fornecedores).';
end;
$$;

-- Só pelo SQL Editor (dono do banco): ninguém chama essas funções pelo programa.
revoke execute on function public.gerar_dados_exemplo(integer, integer) from public, anon, authenticated;
revoke execute on function public.gerar_fechamentos_exemplo(integer) from public, anon, authenticated;
revoke execute on function public.limpar_dados_exemplo(text) from public, anon, authenticated;
