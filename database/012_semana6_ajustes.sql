-- =====================================================================
-- Semana 6 (ajustes pedidos pelo Guilherme em 03/11). Rodar depois do 009. Pode rodar de novo.
--
-- 1. Dias passados só mudam com a senha do gerente: fechar o dia, reabrir o dia e fechar um caixa de um dia que já
--    passou pedem uma autorização "alterar_dia" (usuário e senha de um gerente, conferidos no banco), mesmo para o
--    gerente logado.
-- 2. Calendário de datas especiais (eventos_calendario): feriados nacionais, vésperas e datas festivas de 2021 a 2028,
--    com quanto o movimento costuma subir e o que mais vende. Usado pelos dados de exemplo (013) e pela previsão de
--    compras da semana 7.
-- =====================================================================

-- ---------------------------------------------------------------- 1. Senha do gerente para mexer em dias passados
alter table public.autorizacoes drop constraint if exists autorizacoes_acao_check;
alter table public.autorizacoes add constraint autorizacoes_acao_check
    check (acao in ('cancelar_item', 'cancelar_venda', 'desconto', 'sangria', 'alterar_dia'));

-- Igual ao 008, aceitando também 'alterar_dia'.
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
    if p_acao not in ('cancelar_item', 'cancelar_venda', 'desconto', 'sangria', 'alterar_dia') then
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

-- Usa uma autorização 'alterar_dia' (uma vez só, até 10 minutos). Aqui nem o gerente logado passa sem a senha.
create or replace function public.usar_autorizacao_dia(p_id bigint)
returns text
language plpgsql
security definer
set search_path = public
as $$
declare
    v_nome text;
begin
    update public.autorizacoes
    set usada = true
    where id = p_id and aprovada and not usada and acao = 'alterar_dia' and operador_id = auth.uid()
      and criada_em > now() - interval '10 minutes'
    returning gerente_nome into v_nome;
    if v_nome is null then
        raise exception 'Dia que já passou só muda com a senha do gerente.' using errcode = '22023';
    end if;
    return v_nome;
end;
$$;

-- fechar_dia, reabrir_dia e fechar_caixa ganham p_autorizacao (obrigatória para dias passados)
drop function if exists public.fechar_dia(date, text);
create or replace function public.fechar_dia(p_dia date default null, p_obs text default null, p_autorizacao bigint default null)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_dia date := coalesce(p_dia, public.hoje_local());
    v_nome text;
    v_abertos text;
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente fecha o dia.' using errcode = '42501';
    end if;
    select nome into v_nome from public.perfis where id = auth.uid();
    if v_dia > public.hoje_local() then
        raise exception 'Não dá para fechar um dia que ainda não chegou.' using errcode = '22023';
    end if;
    if exists (select 1 from public.fechamentos_dia where dia = v_dia and not automatico) then
        raise exception 'Este dia já foi fechado.' using errcode = '22023';
    end if;
    select string_agg(operador_nome, ', ' order by aberto_em) into v_abertos
    from public.caixas where dia = v_dia and fechado_em is null;
    if v_abertos is not null then
        raise exception 'Feche antes os caixas abertos: %.', v_abertos using errcode = '22023';
    end if;
    if v_dia < public.hoje_local() then
        perform public.usar_autorizacao_dia(p_autorizacao);
    end if;

    delete from public.fechamentos_dia where dia = v_dia and automatico;
    insert into public.fechamentos_dia (dia, fechado_por, obs, resumo)
    values (v_dia, coalesce(v_nome, 'Gerente'), nullif(trim(coalesce(p_obs, '')), ''), '{}'::jsonb);
    update public.fechamentos_dia set resumo = public.resumo_dia(v_dia) where dia = v_dia;
    return public.resumo_dia(v_dia);
end;
$$;

drop function if exists public.reabrir_dia(date);
create or replace function public.reabrir_dia(p_dia date, p_autorizacao bigint default null)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente reabre o dia.' using errcode = '42501';
    end if;
    if not exists (select 1 from public.fechamentos_dia where dia = p_dia) then
        raise exception 'Este dia não está fechado.' using errcode = '22023';
    end if;
    if p_dia < public.hoje_local() then
        perform public.usar_autorizacao_dia(p_autorizacao);
    end if;
    delete from public.fechamentos_dia where dia = p_dia;
end;
$$;

-- Igual ao 008, mas o caixa de um dia que já passou só fecha com a autorização 'alterar_dia'.
drop function if exists public.fechar_caixa(bigint, numeric, text);
create or replace function public.fechar_caixa(p_caixa_id bigint, p_contado numeric, p_obs text default null, p_autorizacao bigint default null)
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
    if v_caixa.dia < public.hoje_local() then
        perform public.usar_autorizacao_dia(p_autorizacao);
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

-- ---------------------------------------------------------------- 2. Calendário de datas especiais
create table if not exists public.eventos_calendario (
    id          bigint generated always as identity primary key,
    dia         date not null,
    nome        text not null check (length(trim(nome)) between 1 and 80),
    -- feriado (nacional), vespera (dia antes de feriado ou data festiva), festivo (data comemorativa), evento (jogo, promoção)
    tipo        text not null check (tipo in ('feriado', 'vespera', 'festivo', 'evento')),
    -- Quanto o movimento costuma mudar: 1,6 = 60% a mais; 0 = mercado fechado
    fator       numeric(4, 2) not null default 1 check (fator between 0 and 5),
    -- Categorias que mais vendem nesse dia (separadas por vírgula) e produtos típicos (nomes, separados por vírgula)
    categorias  text,
    produtos    text,
    unique (dia, nome)
);

create index if not exists eventos_calendario_dia on public.eventos_calendario (dia);
alter table public.eventos_calendario enable row level security;
drop policy if exists "todos leem o calendario" on public.eventos_calendario;
create policy "todos leem o calendario" on public.eventos_calendario for select to authenticated using (public.usuario_ativo());
drop policy if exists "gerente altera o calendario" on public.eventos_calendario;
create policy "gerente altera o calendario" on public.eventos_calendario for all to authenticated
    using (public.eh_gerente()) with check (public.eh_gerente());

-- Domingo de Páscoa (cálculo de Meeus/Jones/Butcher)
create or replace function public.domingo_de_pascoa(p_ano integer)
returns date
language plpgsql
immutable
as $$
declare
    a int := p_ano % 19; b int := p_ano / 100; c int := p_ano % 100; d int := b / 4; e int := b % 4;
    f int := (b + 8) / 25; g int := (b - f + 1) / 3; h int := (19 * a + b - d - g + 15) % 30;
    i int := c / 4; k int := c % 4; l int := (32 + 2 * e + 2 * i - h - k) % 7; m int := (a + 11 * h + 22 * l) / 451;
    mes int := (h + l - 7 * m + 114) / 31; dia int := ((h + l - 7 * m + 114) % 31) + 1;
begin
    return make_date(p_ano, mes, dia);
end;
$$;

-- n-ésimo dia da semana do mês (ex.: 2º domingo de maio = Dia das Mães)
create or replace function public.nesimo_dia_semana(p_ano integer, p_mes integer, p_dow integer, p_n integer)
returns date
language sql
immutable
as $$
    select make_date(p_ano, p_mes, 1) + ((7 + p_dow - extract(dow from make_date(p_ano, p_mes, 1))::int) % 7) + 7 * (p_n - 1);
$$;

-- Preenche o calendário de um ano (pode rodar de novo; não duplica)
create or replace function public.preencher_calendario(p_ano integer)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    v_pascoa date := public.domingo_de_pascoa(p_ano);
    v_maes date := public.nesimo_dia_semana(p_ano, 5, 0, 2);
    v_pais date := public.nesimo_dia_semana(p_ano, 8, 0, 2);
    v_black date := public.nesimo_dia_semana(p_ano, 11, 5, 4);
    v_n integer;
    churrasco constant text := 'Açougue, Bebidas';
begin
    insert into public.eventos_calendario (dia, nome, tipo, fator, categorias, produtos)
    select x.dia, x.nome, x.tipo, x.fator, x.categorias, x.produtos
    from (values
        -- Feriados nacionais (o movimento sobe no dia anterior; no feriado, churrasco)
        (make_date(p_ano, 1, 1), 'Confraternização Universal', 'feriado', 0.00, null, null),
        (v_pascoa - 48, 'Carnaval (segunda)', 'festivo', 1.20, churrasco, 'Carvão vegetal 3kg'),
        (v_pascoa - 47, 'Carnaval (terça)', 'festivo', 1.15, churrasco, 'Carvão vegetal 3kg'),
        (v_pascoa - 50, 'Sábado de Carnaval', 'festivo', 1.40, churrasco, 'Carvão vegetal 3kg'),
        (v_pascoa - 3, 'Véspera da Sexta-feira Santa', 'vespera', 1.50, 'Mercearia, Bebidas', 'Bacalhau 1kg, Ovo de Páscoa 240g, Chocolate ao leite 90g'),
        (v_pascoa - 2, 'Sexta-feira Santa', 'feriado', 0.80, 'Mercearia', 'Bacalhau 1kg'),
        (v_pascoa - 1, 'Sábado de Aleluia', 'vespera', 1.70, 'Mercearia, Açougue, Bebidas', 'Ovo de Páscoa 240g, Chocolate ao leite 90g, Colomba pascal 500g'),
        (v_pascoa, 'Páscoa', 'festivo', 0.90, 'Mercearia', 'Ovo de Páscoa 240g, Colomba pascal 500g'),
        (make_date(p_ano, 4, 20), 'Véspera de Tiradentes', 'vespera', 1.30, churrasco, 'Carvão vegetal 3kg'),
        (make_date(p_ano, 4, 21), 'Tiradentes', 'feriado', 1.10, churrasco, 'Carvão vegetal 3kg'),
        (make_date(p_ano, 4, 30), 'Véspera do Dia do Trabalho', 'vespera', 1.30, churrasco, 'Carvão vegetal 3kg'),
        (make_date(p_ano, 5, 1), 'Dia do Trabalho', 'feriado', 1.10, churrasco, 'Carvão vegetal 3kg'),
        (v_maes - 1, 'Véspera do Dia das Mães', 'vespera', 1.50, 'Açougue, Bebidas, Padaria', 'Espumante 750ml, Chocolate ao leite 90g'),
        (v_maes, 'Dia das Mães', 'festivo', 1.20, 'Açougue, Padaria', 'Espumante 750ml'),
        (make_date(p_ano, 6, 12), 'Dia dos Namorados', 'festivo', 1.15, 'Bebidas, Frios', 'Espumante 750ml, Chocolate ao leite 90g'),
        (v_pascoa + 60, 'Corpus Christi', 'feriado', 1.05, churrasco, 'Carvão vegetal 3kg'),
        (v_pascoa + 59, 'Véspera de Corpus Christi', 'vespera', 1.30, churrasco, 'Carvão vegetal 3kg'),
        (make_date(p_ano, 6, 24), 'São João', 'festivo', 1.25, 'Mercearia, Grãos, Bebidas', 'Milho de pipoca 500g, Canjica 500g, Paçoca 1kg, Amendoim 500g'),
        (v_pais - 1, 'Véspera do Dia dos Pais', 'vespera', 1.40, churrasco, 'Carvão vegetal 3kg'),
        (v_pais, 'Dia dos Pais', 'festivo', 1.20, churrasco, 'Carvão vegetal 3kg'),
        (make_date(p_ano, 9, 6), 'Véspera da Independência', 'vespera', 1.30, churrasco, 'Carvão vegetal 3kg'),
        (make_date(p_ano, 9, 7), 'Independência do Brasil', 'feriado', 1.10, churrasco, 'Carvão vegetal 3kg'),
        (make_date(p_ano, 10, 11), 'Véspera do Dia das Crianças', 'vespera', 1.30, 'Mercearia, Bebidas', 'Chocolate ao leite 90g'),
        (make_date(p_ano, 10, 12), 'Nossa Senhora Aparecida e Dia das Crianças', 'feriado', 1.05, 'Mercearia, Bebidas', 'Chocolate ao leite 90g'),
        (make_date(p_ano, 11, 2), 'Finados', 'feriado', 0.85, null, null),
        (make_date(p_ano, 11, 15), 'Proclamação da República', 'feriado', 1.05, churrasco, null),
        (v_black, 'Black Friday', 'evento', 1.30, 'Limpeza, Higiene, Mercearia', null),
        (make_date(p_ano, 12, 23), 'Antevéspera de Natal', 'vespera', 1.70, 'Açougue, Bebidas, Mercearia, Frios', 'Panetone 500g, Peru temperado (kg), Chester (kg), Espumante 750ml'),
        (make_date(p_ano, 12, 24), 'Véspera de Natal', 'vespera', 2.20, 'Açougue, Bebidas, Mercearia, Frios', 'Panetone 500g, Peru temperado (kg), Chester (kg), Espumante 750ml, Bacalhau 1kg'),
        (make_date(p_ano, 12, 25), 'Natal', 'feriado', 0.00, null, null),
        (make_date(p_ano, 12, 30), 'Antevéspera de Ano-Novo', 'vespera', 1.60, 'Açougue, Bebidas, Mercearia', 'Espumante 750ml, Lentilha 500g'),
        (make_date(p_ano, 12, 31), 'Véspera de Ano-Novo', 'vespera', 2.00, 'Bebidas, Açougue, Hortifrúti', 'Espumante 750ml, Lentilha 500g, Carvão vegetal 3kg')
    ) as x(dia, nome, tipo, fator, categorias, produtos)
    -- Consciência Negra virou feriado nacional em 2024
    union all
    select make_date(p_ano, 11, 20), 'Consciência Negra', 'feriado', 1.05, churrasco, null where p_ano >= 2024
    on conflict (dia, nome) do nothing;
    get diagnostics v_n = row_count;
    -- Copa do Mundo 2022 (jogos do Brasil)
    if p_ano = 2022 then
        insert into public.eventos_calendario (dia, nome, tipo, fator, categorias, produtos)
        values ('2022-11-24', 'Copa do Mundo: Brasil x Sérvia', 'evento', 1.35, 'Bebidas, Açougue, Congelados', 'Carvão vegetal 3kg'),
               ('2022-11-28', 'Copa do Mundo: Brasil x Suíça', 'evento', 1.30, 'Bebidas, Açougue, Congelados', 'Carvão vegetal 3kg'),
               ('2022-12-02', 'Copa do Mundo: Camarões x Brasil', 'evento', 1.30, 'Bebidas, Açougue, Congelados', 'Carvão vegetal 3kg'),
               ('2022-12-05', 'Copa do Mundo: Brasil x Coreia do Sul', 'evento', 1.30, 'Bebidas, Açougue, Congelados', 'Carvão vegetal 3kg'),
               ('2022-12-09', 'Copa do Mundo: Croácia x Brasil', 'evento', 1.35, 'Bebidas, Açougue, Congelados', 'Carvão vegetal 3kg')
        on conflict (dia, nome) do nothing;
    end if;
    return v_n;
end;
$$;

select public.preencher_calendario(a) from generate_series(2021, 2028) a;

revoke execute on function public.usar_autorizacao_dia(bigint) from public, anon, authenticated;
revoke execute on function public.preencher_calendario(integer) from public, anon, authenticated;
revoke execute on function public.fechar_dia(date, text, bigint) from public, anon;
revoke execute on function public.reabrir_dia(date, bigint) from public, anon;
revoke execute on function public.fechar_caixa(bigint, numeric, text, bigint) from public, anon;
grant execute on function public.fechar_dia(date, text, bigint) to authenticated;
grant execute on function public.reabrir_dia(date, bigint) to authenticated;
grant execute on function public.fechar_caixa(bigint, numeric, text, bigint) to authenticated;
