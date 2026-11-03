-- =====================================================================
-- Semana 6: fechamento do dia, análises, e-mail e mensagens com fornecedores
-- Rodar no SQL Editor do Supabase DEPOIS do 008. Pode rodar de novo.
--
-- - Vendas só para o gerente (pedido do Guilherme em 03/11): o operador passa a ler só as próprias vendas e o
--   próprio caixa. As reservas de todos os caixas continuam aparecendo pela função reservados_hoje().
-- - fechamentos_dia: a "foto" do dia fechado pelo gerente (resumo_dia) e o envio do e-mail.
-- - config_mercado: e-mails que recebem o resumo e o endereço da caixa de e-mail de pedidos.
-- - analise_vendas: vendas por dia, semana ou mês, por categoria e forma, margem e perdas.
-- - fornecedor_mensagens: e-mails trocados com os fornecedores (gravados pelas Edge Functions) e respostas do
--   WhatsApp coladas à mão. A senha do e-mail fica só nos segredos das Edge Functions, nunca no .exe.
-- =====================================================================

-- ---------------------------------------------------------------- Chamada das Edge Functions
-- O agendamento (pg_cron, script 010) chama as Edge Functions, que usam a chave de serviço do próprio Supabase.
-- Nessas chamadas não há usuário logado; esta função diz que quem chamou é o servidor.
create or replace function public.chamada_do_servidor()
returns boolean
language sql
stable
as $$
    select coalesce(nullif(current_setting('request.jwt.claims', true), '')::jsonb ->> 'role', '') = 'service_role'
        or coalesce(current_setting('role', true), '') = 'service_role';
$$;

-- ---------------------------------------------------------------- Vendas só para o gerente
-- Antes todo usuário ativo lia as vendas de hoje de todos os caixas. Agora o operador lê só as dele.
drop policy if exists "le caixas de hoje" on public.caixas;
create policy "le caixas de hoje"
    on public.caixas for select
    to authenticated
    using (public.eh_gerente() or (public.usuario_ativo() and dia = public.hoje_local() and operador_id = auth.uid()));

drop policy if exists "le vendas de hoje" on public.vendas;
create policy "le vendas de hoje"
    on public.vendas for select
    to authenticated
    using (public.eh_gerente() or (public.usuario_ativo() and dia = public.hoje_local() and operador_id = auth.uid()));

drop policy if exists "le itens das vendas de hoje" on public.venda_itens;
create policy "le itens das vendas de hoje"
    on public.venda_itens for select
    to authenticated
    using (exists (select 1 from public.vendas v where v.id = venda_id
                   and (public.eh_gerente() or (public.usuario_ativo() and v.dia = public.hoje_local() and v.operador_id = auth.uid()))));

drop policy if exists "le pagamentos das vendas de hoje" on public.venda_pagamentos;
create policy "le pagamentos das vendas de hoje"
    on public.venda_pagamentos for select
    to authenticated
    using (exists (select 1 from public.vendas v where v.id = venda_id
                   and (public.eh_gerente() or (public.usuario_ativo() and v.dia = public.hoje_local() and v.operador_id = auth.uid()))));

-- Reservado em vendas abertas e em espera de hoje, de todos os caixas (o operador não lê as vendas dos outros,
-- mas precisa saber o que já está reservado). Só produto e quantidade.
create or replace function public.reservados_hoje()
returns table (produto_id bigint, quantidade numeric)
language sql
stable
security definer
set search_path = public
as $$
    select i.produto_id, sum(i.quantidade)
    from public.venda_itens i join public.vendas v on v.id = i.venda_id
    where public.usuario_ativo() and v.situacao in ('aberta', 'espera') and v.dia = public.hoje_local() and i.produto_id is not null
    group by i.produto_id;
$$;

-- ---------------------------------------------------------------- Configuração do mercado (uma linha só)
create table if not exists public.config_mercado (
    id              integer primary key default 1 check (id = 1),
    -- Quem recebe o resumo do dia, separados por vírgula
    emails_resumo   text check (emails_resumo is null or length(emails_resumo) <= 500),
    -- Endereço da caixa de e-mail de pedidos (só para mostrar; a senha fica nos segredos das Edge Functions)
    email_pedidos   text check (email_pedidos is null or email_pedidos ~ '^[^\s@]+@[^\s@]+\.[^\s@]+$'),
    -- Mandar o resumo sozinho às 23h se ninguém fechar o dia (script 010)
    resumo_automatico boolean not null default true,
    atualizado_em   timestamptz not null default now()
);

insert into public.config_mercado (id) values (1) on conflict (id) do nothing;

-- ---------------------------------------------------------------- Fechamento do dia
create table if not exists public.fechamentos_dia (
    id               bigint generated always as identity primary key,
    dia              date not null unique,
    fechado_em       timestamptz not null default now(),
    fechado_por      text not null,
    automatico       boolean not null default false,   -- resumo das 23h, sem ninguém fechar
    obs              text check (obs is null or length(obs) <= 300),
    resumo           jsonb not null,
    email_enviado_em timestamptz,
    email_para       text,
    email_erro       text
);

-- ---------------------------------------------------------------- Mensagens com fornecedores
create table if not exists public.fornecedor_mensagens (
    id              bigint generated always as identity primary key,
    fornecedor_id   bigint not null references public.fornecedores (id) on delete cascade,
    pedido_id       bigint references public.pedidos (id) on delete set null,
    direcao         text not null check (direcao in ('recebida', 'enviada')),
    canal           text not null default 'email' check (canal in ('email', 'whatsapp')),
    de              text check (de is null or length(de) <= 200),
    para            text check (para is null or length(para) <= 200),
    assunto         text check (assunto is null or length(assunto) <= 300),
    corpo           text not null check (length(corpo) <= 20000),
    -- Message-ID do e-mail: não grava o mesmo e-mail duas vezes e liga a resposta (In-Reply-To)
    message_id      text unique check (message_id is null or length(message_id) <= 300),
    em_resposta_a   text check (em_resposta_a is null or length(em_resposta_a) <= 300),
    -- Anexos guardados no Storage (bucket "anexos-fornecedores"): [{nome, caminho, tipo, tamanho}]
    anexos          jsonb not null default '[]'::jsonb,
    -- Quem do mercado mandou (enviada) ou registrou a resposta do WhatsApp
    usuario_nome    text,
    criada_em       timestamptz not null default now(),
    lida_em         timestamptz,
    erro            text   -- envio que falhou (fica na conversa para tentar de novo)
);

create index if not exists fornecedor_mensagens_fornecedor on public.fornecedor_mensagens (fornecedor_id, criada_em);
create index if not exists fornecedor_mensagens_pedido on public.fornecedor_mensagens (pedido_id);
create index if not exists fornecedor_mensagens_nao_lidas on public.fornecedor_mensagens (fornecedor_id) where lida_em is null and direcao = 'recebida';

-- ---------------------------------------------------------------- Regras de acesso (RLS)
alter table public.config_mercado enable row level security;
alter table public.fechamentos_dia enable row level security;
alter table public.fornecedor_mensagens enable row level security;

drop policy if exists "gerente le a configuracao" on public.config_mercado;
create policy "gerente le a configuracao"
    on public.config_mercado for select to authenticated using (public.eh_gerente());
drop policy if exists "gerente altera a configuracao" on public.config_mercado;
create policy "gerente altera a configuracao"
    on public.config_mercado for update to authenticated using (public.eh_gerente()) with check (public.eh_gerente());

drop policy if exists "gerente le os fechamentos" on public.fechamentos_dia;
create policy "gerente le os fechamentos"
    on public.fechamentos_dia for select to authenticated using (public.eh_gerente());

drop policy if exists "gerente le as mensagens" on public.fornecedor_mensagens;
create policy "gerente le as mensagens"
    on public.fornecedor_mensagens for select to authenticated using (public.eh_gerente());
-- O gerente só marca como lida pelo programa (função marcar_mensagens_lidas); e-mails entram pelas Edge Functions.
-- Resposta do WhatsApp colada à mão: função registrar_mensagem_whatsapp.

-- ---------------------------------------------------------------- Resumo do dia
-- Tudo o que a tela Fechamento do dia, o e-mail e o celular mostram: totais, formas, caixas, mais vendidos,
-- vendas por hora, cancelamentos e a comparação com o mesmo dia da semana anterior.
create or replace function public.resumo_dia(p_dia date default null)
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
    v_dia date := coalesce(p_dia, public.hoje_local());
    v_total numeric;
    v_vendas integer;
begin
    if not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente vê o resumo do dia.' using errcode = '42501';
    end if;
    select coalesce(sum(total), 0), count(*) into v_total, v_vendas
    from public.vendas where dia = v_dia and situacao = 'paga';

    return jsonb_build_object(
        'dia', v_dia,
        'total', v_total,
        'vendas', v_vendas,
        'ticket_medio', case when v_vendas > 0 then round(v_total / v_vendas, 2) else 0 end,
        'descontos', coalesce((select sum(desconto) from public.vendas where dia = v_dia and situacao = 'paga'), 0),
        'promocoes', coalesce((select sum(i.desconto_promocao) from public.venda_itens i join public.vendas v on v.id = i.venda_id
                               where v.dia = v_dia and v.situacao = 'paga'), 0),
        'itens_vendidos', coalesce((select count(*) from public.venda_itens i join public.vendas v on v.id = i.venda_id
                                    where v.dia = v_dia and v.situacao = 'paga'), 0),
        'canceladas', (select count(*) from public.vendas where dia = v_dia and situacao = 'cancelada' and paga_em is not null),
        'canceladas_valor', coalesce((select sum(vi.subtotal) from public.vendas v join public.venda_itens vi on vi.venda_id = v.id
                                      where v.dia = v_dia and v.situacao = 'cancelada' and v.paga_em is not null), 0),
        -- Custo do que saiu dos lotes (só onde o lote tem preço de custo) e a margem sobre essas vendas
        'custo', coalesce((select sum(l.quantidade * lo.preco_custo)
                           from public.venda_item_lotes l join public.lotes lo on lo.id = l.lote_id
                           join public.venda_itens i on i.id = l.item_id join public.vendas v on v.id = i.venda_id
                           where v.dia = v_dia and v.situacao = 'paga' and lo.preco_custo is not null), 0),
        'vendido_com_custo', coalesce((select sum(i.subtotal) from public.venda_itens i join public.vendas v on v.id = i.venda_id
                                       where v.dia = v_dia and v.situacao = 'paga'
                                       and exists (select 1 from public.venda_item_lotes l join public.lotes lo on lo.id = l.lote_id
                                                   where l.item_id = i.id and lo.preco_custo is not null)), 0),
        'formas', coalesce((select jsonb_object_agg(forma, valor) from (
                       select pg.forma, sum(pg.valor) as valor from public.venda_pagamentos pg join public.vendas v on v.id = pg.venda_id
                       where v.dia = v_dia and v.situacao = 'paga' group by pg.forma) f), '{}'::jsonb),
        'caixas', coalesce((select jsonb_agg(jsonb_build_object(
                       'caixa_id', c.id, 'operador_nome', c.operador_nome, 'aberto_em', c.aberto_em, 'fechado_em', c.fechado_em,
                       'troco_inicial', c.troco_inicial,
                       'vendas', (select count(*) from public.vendas where caixa_id = c.id and situacao = 'paga'),
                       'total', coalesce((select sum(total) from public.vendas where caixa_id = c.id and situacao = 'paga'), 0),
                       'esperado', coalesce(c.fechado_esperado, public.dinheiro_na_gaveta(c.id)),
                       'contado', c.fechado_contado, 'diferenca', c.fechado_diferenca, 'obs', c.fechado_obs,
                       'fechado_por', c.fechado_por,
                       'sangrias', coalesce((select sum(valor) from public.caixa_movimentos where caixa_id = c.id and tipo = 'sangria'), 0),
                       'suprimentos', coalesce((select sum(valor) from public.caixa_movimentos where caixa_id = c.id and tipo = 'suprimento'), 0))
                       order by c.aberto_em)
                   from public.caixas c where c.dia = v_dia), '[]'::jsonb),
        'diferenca_caixas', coalesce((select sum(fechado_diferenca) from public.caixas where dia = v_dia), 0),
        'mais_vendidos', coalesce((select jsonb_agg(m) from (
                       select i.nome_produto as nome, i.unidade_medida as unidade, sum(i.quantidade) as quantidade, sum(i.subtotal) as valor
                       from public.venda_itens i join public.vendas v on v.id = i.venda_id
                       where v.dia = v_dia and v.situacao = 'paga'
                       group by i.nome_produto, i.unidade_medida order by sum(i.subtotal) desc limit 10) m), '[]'::jsonb),
        'por_hora', coalesce((select jsonb_agg(h order by h.hora) from (
                       select extract(hour from (paga_em at time zone 'America/Sao_Paulo'))::int as hora, count(*) as vendas, sum(total) as total
                       from public.vendas where dia = v_dia and situacao = 'paga' and paga_em is not null group by 1) h), '[]'::jsonb),
        'itens_cancelados', coalesce((select jsonb_agg(jsonb_build_object(
                       'nome', ic.nome_produto, 'quantidade', ic.quantidade, 'unidade', ic.unidade_medida, 'valor', ic.valor,
                       'operador_nome', ic.operador_nome, 'autorizado_por', ic.autorizado_por, 'criado_em', ic.criado_em) order by ic.id)
                   from public.venda_itens_cancelados ic join public.vendas v on v.id = ic.venda_id where v.dia = v_dia), '[]'::jsonb),
        'semana_anterior', jsonb_build_object(
                       'dia', v_dia - 7,
                       'total', coalesce((select sum(total) from public.vendas where dia = v_dia - 7 and situacao = 'paga'), 0),
                       'vendas', (select count(*) from public.vendas where dia = v_dia - 7 and situacao = 'paga')),
        'vendas_abertas', (select count(*) from public.vendas v where v.dia = v_dia and v.situacao in ('aberta', 'espera')
                           and exists (select 1 from public.venda_itens i where i.venda_id = v.id)),
        'fechamento', (select jsonb_build_object('fechado_em', f.fechado_em, 'fechado_por', f.fechado_por, 'automatico', f.automatico,
                                                 'obs', f.obs, 'email_enviado_em', f.email_enviado_em, 'email_para', f.email_para,
                                                 'email_erro', f.email_erro)
                       from public.fechamentos_dia f where f.dia = v_dia));
end;
$$;

-- Fecha o dia (só o gerente): todos os caixas do dia precisam estar fechados. Grava a foto do dia; o programa
-- manda o e-mail em seguida pela Edge Function enviar-email.
create or replace function public.fechar_dia(p_dia date default null, p_obs text default null)
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

    delete from public.fechamentos_dia where dia = v_dia and automatico;
    insert into public.fechamentos_dia (dia, fechado_por, obs, resumo)
    values (v_dia, coalesce(v_nome, 'Gerente'), nullif(trim(coalesce(p_obs, '')), ''), '{}'::jsonb);
    update public.fechamentos_dia set resumo = public.resumo_dia(v_dia) where dia = v_dia;
    return public.resumo_dia(v_dia);
end;
$$;

-- Desfaz o fechamento (só o gerente), para corrigir e fechar de novo. Os caixas continuam como estão.
create or replace function public.reabrir_dia(p_dia date)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente reabre o dia.' using errcode = '42501';
    end if;
    delete from public.fechamentos_dia where dia = p_dia;
    if not found then
        raise exception 'Este dia não está fechado.' using errcode = '22023';
    end if;
end;
$$;

-- Resumo automático das 23h (chamado só pelo servidor): grava a foto do dia se ninguém fechou.
create or replace function public.fechamento_automatico(p_dia date default null)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_dia date := coalesce(p_dia, public.hoje_local());
begin
    if not public.chamada_do_servidor() then
        raise exception 'Só o agendamento do servidor usa esta função.' using errcode = '42501';
    end if;
    if not exists (select 1 from public.fechamentos_dia where dia = v_dia) then
        insert into public.fechamentos_dia (dia, fechado_por, automatico, resumo)
        values (v_dia, 'Envio automático das 23h', true, public.resumo_dia(v_dia));
    end if;
    return public.resumo_dia(v_dia);
end;
$$;

-- A Edge Function anota se o e-mail do resumo saiu (ou o erro).
create or replace function public.registrar_envio_resumo(p_dia date, p_para text, p_erro text default null)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
    if not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Sem permissão.' using errcode = '42501';
    end if;
    update public.fechamentos_dia
    set email_para = p_para, email_erro = p_erro, email_enviado_em = case when p_erro is null then now() else email_enviado_em end
    where dia = p_dia;
end;
$$;

-- ---------------------------------------------------------------- Análises (dia, semana e mês)
-- Vendas pagas de p_inicio a p_fim (inclusive), agrupadas por 'dia', 'semana' (começa na segunda) ou 'mes'.
-- Junto: o período anterior do mesmo tamanho, por categoria, por forma, por dia da semana, margem e perdas.
create or replace function public.analise_vendas(p_inicio date, p_fim date, p_agrupar text default 'dia')
returns jsonb
language plpgsql
stable
security definer
set search_path = public
as $$
declare
    v_dias integer := p_fim - p_inicio + 1;
begin
    if not public.eh_gerente() and not public.chamada_do_servidor() then
        raise exception 'Só o gerente vê as análises de vendas.' using errcode = '42501';
    end if;
    if p_inicio is null or p_fim is null or p_fim < p_inicio or v_dias > 800 then
        raise exception 'Período inválido.' using errcode = '22023';
    end if;
    if p_agrupar not in ('dia', 'semana', 'mes') then
        raise exception 'Agrupamento inválido.' using errcode = '22023';
    end if;

    return jsonb_build_object(
        'inicio', p_inicio, 'fim', p_fim, 'agrupar', p_agrupar,
        -- Uma linha por período, inclusive os sem venda (o gráfico não pula dias)
        'serie', coalesce((select jsonb_agg(jsonb_build_object('periodo', s.periodo, 'total', coalesce(t.total, 0),
                                                              'vendas', coalesce(t.vendas, 0), 'descontos', coalesce(t.descontos, 0)) order by s.periodo)
                 from (select distinct date_trunc(case p_agrupar when 'dia' then 'day' when 'semana' then 'week' else 'month' end, d)::date as periodo
                       from generate_series(p_inicio, p_fim, interval '1 day') d) s
                 left join (select date_trunc(case p_agrupar when 'dia' then 'day' when 'semana' then 'week' else 'month' end, dia)::date as periodo,
                                   sum(total) as total, count(*) as vendas, sum(desconto) as descontos
                            from public.vendas where situacao = 'paga' and dia between p_inicio and p_fim group by 1) t
                   on t.periodo = s.periodo), '[]'::jsonb),
        'total', coalesce((select sum(total) from public.vendas where situacao = 'paga' and dia between p_inicio and p_fim), 0),
        'vendas', (select count(*) from public.vendas where situacao = 'paga' and dia between p_inicio and p_fim),
        'anterior', jsonb_build_object(
            'inicio', p_inicio - v_dias, 'fim', p_inicio - 1,
            'total', coalesce((select sum(total) from public.vendas where situacao = 'paga' and dia between p_inicio - v_dias and p_inicio - 1), 0),
            'vendas', (select count(*) from public.vendas where situacao = 'paga' and dia between p_inicio - v_dias and p_inicio - 1)),
        'categorias', coalesce((select jsonb_agg(c order by c.valor desc) from (
                       select coalesce(nullif(p.categoria, ''), 'Sem categoria') as categoria, sum(i.subtotal) as valor
                       from public.venda_itens i join public.vendas v on v.id = i.venda_id
                       left join public.produtos p on p.id = i.produto_id
                       where v.situacao = 'paga' and v.dia between p_inicio and p_fim group by 1) c), '[]'::jsonb),
        'formas', coalesce((select jsonb_object_agg(forma, valor) from (
                       select pg.forma, sum(pg.valor) as valor from public.venda_pagamentos pg join public.vendas v on v.id = pg.venda_id
                       where v.situacao = 'paga' and v.dia between p_inicio and p_fim group by pg.forma) f), '{}'::jsonb),
        -- 0 = domingo ... 6 = sábado
        'dias_semana', coalesce((select jsonb_agg(w order by w.dia_semana) from (
                       select extract(dow from dia)::int as dia_semana, sum(total) as total, count(*) as vendas, count(distinct dia) as dias
                       from public.vendas where situacao = 'paga' and dia between p_inicio and p_fim group by 1) w), '[]'::jsonb),
        'mais_vendidos', coalesce((select jsonb_agg(m) from (
                       select i.nome_produto as nome, i.unidade_medida as unidade, sum(i.quantidade) as quantidade, sum(i.subtotal) as valor
                       from public.venda_itens i join public.vendas v on v.id = i.venda_id
                       where v.situacao = 'paga' and v.dia between p_inicio and p_fim
                       group by i.nome_produto, i.unidade_medida order by sum(i.subtotal) desc limit 10) m), '[]'::jsonb),
        'custo', coalesce((select sum(l.quantidade * lo.preco_custo)
                           from public.venda_item_lotes l join public.lotes lo on lo.id = l.lote_id
                           join public.venda_itens i on i.id = l.item_id join public.vendas v on v.id = i.venda_id
                           where v.situacao = 'paga' and v.dia between p_inicio and p_fim and lo.preco_custo is not null), 0),
        'vendido_com_custo', coalesce((select sum(i.subtotal) from public.venda_itens i join public.vendas v on v.id = i.venda_id
                                       where v.situacao = 'paga' and v.dia between p_inicio and p_fim
                                       and exists (select 1 from public.venda_item_lotes l join public.lotes lo on lo.id = l.lote_id
                                                   where l.item_id = i.id and lo.preco_custo is not null)), 0),
        'descontos', coalesce((select sum(desconto) from public.vendas where situacao = 'paga' and dia between p_inicio and p_fim), 0),
        'promocoes', coalesce((select sum(i.desconto_promocao) from public.venda_itens i join public.vendas v on v.id = i.venda_id
                               where v.situacao = 'paga' and v.dia between p_inicio and p_fim), 0),
        -- Perdas: lotes que venceram no período ainda com unidades (valor pelo custo do lote ou do produto)
        'perdas', jsonb_build_object(
            'lotes', (select count(*) from public.lotes where quantidade_restante > 0 and validade between p_inicio and least(p_fim, public.hoje_local() - 1)),
            'valor', coalesce((select sum(lo.quantidade_restante * coalesce(lo.preco_custo, p.preco_custo, 0))
                               from public.lotes lo join public.produtos p on p.id = lo.produto_id
                               where lo.quantidade_restante > 0 and lo.validade between p_inicio and least(p_fim, public.hoje_local() - 1)), 0)),
        'cancelamentos', jsonb_build_object(
            'vendas', (select count(*) from public.vendas where situacao = 'cancelada' and paga_em is not null and dia between p_inicio and p_fim),
            'itens', (select count(*) from public.venda_itens_cancelados ic join public.vendas v on v.id = ic.venda_id where v.dia between p_inicio and p_fim),
            'valor_itens', coalesce((select sum(ic.valor) from public.venda_itens_cancelados ic join public.vendas v on v.id = ic.venda_id
                                     where v.dia between p_inicio and p_fim), 0)));
end;
$$;

-- ---------------------------------------------------------------- Mensagens
-- Não lidas por fornecedor (ícone de mensagens e aviso do Painel).
create or replace function public.mensagens_nao_lidas()
returns table (fornecedor_id bigint, fornecedor_nome text, quantidade bigint, ultima_em timestamptz, pedido_id bigint)
language sql
stable
security definer
set search_path = public
as $$
    select m.fornecedor_id, f.nome, count(*), max(m.criada_em),
           (array_agg(m.pedido_id order by m.criada_em desc))[1]
    from public.fornecedor_mensagens m join public.fornecedores f on f.id = m.fornecedor_id
    where public.eh_gerente() and m.direcao = 'recebida' and m.lida_em is null
    group by m.fornecedor_id, f.nome;
$$;

create or replace function public.marcar_mensagens_lidas(p_fornecedor_id bigint)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente lê as mensagens dos fornecedores.' using errcode = '42501';
    end if;
    update public.fornecedor_mensagens set lida_em = now()
    where fornecedor_id = p_fornecedor_id and direcao = 'recebida' and lida_em is null;
end;
$$;

-- Resposta que chegou pelo WhatsApp, colada à mão pelo gerente (até o WhatsApp oficial da semana 7).
create or replace function public.registrar_mensagem_whatsapp(p_fornecedor_id bigint, p_pedido_id bigint, p_corpo text, p_direcao text default 'recebida')
returns bigint
language plpgsql
security definer
set search_path = public
as $$
declare
    v_nome text;
    v_id bigint;
begin
    if not public.eh_gerente() then
        raise exception 'Só o gerente registra mensagens dos fornecedores.' using errcode = '42501';
    end if;
    if p_direcao not in ('recebida', 'enviada') then
        raise exception 'Direção inválida.' using errcode = '22023';
    end if;
    if p_corpo is null or length(trim(p_corpo)) = 0 or length(p_corpo) > 5000 then
        raise exception 'Cole a mensagem (até 5000 letras).' using errcode = '22023';
    end if;
    if not exists (select 1 from public.fornecedores where id = p_fornecedor_id) then
        raise exception 'Fornecedor não encontrado.' using errcode = '22023';
    end if;
    if p_pedido_id is not null and not exists (select 1 from public.pedidos where id = p_pedido_id and fornecedor_id = p_fornecedor_id) then
        raise exception 'O pedido nº % não é deste fornecedor.', p_pedido_id using errcode = '22023';
    end if;
    select nome into v_nome from public.perfis where id = auth.uid();
    insert into public.fornecedor_mensagens (fornecedor_id, pedido_id, direcao, canal, corpo, usuario_nome, lida_em)
    values (p_fornecedor_id, p_pedido_id, p_direcao, 'whatsapp', trim(p_corpo), v_nome, now())
    returning id into v_id;
    return v_id;
end;
$$;

-- ---------------------------------------------------------------- Anexos (Storage)
-- Notas fiscais (PDF/XML) que chegam por e-mail. Bucket privado: só o gerente baixa; quem grava é a Edge Function.
insert into storage.buckets (id, name, public) values ('anexos-fornecedores', 'anexos-fornecedores', false)
on conflict (id) do nothing;

drop policy if exists "gerente baixa anexos de fornecedores" on storage.objects;
create policy "gerente baixa anexos de fornecedores"
    on storage.objects for select to authenticated
    using (bucket_id = 'anexos-fornecedores' and public.eh_gerente());

-- ---------------------------------------------------------------- Permissões das funções
revoke execute on function public.chamada_do_servidor() from public, anon;
revoke execute on function public.reservados_hoje() from public, anon;
revoke execute on function public.resumo_dia(date) from public, anon;
revoke execute on function public.fechar_dia(date, text) from public, anon;
revoke execute on function public.reabrir_dia(date) from public, anon;
revoke execute on function public.fechamento_automatico(date) from public, anon, authenticated;
revoke execute on function public.registrar_envio_resumo(date, text, text) from public, anon;
revoke execute on function public.analise_vendas(date, date, text) from public, anon;
revoke execute on function public.mensagens_nao_lidas() from public, anon;
revoke execute on function public.marcar_mensagens_lidas(bigint) from public, anon;
revoke execute on function public.registrar_mensagem_whatsapp(bigint, bigint, text, text) from public, anon;
grant execute on function public.chamada_do_servidor() to authenticated;
grant execute on function public.reservados_hoje() to authenticated;
grant execute on function public.resumo_dia(date) to authenticated, service_role;
grant execute on function public.fechar_dia(date, text) to authenticated;
grant execute on function public.reabrir_dia(date) to authenticated;
grant execute on function public.fechamento_automatico(date) to service_role;
grant execute on function public.registrar_envio_resumo(date, text, text) to authenticated, service_role;
grant execute on function public.analise_vendas(date, date, text) to authenticated, service_role;
grant execute on function public.mensagens_nao_lidas() to authenticated;
grant execute on function public.marcar_mensagens_lidas(bigint) to authenticated;
grant execute on function public.registrar_mensagem_whatsapp(bigint, bigint, text, text) to authenticated;
