using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MercadinhoSeuZe.Models;
using MercadinhoSeuZe.Services;
using MercadinhoSeuZe.Testes.Motor;
using MercadinhoSeuZe.Views;

namespace MercadinhoSeuZe.Testes.Testes;

/// <summary>
/// Regras e contas que ficam no programa (sem banco e sem internet): campos dos formulários, código de barras, balança,
/// CPF/CNPJ, Pix, validade, estoque, promoções, fornecedores, mensagem do pedido, nota fiscal e paginação.
/// </summary>
public static class LogicaTestes
{
    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Today);

    public static Lista Montar()
    {
        var l = new Lista("Lógica");

        // ---------------------------------------------------------------- Campos dos formulários
        l.Certo("Preço: \"8,50\", \"R$ 1.234,90\", \"1234,9\" e \"99.999.999,99\" são lidos certo", () =>
        {
            Verificar.Igual(8.50m, Formulario.LerPreco("8,50"), "8,50");
            Verificar.Igual(1234.90m, Formulario.LerPreco("R$ 1.234,90"), "R$ 1.234,90");
            Verificar.Igual(1234.9m, Formulario.LerPreco("1234,9"), "1234,9");
            Verificar.Igual(99_999_999.99m, Formulario.LerPreco("99.999.999,99"), "99.999.999,99");
        });
        l.Errado("Preço: texto, negativo, três casas e vazio são recusados", () =>
        {
            foreach (var t in new[] { "", "abc", "-5", "1,234", "8.5", "R$", "1,2,3", "1.23,00" })
                Verificar.Nulo(Formulario.LerPreco(t), $"Preço \"{t}\"");
        });
        l.Limite("Preço: \"0\" passa no campo (quem barra é a tela e o banco, que exige preço > 0)", () =>
        {
            Verificar.Igual(0m, Formulario.LerPreco("0"), "0");
            Verificar.Igual(0.01m, Formulario.LerPreco("0,01"), "0,01");
        });
        l.Certo("Inteiro e quantidade (unidade e quilo)", () =>
        {
            Verificar.Igual(10, Formulario.LerInteiro("10"), "10");
            Verificar.Igual(7, Formulario.LerInteiro(" 7 "), "\" 7 \"");
            Verificar.Igual(12.35m, Formulario.LerQuantidade("12,350", true), "12,350 kg");
            Verificar.Igual(12.5m, Formulario.LerQuantidade("12.5", true), "12.5 kg");
            Verificar.Igual(0.001m, Formulario.LerQuantidade("0,001 kg", true), "0,001 kg");
            Verificar.Igual(5m, Formulario.LerQuantidade("5", false), "5 un.");
        });
        l.Errado("Quantidade: fração em produto por unidade, negativo e texto são recusados", () =>
        {
            Verificar.Nulo(Formulario.LerQuantidade("2,5", false), "2,5 un.");
            Verificar.Nulo(Formulario.LerQuantidade("-1", false), "-1 un.");
            Verificar.Nulo(Formulario.LerQuantidade("-1", true), "-1 kg");
            Verificar.Nulo(Formulario.LerQuantidade("abc", true), "abc kg");
            Verificar.Nulo(Formulario.LerInteiro("1,5"), "1,5 inteiro");
        });
        l.Limite("Quantidade: até 999.999 kg com 3 casas; 7 dígitos ou 4 casas são recusados; inteiro gigante não estoura", () =>
        {
            Verificar.Igual(999999.999m, Formulario.LerQuantidade("999999,999", true), "999999,999 kg");
            Verificar.Nulo(Formulario.LerQuantidade("1000000", true), "1000000 kg");
            Verificar.Nulo(Formulario.LerQuantidade("1,2345", true), "1,2345 kg");
            Verificar.Nulo(Formulario.LerInteiro("99999999999"), "99999999999");
            Verificar.Igual(int.MaxValue, Formulario.LerInteiro(int.MaxValue.ToString()), "int máximo");
        });
        l.Certo("Datas dd/mm/aaaa, d/m/aa e ddmmaaaa; 29/02 em ano bissexto", () =>
        {
            Verificar.Igual(new DateOnly(2025, 1, 1), Formulario.LerData("01/01/2025"), "01/01/2025");
            Verificar.Igual(new DateOnly(2025, 1, 1), Formulario.LerData("1/1/25"), "1/1/25");
            Verificar.Igual(new DateOnly(2025, 1, 1), Formulario.LerData("01012025"), "01012025");
            Verificar.Igual(new DateOnly(2024, 2, 29), Formulario.LerData("29/02/2024"), "29/02/2024");
        });
        l.Errado("Datas impossíveis ou em outro formato são recusadas", () =>
        {
            foreach (var t in new[] { "29/02/2023", "31/04/2025", "32/01/2025", "00/01/2025", "2025-01-01", "amanhã", "" })
                Verificar.Nulo(Formulario.LerData(t), $"Data \"{t}\"");
        });
        l.Certo("Campo de dinheiro: digitar 1,5,0,0,0 vira 150,00 e só números entram", () =>
        {
            var campo = CampoDinheiro();
            Digitar(campo, "15000");
            Verificar.Igual("150,00", campo.Text, "Depois de 1 5 0 0 0");
            Digitar(campo, "abc");
            Verificar.Igual("150,00", campo.Text, "Letras no campo de dinheiro");
            Digitar(campo, "0");
            Verificar.Igual("1.500,00", campo.Text, "Acima de mil");
        });
        l.Limite("Campo de dinheiro: para em R$ 99.999,99", () =>
        {
            var campo = CampoDinheiro();
            Digitar(campo, "9999999");
            Verificar.Igual("99.999,99", campo.Text, "Depois de 7 noves");
            Digitar(campo, "9");
            Verificar.Igual("99.999,99", campo.Text, "Um dígito a mais");
            var outro = CampoDinheiro();
            Digitar(outro, "123456789012");
            Verificar.Igual("12.345,67", outro.Text, "12 dígitos (os que passam do máximo são ignorados)");
        });

        // ---------------------------------------------------------------- Código de barras e balança
        l.Certo("Dígito do EAN-13 (Coca-Cola 2 L: 789490001151 → 7)", () =>
        {
            Verificar.Igual("7894900011517", CodigoBarras.ComDigito("789490001151"), "ComDigito");
            Verificar.Verdadeiro(CodigoBarras.DigitoOk("7894900011517"), "DigitoOk deveria aceitar 7894900011517");
            Verificar.Verdadeiro(!CodigoBarras.DigitoOk("7894900011518"), "DigitoOk deveria recusar 7894900011518");
            Verificar.Igual('7', OpenFoodFactsService.DigitoVerificador("789490001151"), "Dígito do leitor de cadastro");
        });
        l.Certo("Leitor no caixa: código da caixa (DUN-14) e da unidade (EAN-13) acham o mesmo produto", () =>
        {
            var dun = "1789490001151" + OpenFoodFactsService.DigitoVerificador("1789490001151");
            var p = new Produto { Id = 1, Nome = "Refrigerante 2 L", CodigoBarrasCaixa = dun, Preco = 9.99m };
            var outros = new[] { new Produto { Id = 2, Nome = "Outro", CodigoBarrasCaixa = "17891234567895" }, p };
            Verificar.Igual(1L, CodigoBarras.Ler(outros, dun)?.Produto.Id ?? 0, "Leitura da caixa");
            Verificar.Igual(1L, CodigoBarras.Ler(outros, "7894900011517")?.Produto.Id ?? 0, "Leitura da unidade");
            Verificar.Igual(1L, CodigoBarras.Ler(outros, "  " + dun + " ")?.Produto.Id ?? 0, "Leitura com espaços");
            var consulta = OpenFoodFactsService.CodigosParaConsultar(dun).ToList();
            Verificar.Verdadeiro(consulta.Contains("7894900011517"), "O cadastro deveria procurar também o código da unidade");
        });
        l.Errado("Leitor: código desconhecido, letras e vazio não acham nada", () =>
        {
            var p = new[] { new Produto { Id = 1, CodigoBarrasCaixa = "17894900011514" } };
            Verificar.Nulo(CodigoBarras.Ler(p, "7890000000000"), "Código desconhecido");
            Verificar.Nulo(CodigoBarras.Ler(p, "ABC"), "Letras");
            Verificar.Nulo(CodigoBarras.Ler(p, ""), "Vazio");
            Verificar.Verdadeiro(!OpenFoodFactsService.CodigosParaConsultar("12a4").Any(), "Código com letra não vai para a busca");
        });
        l.Certo("Etiqueta da balança: PLU de 4 e de 5 dígitos, valor e peso", () =>
        {
            var carne = new Produto { Id = 7, Nome = "Alcatra", UnidadeMedida = "kg", Preco = 40m, CodigoBalanca = 1234 };
            var frango = new Produto { Id = 8, Nome = "Frango", UnidadeMedida = "kg", Preco = 20m, CodigoBalanca = 12345 };
            var lista = new[] { carne, frango };
            var e4 = CodigoBarras.ComDigito("212340001500");
            var l4 = Verificar.NaoNulo(CodigoBarras.Ler(lista, e4), "Etiqueta de 4 dígitos");
            Verificar.Igual(7L, l4.Produto.Id, "Produto da etiqueta de 4 dígitos");
            Verificar.Igual(15.00m, l4.ValorEtiqueta, "Valor impresso");
            Verificar.Igual(0.375m, l4.PesoEtiqueta, "Peso (15,00 ÷ 40,00)");
            var e5 = CodigoBarras.ComDigito("212345001500");
            Verificar.Igual(8L, Verificar.NaoNulo(CodigoBarras.Ler(lista, e5), "Etiqueta de 5 dígitos").Produto.Id, "Produto da etiqueta de 5 dígitos");
        });
        l.Errado("Etiqueta da balança: dígito errado, valor zero e PLU sem produto são recusados", () =>
        {
            var lista = new[] { new Produto { Id = 7, UnidadeMedida = "kg", Preco = 40m, CodigoBalanca = 1234 } };
            var certa = CodigoBarras.ComDigito("212340001500");
            var errada = certa[..12] + (char)('0' + (certa[12] - '0' + 1) % 10);
            Verificar.Nulo(CodigoBarras.Etiqueta(lista, errada), "Dígito verificador errado");
            Verificar.Nulo(CodigoBarras.Etiqueta(lista, CodigoBarras.ComDigito("212340000000")), "Valor R$ 0,00");
            Verificar.Nulo(CodigoBarras.Etiqueta(lista, CodigoBarras.ComDigito("299990001500")), "PLU sem produto");
        });
        l.Limite("Etiqueta da balança: valor máximo (R$ 9.999,99) e peso de produto de R$ 0,01/kg", () =>
        {
            var p = new Produto { Id = 1, UnidadeMedida = "kg", Preco = 0.01m, CodigoBalanca = 1 };
            var leitura = Verificar.NaoNulo(CodigoBarras.Etiqueta([p], CodigoBarras.ComDigito("200010999999")), "Etiqueta R$ 9.999,99");
            Verificar.Igual(9999.99m, leitura.ValorEtiqueta, "Valor máximo da etiqueta");
            Verificar.Igual(999999m, leitura.PesoEtiqueta, "Peso calculado");
        });

        // ---------------------------------------------------------------- CPF, CNPJ e Pix
        l.Certo("CPF e CNPJ válidos, com e sem pontuação", () =>
        {
            Verificar.Verdadeiro(Documento.Valido("52998224725"), "CPF 529.982.247-25");
            Verificar.Verdadeiro(Documento.Valido("529.982.247-25"), "CPF com pontos");
            Verificar.Verdadeiro(Documento.Valido("11.222.333/0001-81"), "CNPJ 11.222.333/0001-81");
            Verificar.Igual("529.982.247-25", Documento.Formatar("52998224725"), "Formatar CPF");
            Verificar.Igual("11.222.333/0001-81", Documento.Formatar("11222333000181"), "Formatar CNPJ");
        });
        l.Errado("CPF/CNPJ com dígito errado, repetido ou tamanho errado são recusados", () =>
        {
            foreach (var d in new[] { "52998224724", "11111111111", "00000000000", "11222333000180", "123", "", "529982247251", "abc.def.ghi-jk" })
                Verificar.Verdadeiro(!Documento.Valido(d), $"\"{d}\" deveria ser inválido");
        });
        l.Certo("Pix copia e cola: campos, valor e CRC16 conferem", () =>
        {
            Verificar.Igual("29B1", Crc16("123456789"), "CRC16 de referência");
            var pix = PixCopiaECola.Gerar(10.5m, "VENDA-123");
            Verificar.Verdadeiro(pix.StartsWith("000201"), "O Pix tem que começar com 000201");
            Verificar.Verdadeiro(pix.Contains("540510.50"), "Valor 10.50 no campo 54");
            Verificar.Verdadeiro(pix.Contains("VENDA123"), "Identificador sem o traço");
            Verificar.Igual(Crc16(pix[..^4]), pix[^4..], "CRC16 do Pix");
        });
        l.Limite("Pix: valor de R$ 99.999,99, identificador com 40 letras (corta em 25) e sem identificador", () =>
        {
            var pix = PixCopiaECola.Gerar(99999.99m, new string('A', 40));
            Verificar.Verdadeiro(pix.Contains("540899999.99"), "Valor 99999.99");
            Verificar.Verdadeiro(pix.Contains("0525" + new string('A', 25)) && !pix.Contains(new string('A', 26)), "Identificador cortado em 25");
            Verificar.Igual(Crc16(pix[..^4]), pix[^4..], "CRC16");
            Verificar.Verdadeiro(PixCopiaECola.Gerar(1m, "---").Contains("0503***"), "Sem letras: ***");
            Verificar.Verdadeiro(pix.Length < 512, "O Pix passa de 512 letras");
        });

        // ---------------------------------------------------------------- Validade, estoque e reposição
        l.Certo("Situação da validade: venceu, hoje, amanhã, 30 dias e 31 dias", () =>
        {
            Verificar.Igual("Venceu ontem", SituacaoValidade.De(Hoje.AddDays(-1))!.Texto, "Ontem");
            Verificar.Igual(NivelValidade.Vencido, SituacaoValidade.De(Hoje.AddDays(-10))!.Nivel, "10 dias atrás");
            Verificar.Igual("Vence hoje", SituacaoValidade.De(Hoje)!.Texto, "Hoje");
            Verificar.Igual("Vence amanhã", SituacaoValidade.De(Hoje.AddDays(1))!.Texto, "Amanhã");
            Verificar.Igual(NivelValidade.Atencao, SituacaoValidade.De(Hoje.AddDays(30))!.Nivel, "30 dias (ainda é alerta)");
            Verificar.Igual(NivelValidade.Ok, SituacaoValidade.De(Hoje.AddDays(31))!.Nivel, "31 dias");
            Verificar.Nulo(SituacaoValidade.De(null), "Sem validade");
        });
        l.Certo("Estoque: sem estoque, baixo, aviso antecipado e vencido não entra no vendável", () =>
        {
            var p = NovoProduto(minimo: 10, lotes: [NovoLote(3)]);
            Verificar.Verdadeiro(p.EstoqueBaixo && !p.SemEstoque, "3 com mínimo 10 = estoque baixo");
            Verificar.Verdadeiro(NovoProduto(minimo: 10, lotes: []).SemEstoque, "Sem lote = sem estoque");
            var aviso = NovoProduto(minimo: 10, lotes: [NovoLote(14)]);
            Verificar.Verdadeiro(aviso.AvisoAntecipado, "14 com mínimo 10 = aviso antecipado (até 15)");
            Verificar.Verdadeiro(!NovoProduto(minimo: 10, lotes: [NovoLote(16)]).PrecisaRepor, "16 com mínimo 10 não precisa repor");
            var vencido = NovoProduto(minimo: 0, lotes: [NovoLote(5, validade: Hoje.AddDays(-1)), NovoLote(7, validade: Hoje)]);
            Verificar.Igual(7m, vencido.Vendavel, "Vendável (sem o lote vencido; o de hoje ainda vende)");
            Verificar.Igual(5m, vencido.QuantidadeVencida, "Quantidade vencida");
            vencido.Reservado = 20;
            Verificar.Igual(0m, vencido.DisponivelParaVenda, "Reservado maior que o vendável não fica negativo");
        });
        l.Certo("Aviso padrão (mínimo + 50%) e quantidade sugerida em caixas inteiras", () =>
        {
            Verificar.Igual(15m, Models.Produto.AvisoPadrao(10, false), "Aviso de mínimo 10 un.");
            Verificar.Igual(3.8m, Models.Produto.AvisoPadrao(2.5m, true), "Aviso de mínimo 2,5 kg");
            var p = NovoProduto(minimo: 10, lotes: [NovoLote(3)]);
            p.UnidadesPorCaixa = 12;
            Verificar.Igual(24m, p.QuantidadeSugerida, "Sugestão (17 → 2 caixas de 12)");
            var kg = NovoProduto(minimo: 5, lotes: [NovoLote(1.25m)], porPeso: true);
            Verificar.Igual(8.8m, kg.QuantidadeSugerida, "Sugestão por quilo");
        });
        l.Certo("Pedir antes de acabar: sai 5 por dia, prazo 3 e mínimo 10 → ponto de pedido 25", () =>
        {
            var p = NovoProduto(minimo: 10, lotes: [NovoLote(50, inicial: 100, compra: Hoje.AddDays(-10))]);
            p.PedirAntes = true;
            p.PrazoEntregaDias = 3;
            Verificar.Igual(5m, p.ConsumoDiario, "Sai por dia");
            Verificar.Igual(25m, p.PontoDePedido, "Ponto de pedido");
            p.PedirAntes = false;
            Verificar.Nulo(p.PontoDePedido, "Desligado não tem ponto de pedido");
        });
        l.Limite("Estoque: produto com 20.000 lotes calcula quantidade, validade e vendável em menos de 0,5 s", () =>
        {
            var lotes = Enumerable.Range(0, 20_000).Select(i => NovoLote(1 + i % 7, validade: Hoje.AddDays(i % 90 - 10))).ToList();
            var p = NovoProduto(minimo: 100, lotes: lotes);
            var r = Stopwatch.StartNew();
            _ = (p.Quantidade, p.Vendavel, p.ProximaValidade, p.Situacao, p.PrecisaRepor, p.QuantidadeSugerida, p.DiasValidadeSugerida);
            r.Stop();
            Ambiente.Nota($"20.000 lotes: {r.Elapsed.TotalMilliseconds:0} ms");
            Verificar.Verdadeiro(r.Elapsed < TimeSpan.FromSeconds(0.5), $"Levou {r.Elapsed.TotalMilliseconds:0} ms");
        });

        // ---------------------------------------------------------------- Promoções
        l.Certo("Promoção de validade: escada do mínimo ao máximo e máximo no dia do vencimento", () =>
        {
            var r = new RegraValidade { DiasAntes = 5, DescontoMin = 10, DescontoMax = 50 };
            int[] dias = [6, 5, 4, 3, 2, 1, 0, -1];
            decimal[] esperado = [0, 10, 20, 30, 40, 50, 50, 0];
            for (var i = 0; i < dias.Length; i++)
                Verificar.Igual(esperado[i], r.Desconto(Hoje.AddDays(dias[i]), Hoje), $"Desconto vencendo em {dias[i]} dia(s)");
            Verificar.Igual(0m, r.Desconto(null, Hoje), "Sem validade");
            r.Ativa = false;
            Verificar.Igual(0m, r.Desconto(Hoje.AddDays(1), Hoje), "Regra desligada");
        });
        l.Limite("Promoção de validade: 1 dia antes e 60 dias antes", () =>
        {
            var um = new RegraValidade { DiasAntes = 1, DescontoMin = 10, DescontoMax = 90 };
            Verificar.Igual(90m, um.Desconto(Hoje.AddDays(1), Hoje), "1 dia antes");
            var sessenta = new RegraValidade { DiasAntes = 60, DescontoMin = 0, DescontoMax = 90 };
            Verificar.Igual(0m, sessenta.Desconto(Hoje.AddDays(60), Hoje), "Primeiro dia (mínimo 0)");
            Verificar.Igual(90m, sessenta.Desconto(Hoje.AddDays(1), Hoje), "Último dia");
            Verificar.Igual(61, sessenta.Escada().Count(), "Escada de 60 a 0");
        });
        l.Certo("Promoção por produto: preço fixo, porcentagem e situação (agendada, ativa, encerrada)", () =>
        {
            Verificar.Igual(6.99m, new Promocao { Tipo = "preco", Valor = 6.99m }.PrecoCom(8.90m), "De 8,90 por 6,99");
            Verificar.Igual(8.90m, new Promocao { Tipo = "preco", Valor = 9.50m }.PrecoCom(8.90m), "Preço fixo maior que o normal não aumenta");
            Verificar.Igual(5.00m, new Promocao { Tipo = "percentual", Valor = 50 }.PrecoCom(10m), "50% de 10,00");
            Verificar.Igual("Agendada", new Promocao { Inicio = Hoje.AddDays(1) }.Situacao, "Começa amanhã");
            Verificar.Igual("Ativa", new Promocao { Inicio = Hoje, Fim = Hoje }.Situacao, "Só hoje");
            Verificar.Igual("Encerrada", new Promocao { Inicio = Hoje.AddDays(-5), Fim = Hoje.AddDays(-1) }.Situacao, "Acabou ontem");
            Verificar.Igual("Encerrada", new Promocao { Inicio = Hoje, EncerradaEm = DateTimeOffset.Now }.Situacao, "Encerrada agora");
        });
        l.Certo("Arredondamento igual ao do caixa: 15% de R$ 8,90 = R$ 7,57 (o banco arredonda o meio centavo para cima)", () =>
        {
            // O caixa cobra pelo banco (round do PostgreSQL: 7,565 → 7,57). A tela usa Math.Round do C#, que por padrão
            // arredonda o meio para o par (7,565 → 7,56). Se der diferente, a etiqueta e o caixa mostram preços diferentes.
            var tela = new Promocao { Tipo = "percentual", Valor = 15 }.PrecoCom(8.90m);
            Verificar.Igual(7.57m, tela, "Preço com 15% na tela (o caixa cobra 7,57)");
        });
        l.Certo("Maior desconto vence: promoção do produto x promoção de validade (não soma)", () =>
        {
            var p = NovoProduto(minimo: 0, lotes: [NovoLote(5, validade: Hoje.AddDays(1)), NovoLote(5, validade: Hoje.AddDays(40))]);
            p.Preco = 10m;
            p.Categoria = "Padaria";
            Promocoes.Aplicar([p], [new Promocao { ProdutoId = p.Id, Tipo = "percentual", Valor = 20, Inicio = Hoje }],
                [new RegraValidade { Categoria = "padaria", DiasAntes = 3, DescontoMin = 10, DescontoMax = 50 }]);
            Verificar.Igual(5.00m, Promocoes.PrecoDoLote(p, p.Lotes[0]), "Lote que vence amanhã (50% de validade > 20%)");
            Verificar.Igual(8.00m, Promocoes.PrecoDoLote(p, p.Lotes[1]), "Lote que vence em 40 dias (só os 20%)");
            p.SemPromocaoValidade = true;
            Verificar.Igual(8.00m, Promocoes.PrecoDoLote(p, p.Lotes[0]), "Produto fora da promoção de validade");
        });

        // ---------------------------------------------------------------- Fornecedores e pedidos
        l.Certo("Fornecedor: telefone, WhatsApp com 55, dias de entrega e previsão", () =>
        {
            Verificar.Igual("(11) 98888-7777", Fornecedor.MascaraTelefone("11988887777"), "Celular");
            Verificar.Igual("(11) 4002-1122", Fornecedor.MascaraTelefone("1140021122"), "Fixo");
            Verificar.Igual("551140021122", new Fornecedor { Telefone = "(11) 4002-1122" }.NumeroWhatsapp, "WhatsApp");
            var f = new Fornecedor { DiasEntrega = "1,3", PrazoEntregaDias = 2 };
            Verificar.Igual("seg, qua", f.TextoDiasEntrega, "Dias de entrega");
            var domingo = new DateOnly(2026, 11, 1);
            Verificar.Igual(new DateOnly(2026, 11, 2), f.ProximaEntrega(domingo), "Próxima entrega a partir de domingo");
            Verificar.Igual(new DateOnly(2026, 11, 4), f.ProximaEntrega(new DateOnly(2026, 11, 3)), "Próxima entrega a partir de terça");
            var p = f.PrevisaoParaPrazo(2);
            Verificar.Verdadeiro(p.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday && p >= Hoje.AddDays(2), "Previsão cai num dia de entrega");
        });
        l.Certo("Fornecedor entrega a categoria sem diferenciar acento e maiúscula", () =>
        {
            var f = new Fornecedor { Id = 3, Categorias = "Laticínios, Frios" };
            Verificar.Verdadeiro(f.Fornece(new Produto { Categoria = "laticinios" }), "laticinios x Laticínios");
            Verificar.Verdadeiro(!f.Fornece(new Produto { Categoria = "Bebidas" }), "Bebidas não é dele");
            Verificar.Verdadeiro(!f.Fornece(new Produto { Categoria = "Frios", FornecedorId = 99 }), "Produto ligado a outro fornecedor");
        });
        l.Certo("Mensagem do pedido: contato, itens com caixas, previsão e \"Pedido nº\"", () =>
        {
            var f = new Fornecedor { Nome = "Laticínios Boa Vista", Contato = "João" };
            var m = MensagemPedido.Montar(f, [new("Leite 1 L", 24, false, 12), new("Queijo", 2.5m, true, null)], new DateOnly(2026, 11, 10), "Marcos");
            Verificar.Verdadeiro(m.StartsWith("Olá, João! Aqui é Marcos"), "Saudação: " + m.Split('\n')[0]);
            Verificar.Verdadeiro(m.Contains("- 24 un. Leite 1 L (2 caixas)"), "Item em caixas");
            Verificar.Verdadeiro(m.Contains("- 2,5 kg Queijo"), "Item por quilo");
            Verificar.Verdadeiro(m.Contains("10/11/2026"), "Previsão");
            var sem = MensagemPedido.Montar(new Fornecedor { Nome = "X" }, [new("A", 1, false, null)], null, "Ana");
            Verificar.Verdadeiro(sem.StartsWith("Olá! Aqui é Ana") && sem.Contains("a combinar"), "Sem contato e sem previsão");
            var n = MensagemPedido.ComNumero(42, m);
            Verificar.Verdadeiro(n.StartsWith("Pedido nº 42"), "Número no começo");
            Verificar.Igual(n, MensagemPedido.ComNumero(42, n), "Não repete o número");
        });
        l.Limite("Mensagem do pedido com 500 itens e nomes de 80 letras", () =>
        {
            var itens = Enumerable.Range(1, 500).Select(i => new MensagemPedido.Linha(new string('x', 75) + i.ToString("000"), i, false, 6)).ToList();
            var m = MensagemPedido.Montar(new Fornecedor { Nome = "F" }, itens, Hoje, "A");
            Verificar.Igual(500, m.Split('\n').Count(x => x.StartsWith("- ")), "Itens na mensagem");
            Ambiente.Nota($"Mensagem com {m.Length:N0} letras");
            if (Uri.EscapeDataString(m).Length > 30_000)
                Ambiente.AvisoPendente($"O link do WhatsApp fica com {Uri.EscapeDataString(m).Length:N0} letras: o Windows/navegador pode cortar. Pedidos muito grandes vão melhor por e-mail.");
        });
        l.Certo("Pedido: total pelo custo e atraso pela previsão", () =>
        {
            var p = new Pedido
            {
                Previsao = Hoje.AddDays(-1),
                Itens = [new ItemPedido { Quantidade = 10, PrecoCusto = 2.5m }, new ItemPedido { Quantidade = 4, QuantidadeRecebida = 3, PrecoCusto = 10m }]
            };
            Verificar.Igual(55m, p.Total, "Total (10 × 2,50 + 3 × 10,00)");
            Verificar.Verdadeiro(p.Atrasado, "Pendente com previsão ontem está atrasado");
            p.Itens.Add(new ItemPedido { Quantidade = 1 });
            Verificar.Nulo(p.Total, "Total com item sem custo");
        });

        // ---------------------------------------------------------------- Nota fiscal (XML)
        l.Certo("XML da nota: número, emitente, itens, validade e ligação pelo código de barras e pelo nome", () =>
        {
            var arq = Xml(NotaExemplo);
            var nota = NotaFiscalXml.Ler(arq);
            Verificar.Igual("12345", nota.Numero, "Número da nota");
            Verificar.Igual("Distribuidora Teste", nota.Emitente, "Emitente");
            Verificar.Igual(2, nota.Itens.Count, "Itens");
            Verificar.Igual(new DateOnly(2027, 1, 31), nota.Itens[0].Validade, "Validade do lote");
            var produtos = new List<Produto>
            {
                new() { Id = 1, Nome = "Leite Integral 1L", CodigoBarrasCaixa = "17891000100103", UnidadesPorCaixa = 12 },
                new() { Id = 2, Nome = "Arroz Tipo 1 5kg", UnidadesPorCaixa = 1 },
            };
            var (achados, sobraram) = NotaFiscalXml.Ligar(nota, produtos);
            Verificar.Igual(24m, achados[1].Quantidade, "Leite: 2 caixas de 12");
            Verificar.Verdadeiro(achados.ContainsKey(2), "Arroz ligado pelo nome");
            Verificar.Igual(0, sobraram.Count, "Linhas da nota sem produto");
        });
        l.Errado("XML da nota: arquivo quebrado, vazio, de outra coisa ou sem produtos é recusado", () =>
        {
            Verificar.Recusa(() => NotaFiscalXml.Ler(Xml("<nfeProc><infNFe>")), "XML quebrado", "não é um XML válido");
            Verificar.Recusa(() => NotaFiscalXml.Ler(Xml("")), "Arquivo vazio");
            Verificar.Recusa(() => NotaFiscalXml.Ler(Xml("<html><body>oi</body></html>")), "Outro XML", "NF-e");
            Verificar.Recusa(() => NotaFiscalXml.Ler(Xml("<nfeProc xmlns=\"http://www.portalfiscal.inf.br/nfe\"><NFe><infNFe/></NFe></nfeProc>")), "Nota sem produtos", "não tem produtos");
        });
        l.Limite("XML da nota com 990 itens (máximo da NF-e) é lido em menos de 4 s", () =>
        {
            var sb = new StringBuilder("<nfeProc xmlns=\"http://www.portalfiscal.inf.br/nfe\"><NFe><infNFe><ide><nNF>1</nNF></ide>");
            for (var i = 1; i <= 990; i++)
                sb.Append($"<det nItem=\"{i}\"><prod><cEAN>SEM GTIN</cEAN><xProd>Produto {i}</xProd><uCom>UN</uCom><qCom>1.0000</qCom><vUnCom>1.00</vUnCom><cEANTrib>SEM GTIN</cEANTrib><uTrib>UN</uTrib><qTrib>1.0000</qTrib><vUnTrib>1.00</vUnTrib></prod></det>");
            sb.Append("</infNFe></NFe></nfeProc>");
            var r = Stopwatch.StartNew();
            var nota = NotaFiscalXml.Ler(Xml(sb.ToString()));
            NotaFiscalXml.Ligar(nota, Enumerable.Range(1, 300).Select(i => new Produto { Id = i, Nome = $"Produto {i}" }).ToList());
            r.Stop();
            Ambiente.Nota($"990 itens × 300 produtos: {r.Elapsed.TotalMilliseconds:0} ms");
            Verificar.Igual(990, nota.Itens.Count, "Itens lidos");
            Verificar.Verdadeiro(r.Elapsed < TimeSpan.FromSeconds(4), $"Levou {r.Elapsed.TotalSeconds:0.0} s");
        });

        // ---------------------------------------------------------------- Paginação e resumo
        l.Certo("Paginação: 137 itens em páginas de 10, \"11–20 de 137\" e última página", () =>
        {
            var pg = new Paginacao("teste-" + Guid.NewGuid(), [5, 10, 25], 10);
            var itens = Enumerable.Range(1, 137).ToList();
            Verificar.Igual(10, pg.Recortar(itens).Count, "Itens na página 1");
            Verificar.Igual(14, pg.Paginas, "Páginas");
            Verificar.Igual("1–10 de 137", pg.Faixa(), "Faixa da página 1");
            IrPara(pg, 2);
            Verificar.Igual(11, pg.Recortar(itens)[0], "Primeiro item da página 2");
            Verificar.Igual("11–20 de 137", pg.Faixa(), "Faixa da página 2");
            IrPara(pg, 14);
            Verificar.Igual(7, pg.Recortar(itens).Count, "Itens na última página");
        });
        l.Limite("Paginação: lista vazia, página que deixou de existir e 1 milhão de itens", () =>
        {
            var pg = new Paginacao("teste-" + Guid.NewGuid(), [5, 10], 10);
            Verificar.Igual(0, pg.Recortar(new List<int>()).Count, "Lista vazia");
            Verificar.Igual(1, pg.Paginas, "Páginas da lista vazia");
            IrPara(pg, 99);
            Verificar.Igual(5, pg.Recortar(Enumerable.Range(1, 25).ToList()).Count, "Página 99 de 3 vai para a última");
            Verificar.Igual(3, pg.Pagina, "Página depois do ajuste");
            var milhao = Enumerable.Range(1, 1_000_000).ToList();
            IrPara(pg, 50_000);
            var r = Stopwatch.StartNew();
            var pagina = pg.Recortar(milhao);
            r.Stop();
            Verificar.Igual(499_991, pagina[0], "Primeiro item da página 50.000");
            Verificar.Verdadeiro(r.Elapsed < TimeSpan.FromSeconds(1), $"Recortar 1 milhão levou {r.Elapsed.TotalMilliseconds:0} ms");
        });
        l.Certo("Resumo do caixa soma só as vendas pagas", () =>
        {
            var vendas = new List<Venda>
            {
                new() { Situacao = "paga", Total = 10 }, new() { Situacao = "paga", Total = 5.5m },
                new() { Situacao = "cancelada", Total = 100 }, new() { Situacao = "aberta", Total = 7 },
            };
            var r = CaixaService.Resumo(vendas);
            Verificar.Igual(15.5m, r.Total, "Total");
            Verificar.Igual(2, r.QuantidadeVendas, "Vendas");
            Verificar.Igual(7.75m, r.TicketMedio, "Ticket médio");
            Verificar.Igual(0m, CaixaService.Resumo([]).TicketMedio, "Ticket sem vendas (não divide por zero)");
        });
        l.Certo("Usuário vira e-mail interno (ana.caixa → ana.caixa@mercado.local)", () =>
        {
            Verificar.Igual("ana.caixa@mercado.local", AuthService.EmailDoUsuario("ana.caixa"), "Usuário");
            Verificar.Igual("x@y.com", AuthService.EmailDoUsuario("x@y.com"), "E-mail completo");
        });

        return l;
    }

    // ---------------------------------------------------------------- Ajudantes

    private static Produto NovoProduto(decimal minimo, List<Lote> lotes, bool porPeso = false) =>
        new() { Id = 1, Nome = "Produto", Categoria = "Mercearia", Preco = 10, EstoqueMinimo = minimo, Lotes = lotes, UnidadeMedida = porPeso ? "kg" : "un" };

    private static Lote NovoLote(decimal restante, decimal? inicial = null, DateOnly? compra = null, DateOnly? validade = null) =>
        new() { QuantidadeInicial = inicial ?? restante, QuantidadeRestante = restante, DataCompra = compra ?? Hoje.AddDays(-1), Validade = validade };

    private static TextBox CampoDinheiro()
    {
        var campo = new TextBox();
        Formulario.MascaraDinheiro(campo);
        return campo;
    }

    /// <summary>Simula o teclado: cada letra passa pelo PreviewTextInput, como quando a pessoa digita.</summary>
    private static void Digitar(TextBox campo, string texto)
    {
        foreach (var c in texto)
        {
            var e = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, campo, c.ToString()))
            {
                RoutedEvent = UIElement.PreviewTextInputEvent
            };
            campo.RaiseEvent(e);
        }
    }

    private static void IrPara(Paginacao pg, int pagina) =>
        typeof(Paginacao).GetProperty(nameof(Paginacao.Pagina))!.SetValue(pg, pagina);

    /// <summary>CRC16/CCITT-FALSE escrito de novo aqui, para conferir o do programa por outro caminho.</summary>
    private static string Crc16(string texto)
    {
        var crc = 0xFFFF;
        foreach (var b in Encoding.UTF8.GetBytes(texto))
        {
            crc ^= b << 8;
            for (var i = 0; i < 8; i++) crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
        }
        return crc.ToString("X4");
    }

    private static string Xml(string conteudo)
    {
        var arq = Path.Combine(Path.GetTempPath(), $"mercado-teste-{Guid.NewGuid():N}.xml");
        File.WriteAllText(arq, conteudo);
        return arq;
    }

    private const string NotaExemplo = """
        <?xml version="1.0" encoding="UTF-8"?>
        <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe"><NFe><infNFe Id="NFe1">
          <ide><nNF>12345</nNF></ide><emit><xNome>Distribuidora Teste</xNome></emit>
          <det nItem="1"><prod><cEAN>17891000100103</cEAN><xProd>LEITE INTEGRAL 1L CX 12</xProd><uCom>CX</uCom><qCom>2.0000</qCom>
            <vUnCom>60.00</vUnCom><cEANTrib>7891000100103</cEANTrib><uTrib>UN</uTrib><qTrib>24.0000</qTrib><vUnTrib>5.00</vUnTrib>
            <rastro><nLote>A1</nLote><qLote>24</qLote><dFab>2026-10-01</dFab><dVal>2027-01-31</dVal></rastro></prod></det>
          <det nItem="2"><prod><cEAN>SEM GTIN</cEAN><xProd>ARROZ TIPO 1 5KG</xProd><uCom>UN</uCom><qCom>10.0000</qCom>
            <vUnCom>25.00</vUnCom><cEANTrib>SEM GTIN</cEANTrib><uTrib>UN</uTrib><qTrib>10.0000</qTrib><vUnTrib>25.00</vUnTrib></prod></det>
        </infNFe></NFe></nfeProc>
        """;
}
