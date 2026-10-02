-- Semana 1: perfis de usuário (caixa e dono)
-- Rodar no Supabase: painel do projeto > SQL Editor > New query > colar e executar.

-- Cada usuário do Supabase Auth ganha uma linha aqui com o seu papel no sistema.
create table if not exists public.perfis (
    id         uuid primary key references auth.users (id) on delete cascade,
    nome       text not null,
    papel      text not null default 'caixa' check (papel in ('caixa', 'dono')),
    ativo      boolean not null default true,
    criado_em  timestamptz not null default now()
);

-- Diz se o usuário logado é dono. "security definer" evita recursão nas regras abaixo.
create or replace function public.eh_dono()
returns boolean
language sql
stable
security definer
set search_path = public
as $$
    select exists (
        select 1 from public.perfis
        where id = auth.uid() and papel = 'dono' and ativo
    );
$$;

-- Regras de acesso (Row Level Security): sem elas, qualquer um com a chave pública leria tudo.
alter table public.perfis enable row level security;

drop policy if exists "usuario le o proprio perfil" on public.perfis;
create policy "usuario le o proprio perfil"
    on public.perfis for select
    using (id = auth.uid() or public.eh_dono());

drop policy if exists "dono altera perfis" on public.perfis;
create policy "dono altera perfis"
    on public.perfis for update
    using (public.eh_dono())
    with check (public.eh_dono());

-- Quando um usuário é criado no Supabase Auth, cria o perfil dele como caixa.
-- O nome vem dos metadados ("nome") ou, se não houver, do e-mail.
create or replace function public.criar_perfil_novo_usuario()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
    insert into public.perfis (id, nome)
    values (new.id, coalesce(new.raw_user_meta_data ->> 'nome', split_part(new.email, '@', 1)));
    return new;
end;
$$;

drop trigger if exists ao_criar_usuario on auth.users;
create trigger ao_criar_usuario
    after insert on auth.users
    for each row execute function public.criar_perfil_novo_usuario();

-- Para tornar alguém dono (rodar uma vez, trocando o e-mail):
-- update public.perfis set papel = 'dono'
-- where id = (select id from auth.users where email = 'dono@mercadinho.com');
