-- Semana 4 (ajustes): completa o módulo de fornecedores.
-- Rodar no Supabase: painel do projeto > SQL Editor > New query > colar e executar (depois do 004).
-- Pode ser rodado de novo sem perder dados (só cria o que ainda não existe e atualiza as funções).
--
-- 1. Editar pedido pendente (função editar_pedido).
-- 2. Entrega parcial: número da nota, motivo da diferença de cada item e o que faltou vira um pedido novo.
-- 3. Preço de custo no produto, no item do pedido e no lote.
-- 4. Dias da semana em que o fornecedor entrega e valor mínimo do pedido.
-- 8. Mais de um fornecedor por produto, com o último custo de cada um (produto_fornecedores).

-- ---------------------------------------------------------------- Colunas novas
-- Passo 2: entrega parcial
alter table public.pedidos add column if not exists numero_nota text
    check (numero_nota is null or length(trim(numero_nota)) between 1 and 20);
-- Pedido criado com o que faltou de outro pedido
alter table public.pedidos add column if not exists pedido_origem_id bigint
    references public.pedidos (id) on delete set null;
alter table public.pedido_itens add column if not exists motivo_diferenca text
    check (motivo_diferenca is null or motivo_diferenca in ('faltou', 'avariado', 'vencido', 'errado', 'outro'));

-- Passo 3: preço de custo (por unidade, ou por kg nos produtos por quilo)
alter table public.produtos add column if not exists preco_custo numeric(10, 2)
    check (preco_custo is null or preco_custo >= 0);
alter table public.pedido_itens add column if not exists preco_custo numeric(10, 2)
    check (preco_custo is null or preco_custo >= 0);
alter table public.lotes add column if not exists preco_custo numeric(10, 2)
    check (preco_custo is null or preco_custo >= 0);

-- Passo 4: dias da semana da entrega ("1,3" = segunda e quarta; 0 = domingo) e pedido mínimo em R$
alter table public.fornecedores add column if not exists dias_entrega text
    check (dias_entrega is null or dias_entrega ~ '^[0-6](,[0-6])*$');
alter table public.fornecedores add column if not exists pedido_minimo numeric(10, 2)
    check (pedido_minimo is null or pedido_minimo >= 0);

-- ---------------------------------------------------------------- Passo 8: produto x fornecedores
-- Outros fornecedores de quem o produto também é comprado (o principal continua em produtos.fornecedor_id),
-- com o último custo e a última compra de cada um, para comparar na hora de pedir.
create table if not exists public.produto_fornecedores (
    produto_id     bigint not null references public.produtos (id) on delete cascade,
    fornecedor_id  bigint not null references public.fornecedores (id) on delete cascade,
    ultimo_custo   numeric(10, 2) check (ultimo_custo is null or ultimo_custo >= 0),
    ultima_compra  date,
    primary key (produto_id, fornecedor_id)
);

create index if not exists produto_fornecedores_fornecedor on public.produto_fornecedores (fornecedor_id);

alter table public.produto_fornecedores enable row level security;

drop policy if exists "gerente le e altera produto_fornecedores" on public.produto_fornecedores;
create policy "gerente le e altera produto_fornecedores"
    on public.produto_fornecedores for all
    to authenticated
    using (public.eh_gerente())
    with check (public.eh_gerente());

-- ---------------------------------------------------------------- Funções (tudo ou nada)
-- Rodam com o login de quem chamou (security invoker): as regras de acesso continuam valendo.

-- Cria o pedido. itens = [{"produto_id": 1, "quantidade": 20, "custo": 4.50}, ...] (custo opcional).
-- Também liga o produto ao fornecedor em produto_fornecedores.
create or replace function public.criar_pedido(
    p_fornecedor_id bigint, p_previsao date, p_observacao text, p_itens jsonb)
returns bigint
language plpgsql
security invoker
set search_path = public
as $$
declare
    v_pedido bigint;
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente pode criar pedidos.' using errcode = '42501';
    end if;
    insert into public.pedidos (fornecedor_id, previsao_entrega, observacao)
    values (p_fornecedor_id, p_previsao, nullif(trim(p_observacao), ''))
    returning id into v_pedido;
    perform public.gravar_itens_pedido(v_pedido, p_fornecedor_id, p_itens);
    return v_pedido;
end;
$$;

-- Troca previsão, observação e itens de um pedido que ainda não chegou.
create or replace function public.editar_pedido(
    p_pedido_id bigint, p_previsao date, p_observacao text, p_itens jsonb)
returns void
language plpgsql
security invoker
set search_path = public
as $$
declare
    v_pedido public.pedidos%rowtype;
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente pode alterar pedidos.' using errcode = '42501';
    end if;
    select * into v_pedido from public.pedidos where id = p_pedido_id for update;
    if not found or v_pedido.situacao <> 'pendente' then
        raise exception 'Só dá para alterar pedido que ainda não chegou.' using errcode = '22023';
    end if;
    update public.pedidos
    set previsao_entrega = p_previsao, observacao = nullif(trim(p_observacao), '')
    where id = p_pedido_id;
    delete from public.pedido_itens where pedido_id = p_pedido_id;
    perform public.gravar_itens_pedido(p_pedido_id, v_pedido.fornecedor_id, p_itens);
end;
$$;

-- Usada pelas duas funções acima: grava os itens e liga cada produto ao fornecedor.
create or replace function public.gravar_itens_pedido(p_pedido_id bigint, p_fornecedor_id bigint, p_itens jsonb)
returns void
language plpgsql
security invoker
set search_path = public
as $$
declare
    v_item jsonb;
    v_produto public.produtos%rowtype;
begin
    if p_itens is null or jsonb_array_length(p_itens) = 0 then
        raise exception 'O pedido precisa de pelo menos um item.' using errcode = '22023';
    end if;
    for v_item in select * from jsonb_array_elements(p_itens) loop
        select * into v_produto from public.produtos where id = (v_item ->> 'produto_id')::bigint;
        if not found then
            raise exception 'Um dos produtos do pedido não existe mais. Confira os itens.' using errcode = '22023';
        end if;
        insert into public.pedido_itens (pedido_id, produto_id, nome_produto, unidade_medida, quantidade, preco_custo)
        values (p_pedido_id, v_produto.id, v_produto.nome, v_produto.unidade_medida,
                (v_item ->> 'quantidade')::numeric, nullif(v_item ->> 'custo', '')::numeric);
        insert into public.produto_fornecedores (produto_id, fornecedor_id)
        values (v_produto.id, p_fornecedor_id)
        on conflict do nothing;
    end loop;
end;
$$;

-- Recebimento (substitui o registrar_entrega do 004, que continua existindo).
-- itens = [{"item_id": 1, "recebido": 18, "validade": "2026-12-31", "custo": 4.50, "motivo": "avariado"}, ...]
-- Cria um lote por item recebido (com o custo), atualiza o custo do produto e o último custo com o fornecedor,
-- guarda a nota e marca o pedido como entregue. Com p_resto_pendente, o que faltou vira um pedido novo
-- (pendente, previsão p_previsao_resto). Devolve o id desse pedido novo, ou null.
create or replace function public.registrar_recebimento(
    p_pedido_id bigint, p_entregue_em date, p_numero_nota text, p_itens jsonb,
    p_resto_pendente boolean, p_previsao_resto date)
returns bigint
language plpgsql
security invoker
set search_path = public
as $$
declare
    v_pedido public.pedidos%rowtype;
    v_item jsonb;
    v_linha public.pedido_itens%rowtype;
    v_recebido numeric(12, 3);
    v_custo numeric(10, 2);
    v_resto jsonb := '[]'::jsonb;
    v_novo bigint;
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente pode registrar entregas.' using errcode = '42501';
    end if;
    select * into v_pedido from public.pedidos where id = p_pedido_id for update;
    if not found or v_pedido.situacao <> 'pendente' then
        raise exception 'Este pedido não está mais pendente.' using errcode = '22023';
    end if;

    for v_item in select * from jsonb_array_elements(p_itens) loop
        select * into v_linha from public.pedido_itens
        where id = (v_item ->> 'item_id')::bigint and pedido_id = p_pedido_id;
        if not found then continue; end if;

        v_recebido := coalesce((v_item ->> 'recebido')::numeric, 0);
        v_custo := coalesce(nullif(v_item ->> 'custo', '')::numeric, v_linha.preco_custo);
        update public.pedido_itens
        set quantidade_recebida = v_recebido,
            preco_custo = v_custo,
            motivo_diferenca = case when v_recebido <> v_linha.quantidade
                                    then coalesce(nullif(v_item ->> 'motivo', ''), 'outro') end
        where id = v_linha.id;

        if v_linha.produto_id is not null then
            if v_recebido > 0 then
                insert into public.lotes (produto_id, quantidade_inicial, quantidade_restante, data_compra, validade, preco_custo)
                values (v_linha.produto_id, v_recebido, v_recebido, p_entregue_em, (v_item ->> 'validade')::date, v_custo);
                if v_custo is not null then
                    update public.produtos set preco_custo = v_custo where id = v_linha.produto_id;
                end if;
                insert into public.produto_fornecedores (produto_id, fornecedor_id, ultimo_custo, ultima_compra)
                values (v_linha.produto_id, v_pedido.fornecedor_id, v_custo, p_entregue_em)
                on conflict (produto_id, fornecedor_id) do update
                set ultimo_custo = coalesce(excluded.ultimo_custo, produto_fornecedores.ultimo_custo),
                    ultima_compra = excluded.ultima_compra;
            end if;
            if p_resto_pendente and v_recebido < v_linha.quantidade then
                v_resto := v_resto || jsonb_build_array(jsonb_build_object(
                    'produto_id', v_linha.produto_id, 'quantidade', v_linha.quantidade - v_recebido, 'custo', v_custo));
            end if;
        end if;
    end loop;

    update public.pedidos
    set situacao = 'entregue', entregue_em = p_entregue_em, numero_nota = nullif(trim(p_numero_nota), '')
    where id = p_pedido_id;

    if jsonb_array_length(v_resto) > 0 then
        insert into public.pedidos (fornecedor_id, previsao_entrega, observacao, pedido_origem_id)
        values (v_pedido.fornecedor_id, p_previsao_resto,
                'O que faltou do pedido de ' || to_char(v_pedido.data_pedido, 'DD/MM/YYYY'), p_pedido_id)
        returning id into v_novo;
        perform public.gravar_itens_pedido(v_novo, v_pedido.fornecedor_id, v_resto);
    end if;
    return v_novo;
end;
$$;

-- Define de quais fornecedores (além do principal) o produto é comprado; mantém o último custo dos que ficam.
create or replace function public.definir_fornecedores_produto(p_produto_id bigint, p_fornecedores bigint[])
returns void
language plpgsql
security invoker
set search_path = public
as $$
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente pode alterar fornecedores do produto.' using errcode = '42501';
    end if;
    delete from public.produto_fornecedores
    where produto_id = p_produto_id and not (fornecedor_id = any (coalesce(p_fornecedores, '{}')));
    insert into public.produto_fornecedores (produto_id, fornecedor_id)
    select p_produto_id, f from unnest(coalesce(p_fornecedores, '{}')) as f
    on conflict do nothing;
end;
$$;

-- Só usuários logados podem chamar as funções (e elas mesmas conferem se é gerente).
revoke execute on function public.criar_pedido(bigint, date, text, jsonb) from public, anon;
revoke execute on function public.editar_pedido(bigint, date, text, jsonb) from public, anon;
revoke execute on function public.gravar_itens_pedido(bigint, bigint, jsonb) from public, anon;
revoke execute on function public.registrar_recebimento(bigint, date, text, jsonb, boolean, date) from public, anon;
revoke execute on function public.definir_fornecedores_produto(bigint, bigint[]) from public, anon;
grant execute on function public.criar_pedido(bigint, date, text, jsonb) to authenticated;
grant execute on function public.editar_pedido(bigint, date, text, jsonb) to authenticated;
grant execute on function public.gravar_itens_pedido(bigint, bigint, jsonb) to authenticated;
grant execute on function public.registrar_recebimento(bigint, date, text, jsonb, boolean, date) to authenticated;
grant execute on function public.definir_fornecedores_produto(bigint, bigint[]) to authenticated;
