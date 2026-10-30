-- Semana 5 (caixa padrão): o que todo caixa de supermercado tem, além do 006.
-- Rodar no Supabase: painel do projeto > SQL Editor > New query > colar e executar (depois do 006).
-- Pode ser rodado de novo sem perder dados (só cria o que ainda não existe e atualiza as funções).
--
-- 1. Pagamento dividido: uma venda pode ser paga com várias formas (venda_pagamentos), incluindo vale-alimentação
--    e vale-refeição. Cartão, vale e Pix guardam o que a maquininha devolveu (bandeira, NSU, autorização, parcelas).
-- 2. CPF/CNPJ na nota (vendas.cpf_nota) e desconto na venda (vendas.desconto, só com autorização do gerente).
-- 3. Autorização do gerente: o operador não cancela item, não cancela a venda nem dá desconto sozinho. O gerente
--    digita usuário e senha no caixa (autorizar_gerente) e a autorização vale uma vez, para aquela venda.
--    Itens cancelados ficam registrados (venda_itens_cancelados).
-- 4. Venda em espera: suspende a venda (os itens continuam reservados) e atende o próximo cliente.

-- ---------------------------------------------------------------- Vendas: colunas novas
alter table public.vendas add column if not exists cpf_nota text
    check (cpf_nota is null or cpf_nota ~ '^([0-9]{11}|[0-9]{14})$');
alter table public.vendas add column if not exists desconto numeric(10, 2) not null default 0 check (desconto >= 0);
alter table public.vendas add column if not exists desconto_autorizado_por text;

-- 'espera' = venda suspensa (continua reservando os itens)
alter table public.vendas drop constraint if exists vendas_situacao_check;
alter table public.vendas add constraint vendas_situacao_check
    check (situacao in ('aberta', 'espera', 'paga', 'cancelada'));
-- 'varias' = paga com mais de uma forma (o detalhe fica em venda_pagamentos)
alter table public.vendas drop constraint if exists vendas_forma_pagamento_check;
alter table public.vendas add constraint vendas_forma_pagamento_check
    check (forma_pagamento is null or forma_pagamento in
           ('dinheiro', 'pix', 'debito', 'credito', 'vale_alimentacao', 'vale_refeicao', 'varias'));

-- ---------------------------------------------------------------- Pagamentos da venda
create table if not exists public.venda_pagamentos (
    id            bigint generated always as identity primary key,
    venda_id      bigint not null references public.vendas (id) on delete cascade,
    forma         text not null check (forma in ('dinheiro', 'pix', 'debito', 'credito', 'vale_alimentacao', 'vale_refeicao')),
    valor         numeric(10, 2) not null check (valor > 0),          -- quanto desta forma foi para a venda
    recebido      numeric(10, 2) check (recebido is null or recebido >= valor),   -- dinheiro entregue
    troco         numeric(10, 2) not null default 0 check (troco >= 0),
    bandeira      text check (bandeira is null or length(bandeira) <= 30),
    nsu           text check (nsu is null or length(nsu) <= 30),
    autorizacao   text check (autorizacao is null or length(autorizacao) <= 30),
    parcelas      integer check (parcelas is null or parcelas between 1 and 12),
    final_cartao  text check (final_cartao is null or final_cartao ~ '^[0-9]{4}$'),
    criado_em     timestamptz not null default now()
);

create index if not exists venda_pagamentos_venda on public.venda_pagamentos (venda_id);

-- ---------------------------------------------------------------- Autorizações do gerente
create table if not exists public.autorizacoes (
    id               bigint generated always as identity primary key,
    venda_id         bigint references public.vendas (id) on delete cascade,
    acao             text not null check (acao in ('cancelar_item', 'cancelar_venda', 'desconto')),
    operador_id      uuid not null references public.perfis (id),
    gerente_usuario  text not null,
    gerente_nome     text,
    aprovada         boolean not null,
    usada            boolean not null default false,
    criada_em        timestamptz not null default now()
);

create index if not exists autorizacoes_gerente on public.autorizacoes (gerente_usuario, criada_em);

-- Itens tirados da venda (cancelamento de item), com quem autorizou
create table if not exists public.venda_itens_cancelados (
    id              bigint generated always as identity primary key,
    venda_id        bigint not null references public.vendas (id) on delete cascade,
    nome_produto    text not null,
    unidade_medida  text not null default 'un',
    quantidade      numeric(12, 3) not null check (quantidade > 0),
    valor           numeric(10, 2) not null default 0,
    operador_nome   text not null,
    autorizado_por  text,
    criado_em       timestamptz not null default now()
);

create index if not exists venda_itens_cancelados_venda on public.venda_itens_cancelados (venda_id);

-- ---------------------------------------------------------------- Regras de acesso (RLS)
alter table public.venda_pagamentos enable row level security;
alter table public.autorizacoes enable row level security;
alter table public.venda_itens_cancelados enable row level security;

drop policy if exists "le pagamentos das vendas de hoje" on public.venda_pagamentos;
create policy "le pagamentos das vendas de hoje"
    on public.venda_pagamentos for select
    to authenticated
    using (exists (select 1 from public.vendas v where v.id = venda_id
                   and (public.eh_gerente() or (public.usuario_ativo() and v.dia = public.hoje_local()))));

drop policy if exists "gerente le autorizacoes" on public.autorizacoes;
create policy "gerente le autorizacoes"
    on public.autorizacoes for select
    to authenticated
    using (public.eh_gerente());

drop policy if exists "gerente le itens cancelados" on public.venda_itens_cancelados;
create policy "gerente le itens cancelados"
    on public.venda_itens_cancelados for select
    to authenticated
    using (public.eh_gerente());

-- ---------------------------------------------------------------- Funções

-- Reservas: venda aberta e venda em espera seguram os itens.
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
                     where i.produto_id = p_produto_id and v.situacao in ('aberta', 'espera') and v.dia = public.hoje_local()
                       and i.id is distinct from p_sem_item), 0);
$$;

-- Total da venda = itens - desconto (o desconto nunca passa do valor dos itens).
create or replace function public.recalcular_venda(p_venda_id bigint)
returns void
language sql
security definer
set search_path = public
as $$
    update public.vendas v
    set desconto = least(v.desconto, coalesce((select sum(subtotal) from public.venda_itens where venda_id = v.id), 0)),
        total = coalesce((select sum(subtotal) from public.venda_itens where venda_id = v.id), 0)
                - least(v.desconto, coalesce((select sum(subtotal) from public.venda_itens where venda_id = v.id), 0))
    where v.id = p_venda_id;
$$;

-- O gerente digita usuário e senha no caixa de outra pessoa. Devolve o id da autorização, ou null se usuário ou
-- senha estiverem errados (ou não for gerente ativo). 5 erros em 5 minutos bloqueiam aquele gerente por 5 minutos.
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
    if p_acao not in ('cancelar_item', 'cancelar_venda', 'desconto') then
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

-- Usa uma autorização (uma vez só, até 10 minutos depois) e devolve o nome do gerente. O gerente logado não precisa.
create or replace function public.usar_autorizacao(p_id bigint, p_acao text, p_venda_id bigint)
returns text
language plpgsql
security definer
set search_path = public
as $$
declare
    v_nome text;
begin
    if public.eh_gerente() then
        return (select nome from public.perfis where id = auth.uid());
    end if;
    update public.autorizacoes
    set usada = true
    where id = p_id and aprovada and not usada and acao = p_acao and operador_id = auth.uid()
      and venda_id is not distinct from p_venda_id and criada_em > now() - interval '10 minutes'
    returning gerente_nome into v_nome;
    if v_nome is null then
        raise exception 'Esta ação precisa da autorização do gerente.' using errcode = '22023';
    end if;
    return v_nome;
end;
$$;

-- Troca o reservar_item do 006: diminuir ou tirar um item já lido é "cancelar item" e precisa de autorização.
drop function if exists public.reservar_item(bigint, numeric, boolean, numeric);
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
    v_subtotal numeric(10, 2);
    v_disponivel numeric;
    v_vencido numeric;
    v_gerente text;
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
        else round(v_qtd * coalesce(v_item.preco_unitario, v_produto.preco), 2) end;

    if v_item.id is null then
        insert into public.venda_itens (venda_id, produto_id, nome_produto, unidade_medida, quantidade, preco_unitario, subtotal)
        values (v_venda, p_produto_id, v_produto.nome, v_produto.unidade_medida, v_qtd, v_produto.preco, v_subtotal)
        returning * into v_item;
    else
        update public.venda_itens set quantidade = v_qtd, subtotal = v_subtotal
        where id = v_item.id
        returning * into v_item;
    end if;
    perform public.recalcular_venda(v_venda);
    return jsonb_build_object('venda_id', v_venda, 'item_id', v_item.id, 'quantidade', v_item.quantidade, 'subtotal', v_item.subtotal);
end;
$$;

-- Desconto em R$ na venda aberta (0 tira o desconto). Operador precisa da autorização do gerente.
create or replace function public.aplicar_desconto(p_venda_id bigint, p_valor numeric, p_autorizacao bigint default null)
returns numeric
language plpgsql
security definer
set search_path = public
as $$
declare
    v_venda public.vendas%rowtype;
    v_itens numeric(10, 2);
    v_gerente text;
begin
    select * into v_venda from public.vendas where id = p_venda_id for update;
    if not found or v_venda.situacao <> 'aberta' then
        raise exception 'Esta venda não está mais aberta. Atualize a tela.' using errcode = '22023';
    end if;
    if v_venda.operador_id <> auth.uid() and not public.eh_gerente() then
        raise exception 'Esta venda é de outro caixa.' using errcode = '42501';
    end if;
    select coalesce(sum(subtotal), 0) into v_itens from public.venda_itens where venda_id = p_venda_id;
    if p_valor is null or p_valor < 0 or p_valor > v_itens then
        raise exception 'O desconto precisa ficar entre R$ 0,00 e o valor dos itens.' using errcode = '22023';
    end if;
    v_gerente := public.usar_autorizacao(p_autorizacao, 'desconto', p_venda_id);
    update public.vendas
    set desconto = round(p_valor, 2), desconto_autorizado_por = case when p_valor > 0 then v_gerente end
    where id = p_venda_id;
    perform public.recalcular_venda(p_venda_id);
    return (select total from public.vendas where id = p_venda_id);
end;
$$;

-- Troca o finalizar_venda do 006: aceita várias formas de pagamento e o CPF/CNPJ da nota.
-- p_pagamentos = [{"forma": "dinheiro", "valor": 50, "recebido": 100},
--                 {"forma": "credito", "valor": 30.5, "bandeira": "Visa", "nsu": "...", "autorizacao": "...",
--                  "parcelas": 2, "final_cartao": "1234"}, ...]
-- A soma dos valores tem que ser o total. Só o dinheiro tem troco. Devolve {"id", "total", "troco"}.
drop function if exists public.finalizar_venda(bigint, text, numeric);
create or replace function public.finalizar_venda(p_venda_id bigint, p_pagamentos jsonb, p_cpf text default null)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_venda public.vendas%rowtype;
    v_item public.venda_itens%rowtype;
    v_lote public.lotes%rowtype;
    v_pag jsonb;
    v_falta numeric(12, 3);
    v_sai numeric(12, 3);
    v_total numeric(10, 2);
    v_pago numeric(10, 2) := 0;
    v_troco numeric(10, 2) := 0;
    v_valor numeric(10, 2);
    v_recebido numeric(10, 2);
    v_formas text[] := '{}';
    v_cpf text := nullif(regexp_replace(coalesce(p_cpf, ''), '[^0-9]', '', 'g'), '');
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
    if not exists (select 1 from public.venda_itens where venda_id = p_venda_id) then
        raise exception 'Adicione pelo menos um produto à venda.' using errcode = '22023';
    end if;
    if v_cpf is not null and length(v_cpf) not in (11, 14) then
        raise exception 'CPF ou CNPJ da nota inválido.' using errcode = '22023';
    end if;
    if p_pagamentos is null or jsonb_typeof(p_pagamentos) <> 'array' or jsonb_array_length(p_pagamentos) = 0 then
        raise exception 'Receba o pagamento antes de finalizar.' using errcode = '22023';
    end if;

    perform public.recalcular_venda(p_venda_id);
    select total into v_total from public.vendas where id = p_venda_id;

    -- Confere os pagamentos antes de mexer no estoque
    for v_pag in select * from jsonb_array_elements(p_pagamentos) loop
        v_valor := round((v_pag ->> 'valor')::numeric, 2);
        if (v_pag ->> 'forma') not in ('dinheiro', 'pix', 'debito', 'credito', 'vale_alimentacao', 'vale_refeicao') or v_valor is null or v_valor <= 0 then
            raise exception 'Pagamento inválido. Confira as formas de pagamento.' using errcode = '22023';
        end if;
        v_recebido := round(nullif(v_pag ->> 'recebido', '')::numeric, 2);
        if (v_pag ->> 'forma') = 'dinheiro' and v_recebido is not null and v_recebido < v_valor then
            raise exception 'O valor recebido em dinheiro é menor que o valor pago.' using errcode = '22023';
        end if;
        v_pago := v_pago + v_valor;
        if not (v_pag ->> 'forma') = any (v_formas) then v_formas := v_formas || (v_pag ->> 'forma'); end if;
    end loop;
    if v_pago <> v_total then
        raise exception 'Os pagamentos (R$ %) não fecham com o total da venda (R$ %).',
            replace(to_char(v_pago, 'FM999990.00'), '.', ','), replace(to_char(v_total, 'FM999990.00'), '.', ',')
            using errcode = '22023';
    end if;

    -- Baixa nos lotes: o que vence primeiro sai primeiro; lote vencido não entra
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

    for v_pag in select * from jsonb_array_elements(p_pagamentos) loop
        v_valor := round((v_pag ->> 'valor')::numeric, 2);
        v_recebido := case when (v_pag ->> 'forma') = 'dinheiro'
                           then coalesce(round(nullif(v_pag ->> 'recebido', '')::numeric, 2), v_valor) end;
        insert into public.venda_pagamentos (venda_id, forma, valor, recebido, troco, bandeira, nsu, autorizacao, parcelas, final_cartao)
        values (p_venda_id, v_pag ->> 'forma', v_valor, v_recebido, coalesce(v_recebido - v_valor, 0),
                nullif(v_pag ->> 'bandeira', ''), nullif(v_pag ->> 'nsu', ''), nullif(v_pag ->> 'autorizacao', ''),
                nullif(v_pag ->> 'parcelas', '')::integer, nullif(v_pag ->> 'final_cartao', ''));
        v_troco := v_troco + coalesce(v_recebido - v_valor, 0);
    end loop;

    update public.vendas
    set situacao = 'paga', forma_pagamento = case when cardinality(v_formas) = 1 then v_formas[1] else 'varias' end,
        recebido = case when 'dinheiro' = any (v_formas) then (select sum(recebido) from public.venda_pagamentos where venda_id = p_venda_id) end,
        troco = v_troco, cpf_nota = v_cpf, paga_em = now()
    where id = p_venda_id;
    return jsonb_build_object('id', p_venda_id, 'total', v_total, 'troco', v_troco);
end;
$$;

-- Troca o cancelar_venda do 006: venda aberta ou em espera com itens precisa do gerente (o operador sozinho não
-- cancela). Venda paga continua só pelo gerente logado; os itens voltam para os mesmos lotes.
drop function if exists public.cancelar_venda(bigint);
create or replace function public.cancelar_venda(p_venda_id bigint, p_autorizacao bigint default null)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
    v_venda public.vendas%rowtype;
    v_baixa record;
    v_nome text;
    v_gerente text;
begin
    select nome into v_nome from public.perfis where id = auth.uid() and ativo;
    if not found then
        raise exception 'Usuário desativado ou sem perfil. Fale com o gerente.' using errcode = '42501';
    end if;
    select * into v_venda from public.vendas where id = p_venda_id for update;
    if not found or v_venda.situacao = 'cancelada' then
        raise exception 'Esta venda já foi cancelada. Atualize a tela.' using errcode = '22023';
    end if;

    if v_venda.situacao in ('aberta', 'espera') then
        if v_venda.operador_id <> auth.uid() and not public.eh_gerente() then
            raise exception 'Esta venda é de outro caixa.' using errcode = '42501';
        end if;
        if exists (select 1 from public.venda_itens where venda_id = p_venda_id) then
            v_gerente := public.usar_autorizacao(p_autorizacao, 'cancelar_venda', p_venda_id);
            insert into public.venda_itens_cancelados (venda_id, nome_produto, unidade_medida, quantidade, valor, operador_nome, autorizado_por)
            select p_venda_id, nome_produto, unidade_medida, quantidade, subtotal, v_venda.operador_nome, v_gerente
            from public.venda_itens where venda_id = p_venda_id;
        end if;
        delete from public.venda_itens where venda_id = p_venda_id;
        update public.vendas set situacao = 'cancelada', total = 0, desconto = 0, cancelada_em = now(),
                                 cancelada_por = coalesce(v_gerente, v_nome)
        where id = p_venda_id;
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

-- Põe a venda aberta em espera (os itens continuam reservados) para atender o próximo cliente.
create or replace function public.suspender_venda(p_venda_id bigint)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
    v_venda public.vendas%rowtype;
begin
    select * into v_venda from public.vendas where id = p_venda_id for update;
    if not found or v_venda.situacao <> 'aberta' or v_venda.operador_id <> auth.uid() then
        raise exception 'Esta venda não está mais aberta. Atualize a tela.' using errcode = '22023';
    end if;
    if not exists (select 1 from public.venda_itens where venda_id = p_venda_id) then
        raise exception 'A venda está vazia: não precisa ficar em espera.' using errcode = '22023';
    end if;
    update public.vendas set situacao = 'espera' where id = p_venda_id;
end;
$$;

-- Volta uma venda em espera para o caixa. A venda aberta atual precisa estar vazia (vazia, ela é encerrada).
create or replace function public.retomar_venda(p_venda_id bigint)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
    v_venda public.vendas%rowtype;
    v_aberta bigint;
begin
    select * into v_venda from public.vendas where id = p_venda_id for update;
    if not found or v_venda.situacao <> 'espera' or v_venda.operador_id <> auth.uid() or v_venda.dia <> public.hoje_local() then
        raise exception 'Esta venda não está mais em espera. Atualize a tela.' using errcode = '22023';
    end if;
    select id into v_aberta from public.vendas where caixa_id = v_venda.caixa_id and situacao = 'aberta';
    if found then
        if exists (select 1 from public.venda_itens where venda_id = v_aberta) then
            raise exception 'Termine ou ponha em espera a venda atual antes de retomar outra.' using errcode = '22023';
        end if;
        update public.vendas set situacao = 'cancelada', total = 0, cancelada_em = now(), cancelada_por = 'Venda vazia'
        where id = v_aberta;
    end if;
    update public.vendas set situacao = 'aberta' where id = p_venda_id;
end;
$$;

-- Abrir o caixa também cancela as vendas em espera de dias anteriores (deixam de reservar).
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

    delete from public.venda_itens i using public.vendas v
    where i.venda_id = v.id and v.operador_id = auth.uid() and v.situacao in ('aberta', 'espera') and v.dia < public.hoje_local();
    update public.vendas
    set situacao = 'cancelada', total = 0, cancelada_em = now(), cancelada_por = 'Venda não terminada no dia'
    where operador_id = auth.uid() and situacao in ('aberta', 'espera') and dia < public.hoje_local();

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

-- Só usuários logados chamam as funções novas.
revoke execute on function public.recalcular_venda(bigint) from public, anon, authenticated;
revoke execute on function public.usar_autorizacao(bigint, text, bigint) from public, anon, authenticated;
revoke execute on function public.disponivel_para_venda(bigint, bigint) from public, anon;
revoke execute on function public.autorizar_gerente(text, text, text, bigint) from public, anon;
revoke execute on function public.reservar_item(bigint, numeric, boolean, numeric, bigint) from public, anon;
revoke execute on function public.aplicar_desconto(bigint, numeric, bigint) from public, anon;
revoke execute on function public.finalizar_venda(bigint, jsonb, text) from public, anon;
revoke execute on function public.cancelar_venda(bigint, bigint) from public, anon;
revoke execute on function public.suspender_venda(bigint) from public, anon;
revoke execute on function public.retomar_venda(bigint) from public, anon;
revoke execute on function public.abrir_caixa(numeric) from public, anon;
grant execute on function public.disponivel_para_venda(bigint, bigint) to authenticated;
grant execute on function public.autorizar_gerente(text, text, text, bigint) to authenticated;
grant execute on function public.reservar_item(bigint, numeric, boolean, numeric, bigint) to authenticated;
grant execute on function public.aplicar_desconto(bigint, numeric, bigint) to authenticated;
grant execute on function public.finalizar_venda(bigint, jsonb, text) to authenticated;
grant execute on function public.cancelar_venda(bigint, bigint) to authenticated;
grant execute on function public.suspender_venda(bigint) to authenticated;
grant execute on function public.retomar_venda(bigint) to authenticated;
grant execute on function public.abrir_caixa(numeric) to authenticated;
