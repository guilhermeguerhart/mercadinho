-- Semana 3: estoque (produtos e lotes).
-- Rodar no Supabase: painel do projeto > SQL Editor > New query > colar e executar (depois do 001 e 002).
-- Pode ser rodado de novo sem perder dados (só cria o que ainda não existe).
--
-- Cada produto tem vários lotes (cada compra é um lote, com quantidade e validade próprias).
-- A quantidade em estoque do produto é a soma da quantidade restante dos lotes.
-- Produtos do açougue (e outros a granel) são vendidos por quilo: unidade_medida = 'kg' e as quantidades
-- têm até 3 casas decimais (ex.: 12,350 kg). Os demais usam 'un' e quantidades inteiras.

-- ---------------------------------------------------------------- Produtos
create table if not exists public.produtos (
    id              bigint generated always as identity primary key,
    nome            text not null check (length(trim(nome)) between 1 and 80),
    categoria       text not null check (length(trim(categoria)) between 1 and 40),
    preco           numeric(10, 2) not null check (preco > 0),
    estoque_minimo  numeric(12, 3) not null default 0 check (estoque_minimo >= 0),
    criado_em       timestamptz not null default now()
);

-- Código de barras da caixa em que o produto chega (o leitor preenche) e quantas unidades vêm nela.
-- estoque_aviso: com essa quantidade ou menos aparece o aviso amarelo antecipado (antes do mínimo).
-- Vazio = o programa usa o mínimo + 50%.
alter table public.produtos add column if not exists codigo_barras_caixa text
    check (codigo_barras_caixa is null or length(trim(codigo_barras_caixa)) between 1 and 40);
alter table public.produtos add column if not exists unidades_por_caixa numeric(12, 3) not null default 1
    check (unidades_por_caixa > 0);
alter table public.produtos add column if not exists estoque_aviso numeric(12, 3)
    check (estoque_aviso is null or estoque_aviso >= 0);

-- Validade padrão: quantos dias depois da compra o produto vence (ex.: leite 120, carne 10).
-- Preenche sozinha a validade dos lotes novos. Vazio = produto sem validade pré-definida.
alter table public.produtos add column if not exists dias_validade integer
    check (dias_validade is null or dias_validade between 1 and 3650);

-- 'un' = vendido por unidade; 'kg' = vendido por quilo (preço, mínimo, aviso e lotes em kg).
alter table public.produtos add column if not exists unidade_medida text not null default 'un'
    check (unidade_medida in ('un', 'kg'));

-- Se o script já tinha sido rodado com quantidades inteiras, passa para decimal (não perde dados).
alter table public.produtos alter column estoque_minimo type numeric(12, 3);
alter table public.produtos alter column unidades_por_caixa type numeric(12, 3);
alter table public.produtos alter column estoque_aviso type numeric(12, 3);

-- Não deixa cadastrar dois produtos com o mesmo nome (sem diferenciar maiúsculas).
create unique index if not exists produtos_nome_unico on public.produtos (lower(trim(nome)));

-- Cada código de barras de caixa pertence a um produto só.
create unique index if not exists produtos_codigo_caixa_unico on public.produtos (trim(codigo_barras_caixa))
    where codigo_barras_caixa is not null;

-- ---------------------------------------------------------------- Lotes
create table if not exists public.lotes (
    id                   bigint generated always as identity primary key,
    produto_id           bigint not null references public.produtos (id) on delete cascade,
    quantidade_inicial   numeric(12, 3) not null check (quantidade_inicial > 0),
    quantidade_restante  numeric(12, 3) not null check (quantidade_restante >= 0),
    data_compra          date not null default current_date,
    validade             date,
    criado_em            timestamptz not null default now(),
    check (quantidade_restante <= quantidade_inicial),
    check (validade is null or validade >= data_compra)
);

create index if not exists lotes_produto on public.lotes (produto_id);

alter table public.lotes alter column quantidade_inicial type numeric(12, 3);
alter table public.lotes alter column quantidade_restante type numeric(12, 3);

-- ---------------------------------------------------------------- Regras de acesso (RLS)
-- Todo usuário logado pode ver o estoque (o caixa vai precisar). Só o gerente cadastra, altera e exclui.
alter table public.produtos enable row level security;
alter table public.lotes enable row level security;

drop policy if exists "usuario logado le produtos" on public.produtos;
create policy "usuario logado le produtos"
    on public.produtos for select
    to authenticated
    using (true);

drop policy if exists "gerente altera produtos" on public.produtos;
create policy "gerente altera produtos"
    on public.produtos for all
    to authenticated
    using (public.eh_gerente())
    with check (public.eh_gerente());

drop policy if exists "usuario logado le lotes" on public.lotes;
create policy "usuario logado le lotes"
    on public.lotes for select
    to authenticated
    using (true);

drop policy if exists "gerente altera lotes" on public.lotes;
create policy "gerente altera lotes"
    on public.lotes for all
    to authenticated
    using (public.eh_gerente())
    with check (public.eh_gerente());
