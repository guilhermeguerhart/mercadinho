-- =====================================================================
-- Semana 6: agendamentos das Edge Functions de e-mail
-- Rodar DEPOIS do 009 e depois de publicar as funções enviar-email e ler-emails (veja o README).
-- Pode rodar de novo.
--
-- ANTES de rodar, guarde no Vault do banco o mesmo texto do segredo SEGREDO_AGENDAMENTO das Edge Functions
-- (rode só uma vez, no SQL Editor, trocando o texto; ele NÃO vai para nenhum arquivo do projeto):
--     select vault.create_secret('cole-aqui-o-mesmo-segredo', 'segredo_agendamento');
--
-- - A cada 5 minutos: ler-emails busca as respostas dos fornecedores na caixa de pedidos.
-- - Às 23h (Brasília): enviar-email manda o resumo do dia se ninguém fechou (config_mercado.resumo_automatico).
-- =====================================================================

create extension if not exists pg_cron;
create extension if not exists pg_net;

-- Chama uma Edge Function deste projeto com o segredo do Vault.
create or replace function public.chamar_funcao_agendada(p_funcao text, p_corpo jsonb default '{}'::jsonb)
returns bigint
language plpgsql
security definer
set search_path = public
as $$
declare
    v_segredo text;
begin
    select decrypted_secret into v_segredo from vault.decrypted_secrets where name = 'segredo_agendamento';
    if v_segredo is null then
        raise exception 'Guarde o segredo no Vault antes (veja o começo do script 010).';
    end if;
    return net.http_post(
        url := 'https://fswyphqpswrsxvbblhyf.supabase.co/functions/v1/' || p_funcao,
        headers := jsonb_build_object('Content-Type', 'application/json', 'x-segredo', v_segredo),
        body := p_corpo,
        timeout_milliseconds := 60000);
end;
$$;

revoke execute on function public.chamar_funcao_agendada(text, jsonb) from public, anon, authenticated;

-- Tira os agendamentos antigos com o mesmo nome (para rodar o script de novo)
select cron.unschedule(jobid) from cron.job where jobname in ('mercado-ler-emails', 'mercado-resumo-23h');

select cron.schedule('mercado-ler-emails', '*/5 * * * *', $$select public.chamar_funcao_agendada('ler-emails')$$);
-- O pg_cron do Supabase usa UTC: 23h em Brasília = 2h UTC do dia seguinte. A função usa a data de Brasília.
select cron.schedule('mercado-resumo-23h', '0 2 * * *', $$select public.chamar_funcao_agendada('enviar-email', '{"tipo":"resumo_automatico"}'::jsonb)$$);
