-- Usuários de TESTE (só para desenvolvimento). Rodar no SQL Editor DEPOIS do 001_semana1_perfis.sql.
-- Cria os usuários direto no Supabase Auth, já confirmados, e define o perfil de cada um.
-- Pode rodar mais de uma vez: quem já existe é ignorado.
-- Antes de usar o sistema de verdade, apague esses usuários (Authentication > Users).

do $$
declare
    u   record;
    uid uuid;
begin
    for u in
        select * from (values
            ('marcos.gerente', 'Marcos', 'Gerente#4821',  'gerente',  true),
            ('julia.gerente',  'Júlia',  'Gerente#7350',  'gerente',  true),
            ('ana.caixa',      'Ana',    'Caixa#1964',    'operador', true),
            ('pedro.caixa',    'Pedro',  'Caixa#5307',    'operador', true),
            ('carla.caixa',    'Carla',  'Caixa#8142',    'operador', false)  -- desativada, para testar o bloqueio
        ) as t(usuario, nome, senha, perfil, ativo)
    loop
        if exists (select 1 from auth.users where email = u.usuario || '@mercado.local') then
            continue;
        end if;

        uid := gen_random_uuid();

        insert into auth.users (
            instance_id, id, aud, role, email, encrypted_password, email_confirmed_at,
            raw_app_meta_data, raw_user_meta_data, created_at, updated_at,
            confirmation_token, email_change, email_change_token_new, recovery_token
        ) values (
            '00000000-0000-0000-0000-000000000000', uid, 'authenticated', 'authenticated',
            u.usuario || '@mercado.local', extensions.crypt(u.senha, extensions.gen_salt('bf')), now(),
            '{"provider":"email","providers":["email"]}', jsonb_build_object('nome', u.nome), now(), now(),
            '', '', '', ''
        );

        insert into auth.identities (id, user_id, provider_id, identity_data, provider, last_sign_in_at, created_at, updated_at)
        values (
            gen_random_uuid(), uid, uid::text,
            jsonb_build_object('sub', uid::text, 'email', u.usuario || '@mercado.local', 'email_verified', true),
            'email', now(), now(), now()
        );

        -- O gatilho do 001 já criou o perfil como operador; aqui ajusta perfil e situação.
        update public.perfis set perfil = u.perfil, ativo = u.ativo where id = uid;
    end loop;
end;
$$;

select usuario, nome, perfil, ativo from public.perfis order by perfil, usuario;
