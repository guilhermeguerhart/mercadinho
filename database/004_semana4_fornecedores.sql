-- Semana 4: fornecedores e pedidos de reposição.
-- Rodar no Supabase: painel do projeto > SQL Editor > New query > colar e executar (depois do 001, 002 e 003).
-- Pode ser rodado de novo sem perder dados (só cria o que ainda não existe).
--
-- Cada fornecedor entrega algumas categorias do estoque (ex.: "Grãos, Mercearia"). Um produto pode ter um
-- fornecedor principal (produtos.fornecedor_id); sem ele, o produto é de quem entrega a categoria dele.
-- Cada pedido tem vários itens. Registrar a entrega cria os lotes no estoque (função registrar_entrega).
-- Fornecedor com pedidos não é apagado: fica desativado (ativo = false) e o histórico continua.

-- ---------------------------------------------------------------- Fornecedores
create table if not exists public.fornecedores (
    id                  bigint generated always as identity primary key,
    nome                text not null check (length(trim(nome)) between 1 and 80),
    -- Telefone com DDD (10 ou 11 dígitos) e, se for outro número, o WhatsApp (pode ter o 55 do Brasil).
    telefone            text not null check (length(regexp_replace(telefone, '\D', '', 'g')) between 10 and 11),
    whatsapp            text check (whatsapp is null or length(regexp_replace(whatsapp, '\D', '', 'g')) between 10 and 13),
    email               text check (email is null or email ~ '^[^\s@]+@[^\s@]+\.[^\s@]+$'),
    contato             text check (contato is null or length(trim(contato)) between 1 and 60),
    -- O que entrega: categorias do estoque separadas por vírgula.
    categorias          text not null check (length(trim(categorias)) between 1 and 200),
    -- Prazo padrão de entrega, para os produtos sem prazo próprio (produtos.prazo_entrega_dias). Vazio = 3 dias.
    prazo_entrega_dias  integer check (prazo_entrega_dias is null or prazo_entrega_dias between 0 and 90),
    -- Mensagem de pedido enviada pelo WhatsApp ou e-mail. Vazio = modelo padrão do programa.
    modelo_mensagem     text check (modelo_mensagem is null or length(modelo_mensagem) <= 1000),
    observacao          text check (observacao is null or length(observacao) <= 300),
    ativo               boolean not null default true,
    criado_em           timestamptz not null default now()
);

create unique index if not exists fornecedores_nome_unico on public.fornecedores (lower(trim(nome)));

-- Por onde mandar os pedidos: 'whatsapp' ou 'email' (o outro continua disponível na hora de enviar).
alter table public.fornecedores add column if not exists envio_preferido text not null default 'whatsapp'
    check (envio_preferido in ('whatsapp', 'email'));

-- Fornecedor principal do produto (opcional). Excluir o fornecedor só tira a ligação.
alter table public.produtos add column if not exists fornecedor_id bigint
    references public.fornecedores (id) on delete set null;

create index if not exists produtos_fornecedor on public.produtos (fornecedor_id);

-- Prazo de entrega do produto em dias (varia de produto para produto). Vazio = prazo padrão do fornecedor.
alter table public.produtos add column if not exists prazo_entrega_dias integer
    check (prazo_entrega_dias is null or prazo_entrega_dias between 0 and 90);

-- "Pedir antes de acabar": o aviso antecipado é calculado sozinho (ponto de pedido =
-- mínimo + o que sai por dia x prazo de entrega), no lugar do número digitado em estoque_aviso.
alter table public.produtos add column if not exists pedir_antes boolean not null default false;

-- ---------------------------------------------------------------- Pedidos
create table if not exists public.pedidos (
    id                bigint generated always as identity primary key,
    -- Não deixa apagar fornecedor com pedidos (o programa desativa em vez de apagar).
    fornecedor_id     bigint not null references public.fornecedores (id) on delete restrict,
    data_pedido       date not null default current_date,
    previsao_entrega  date,
    situacao          text not null default 'pendente' check (situacao in ('pendente', 'entregue', 'cancelado')),
    entregue_em       date,
    observacao        text check (observacao is null or length(observacao) <= 300),
    criado_por        uuid default auth.uid(),
    criado_em         timestamptz not null default now(),
    check (previsao_entrega is null or previsao_entrega >= data_pedido),
    check ((situacao = 'entregue') = (entregue_em is not null)),
    check (entregue_em is null or entregue_em >= data_pedido)
);

create index if not exists pedidos_fornecedor on public.pedidos (fornecedor_id);

-- Itens do pedido. O nome e a unidade ficam guardados para o histórico continuar certo se o produto mudar
-- ou for excluído. quantidade_recebida é preenchida na entrega (0 = não veio).
create table if not exists public.pedido_itens (
    id                   bigint generated always as identity primary key,
    pedido_id            bigint not null references public.pedidos (id) on delete cascade,
    produto_id           bigint references public.produtos (id) on delete set null,
    nome_produto         text not null check (length(trim(nome_produto)) between 1 and 80),
    unidade_medida       text not null default 'un' check (unidade_medida in ('un', 'kg')),
    quantidade           numeric(12, 3) not null check (quantidade > 0),
    quantidade_recebida  numeric(12, 3) check (quantidade_recebida is null or quantidade_recebida >= 0)
);

create index if not exists pedido_itens_pedido on public.pedido_itens (pedido_id);
create index if not exists pedido_itens_produto on public.pedido_itens (produto_id);

-- ---------------------------------------------------------------- Regras de acesso (RLS)
-- Fornecedores e pedidos são só do gerente (igual ao site: o operador não vê esta tela).
alter table public.fornecedores enable row level security;
alter table public.pedidos enable row level security;
alter table public.pedido_itens enable row level security;

drop policy if exists "gerente le e altera fornecedores" on public.fornecedores;
create policy "gerente le e altera fornecedores"
    on public.fornecedores for all
    to authenticated
    using (public.eh_gerente())
    with check (public.eh_gerente());

drop policy if exists "gerente le e altera pedidos" on public.pedidos;
create policy "gerente le e altera pedidos"
    on public.pedidos for all
    to authenticated
    using (public.eh_gerente())
    with check (public.eh_gerente());

drop policy if exists "gerente le e altera itens de pedido" on public.pedido_itens;
create policy "gerente le e altera itens de pedido"
    on public.pedido_itens for all
    to authenticated
    using (public.eh_gerente())
    with check (public.eh_gerente());

-- ---------------------------------------------------------------- Funções (tudo ou nada)
-- Rodam com o login de quem chamou (security invoker), então as regras acima continuam valendo.

-- Cria o pedido com os itens de uma vez. itens = [{"produto_id": 1, "quantidade": 20}, ...]
create or replace function public.criar_pedido(
    p_fornecedor_id bigint, p_previsao date, p_observacao text, p_itens jsonb)
returns bigint
language plpgsql
security invoker
set search_path = public
as $$
declare
    v_pedido bigint;
    v_item jsonb;
    v_produto public.produtos%rowtype;
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente pode criar pedidos.' using errcode = '42501';
    end if;
    if p_itens is null or jsonb_array_length(p_itens) = 0 then
        raise exception 'O pedido precisa de pelo menos um item.' using errcode = '22023';
    end if;

    insert into public.pedidos (fornecedor_id, previsao_entrega, observacao)
    values (p_fornecedor_id, p_previsao, nullif(trim(p_observacao), ''))
    returning id into v_pedido;

    for v_item in select * from jsonb_array_elements(p_itens) loop
        select * into v_produto from public.produtos where id = (v_item ->> 'produto_id')::bigint;
        if not found then
            raise exception 'Um dos produtos do pedido não existe mais. Confira os itens.' using errcode = '22023';
        end if;
        insert into public.pedido_itens (pedido_id, produto_id, nome_produto, unidade_medida, quantidade)
        values (v_pedido, v_produto.id, v_produto.nome, v_produto.unidade_medida, (v_item ->> 'quantidade')::numeric);
    end loop;

    return v_pedido;
end;
$$;

-- Registra a entrega: guarda o que chegou de cada item, cria um lote para cada item recebido e marca o pedido
-- como entregue. itens = [{"item_id": 1, "recebido": 20, "validade": "2026-12-31"}, ...] (validade pode ser null)
create or replace function public.registrar_entrega(p_pedido_id bigint, p_entregue_em date, p_itens jsonb)
returns void
language plpgsql
security invoker
set search_path = public
as $$
declare
    v_pedido public.pedidos%rowtype;
    v_item jsonb;
    v_linha public.pedido_itens%rowtype;
    v_recebido numeric(12, 3);
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
        update public.pedido_itens set quantidade_recebida = v_recebido where id = v_linha.id;

        if v_recebido > 0 and v_linha.produto_id is not null then
            insert into public.lotes (produto_id, quantidade_inicial, quantidade_restante, data_compra, validade)
            values (v_linha.produto_id, v_recebido, v_recebido, p_entregue_em, (v_item ->> 'validade')::date);
        end if;
    end loop;

    update public.pedidos set situacao = 'entregue', entregue_em = p_entregue_em where id = p_pedido_id;
end;
$$;

-- Só usuários logados podem chamar as funções (e elas mesmas conferem se é gerente).
revoke execute on function public.criar_pedido(bigint, date, text, jsonb) from public, anon;
revoke execute on function public.registrar_entrega(bigint, date, jsonb) from public, anon;
grant execute on function public.criar_pedido(bigint, date, text, jsonb) to authenticated;
grant execute on function public.registrar_entrega(bigint, date, jsonb) to authenticated;
