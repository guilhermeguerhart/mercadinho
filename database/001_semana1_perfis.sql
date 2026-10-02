-- Semana 1: usuários, perfis (gerente e operador), auditoria de login e contatos da tela inicial.
-- Rodar no Supabase: painel do projeto > SQL Editor > New query > colar e executar.
--
-- Login por nome de usuário: o Supabase Auth exige e-mail, então cada usuário é criado com o
-- e-mail interno "<usuario>@mercado.local" (ex.: ana.caixa@mercado.local). O programa faz essa
-- conversão sozinho; o funcionário digita só "ana.caixa".

-- ---------------------------------------------------------------- Perfis
create table if not exists public.perfis (
    id         uuid primary key references auth.users (id) on delete cascade,
    usuario    text not null unique,
    nome       text not null,
    perfil     text not null default 'operador' check (perfil in ('gerente', 'operador')),
    ativo      boolean not null default true,
    criado_em  timestamptz not null default now()
);

-- Diz se o usuário logado é gerente. "security definer" evita recursão nas regras abaixo.
create or replace function public.eh_gerente()
returns boolean
language sql
stable
security definer
set search_path = public
as $$
    select exists (
        select 1 from public.perfis
        where id = auth.uid() and perfil = 'gerente' and ativo
    );
$$;

-- Regras de acesso (Row Level Security): sem elas, qualquer um com a chave pública leria tudo.
alter table public.perfis enable row level security;

drop policy if exists "usuario le o proprio perfil" on public.perfis;
create policy "usuario le o proprio perfil"
    on public.perfis for select
    using (id = auth.uid() or public.eh_gerente());

drop policy if exists "gerente altera perfis" on public.perfis;
create policy "gerente altera perfis"
    on public.perfis for update
    using (public.eh_gerente())
    with check (public.eh_gerente());

-- Quando um usuário é criado no Supabase Auth, cria o perfil dele como operador.
-- O usuário é a parte do e-mail antes do "@"; o nome vem dos metadados ("nome") ou do usuário.
create or replace function public.criar_perfil_novo_usuario()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
    v_usuario text := lower(split_part(new.email, '@', 1));
begin
    insert into public.perfis (id, usuario, nome)
    values (new.id, v_usuario, coalesce(new.raw_user_meta_data ->> 'nome', initcap(split_part(v_usuario, '.', 1))));
    return new;
end;
$$;

drop trigger if exists ao_criar_usuario on auth.users;
create trigger ao_criar_usuario
    after insert on auth.users
    for each row execute function public.criar_perfil_novo_usuario();

-- ---------------------------------------------------------------- Auditoria de login
-- Cada tentativa de login (certa ou errada) fica registrada. Só o gerente consegue ler.
create table if not exists public.auditoria_login (
    id         bigint generated always as identity primary key,
    usuario    text not null,
    sucesso    boolean not null,
    data_hora  timestamptz not null default now()
);

alter table public.auditoria_login enable row level security;

drop policy if exists "qualquer um registra tentativa" on public.auditoria_login;
create policy "qualquer um registra tentativa"
    on public.auditoria_login for insert
    with check (data_hora > now() - interval '1 minute' and length(usuario) <= 100);

drop policy if exists "gerente le auditoria" on public.auditoria_login;
create policy "gerente le auditoria"
    on public.auditoria_login for select
    using (public.eh_gerente());

-- ---------------------------------------------------------------- Pedidos de nova senha
-- "Esqueci minha senha": o pedido fica aqui para o gerente redefinir a senha no Supabase.
create table if not exists public.pedidos_senha (
    id         bigint generated always as identity primary key,
    usuario    text not null,
    resolvido  boolean not null default false,
    data_hora  timestamptz not null default now()
);

alter table public.pedidos_senha enable row level security;

drop policy if exists "qualquer um pede nova senha" on public.pedidos_senha;
create policy "qualquer um pede nova senha"
    on public.pedidos_senha for insert
    with check (not resolvido and length(usuario) <= 100);

drop policy if exists "gerente gerencia pedidos de senha" on public.pedidos_senha;
create policy "gerente gerencia pedidos de senha"
    on public.pedidos_senha for all
    using (public.eh_gerente())
    with check (public.eh_gerente());

-- ---------------------------------------------------------------- Contatos da tela inicial
-- Mensagens da janela "Fale com a gente". Só o gerente consegue ler.
create table if not exists public.contatos (
    id         bigint generated always as identity primary key,
    nome       text not null check (length(nome) between 1 and 120),
    email      text not null check (length(email) between 3 and 200),
    mensagem   text not null check (length(mensagem) between 1 and 2000),
    data_hora  timestamptz not null default now()
);

alter table public.contatos enable row level security;

drop policy if exists "qualquer um envia contato" on public.contatos;
create policy "qualquer um envia contato"
    on public.contatos for insert
    with check (true);

drop policy if exists "gerente le contatos" on public.contatos;
create policy "gerente le contatos"
    on public.contatos for select
    using (public.eh_gerente());

-- ---------------------------------------------------------------- Usuários
-- 1. Authentication > Users > Add user > Create new user, marcando "Auto Confirm User":
--      marcos.gerente@mercado.local   (gerente)
--      ana.caixa@mercado.local        (operador)
-- 2. Tornar o gerente gerente (rodar depois de criar os usuários):
-- update public.perfis set perfil = 'gerente' where usuario = 'marcos.gerente';
