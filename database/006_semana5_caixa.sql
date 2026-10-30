-- Semana 5: caixa (vendas com reserva e baixa por lote) e funcionários.
-- Rodar no Supabase: painel do projeto > SQL Editor > New query > colar e executar (depois do 005).
-- Pode ser rodado de novo sem perder dados (só cria o que ainda não existe e atualiza as funções).
--
-- 1. Caixa: cada pessoa abre o próprio caixa no dia, com o troco inicial.
-- 2. Venda: o item colocado na venda fica reservado (continua nos lotes, mas já não pode ser vendido por outro
--    caixa). Só depois do pagamento sai dos lotes, o que vence primeiro sai primeiro. Lote vencido nunca é vendido.
-- 3. Cancelar: venda aberta, quem está vendendo (nada sai do estoque); venda paga, só o gerente (os itens voltam
--    para os mesmos lotes).
-- 4. Etiqueta da balança do açougue: produtos.codigo_balanca (código do produto na balança, o "PLU").
-- 5. Funcionários: o gerente cadastra, edita, redefine a senha e remove (com vendas, só desativa).
--
-- Gravar venda e funcionário só pelas funções abaixo (security definer: conferem quem chamou antes de gravar).
-- As tabelas não têm regra de insert/update/delete para o usuário logado.

-- ---------------------------------------------------------------- Dia de hoje no horário do mercado
-- O servidor do Supabase usa UTC; a venda das 22h ainda é "hoje" em Brasília.
create or replace function public.hoje_local()
returns date
language sql
stable
as $$
    select (now() at time zone 'America/Sao_Paulo')::date;
$$;

-- Usuário logado com perfil ativo (o gerente desativa quem saiu da empresa).
create or replace function public.usuario_ativo()
returns boolean
language sql
stable
security definer
set search_path = public
as $$
    select exists (select 1 from public.perfis where id = auth.uid() and ativo);
$$;

-- ---------------------------------------------------------------- Etiqueta da balança
alter table public.produtos add column if not exists codigo_balanca integer
    check (codigo_balanca is null or codigo_balanca between 1 and 99999);
create unique index if not exists produtos_codigo_balanca_unico on public.produtos (codigo_balanca)
    where codigo_balanca is not null;

-- ---------------------------------------------------------------- Caixas
create table if not exists public.caixas (
    id             bigint generated always as identity primary key,
    operador_id    uuid not null references public.perfis (id),
    operador_nome  text not null,
    dia            date not null default public.hoje_local(),
    aberto_em      timestamptz not null default now(),
    troco_inicial  numeric(10, 2) not null default 0 check (troco_inicial >= 0),
    fechado_em     timestamptz,   -- fechamento do caixa: semana 6
    unique (operador_id, dia)
);

-- ---------------------------------------------------------------- Vendas
create table if not exists public.vendas (
    id               bigint generated always as identity primary key,
    caixa_id         bigint not null references public.caixas (id),
    operador_id      uuid not null references public.perfis (id),
    operador_nome    text not null,
    dia              date not null default public.hoje_local(),
    situacao         text not null default 'aberta' check (situacao in ('aberta', 'paga', 'cancelada')),
    forma_pagamento  text check (forma_pagamento is null or forma_pagamento in ('dinheiro', 'pix', 'debito', 'credito')),
    total            numeric(10, 2) not null default 0 check (total >= 0),
    recebido         numeric(10, 2) check (recebido is null or recebido >= 0),
    troco            numeric(10, 2) check (troco is null or troco >= 0),
    criada_em        timestamptz not null default now(),
    paga_em          timestamptz,
    cancelada_em     timestamptz,
    cancelada_por    text
);

create index if not exists vendas_dia on public.vendas (dia, situacao);
create index if not exists vendas_caixa on public.vendas (caixa_id);
-- Uma venda aberta por caixa
create unique index if not exists vendas_uma_aberta_por_caixa on public.vendas (caixa_id) where situacao = 'aberta';

-- Itens: nome, unidade e preço guardados como estavam na hora da venda (o produto pode mudar ou ser excluído).
create table if not exists public.venda_itens (
    id              bigint generated always as identity primary key,
    venda_id        bigint not null references public.vendas (id) on delete cascade,
    produto_id      bigint references public.produtos (id) on delete set null,
    nome_produto    text not null,
    unidade_medida  text not null default 'un' check (unidade_medida in ('un', 'kg')),
    quantidade      numeric(12, 3) not null check (quantidade > 0),
    preco_unitario  numeric(10, 2) not null check (preco_unitario >= 0),
    subtotal        numeric(10, 2) not null check (subtotal >= 0),
    unique (venda_id, produto_id)
);

create index if not exists venda_itens_produto on public.venda_itens (produto_id);

-- De qual lote saiu cada item pago (para devolver ao lote certo se a venda for cancelada).
create table if not exists public.venda_item_lotes (
    item_id     bigint not null references public.venda_itens (id) on delete cascade,
    lote_id     bigint references public.lotes (id) on delete set null,
    quantidade  numeric(12, 3) not null check (quantidade > 0)
);

create index if not exists venda_item_lotes_item on public.venda_item_lotes (item_id);
create index if not exists venda_item_lotes_lote on public.venda_item_lotes (lote_id);

-- ---------------------------------------------------------------- Regras de acesso (RLS)
-- Todos veem as vendas e os caixas de hoje (resumo do dia na tela do caixa); o gerente vê tudo.
alter table public.caixas enable row level security;
alter table public.vendas enable row level security;
alter table public.venda_itens enable row level security;
alter table public.venda_item_lotes enable row level security;

drop policy if exists "le caixas de hoje" on public.caixas;
create policy "le caixas de hoje"
    on public.caixas for select
    to authenticated
    using (public.eh_gerente() or (public.usuario_ativo() and dia = public.hoje_local()));

drop policy if exists "le vendas de hoje" on public.vendas;
create policy "le vendas de hoje"
    on public.vendas for select
    to authenticated
    using (public.eh_gerente() or (public.usuario_ativo() and dia = public.hoje_local()));

drop policy if exists "le itens das vendas de hoje" on public.venda_itens;
create policy "le itens das vendas de hoje"
    on public.venda_itens for select
    to authenticated
    using (exists (select 1 from public.vendas v where v.id = venda_id
                   and (public.eh_gerente() or (public.usuario_ativo() and v.dia = public.hoje_local()))));

drop policy if exists "gerente le baixas por lote" on public.venda_item_lotes;
create policy "gerente le baixas por lote"
    on public.venda_item_lotes for select
    to authenticated
    using (public.eh_gerente());

-- ---------------------------------------------------------------- Funções do caixa

-- Abre o caixa de hoje de quem chamou (se já estiver aberto, devolve o mesmo). Vendas abertas de dias
-- anteriores (programa fechado no meio da venda) são canceladas: deixam de reservar.
create or replace function public.abrir_caixa(p_troco numeric)
returns bigint
language plpgsql
security definer
set search_path = public
as $$
declare
    v_caixa bigint;
    v_nome text;
begin
    select nome into v_nome from public.perfis where id = auth.uid() and ativo;
    if not found then
        raise exception 'Usuário desativado ou sem perfil. Fale com o gerente.' using errcode = '42501';
    end if;
    if p_troco is null or p_troco < 0 or p_troco > 100000 then
        raise exception 'Digite um troco inicial válido, por exemplo 150,00.' using errcode = '22023';
    end if;

    update public.vendas
    set situacao = 'cancelada', cancelada_em = now(), cancelada_por = 'Venda não terminada no dia'
    where operador_id = auth.uid() and situacao = 'aberta' and dia < public.hoje_local();
    delete from public.venda_itens i using public.vendas v
    where i.venda_id = v.id and v.operador_id = auth.uid() and v.cancelada_por = 'Venda não terminada no dia';

    select id into v_caixa from public.caixas where operador_id = auth.uid() and dia = public.hoje_local();
    if found then
        return v_caixa;
    end if;
    insert into public.caixas (operador_id, operador_nome, troco_inicial)
    values (auth.uid(), v_nome, round(p_troco, 2))
    returning id into v_caixa;
    return v_caixa;
end;
$$;

-- Quanto do produto ainda pode ser vendido: lotes dentro da validade menos o que está reservado em vendas
-- abertas de hoje (de qualquer caixa), sem contar o item p_sem_item (o que está sendo alterado).
create or replace function public.disponivel_para_venda(p_produto_id bigint, p_sem_item bigint default null)
returns numeric
language sql
stable
security definer
set search_path = public
as $$
    select coalesce((select sum(quantidade_restante) from public.lotes
                     where produto_id = p_produto_id and quantidade_restante > 0
                       and (validade is null or validade >= public.hoje_local())), 0)
         - coalesce((select sum(i.quantidade) from public.venda_itens i
                     join public.vendas v on v.id = i.venda_id
                     where i.produto_id = p_produto_id and v.situacao = 'aberta' and v.dia = public.hoje_local()
                       and i.id is distinct from p_sem_item), 0);
$$;

-- Põe (ou muda) um produto na venda aberta do caixa de quem chamou e reserva a quantidade.
-- p_somar = true soma à quantidade que já está na venda (leitor); false troca pela quantidade digitada.
-- Quantidade 0 tira o produto da venda. p_valor = preço impresso na etiqueta da balança (o subtotal fica igual
-- ao da etiqueta); null = quantidade x preço do cadastro.
-- Devolve {"venda_id", "item_id", "quantidade", "subtotal"} (item_id null quando o produto saiu da venda).
create or replace function public.reservar_item(
    p_produto_id bigint, p_quantidade numeric, p_somar boolean default false, p_valor numeric default null)
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
    v_subtotal numeric(10, 2);
    v_disponivel numeric;
    v_vencido numeric;
begin
    if not public.usuario_ativo() then
        raise exception 'Usuário desativado ou sem perfil. Fale com o gerente.' using errcode = '42501';
    end if;
    select * into v_caixa from public.caixas where operador_id = auth.uid() and dia = public.hoje_local();
    if not found or v_caixa.fechado_em is not null then
        raise exception 'Abra o caixa antes de vender.' using errcode = '22023';
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
    if v_qtd <= 0 then
        delete from public.venda_itens where id = v_item.id;
        update public.vendas set total = coalesce((select sum(subtotal) from public.venda_itens where venda_id = v_venda), 0)
        where id = v_venda;
        return jsonb_build_object('venda_id', v_venda, 'item_id', null, 'quantidade', 0, 'subtotal', 0);
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

    v_subtotal := case
        when p_valor is not null and p_somar and v_item.id is not null then v_item.subtotal + round(p_valor, 2)
        when p_valor is not null then round(p_valor, 2)
        else round(v_qtd * v_produto.preco, 2) end;

    if v_item.id is null then
        insert into public.venda_itens (venda_id, produto_id, nome_produto, unidade_medida, quantidade, preco_unitario, subtotal)
        values (v_venda, p_produto_id, v_produto.nome, v_produto.unidade_medida, v_qtd, v_produto.preco, v_subtotal)
        returning * into v_item;
    else
        update public.venda_itens set quantidade = v_qtd, subtotal = v_subtotal
        where id = v_item.id
        returning * into v_item;
    end if;
    update public.vendas set total = (select sum(subtotal) from public.venda_itens where venda_id = v_venda)
    where id = v_venda;
    return jsonb_build_object('venda_id', v_venda, 'item_id', v_item.id, 'quantidade', v_item.quantidade, 'subtotal', v_item.subtotal);
end;
$$;

-- Recebe o pagamento e só então dá baixa nos lotes (o que vence primeiro sai primeiro; lote vencido não entra).
-- p_recebido: dinheiro entregue pelo cliente (null = valor exato). Devolve {"id", "total", "troco"}.
create or replace function public.finalizar_venda(p_venda_id bigint, p_forma text, p_recebido numeric default null)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_venda public.vendas%rowtype;
    v_item public.venda_itens%rowtype;
    v_lote public.lotes%rowtype;
    v_falta numeric(12, 3);
    v_sai numeric(12, 3);
    v_total numeric(10, 2);
    v_recebido numeric(10, 2);
begin
    if not public.usuario_ativo() then
        raise exception 'Usuário desativado ou sem perfil. Fale com o gerente.' using errcode = '42501';
    end if;
    select * into v_venda from public.vendas where id = p_venda_id for update;
    if not found or v_venda.situacao <> 'aberta' then
        raise exception 'Esta venda não está mais aberta. Atualize a tela.' using errcode = '22023';
    end if;
    if v_venda.operador_id <> auth.uid() and not public.eh_gerente() then
        raise exception 'Esta venda é de outro caixa.' using errcode = '42501';
    end if;
    if p_forma is null or p_forma not in ('dinheiro', 'pix', 'debito', 'credito') then
        raise exception 'Escolha a forma de pagamento.' using errcode = '22023';
    end if;
    if not exists (select 1 from public.venda_itens where venda_id = p_venda_id) then
        raise exception 'Adicione pelo menos um produto à venda.' using errcode = '22023';
    end if;

    select sum(subtotal) into v_total from public.venda_itens where venda_id = p_venda_id;
    v_recebido := case when p_forma = 'dinheiro' then coalesce(round(p_recebido, 2), v_total) end;
    if p_forma = 'dinheiro' and v_recebido < v_total then
        raise exception 'O valor recebido é menor que o total (R$ %).', replace(to_char(v_total, 'FM999990.00'), '.', ',')
            using errcode = '22023';
    end if;

    for v_item in select * from public.venda_itens where venda_id = p_venda_id order by id loop
        if v_item.produto_id is null then
            raise exception '% foi excluído do estoque. Tire o item da venda.', v_item.nome_produto using errcode = '22023';
        end if;
        perform 1 from public.produtos where id = v_item.produto_id for update;
        v_falta := v_item.quantidade;
        for v_lote in
            select * from public.lotes
            where produto_id = v_item.produto_id and quantidade_restante > 0
              and (validade is null or validade >= public.hoje_local())
            order by validade nulls last, data_compra, id
            for update
        loop
            exit when v_falta <= 0;
            v_sai := least(v_falta, v_lote.quantidade_restante);
            update public.lotes set quantidade_restante = quantidade_restante - v_sai where id = v_lote.id;
            insert into public.venda_item_lotes (item_id, lote_id, quantidade) values (v_item.id, v_lote.id, v_sai);
            v_falta := v_falta - v_sai;
        end loop;
        if v_falta > 0 then
            raise exception 'Não há % suficiente nos lotes dentro da validade. Confira o item.', v_item.nome_produto
                using errcode = '22023';
        end if;
    end loop;

    update public.vendas
    set situacao = 'paga', forma_pagamento = p_forma, total = v_total, recebido = v_recebido,
        troco = case when v_recebido is not null then v_recebido - v_total else 0 end, paga_em = now()
    where id = p_venda_id;
    return jsonb_build_object('id', p_venda_id, 'total', v_total,
                              'troco', case when v_recebido is not null then v_recebido - v_total else 0 end);
end;
$$;

-- Venda aberta: tira todos os itens (nada sai do estoque). Venda paga: só o gerente; os itens voltam para os
-- mesmos lotes (lote excluído nesse meio-tempo vira um lote novo com a data de hoje).
create or replace function public.cancelar_venda(p_venda_id bigint)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
    v_venda public.vendas%rowtype;
    v_baixa record;
    v_nome text;
begin
    select nome into v_nome from public.perfis where id = auth.uid() and ativo;
    if not found then
        raise exception 'Usuário desativado ou sem perfil. Fale com o gerente.' using errcode = '42501';
    end if;
    select * into v_venda from public.vendas where id = p_venda_id for update;
    if not found or v_venda.situacao = 'cancelada' then
        raise exception 'Esta venda já foi cancelada. Atualize a tela.' using errcode = '22023';
    end if;

    if v_venda.situacao = 'aberta' then
        if v_venda.operador_id <> auth.uid() and not public.eh_gerente() then
            raise exception 'Esta venda é de outro caixa.' using errcode = '42501';
        end if;
        delete from public.venda_itens where venda_id = p_venda_id;
        update public.vendas set total = 0 where id = p_venda_id;
        return;
    end if;

    if not public.eh_gerente() then
        raise exception 'Só o gerente pode cancelar uma venda já paga.' using errcode = '42501';
    end if;
    for v_baixa in
        select b.lote_id, b.quantidade, i.produto_id from public.venda_item_lotes b
        join public.venda_itens i on i.id = b.item_id
        where i.venda_id = p_venda_id
    loop
        if v_baixa.lote_id is not null and exists (select 1 from public.lotes where id = v_baixa.lote_id) then
            update public.lotes
            set quantidade_restante = least(quantidade_inicial, quantidade_restante + v_baixa.quantidade)
            where id = v_baixa.lote_id;
        elsif v_baixa.produto_id is not null then
            insert into public.lotes (produto_id, quantidade_inicial, quantidade_restante, data_compra)
            values (v_baixa.produto_id, v_baixa.quantidade, v_baixa.quantidade, public.hoje_local());
        end if;
    end loop;
    update public.vendas set situacao = 'cancelada', cancelada_em = now(), cancelada_por = v_nome where id = p_venda_id;
end;
$$;

-- ---------------------------------------------------------------- Funcionários (só o gerente)

-- Lista para a tela Funcionários: dados do perfil, último acesso, se tem vendas e pedido de nova senha aberto.
create or replace function public.listar_funcionarios()
returns table (id uuid, usuario text, nome text, perfil text, ativo boolean, criado_em timestamptz,
               ultimo_acesso timestamptz, tem_vendas boolean, pediu_senha timestamptz)
language plpgsql
stable
security definer
set search_path = public
as $$
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente vê os funcionários.' using errcode = '42501';
    end if;
    return query
    select p.id, p.usuario, p.nome, p.perfil, p.ativo, p.criado_em,
           (select max(a.data_hora) from public.auditoria_login a where a.usuario = p.usuario and a.sucesso),
           exists (select 1 from public.caixas c where c.operador_id = p.id),
           (select max(s.data_hora) from public.pedidos_senha s where s.usuario = p.usuario and not s.resolvido)
    from public.perfis p
    order by p.ativo desc, p.nome;
end;
$$;

-- Dados da janela "Meu perfil" de quem chamou: acesso anterior a este e vendas pagas de hoje.
create or replace function public.meu_perfil()
returns jsonb
language sql
stable
security definer
set search_path = public
as $$
    select jsonb_build_object(
        'acesso_anterior', (select a.data_hora from public.auditoria_login a
                            where a.usuario = p.usuario and a.sucesso order by a.data_hora desc offset 1 limit 1),
        'criado_em', p.criado_em,
        'vendas_hoje', (select count(*) from public.vendas v
                        where v.operador_id = p.id and v.dia = public.hoje_local() and v.situacao = 'paga'),
        'total_hoje', (select coalesce(sum(v.total), 0) from public.vendas v
                       where v.operador_id = p.id and v.dia = public.hoje_local() and v.situacao = 'paga'))
    from public.perfis p
    where p.id = auth.uid() and p.ativo;
$$;

-- Sobra pelo menos um gerente ativo além de p_sem (quem está sendo rebaixado, desativado ou excluído)?
create or replace function public.outro_gerente_ativo(p_sem uuid)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
    select exists (select 1 from public.perfis where perfil = 'gerente' and ativo and id <> p_sem);
$$;

-- Cria o usuário no login do Supabase (usuario@mercado.local, já confirmado, como o script 002) e o perfil.
create or replace function public.criar_funcionario(p_usuario text, p_nome text, p_perfil text, p_senha text)
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
    v_usuario text := lower(trim(p_usuario));
    v_id uuid := gen_random_uuid();
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente cadastra funcionários.' using errcode = '42501';
    end if;
    if v_usuario !~ '^[a-z0-9][a-z0-9._-]{2,39}$' then
        raise exception 'Usuário inválido: use de 3 a 40 letras sem acento, números, ponto ou traço (ex.: ana.caixa).'
            using errcode = '22023';
    end if;
    if length(trim(coalesce(p_nome, ''))) not between 1 and 60 then
        raise exception 'Digite o nome do funcionário.' using errcode = '22023';
    end if;
    if p_perfil not in ('gerente', 'operador') then
        raise exception 'Escolha a função.' using errcode = '22023';
    end if;
    if length(coalesce(p_senha, '')) < 8 then
        raise exception 'A senha precisa ter pelo menos 8 caracteres.' using errcode = '22023';
    end if;
    if exists (select 1 from auth.users where email = v_usuario || '@mercado.local')
       or exists (select 1 from public.perfis where usuario = v_usuario) then
        raise exception 'Já existe um funcionário com o usuário %.', v_usuario using errcode = '22023';
    end if;

    insert into auth.users (
        instance_id, id, aud, role, email, encrypted_password, email_confirmed_at,
        raw_app_meta_data, raw_user_meta_data, created_at, updated_at,
        confirmation_token, email_change, email_change_token_new, recovery_token
    ) values (
        '00000000-0000-0000-0000-000000000000', v_id, 'authenticated', 'authenticated',
        v_usuario || '@mercado.local', extensions.crypt(p_senha, extensions.gen_salt('bf')), now(),
        '{"provider":"email","providers":["email"]}', jsonb_build_object('nome', trim(p_nome)), now(), now(),
        '', '', '', ''
    );
    insert into auth.identities (id, user_id, provider_id, identity_data, provider, last_sign_in_at, created_at, updated_at)
    values (
        gen_random_uuid(), v_id, v_id::text,
        jsonb_build_object('sub', v_id::text, 'email', v_usuario || '@mercado.local', 'email_verified', true),
        'email', now(), now(), now()
    );
    -- O gatilho do 001 já criou o perfil como operador
    update public.perfis set nome = trim(p_nome), perfil = p_perfil, ativo = true where id = v_id;
    return v_id;
end;
$$;

-- Troca nome, função e situação (ativo). O gerente não rebaixa nem desativa a si mesmo, e sempre sobra um gerente ativo.
create or replace function public.editar_funcionario(p_id uuid, p_nome text, p_perfil text, p_ativo boolean)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
    v_atual public.perfis%rowtype;
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente altera funcionários.' using errcode = '42501';
    end if;
    select * into v_atual from public.perfis where id = p_id for update;
    if not found then
        raise exception 'Este funcionário não existe mais. Atualize a tela.' using errcode = '22023';
    end if;
    if length(trim(coalesce(p_nome, ''))) not between 1 and 60 then
        raise exception 'Digite o nome do funcionário.' using errcode = '22023';
    end if;
    if p_perfil not in ('gerente', 'operador') then
        raise exception 'Escolha a função.' using errcode = '22023';
    end if;
    if p_id = auth.uid() and (p_perfil <> 'gerente' or not p_ativo) then
        raise exception 'Você não pode tirar o seu próprio acesso de gerente. Peça para outro gerente.' using errcode = '22023';
    end if;
    if v_atual.perfil = 'gerente' and v_atual.ativo and (p_perfil <> 'gerente' or not p_ativo)
       and not public.outro_gerente_ativo(p_id) then
        raise exception 'O mercado precisa de pelo menos um gerente ativo.' using errcode = '22023';
    end if;
    update public.perfis set nome = trim(p_nome), perfil = p_perfil, ativo = p_ativo where id = p_id;
    update auth.users set raw_user_meta_data = coalesce(raw_user_meta_data, '{}'::jsonb) || jsonb_build_object('nome', trim(p_nome))
    where id = p_id;
end;
$$;

-- Nova senha dada pelo gerente (o funcionário troca depois em "Meu perfil"). Resolve os pedidos de nova senha.
create or replace function public.redefinir_senha_funcionario(p_id uuid, p_senha text)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
    v_usuario text;
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente redefine senhas.' using errcode = '42501';
    end if;
    if length(coalesce(p_senha, '')) < 8 then
        raise exception 'A senha precisa ter pelo menos 8 caracteres.' using errcode = '22023';
    end if;
    select usuario into v_usuario from public.perfis where id = p_id;
    if not found then
        raise exception 'Este funcionário não existe mais. Atualize a tela.' using errcode = '22023';
    end if;
    update auth.users set encrypted_password = extensions.crypt(p_senha, extensions.gen_salt('bf')), updated_at = now()
    where id = p_id;
    update public.pedidos_senha set resolvido = true where usuario = v_usuario and not resolvido;
end;
$$;

-- Remove o funcionário: quem já abriu caixa fica desativado (o histórico de vendas continua no nome dele);
-- quem nunca vendeu é apagado de vez. Devolve 'desativado' ou 'excluido'.
create or replace function public.remover_funcionario(p_id uuid)
returns text
language plpgsql
security definer
set search_path = public
as $$
declare
    v_atual public.perfis%rowtype;
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente remove funcionários.' using errcode = '42501';
    end if;
    if p_id = auth.uid() then
        raise exception 'Você não pode remover a si mesmo. Peça para outro gerente.' using errcode = '22023';
    end if;
    select * into v_atual from public.perfis where id = p_id for update;
    if not found then
        raise exception 'Este funcionário não existe mais. Atualize a tela.' using errcode = '22023';
    end if;
    if v_atual.perfil = 'gerente' and v_atual.ativo and not public.outro_gerente_ativo(p_id) then
        raise exception 'O mercado precisa de pelo menos um gerente ativo.' using errcode = '22023';
    end if;
    if exists (select 1 from public.caixas where operador_id = p_id) then
        update public.perfis set ativo = false where id = p_id;
        return 'desativado';
    end if;
    delete from auth.users where id = p_id;   -- apaga o perfil junto (on delete cascade)
    return 'excluido';
end;
$$;

-- Só usuários logados chamam as funções (o anônimo da tela de login não).
revoke execute on function public.abrir_caixa(numeric) from public, anon;
revoke execute on function public.disponivel_para_venda(bigint, bigint) from public, anon;
revoke execute on function public.reservar_item(bigint, numeric, boolean, numeric) from public, anon;
revoke execute on function public.finalizar_venda(bigint, text, numeric) from public, anon;
revoke execute on function public.cancelar_venda(bigint) from public, anon;
revoke execute on function public.listar_funcionarios() from public, anon;
revoke execute on function public.meu_perfil() from public, anon;
revoke execute on function public.outro_gerente_ativo(uuid) from public, anon;
revoke execute on function public.criar_funcionario(text, text, text, text) from public, anon;
revoke execute on function public.editar_funcionario(uuid, text, text, boolean) from public, anon;
revoke execute on function public.redefinir_senha_funcionario(uuid, text) from public, anon;
revoke execute on function public.remover_funcionario(uuid) from public, anon;
grant execute on function public.abrir_caixa(numeric) to authenticated;
grant execute on function public.disponivel_para_venda(bigint, bigint) to authenticated;
grant execute on function public.reservar_item(bigint, numeric, boolean, numeric) to authenticated;
grant execute on function public.finalizar_venda(bigint, text, numeric) to authenticated;
grant execute on function public.cancelar_venda(bigint) to authenticated;
grant execute on function public.listar_funcionarios() to authenticated;
grant execute on function public.meu_perfil() to authenticated;
grant execute on function public.outro_gerente_ativo(uuid) to authenticated;
grant execute on function public.criar_funcionario(text, text, text, text) to authenticated;
grant execute on function public.editar_funcionario(uuid, text, text, boolean) to authenticated;
grant execute on function public.redefinir_senha_funcionario(uuid, text) to authenticated;
grant execute on function public.remover_funcionario(uuid) to authenticated;
