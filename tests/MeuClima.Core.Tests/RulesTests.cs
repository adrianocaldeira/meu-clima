using MeuClima.Core.Devices;
using MeuClima.Core.Rules;
using static MeuClima.Core.Devices.DeviceCommand;
using static MeuClima.Core.Tests.TestKit;

namespace MeuClima.Core.Tests;

public class CommandPlannerTests
{
    [Fact]
    public void Turbo_com_economico_ligado_desliga_o_economico_antes()
    {
        var plan = CommandPlanner.Plan(Snap(eco: true, fan: FanSpeed.Low), [new SetFan(FanSpeed.Turbo)]);

        Assert.Equal([new SetEnergySaving(false), new SetFan(FanSpeed.Turbo)], plan);
    }

    [Fact]
    public void Em_ventilar_nao_envia_temperatura()
    {
        var plan = CommandPlanner.Plan(Snap(), [new SetMode(AcMode.FanOnly), new SetTarget(20)]);

        Assert.Equal([new SetMode(AcMode.FanOnly)], plan);
    }

    [Fact]
    public void Descarta_o_que_ja_esta_vigente()
    {
        var plan = CommandPlanner.Plan(Snap(on: true, mode: AcMode.Cool, target: 23),
            [new Power(true), new SetMode(AcMode.Cool), new SetTarget(22)]);

        Assert.Equal([new SetTarget(22)], plan);
    }

    [Fact]
    public void Desligar_ignora_os_demais_ajustes()
    {
        var plan = CommandPlanner.Plan(Snap(on: true), [new SetTarget(20), new Power(false)]);

        Assert.Equal([new Power(false)], plan);
    }

    [Fact]
    public void Liga_antes_de_ajustar()
    {
        var plan = CommandPlanner.Plan(Snap(on: false, target: 25), [new SetTarget(22), new Power(true)]);

        Assert.Equal([new Power(true), new SetTarget(22)], plan);
    }
}

public class EsfriarRapidoTests
{
    private static EsfriarRapidoRule Rule(Schedule? s = null) => new(new EsfriarRapidoConfig(Dev, Schedule: s));

    [Fact]
    public void Fora_quente_entra_na_fase_1_com_turbo_e_avisa()
    {
        var r = Rule();
        var o = r.Evaluate(Ctx(At(14), 31, Snap(ambient: 25)));

        Assert.Equal(EsfriarFase.Fase1, r.State.Fase);
        Assert.Contains(new SetFan(FanSpeed.Turbo), o.Claims[Dev]);
        Assert.Contains(new SetTarget(22), o.Claims[Dev]);
        Assert.Contains(new SetMode(AcMode.Cool), o.Claims[Dev]);
        Assert.Single(o.Notifications);
        Assert.Equal("Esfriar rápido · fase 1 de 2", o.Status!.Title);
    }

    [Fact]
    public void Abaixo_do_gatilho_nao_faz_nada()
    {
        var r = Rule();
        var o = r.Evaluate(Ctx(At(14), 23.9, Snap()));

        Assert.Empty(o.Claims);
        Assert.Equal(EsfriarFase.Parada, r.State.Fase);
    }

    [Fact]
    public void Chegando_a_22_passa_para_a_fase_2_em_auto_24()
    {
        var r = Rule();
        r.Evaluate(Ctx(At(14), 31, Snap(ambient: 25)));
        var o = r.Evaluate(Ctx(At(14, 25), 31, Snap(ambient: 22)));

        Assert.Equal(EsfriarFase.Fase2, r.State.Fase);
        Assert.Contains(new SetMode(AcMode.Auto), o.Claims[Dev]);
        Assert.Contains(new SetTarget(24), o.Claims[Dev]);
        Assert.Contains(new SetFan(FanSpeed.Auto), o.Claims[Dev]);
    }

    [Fact]
    public void Turbo_tem_teto_de_60_min_mesmo_sem_chegar_ao_alvo()
    {
        var r = Rule();
        r.Evaluate(Ctx(At(14), 31, Snap(ambient: 26)));
        r.Evaluate(Ctx(At(15, 1), 31, Snap(ambient: 24)));

        Assert.Equal(EsfriarFase.Fase2, r.State.Fase);
    }

    [Fact]
    public void Dentro_ja_fresco_no_disparo_pula_direto_para_a_fase_2()
    {
        var r = Rule();
        r.Evaluate(Ctx(At(14), 31, Snap(ambient: 22)));

        Assert.Equal(EsfriarFase.Fase2, r.State.Fase);
    }

    [Fact]
    public void Para_so_depois_de_30_min_abaixo_de_23_fora()
    {
        var r = Rule();
        r.Evaluate(Ctx(At(14), 31, Snap(ambient: 25)));
        r.Evaluate(Ctx(At(18), 22.5, Snap(ambient: 24)));
        r.Evaluate(Ctx(At(18, 20), 23.5, Snap(ambient: 24)));
        r.Evaluate(Ctx(At(18, 25), 22.5, Snap(ambient: 24)));
        Assert.NotEqual(EsfriarFase.Parada, r.State.Fase);

        var o = r.Evaluate(Ctx(At(18, 55), 22, Snap(ambient: 24)));

        Assert.Equal(EsfriarFase.Parada, r.State.Fase);
        Assert.Empty(o.Claims);
    }

    [Fact]
    public void Ausente_do_pc_nao_dispara_nem_controla()
    {
        var r = Rule();
        var longe = new PresenceState(true, TimeSpan.FromMinutes(5));

        var o = r.Evaluate(Ctx(At(14), 31, Snap(), longe));

        Assert.Empty(o.Claims);
        Assert.Equal(EsfriarFase.Parada, r.State.Fase);
    }

    [Fact]
    public void Fora_da_agenda_nao_dispara_e_encerra_se_estava_ativa()
    {
        var comercial = new Schedule([DayOfWeek.Wednesday], new TimeOnly(8, 0), new TimeOnly(19, 0));
        var r = Rule(comercial);
        r.Evaluate(Ctx(At(14), 31, Snap(ambient: 25)));
        Assert.Equal(EsfriarFase.Fase1, r.State.Fase);

        r.Evaluate(Ctx(At(19, 30), 31, Snap(ambient: 23)));

        Assert.Equal(EsfriarFase.Parada, r.State.Fase);
    }

    [Fact]
    public void Estado_sobrevive_a_reinicio()
    {
        var r = Rule();
        r.Evaluate(Ctx(At(14), 31, Snap(ambient: 25)));
        var novo = Rule();
        novo.LoadState(r.SaveState());

        Assert.Equal(EsfriarFase.Fase1, novo.State.Fase);
        Assert.Equal(25, novo.State.AmbienteInicial);
    }
}

public class SaiDoPcTests
{
    private static PresenceState Longe(int min) => new(true, TimeSpan.FromMinutes(min));

    [Fact]
    public void Ausencia_curta_nao_mexe()
    {
        var r = new SaiDoPcRule(new SaiDoPcConfig(Dev));
        Assert.Empty(r.Evaluate(Ctx(At(14), 30, Snap(), Longe(19))).Claims);
    }

    [Fact]
    public void Economia_com_20_min_desliga_com_60_e_restaura_na_volta()
    {
        var r = new SaiDoPcRule(new SaiDoPcConfig(Dev));
        var original = Snap(on: true, mode: AcMode.Cool, fan: FanSpeed.Low, target: 22);

        var eco = r.Evaluate(Ctx(At(14, 20), 30, original, Longe(20)));
        Assert.Equal([new Power(true), new SetMode(AcMode.Auto), new SetTarget(26)], eco.Claims[Dev]);

        var off = r.Evaluate(Ctx(At(15), 30, Snap(on: true, mode: AcMode.Auto, target: 26), Longe(60)));
        Assert.Equal([new Power(false)], off.Claims[Dev]);

        var volta = r.Evaluate(Ctx(At(15, 30), 30, Snap(on: false, mode: AcMode.Auto, target: 26)));
        Assert.Equal([new Power(true), new SetMode(AcMode.Cool), new SetFan(FanSpeed.Low), new SetTarget(22)], volta.Claims[Dev]);

        var restaurado = r.Evaluate(Ctx(At(15, 31), 30, original));
        Assert.Empty(restaurado.Claims);
        Assert.Single(restaurado.Notifications);
        Assert.Equal(SaidaEstagio.Presente, r.State.Estagio);
    }

    [Fact]
    public void Restauracao_bloqueada_continua_pedida_ate_o_aparelho_voltar()
    {
        var r = new SaiDoPcRule(new SaiDoPcConfig(Dev));
        r.Evaluate(Ctx(At(14, 20), 30, Snap(on: true, mode: AcMode.Cool, target: 22), Longe(20)));
        r.Evaluate(Ctx(At(15), 30, Snap(on: true, mode: AcMode.Auto, target: 26), Longe(60)));

        // Primeiro ciclo de volta: o motor pode ter bloqueado o pedido (pausa, outra regra, intervalo de 20 s).
        r.Evaluate(Ctx(At(15, 30), 30, Snap(on: false, mode: AcMode.Auto, target: 26)));
        var segundo = r.Evaluate(Ctx(At(15, 32), 30, Snap(on: false, mode: AcMode.Auto, target: 26)));
        Assert.Contains(new Power(true), segundo.Claims[Dev]);

        var desistiu = r.Evaluate(Ctx(At(15, 46), 30, Snap(on: false, mode: AcMode.Auto, target: 26)));
        Assert.Empty(desistiu.Claims);
        Assert.Equal(SaidaEstagio.Presente, r.State.Estagio);
    }

    [Fact]
    public void Restaurar_ventilar_nao_pede_temperatura_e_confirma()
    {
        var r = new SaiDoPcRule(new SaiDoPcConfig(Dev));
        var ventilar = Snap(on: true, mode: AcMode.FanOnly, fan: FanSpeed.High, target: 22);
        r.Evaluate(Ctx(At(14, 20), 30, ventilar, Longe(20)));

        var volta = r.Evaluate(Ctx(At(15), 30, ventilar with { TargetC = 26 }));

        Assert.Empty(volta.Claims);
        Assert.Single(volta.Notifications);
    }

    [Fact]
    public void Aparelho_desligado_ao_sair_continua_desligado_na_volta()
    {
        var r = new SaiDoPcRule(new SaiDoPcConfig(Dev));
        r.Evaluate(Ctx(At(14, 20), 30, Snap(on: false), Longe(30)));

        var volta = r.Evaluate(Ctx(At(15), 30, Snap(on: false)));

        Assert.Empty(volta.Claims);
    }
}

public class AntesDoPicoTests
{
    private static readonly (DateTimeOffset, double)[] Previsao = [(At(13), 31), (At(14), 32.4), (At(15), 34)];

    [Fact]
    public void Liga_30_min_antes_da_primeira_hora_acima_do_limite()
    {
        var r = new AntesDoPicoRule(new AntesDoPicoConfig(Dev));
        Assert.Empty(r.Evaluate(Ctx(At(13, 20), null, Snap(on: false), weather: Clima(30, Previsao))).Claims);

        var o = r.Evaluate(Ctx(At(13, 35), null, Snap(on: false), weather: Clima(30, Previsao)));

        Assert.Equal([new Power(true), new SetMode(AcMode.Auto), new SetTarget(24)], o.Claims[Dev]);
        Assert.Single(o.Notifications);
    }

    [Fact]
    public void Aparelho_ja_ligado_nao_mexe_e_nao_tenta_de_novo_no_dia()
    {
        var r = new AntesDoPicoRule(new AntesDoPicoConfig(Dev));
        Assert.Empty(r.Evaluate(Ctx(At(13, 35), null, Snap(on: true), weather: Clima(30, Previsao))).Claims);

        Assert.Empty(r.Evaluate(Ctx(At(13, 45), null, Snap(on: false), weather: Clima(30, Previsao))).Claims);
    }

    [Fact]
    public void Solta_o_controle_quando_chega_a_hora_do_pico()
    {
        var r = new AntesDoPicoRule(new AntesDoPicoConfig(Dev));
        r.Evaluate(Ctx(At(13, 35), null, Snap(on: false), weather: Clima(30, Previsao)));

        Assert.NotEmpty(r.Evaluate(Ctx(At(13, 55), null, Snap(on: true), weather: Clima(30, Previsao))).Claims);
        Assert.Empty(r.Evaluate(Ctx(At(14, 1), null, Snap(on: true), weather: Clima(30, Previsao))).Claims);
    }
}

public class JanelaAbertaTests
{
    private static FakeHistory Historico(params (int Min, int Amb)[] pontos) =>
        new([.. pontos.Select(p => new HistorySample(At(14).AddMinutes(p.Min), true, true, AcMode.Cool, p.Amb, 22))]);

    [Fact]
    public void Dentro_subindo_2_graus_em_15_min_no_frio_desliga_e_avisa()
    {
        var h = Historico((0, 23), (5, 23), (10, 23), (15, 23), (20, 24), (25, 25));
        var r = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));

        var o = r.Evaluate(Ctx(At(14, 25), 30, Snap(ambient: 25), history: h));

        Assert.Equal([new Power(false)], o.Claims[Dev]);
        Assert.Single(o.Notifications);
    }

    [Fact]
    public void Subida_de_1_grau_nao_dispara()
    {
        var h = Historico((0, 23), (10, 23), (20, 24), (25, 24));
        var r = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));

        Assert.Empty(r.Evaluate(Ctx(At(14, 25), 30, Snap(ambient: 24), history: h)).Claims);
    }

    [Fact]
    public void Recem_ligado_nao_dispara()
    {
        var h = Historico((15, 23), (20, 24), (25, 25));
        var r = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));

        Assert.Empty(r.Evaluate(Ctx(At(14, 25), 30, Snap(ambient: 25), history: h)).Claims);
    }

    [Fact]
    public void Mantem_desligado_durante_a_carencia_e_depois_solta()
    {
        var h = Historico((0, 23), (10, 23), (20, 24), (25, 25));
        var r = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));
        r.Evaluate(Ctx(At(14, 25), 30, Snap(ambient: 25), history: h));

        Assert.NotEmpty(r.Evaluate(Ctx(At(15, 0), 30, Snap(on: false), history: h)).Claims);
        Assert.Empty(r.Evaluate(Ctx(At(15, 30), 30, Snap(on: false), history: h)).Claims);
    }
}

public class ModoNoiteTests
{
    [Fact]
    public void Programa_timer_uma_vez_e_sobe_1_grau_por_hora_ate_o_fim()
    {
        var r = new ModoNoiteRule(new ModoNoiteConfig(Dev));
        r.Activate(At(22), 23);

        var inicio = r.Evaluate(Ctx(At(22), 25, Snap(target: 23)));
        Assert.Equal([new SetTarget(23), new SetStopTimer(4 * 3600)], inicio.Claims[Dev]);

        var umaHora = r.Evaluate(Ctx(At(23, 1), 25, Snap(target: 23) with { StopTimeSeconds = 3 * 3600 }));
        Assert.Equal([new SetTarget(24)], umaHora.Claims[Dev]);

        var fim = r.Evaluate(Ctx(At(2, 0, day: 15), 25, Snap(target: 26)));
        Assert.Empty(fim.Claims);
        Assert.False(r.Active);
    }

    [Fact]
    public void Timer_que_nao_chegou_ao_aparelho_e_pedido_de_novo_e_o_fim_desliga()
    {
        var r = new ModoNoiteRule(new ModoNoiteConfig(Dev));
        r.Activate(At(22), 23);
        r.Evaluate(Ctx(At(22), 25, Snap(target: 23)));

        // Pedido bloqueado (ajuste manual, intervalo): o aparelho segue sem timer.
        var depois = r.Evaluate(Ctx(At(23, 30), 25, Snap(target: 24)));
        Assert.Contains(new SetStopTimer(3 * 3600), depois.Claims[Dev]);

        var fim = r.Evaluate(Ctx(At(2, 5, day: 15), 25, Snap(on: true, target: 26)));
        Assert.Equal([new Power(false)], fim.Claims[Dev]);

        var desligado = r.Evaluate(Ctx(At(2, 6, day: 15), 25, Snap(on: false, target: 26)));
        Assert.Empty(desligado.Claims);
        Assert.False(r.Active);
    }

    [Fact]
    public void Timer_longo_do_usuario_e_trocado_pelo_do_modo_noite()
    {
        var r = new ModoNoiteRule(new ModoNoiteConfig(Dev));
        r.Activate(At(22), 23);

        var o = r.Evaluate(Ctx(At(22), 25, Snap(target: 23) with { StopTimeSeconds = 8 * 3600 }));

        Assert.Contains(new SetStopTimer(4 * 3600), o.Claims[Dev]);
    }

    [Theory]
    [InlineData(4 * 3600, true)]   // o que o modo noite pediu
    [InlineData(1 * 3600, false)]  // o usuário pôs 1 h no controle
    public void So_o_timer_do_modo_noite_e_zerado_ao_cancelar(int segundos, bool doModoNoite)
    {
        var r = new ModoNoiteRule(new ModoNoiteConfig(Dev));
        r.Activate(At(22), 23);

        r.Evaluate(Ctx(At(22, 1), 25, Snap(target: 23) with { StopTimeSeconds = segundos }));

        Assert.True(r.State.TimerEnviado);
        Assert.Equal(doModoNoite, r.State.TimerDoModoNoite);
    }

    [Fact]
    public void Respeita_o_teto_de_temperatura()
    {
        var r = new ModoNoiteRule(new ModoNoiteConfig(Dev, MaxTargetC: 25));
        r.Activate(At(22), 24);

        var o = r.Evaluate(Ctx(At(1, 30, day: 15), 25, Snap(target: 25)));

        Assert.Contains(new SetTarget(25), o.Claims[Dev]);
    }

    [Fact]
    public void Horario_diario_ativa_se_o_aparelho_estiver_ligado()
    {
        var r = new ModoNoiteRule(new ModoNoiteConfig(Dev, DailyStart: new TimeOnly(22, 0)));

        r.Evaluate(Ctx(At(22, 5), 25, Snap(on: true, target: 23)));

        Assert.True(r.Active);
    }
}

public class ForaMaisFrescoTests
{
    [Fact]
    public void Avisa_quando_fora_esta_3_graus_abaixo_e_respeita_carencia()
    {
        var r = new ForaMaisFrescoRule(new ForaMaisFrescoConfig());

        Assert.Single(r.Evaluate(Ctx(At(19), 21, Snap(ambient: 24))).Notifications);
        Assert.Empty(r.Evaluate(Ctx(At(20), 20, Snap(ambient: 24))).Notifications);
        Assert.Single(r.Evaluate(Ctx(At(22, 0), 20, Snap(ambient: 24))).Notifications);
    }

    [Fact]
    public void Nao_avisa_de_madrugada_nem_com_diferenca_pequena()
    {
        var r = new ForaMaisFrescoRule(new ForaMaisFrescoConfig());

        Assert.Empty(r.Evaluate(Ctx(At(23), 18, Snap(ambient: 24))).Notifications);
        Assert.Empty(r.Evaluate(Ctx(At(19), 22, Snap(ambient: 24))).Notifications);
    }
}
