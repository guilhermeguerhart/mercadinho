-- Promoções e fechamento do caixa (pedido do Guilherme em 30/10, depois da semana 5).
-- Rodar no Supabase: painel do projeto > SQL Editor > New query > colar e executar (depois do 007).
-- Pode ser rodado de novo sem perder dados (só cria o que ainda não existe e atualiza as funções).
--
-- 1. Promoção por produto: preço fixo ("de 8,90 por 6,99") ou porcentagem, com início e fim. Uma por vez por produto.
-- 2. Promoção de validade por categoria: começa X dias antes do vencimento com o desconto mínimo e sobe um pouco a
--    cada dia até o desconto máximo, um dia antes do vencimento (no dia do vencimento continua o máximo). Vale só
--    para as unidades dos lotes perto de vencer; produtos.sem_promocao_validade tira o produto da regra.
-- 3. O caixa usa o maior desconto entre as duas (não soma). O preço é calculado aqui, em reservar_item, pelos lotes
--    que vão sair primeiro (o que vence primeiro sai primeiro), pulando o que outros caixas já reservaram.
-- 4. Fechamento do caixa: sangria (com o gerente) e suprimento, contagem da gaveta, sobra ou falta.
--
-- Promoções: todos leem, só o gerente cadastra e altera. Movimentos e fechamento só pelas funções.

-- Coluna do 004 ("pedir antes de acabar") que faltava em bancos onde uma versão antiga do 004 foi rodada. Sem ela o
-- programa lê o estoque sem fornecedor, custo, balança e promoções.
alter table public.produtos add column if not exists pedir_antes boolean not null default false;

-- ---------------------------------------------------------------- Promoção por produto
create table if not exists public.promocoes (
    id            bigint generated always as identity primary key,
    produto_id    bigint not null references public.produtos (id) on delete cascade,
    tipo          text not null check (tipo in ('preco', 'percentual')),
    valor         numeric(10, 2) not null check (valor > 0),       -- preço promocional (R$) ou desconto (%)
    inicio        date not null default public.hoje_local(),
    fim           date,                                            -- null = até o gerente encerrar
    encerrada_em  timestamptz,                                     -- "Encerrar agora"
    criada_por    text,
    criada_em     timestamptz not null default now(),
    check (fim is null or fim >= inicio),
    check (tipo <> 'percentual' or valor < 100)
);

create index if not exists promocoes_produto on public.promocoes (produto_id);

-- Uma promoção por produto no mesmo período; o preço promocional precisa ser menor que o normal.
create or replace function public.conferir_promocao()
returns trigger
language plpgsql
set search_path = public
as $$
declare
    v_preco numeric;
begin
    if new.encerrada_em is not null then
        return new;
    end if;
    if new.tipo = 'preco' then
        select preco into v_preco from public.produtos where id = new.produto_id;
        if new.valor >= v_preco then
            raise exception 'O preço promocional precisa ser menor que o preço normal (R$ %).',
                replace(to_char(v_preco, 'FM999990.00'), '.', ',') using errcode = '22023';
        end if;
    end if;
    if exists (select 1 from public.promocoes p
               where p.produto_id = new.produto_id and p.id is distinct from new.id and p.encerrada_em is null
                 and p.inicio <= coalesce(new.fim, 'infinity'::date) and new.inicio <= coalesce(p.fim, 'infinity'::date)) then
        raise exception 'Este produto já tem uma promoção nesse período. Encerre ou mude a outra primeiro.' using errcode = '22023';
    end if;
    return new;
end;
$$;

drop trigger if exists promocoes_conferir on public.promocoes;
create trigger promocoes_conferir before insert or update on public.promocoes
    for each row execute function public.conferir_promocao();

-- ---------------------------------------------------------------- Promoção de validade por categoria
create table if not exists public.promocoes_validade (
    id             bigint generated always as identity primary key,
    categoria      text not null check (length(trim(categoria)) between 1 and 40),
    dias_antes     integer not null check (dias_antes between 1 and 60),
    desconto_min   numeric(5, 2) not null check (desconto_min >= 0 and desconto_min <= 90),
    desconto_max   numeric(5, 2) not null check (desconto_max > 0 and desconto_max <= 90),
    ativa          boolean not null default true,
    criada_em      timestamptz not null default now(),
    check (desconto_max >= desconto_min)
);

create unique index if not exists promocoes_validade_categoria on public.promocoes_validade (lower(trim(categoria)));

alter table public.produtos add column if not exists sem_promocao_validade boolean not null default false;

-- ---------------------------------------------------------------- Itens da venda: preço normal e promoção
alter table public.venda_itens add column if not exists subtotal_normal numeric(10, 2) check (subtotal_normal is null or subtotal_normal >= 0);
alter table public.venda_itens add column if not exists desconto_promocao numeric(10, 2) not null default 0 check (desconto_promocao >= 0);
alter table public.venda_itens add column if not exists promocao text check (promocao is null or length(promocao) <= 60);

-- ---------------------------------------------------------------- Fechamento do caixa, sangria e suprimento
alter table public.caixas add column if not exists fechado_contado numeric(10, 2);
alter table public.caixas add column if not exists fechado_esperado numeric(10, 2);
alter table public.caixas add column if not exists fechado_diferenca numeric(10, 2);
alter table public.caixas add column if not exists fechado_obs text check (fechado_obs is null or length(fechado_obs) <= 200);
alter table public.caixas add column if not exists fechado_por text;

create table if not exists public.caixa_movimentos (
    id              bigint generated always as identity primary key,
    caixa_id        bigint not null references public.caixas (id) on delete cascade,
    tipo            text not null check (tipo in ('sangria', 'suprimento')),
    valor           numeric(10, 2) not null check (valor > 0),
    motivo          text check (motivo is null or length(motivo) <= 120),
    operador_nome   text not null,
    autorizado_por  text,
    criado_em       timestamptz not null default now()
);

create index if not exists caixa_movimentos_caixa on public.caixa_movimentos (caixa_id);

-- A sangria precisa da senha do gerente (mesma autorização do 007).
alter table public.autorizacoes drop constraint if exists autorizacoes_acao_check;
alter table public.autorizacoes add constraint autorizacoes_acao_check
    check (acao in ('cancelar_item', 'cancelar_venda', 'desconto', 'sangria'));

-- ---------------------------------------------------------------- Regras de acesso (RLS)
alter table public.promocoes enable row level security;
alter table public.promocoes_validade enable row level security;
alter table public.caixa_movimentos enable row level security;

drop policy if exists "usuario logado le promocoes" on public.promocoes;
create policy "usuario logado le promocoes"
    on public.promocoes for select
    to authenticated
    using (true);

drop policy if exists "gerente altera promocoes" on public.promocoes;
create policy "gerente altera promocoes"
    on public.promocoes for all
    to authenticated
    using (public.eh_gerente())
    with check (public.eh_gerente());

drop policy if exists "usuario logado le promocoes de validade" on public.promocoes_validade;
create policy "usuario logado le promocoes de validade"
    on public.promocoes_validade for select
    to authenticated
    using (true);

drop policy if exists "gerente altera promocoes de validade" on public.promocoes_validade;
create policy "gerente altera promocoes de validade"
    on public.promocoes_validade for all
    to authenticated
    using (public.eh_gerente())
    with check (public.eh_gerente());

drop policy if exists "le movimentos dos caixas de hoje" on public.caixa_movimentos;
create policy "le movimentos dos caixas de hoje"
    on public.caixa_movimentos for select
    to authenticated
    using (public.eh_gerente() or (public.usuario_ativo()
           and exists (select 1 from public.caixas c where c.id = caixa_id and c.dia = public.hoje_local())));

-- ---------------------------------------------------------------- Funções: promoções

-- Desconto (%) da promoção de validade de um lote hoje. 0 = fora da regra.
create or replace function public.desconto_validade(p_categoria text, p_validade date, p_fora boolean default false)
returns numeric
language plpgsql
stable
security definer
set search_path = public
as $$
declare
    v_regra public.promocoes_validade%rowtype;
    v_dias integer;
begin
    if p_fora or p_validade is null then
        return 0;
    end if;
    select * into v_regra from public.promocoes_validade
    where lower(trim(categoria)) = lower(trim(p_categoria)) and ativa;
    if not found then
        return 0;
    end if;
    v_dias := p_validade - public.hoje_local();
    if v_dias < 0 or v_dias > v_regra.dias_antes then
        return 0;
    end if;
    if v_dias <= 1 or v_regra.dias_antes = 1 then
        return v_regra.desconto_max;     -- um dia antes e no dia do vencimento
    end if;
    -- Sobe por igual do mínimo (primeiro dia) até o máximo (um dia antes)
    return round(v_regra.desconto_min + (v_regra.desconto_max - v_regra.desconto_min)
                 * (v_regra.dias_antes - v_dias)::numeric / (v_regra.dias_antes - 1), 2);
end;
$$;

-- Desconto em R$ por unidade (ou por kg) da promoção do produto que vale hoje. 0 = sem promoção.
create or replace function public.desconto_promocao_produto(p_produto_id bigint, p_preco numeric)
returns numeric
language sql
stable
security definer
set search_path = public
as $$
    select coalesce((
        select greatest(0, case when p.tipo = 'preco' then p_preco - p.valor else round(p_preco * p.valor / 100, 4) end)
        from public.promocoes p
        where p.produto_id = p_produto_id and p.encerrada_em is null
          and p.inicio <= public.hoje_local() and (p.fim is null or p.fim >= public.hoje_local())
        order by p.id desc limit 1), 0);
$$;

-- Preço de p_quantidade do produto com as promoções: vai pelos lotes que saem primeiro (pulando o que já está
-- reservado em outras vendas) e usa, em cada lote, o maior desconto entre a promoção do produto e a de validade.
-- Devolve {"subtotal", "normal", "promocao"}.
create or replace function public.preco_com_promocao(p_produto_id bigint, p_quantidade numeric, p_preco numeric, p_sem_item bigint default null)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
    v_produto public.produtos%rowtype;
    v_lote public.lotes%rowtype;
    v_pular numeric;
    v_tem numeric;
    v_parte numeric;
    v_falta numeric := p_quantidade;
    v_total numeric := 0;
    v_desc_produto numeric;
    v_pct numeric;
    v_desc numeric;
    v_usou_produto boolean := false;
    v_maior_validade numeric := 0;
begin
    select * into v_produto from public.produtos where id = p_produto_id;
    v_desc_produto := public.desconto_promocao_produto(p_produto_id, p_preco);
    v_pular := coalesce((select sum(i.quantidade) from public.venda_itens i
                         join public.vendas v on v.id = i.venda_id
                         where i.produto_id = p_produto_id and v.situacao in ('aberta', 'espera') and v.dia = public.hoje_local()
                           and i.id is distinct from p_sem_item), 0);
    for v_lote in
        select * from public.lotes
        where produto_id = p_produto_id and quantidade_restante > 0
          and (validade is null or validade >= public.hoje_local())
        order by validade nulls last, data_compra, id
    loop
        exit when v_falta <= 0;
        v_tem := v_lote.quantidade_restante;
        if v_pular > 0 then
            v_parte := least(v_pular, v_tem);
            v_tem := v_tem - v_parte;
            v_pular := v_pular - v_parte;
        end if;
        continue when v_tem <= 0;
        v_parte := least(v_falta, v_tem);
        v_pct := public.desconto_validade(v_produto.categoria, v_lote.validade, v_produto.sem_promocao_validade);
        v_desc := greatest(v_desc_produto, round(p_preco * v_pct / 100, 4));
        if v_desc > 0 and v_desc = v_desc_produto and v_desc_produto > 0 then
            v_usou_produto := true;
        elsif v_desc > 0 then
            v_maior_validade := greatest(v_maior_validade, v_pct);
        end if;
        v_total := v_total + v_parte * (p_preco - v_desc);
        v_falta := v_falta - v_parte;
    end loop;
    if v_falta > 0 then
        v_total := v_total + v_falta * (p_preco - v_desc_produto);
        if v_desc_produto > 0 then v_usou_produto := true; end if;
    end if;
    return jsonb_build_object(
        'subtotal', round(v_total, 2),
        'normal', round(p_quantidade * p_preco, 2),
        'promocao', nullif(concat_ws(' + ',
            case when v_usou_produto then 'Promoção' end,
            case when v_maior_validade > 0 then 'Validade −' || replace(rtrim(rtrim(to_char(v_maior_validade, 'FM990.00'), '0'), '.'), '.', ',') || '%' end), ''));
end;
$$;

-- Troca o reservar_item do 007: o subtotal sai com as promoções (o preço normal fica guardado no item).
create or replace function public.reservar_item(
    p_produto_id bigint, p_quantidade numeric, p_somar boolean default false, p_valor numeric default null,
    p_autorizacao bigint default null)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_caixa public.caixas%rowtype;
    v_produto public.produtos%rowtype;
    v_venda bigint;
    v_item public.venda_itens%rowtype;
    v_qtd numeric(12, 3);
    v_normal numeric(10, 2);
    v_subtotal numeric(10, 2);
    v_preco jsonb;
    v_disponivel numeric;
    v_vencido numeric;
    v_gerente text;
begin
    if not public.usuario_ativo() then
        raise exception 'Usuário desativado ou sem perfil. Fale com o gerente.' using errcode = '42501';
    end if;
    select * into v_caixa from public.caixas where operador_id = auth.uid() and dia = public.hoje_local();
    if not found then
        raise exception 'Abra o caixa antes de vender.' using errcode = '22023';
    end if;
    if v_caixa.fechado_em is not null then
        raise exception 'O seu caixa de hoje já foi fechado. Peça ao gerente para reabrir.' using errcode = '22023';
    end if;
    -- Trava o produto: dois caixas não reservam a mesma última unidade ao mesmo tempo
    select * into v_produto from public.produtos where id = p_produto_id for update;
    if not found then
        raise exception 'Este produto não existe mais. Atualize a tela.' using errcode = '22023';
    end if;
    if p_quantidade is null or p_quantidade < 0 or p_quantidade > 99999 then
        raise exception 'Quantidade inválida.' using errcode = '22023';
    end if;
    if v_produto.unidade_medida = 'un' and p_quantidade <> trunc(p_quantidade) then
        raise exception '% é vendido por unidade: digite um número inteiro.', v_produto.nome using errcode = '22023';
    end if;

    select id into v_venda from public.vendas where caixa_id = v_caixa.id and situacao = 'aberta';
    if not found then
        insert into public.vendas (caixa_id, operador_id, operador_nome)
        values (v_caixa.id, auth.uid(), v_caixa.operador_nome)
        returning id into v_venda;
    end if;
    select * into v_item from public.venda_itens where venda_id = v_venda and produto_id = p_produto_id;

    v_qtd := round(case when p_somar and v_item.id is not null then v_item.quantidade + p_quantidade else p_quantidade end, 3);

    -- Cancelar item (tirar ou diminuir): só com o gerente
    if v_item.id is not null and v_qtd < v_item.quantidade then
        v_gerente := public.usar_autorizacao(p_autorizacao, 'cancelar_item', v_venda);
        insert into public.venda_itens_cancelados (venda_id, nome_produto, unidade_medida, quantidade, valor, operador_nome, autorizado_por)
        values (v_venda, v_item.nome_produto, v_item.unidade_medida, v_item.quantidade - greatest(v_qtd, 0),
                round(v_item.subtotal * (v_item.quantidade - greatest(v_qtd, 0)) / v_item.quantidade, 2), v_caixa.operador_nome, v_gerente);
    end if;

    if v_qtd <= 0 then
        delete from public.venda_itens where id = v_item.id;
        perform public.recalcular_venda(v_venda);
        return jsonb_build_object('venda_id', v_venda, 'item_id', null, 'quantidade', 0, 'subtotal', 0, 'normal', 0, 'promocao', null);
    end if;

    v_disponivel := public.disponivel_para_venda(p_produto_id, v_item.id);
    if v_qtd > v_disponivel then
        if v_disponivel <= 0 then
            select coalesce(sum(quantidade_restante), 0) into v_vencido from public.lotes
            where produto_id = p_produto_id and quantidade_restante > 0 and validade < public.hoje_local();
            if v_vencido > 0 then
                raise exception '%: só há lote vencido. A venda está bloqueada; retire o produto da prateleira.', v_produto.nome
                    using errcode = '22023';
            end if;
            raise exception '% está sem estoque (ou reservado em outro caixa).', v_produto.nome using errcode = '22023';
        end if;
        raise exception 'Só há % % de % disponível.', replace(rtrim(to_char(v_disponivel, 'FM999990.999'), '.'), '.', ','),
            case when v_produto.unidade_medida = 'kg' then 'kg' else 'un.' end, v_produto.nome using errcode = '22023';
    end if;

    -- Preço com as promoções de hoje (pelo preço guardado no item, se o produto mudou de preço no meio da venda)
    v_preco := public.preco_com_promocao(p_produto_id, v_qtd, coalesce(v_item.preco_unitario, v_produto.preco), v_item.id);
    -- Etiqueta da balança: o valor impresso é o preço normal; a promoção vale na mesma proporção
    v_normal := case
        when p_valor is not null and p_somar and v_item.id is not null then coalesce(v_item.subtotal_normal, v_item.subtotal) + round(p_valor, 2)
        when p_valor is not null then round(p_valor, 2)
        else (v_preco ->> 'normal')::numeric end;
    v_subtotal := case
        when p_valor is null then (v_preco ->> 'subtotal')::numeric
        when (v_preco ->> 'normal')::numeric > 0 then round(v_normal * (v_preco ->> 'subtotal')::numeric / (v_preco ->> 'normal')::numeric, 2)
        else v_normal end;

    if v_item.id is null then
        insert into public.venda_itens (venda_id, produto_id, nome_produto, unidade_medida, quantidade, preco_unitario, subtotal,
                                        subtotal_normal, desconto_promocao, promocao)
        values (v_venda, p_produto_id, v_produto.nome, v_produto.unidade_medida, v_qtd, v_produto.preco, v_subtotal,
                v_normal, greatest(0, v_normal - v_subtotal), v_preco ->> 'promocao')
        returning * into v_item;
    else
        update public.venda_itens
        set quantidade = v_qtd, subtotal = v_subtotal, subtotal_normal = v_normal,
            desconto_promocao = greatest(0, v_normal - v_subtotal), promocao = v_preco ->> 'promocao'
        where id = v_item.id
        returning * into v_item;
    end if;
    perform public.recalcular_venda(v_venda);
    return jsonb_build_object('venda_id', v_venda, 'item_id', v_item.id, 'quantidade', v_item.quantidade, 'subtotal', v_item.subtotal,
                              'normal', v_item.subtotal_normal, 'promocao', v_item.promocao);
end;
$$;

-- ---------------------------------------------------------------- Funções: sangria, suprimento e fechamento

-- Troca o autorizar_gerente do 007: aceita também a sangria (sem venda).
create or replace function public.autorizar_gerente(p_usuario text, p_senha text, p_acao text, p_venda_id bigint)
returns bigint
language plpgsql
security definer
set search_path = public
as $$
declare
    v_usuario text := lower(trim(coalesce(p_usuario, '')));
    v_nome text;
    v_senha text;
    v_ok boolean := false;
    v_id bigint;
begin
    if not public.usuario_ativo() then
        raise exception 'Usuário desativado ou sem perfil. Fale com o gerente.' using errcode = '42501';
    end if;
    if p_acao not in ('cancelar_item', 'cancelar_venda', 'desconto', 'sangria') then
        raise exception 'Ação inválida.' using errcode = '22023';
    end if;
    if (select count(*) from public.autorizacoes
        where gerente_usuario = v_usuario and not aprovada and criada_em > now() - interval '5 minutes') >= 5 then
        raise exception 'Muitas senhas erradas para este gerente. Espere 5 minutos.' using errcode = '22023';
    end if;

    select p.nome, u.encrypted_password into v_nome, v_senha
    from public.perfis p join auth.users u on u.id = p.id
    where p.usuario = v_usuario and p.perfil = 'gerente' and p.ativo;
    if v_senha is not null then
        v_ok := v_senha = extensions.crypt(coalesce(p_senha, ''), v_senha);
    end if;

    insert into public.autorizacoes (venda_id, acao, operador_id, gerente_usuario, gerente_nome, aprovada)
    values (p_venda_id, p_acao, auth.uid(), v_usuario, case when v_ok then v_nome end, v_ok)
    returning id into v_id;
    return case when v_ok then v_id end;
end;
$$;

-- Dinheiro que deveria estar na gaveta: troco inicial + dinheiro das vendas pagas (já sem o troco dado)
-- + suprimentos - sangrias.
create or replace function public.dinheiro_na_gaveta(p_caixa_id bigint)
returns numeric
language sql
stable
security definer
set search_path = public
as $$
    select c.troco_inicial
         + coalesce((select sum(pg.valor) from public.venda_pagamentos pg join public.vendas v on v.id = pg.venda_id
                     where v.caixa_id = c.id and v.situacao = 'paga' and pg.forma = 'dinheiro'), 0)
         + coalesce((select sum(valor) from public.caixa_movimentos where caixa_id = c.id and tipo = 'suprimento'), 0)
         - coalesce((select sum(valor) from public.caixa_movimentos where caixa_id = c.id and tipo = 'sangria'), 0)
    from public.caixas c where c.id = p_caixa_id;
$$;

-- Resumo do caixa (para a sangria, o fechamento e o gerente): quem abriu, vendas, formas, movimentos e gaveta.
create or replace function public.resumo_caixa(p_caixa_id bigint)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
    v_caixa public.caixas%rowtype;
begin
    select * into v_caixa from public.caixas where id = p_caixa_id;
    if not found or (v_caixa.operador_id <> auth.uid() and not public.eh_gerente()) or not public.usuario_ativo() then
        raise exception 'Caixa não encontrado.' using errcode = '22023';
    end if;
    return jsonb_build_object(
        'caixa_id', v_caixa.id,
        'operador_nome', v_caixa.operador_nome,
        'aberto_em', v_caixa.aberto_em,
        'fechado_em', v_caixa.fechado_em,
        'troco_inicial', v_caixa.troco_inicial,
        'vendas', (select count(*) from public.vendas where caixa_id = v_caixa.id and situacao = 'paga'),
        'total_vendas', coalesce((select sum(total) from public.vendas where caixa_id = v_caixa.id and situacao = 'paga'), 0),
        'descontos', coalesce((select sum(desconto) from public.vendas where caixa_id = v_caixa.id and situacao = 'paga'), 0),
        'promocoes', coalesce((select sum(i.desconto_promocao) from public.venda_itens i join public.vendas v on v.id = i.venda_id
                               where v.caixa_id = v_caixa.id and v.situacao = 'paga'), 0),
        'canceladas', (select count(*) from public.vendas where caixa_id = v_caixa.id and situacao = 'cancelada' and paga_em is not null),
        'em_espera', (select count(*) from public.vendas where caixa_id = v_caixa.id and situacao = 'espera'),
        'formas', coalesce((select jsonb_object_agg(forma, valor) from (
                       select pg.forma, sum(pg.valor) as valor from public.venda_pagamentos pg join public.vendas v on v.id = pg.venda_id
                       where v.caixa_id = v_caixa.id and v.situacao = 'paga' group by pg.forma) f), '{}'::jsonb),
        'suprimentos', coalesce((select sum(valor) from public.caixa_movimentos where caixa_id = v_caixa.id and tipo = 'suprimento'), 0),
        'sangrias', coalesce((select sum(valor) from public.caixa_movimentos where caixa_id = v_caixa.id and tipo = 'sangria'), 0),
        'movimentos', coalesce((select jsonb_agg(jsonb_build_object('tipo', tipo, 'valor', valor, 'motivo', motivo,
                                    'autorizado_por', autorizado_por, 'criado_em', criado_em) order by id)
                                from public.caixa_movimentos where caixa_id = v_caixa.id), '[]'::jsonb),
        'esperado', public.dinheiro_na_gaveta(v_caixa.id),
        'contado', v_caixa.fechado_contado,
        'diferenca', v_caixa.fechado_diferenca,
        'obs', v_caixa.fechado_obs,
        'fechado_por', v_caixa.fechado_por);
end;
$$;

-- Sangria (tirar dinheiro da gaveta, com o gerente) ou suprimento (pôr mais troco) no caixa aberto de quem chamou.
create or replace function public.registrar_movimento(p_tipo text, p_valor numeric, p_motivo text default null, p_autorizacao bigint default null)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_caixa public.caixas%rowtype;
    v_gerente text;
begin
    if not public.usuario_ativo() then
        raise exception 'Usuário desativado ou sem perfil. Fale com o gerente.' using errcode = '42501';
    end if;
    select * into v_caixa from public.caixas where operador_id = auth.uid() and dia = public.hoje_local() for update;
    if not found or v_caixa.fechado_em is not null then
        raise exception 'O caixa precisa estar aberto.' using errcode = '22023';
    end if;
    if p_tipo not in ('sangria', 'suprimento') then
        raise exception 'Movimento inválido.' using errcode = '22023';
    end if;
    if p_valor is null or p_valor <= 0 or p_valor > 100000 then
        raise exception 'Digite um valor válido, por exemplo 200,00.' using errcode = '22023';
    end if;
    if p_tipo = 'sangria' then
        if round(p_valor, 2) > public.dinheiro_na_gaveta(v_caixa.id) then
            raise exception 'A sangria não pode passar do dinheiro que está na gaveta (R$ %).',
                replace(to_char(public.dinheiro_na_gaveta(v_caixa.id), 'FM999990.00'), '.', ',') using errcode = '22023';
        end if;
        v_gerente := public.usar_autorizacao(p_autorizacao, 'sangria', null);
    end if;
    insert into public.caixa_movimentos (caixa_id, tipo, valor, motivo, operador_nome, autorizado_por)
    values (v_caixa.id, p_tipo, round(p_valor, 2), nullif(trim(coalesce(p_motivo, '')), ''), v_caixa.operador_nome, v_gerente);
    return public.resumo_caixa(v_caixa.id);
end;
$$;

-- Fecha o caixa: confere a gaveta (contado x esperado). A venda em andamento precisa estar terminada; as vendas em
-- espera são canceladas (os itens voltam a ficar disponíveis). O gerente pode fechar o caixa de outra pessoa.
create or replace function public.fechar_caixa(p_caixa_id bigint, p_contado numeric, p_obs text default null)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_caixa public.caixas%rowtype;
    v_nome text;
    v_esperado numeric(10, 2);
begin
    select nome into v_nome from public.perfis where id = auth.uid() and ativo;
    if not found then
        raise exception 'Usuário desativado ou sem perfil. Fale com o gerente.' using errcode = '42501';
    end if;
    select * into v_caixa from public.caixas where id = p_caixa_id for update;
    if not found or (v_caixa.operador_id <> auth.uid() and not public.eh_gerente()) then
        raise exception 'Caixa não encontrado.' using errcode = '22023';
    end if;
    if v_caixa.fechado_em is not null then
        raise exception 'Este caixa já foi fechado.' using errcode = '22023';
    end if;
    if p_contado is null or p_contado < 0 or p_contado > 1000000 then
        raise exception 'Digite quanto dinheiro tem na gaveta, por exemplo 350,00.' using errcode = '22023';
    end if;
    if exists (select 1 from public.vendas v where v.caixa_id = p_caixa_id and v.situacao = 'aberta'
               and exists (select 1 from public.venda_itens i where i.venda_id = v.id)) then
        raise exception 'Termine ou cancele a venda em andamento antes de fechar o caixa.' using errcode = '22023';
    end if;

    delete from public.venda_itens i using public.vendas v
    where i.venda_id = v.id and v.caixa_id = p_caixa_id and v.situacao in ('aberta', 'espera');
    update public.vendas
    set situacao = 'cancelada', total = 0, desconto = 0, cancelada_em = now(), cancelada_por = 'Fechamento do caixa'
    where caixa_id = p_caixa_id and situacao in ('aberta', 'espera');

    v_esperado := public.dinheiro_na_gaveta(p_caixa_id);
    update public.caixas
    set fechado_em = now(), fechado_contado = round(p_contado, 2), fechado_esperado = v_esperado,
        fechado_diferenca = round(p_contado, 2) - v_esperado, fechado_obs = nullif(trim(coalesce(p_obs, '')), ''),
        fechado_por = v_nome
    where id = p_caixa_id;
    return public.resumo_caixa(p_caixa_id);
end;
$$;

-- Só o gerente reabre um caixa de hoje fechado por engano (a contagem é apagada; fecha de novo depois).
create or replace function public.reabrir_caixa(p_caixa_id bigint)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente pode reabrir um caixa.' using errcode = '42501';
    end if;
    update public.caixas
    set fechado_em = null, fechado_contado = null, fechado_esperado = null, fechado_diferenca = null, fechado_obs = null, fechado_por = null
    where id = p_caixa_id and dia = public.hoje_local() and fechado_em is not null;
    if not found then
        raise exception 'Só dá para reabrir um caixa fechado hoje.' using errcode = '22023';
    end if;
end;
$$;

-- Só usuários logados chamam as funções novas.
revoke execute on function public.conferir_promocao() from public, anon, authenticated;
revoke execute on function public.desconto_validade(text, date, boolean) from public, anon;
revoke execute on function public.desconto_promocao_produto(bigint, numeric) from public, anon;
revoke execute on function public.preco_com_promocao(bigint, numeric, numeric, bigint) from public, anon;
revoke execute on function public.reservar_item(bigint, numeric, boolean, numeric, bigint) from public, anon;
revoke execute on function public.autorizar_gerente(text, text, text, bigint) from public, anon;
revoke execute on function public.dinheiro_na_gaveta(bigint) from public, anon, authenticated;
revoke execute on function public.resumo_caixa(bigint) from public, anon;
revoke execute on function public.registrar_movimento(text, numeric, text, bigint) from public, anon;
revoke execute on function public.fechar_caixa(bigint, numeric, text) from public, anon;
revoke execute on function public.reabrir_caixa(bigint) from public, anon;
grant execute on function public.desconto_validade(text, date, boolean) to authenticated;
grant execute on function public.desconto_promocao_produto(bigint, numeric) to authenticated;
grant execute on function public.preco_com_promocao(bigint, numeric, numeric, bigint) to authenticated;
grant execute on function public.reservar_item(bigint, numeric, boolean, numeric, bigint) to authenticated;
grant execute on function public.autorizar_gerente(text, text, text, bigint) to authenticated;
grant execute on function public.resumo_caixa(bigint) to authenticated;
grant execute on function public.registrar_movimento(text, numeric, text, bigint) to authenticated;
grant execute on function public.fechar_caixa(bigint, numeric, text) to authenticated;
grant execute on function public.reabrir_caixa(bigint) to authenticated;
