using MeuClima.Core.Devices;
using MeuClima.Core.Rules;
using static MeuClima.Core.Devices.DeviceCommand;
using MeuClima.Core.Weather;
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

public class ConfortoTests
{
    private static ConfortoRule Rule(ConfortoConfig? cfg = null) => new(cfg ?? new ConfortoConfig(Dev));

    private static readonly DeviceCommand[] Manter =
        [new Power(true), new SetMode(AcMode.Cool), new SetFan(FanSpeed.Auto), new SetEnergySaving(true), new SetTarget(24)];

    [Fact]
    public void Desligado_e_acima_da_maxima_por_5_min_liga_em_frio_economico_no_topo_da_faixa()
    {
        var r = Rule();
        Assert.Empty(r.Evaluate(Ctx(At(14), 30, Snap(on: false, ambient: 25))).Claims);

        var o = r.Evaluate(Ctx(At(14, 5), 30, Snap(on: false, ambient: 25)));

        Assert.Equal(Manter, o.Claims[Dev]);
        Assert.Equal("Conforto ligou o ar", Assert.Single(o.Notifications).Title);
    }

    [Fact]
    public void Leitura_isolada_acima_da_maxima_nao_liga()
    {
        var r = Rule();
        r.Evaluate(Ctx(At(14), 30, Snap(on: false, ambient: 25)));
        r.Evaluate(Ctx(At(14, 1), 30, Snap(on: false, ambient: 24)));

        Assert.Empty(r.Evaluate(Ctx(At(14, 6), 30, Snap(on: false, ambient: 25))).Claims);
    }

    [Fact]
    public void Dentro_da_faixa_e_desligado_nao_mexe()
    {
        var r = Rule();
        r.Evaluate(Ctx(At(14), 30, Snap(on: false, ambient: 24)));

        Assert.Empty(r.Evaluate(Ctx(At(15), 30, Snap(on: false, ambient: 24))).Claims);
    }

    [Fact]
    public void Aparelho_ja_ligado_e_assumido_na_hora_sem_modo_auto()
    {
        var o = Rule().Evaluate(Ctx(At(14), 30, Snap(on: true, mode: AcMode.Auto, ambient: 24)));

        Assert.Equal(Manter, o.Claims[Dev]);
        Assert.Equal("Conforto", o.Status!.Title);
    }

    [Fact]
    public void Turbo_so_com_calor_forte_sustentado_e_volta_a_manter_na_maxima()
    {
        var r = Rule();
        r.Evaluate(Ctx(At(14), 30, Snap(on: false, ambient: 26)));

        var turbo = r.Evaluate(Ctx(At(14, 5), 30, Snap(on: false, ambient: 26)));
        Assert.Equal(ConfortoFase.Turbo, r.State.Fase);
        Assert.Contains(new SetFan(FanSpeed.Turbo), turbo.Claims[Dev]);
        Assert.Contains(new SetTarget(23), turbo.Claims[Dev]);
        Assert.Equal("Conforto: turbo", Assert.Single(turbo.Notifications).Title);

        var chegou = r.Evaluate(Ctx(At(14, 20), 30, Snap(fan: FanSpeed.Turbo, ambient: 24)));
        Assert.Equal(Manter, chegou.Claims[Dev]);
    }

    [Fact]
    public void Turbo_tem_teto_e_so_volta_depois_de_dentro_entrar_na_faixa()
    {
        var r = Rule();
        r.Evaluate(Ctx(At(14), 30, Snap(on: false, ambient: 27)));
        r.Evaluate(Ctx(At(14, 5), 30, Snap(on: false, ambient: 27)));

        r.Evaluate(Ctx(At(14, 36), 30, Snap(fan: FanSpeed.Turbo, ambient: 26)));
        Assert.Equal(ConfortoFase.Manter, r.State.Fase);
        r.Evaluate(Ctx(At(14, 50), 30, Snap(ambient: 26)));
        Assert.Equal(ConfortoFase.Manter, r.State.Fase);

        r.Evaluate(Ctx(At(15), 30, Snap(ambient: 24)));
        r.Evaluate(Ctx(At(15, 10), 30, Snap(ambient: 26)));
        r.Evaluate(Ctx(At(15, 15), 30, Snap(ambient: 26)));
        Assert.Equal(ConfortoFase.Turbo, r.State.Fase);
    }

    private static ConfortoRule Ventilando()
    {
        var r = Rule();
        r.Evaluate(Ctx(At(14), 30, Snap(ambient: 24)));
        r.Evaluate(Ctx(At(14, 1), 30, Snap(ambient: 23)));
        r.Evaluate(Ctx(At(14, 11), 30, Snap(ambient: 23)));
        Assert.Equal(ConfortoFase.Ventilar, r.State.Fase);
        return r;
    }

    [Fact]
    public void Dois_graus_acima_da_maxima_volta_ao_frio_em_2_min_e_diz_ate_quando_espera()
    {
        var r = Ventilando();
        r.Evaluate(Ctx(At(15, 59), 30, Snap(mode: AcMode.FanOnly, ambient: 26)));

        var esperando = r.Evaluate(Ctx(At(16), 30, Snap(mode: AcMode.FanOnly, ambient: 26)));
        Assert.Equal(ConfortoFase.Ventilar, r.State.Fase);
        Assert.Equal("Dentro está 26 °C, acima de 24 °C desde 15:59. Se continuar até 16:01, volta a esfriar.", esperando.Status!.Detail);

        var frio = r.Evaluate(Ctx(At(16, 1), 30, Snap(mode: AcMode.FanOnly, ambient: 26)));
        Assert.Equal(ConfortoFase.Turbo, r.State.Fase);
        Assert.Contains(new SetMode(AcMode.Cool), frio.Claims[Dev]);
    }

    [Fact]
    public void Um_grau_acima_da_maxima_ainda_espera_5_min()
    {
        var r = Ventilando();
        r.Evaluate(Ctx(At(15, 59), 30, Snap(mode: AcMode.FanOnly, ambient: 25)));

        r.Evaluate(Ctx(At(16, 1), 30, Snap(mode: AcMode.FanOnly, ambient: 25)));
        Assert.Equal(ConfortoFase.Ventilar, r.State.Fase);
        r.Evaluate(Ctx(At(16, 4), 30, Snap(mode: AcMode.FanOnly, ambient: 25)));
        Assert.Equal(ConfortoFase.Manter, r.State.Fase);
    }

    [Fact]
    public void Na_minima_por_10_min_so_ventila_e_volta_ao_frio_acima_da_maxima()
    {
        var r = Rule();
        r.Evaluate(Ctx(At(14), 30, Snap(ambient: 24)));
        r.Evaluate(Ctx(At(14, 1), 30, Snap(ambient: 23)));
        Assert.Equal(ConfortoFase.Manter, r.State.Fase);

        var ventilar = r.Evaluate(Ctx(At(14, 11), 30, Snap(ambient: 23)));
        Assert.Equal([new Power(true), new SetMode(AcMode.FanOnly), new SetFan(FanSpeed.Low)], ventilar.Claims[Dev]);

        r.Evaluate(Ctx(At(14, 30), 30, Snap(mode: AcMode.FanOnly, ambient: 24)));
        Assert.Equal(ConfortoFase.Ventilar, r.State.Fase);
        r.Evaluate(Ctx(At(14, 40), 30, Snap(mode: AcMode.FanOnly, ambient: 25)));
        var frio = r.Evaluate(Ctx(At(14, 45), 30, Snap(mode: AcMode.FanOnly, ambient: 25)));
        Assert.Equal(Manter, frio.Claims[Dev]);
    }

    [Fact]
    public void Sem_ventilar_desliga_quando_dentro_ja_esta_na_minima()
    {
        var o = Rule(new ConfortoConfig(Dev, FanAtMin: false)).Evaluate(Ctx(At(14), 30, Snap(on: true, ambient: 23)));

        Assert.Equal([new Power(false)], o.Claims[Dev]);
    }

    [Fact]
    public void Nunca_aquece_por_padrao()
    {
        var r = Rule();
        r.Evaluate(Ctx(At(7), 15, Snap(on: false, ambient: 21)));

        Assert.Empty(r.Evaluate(Ctx(At(7, 10), 15, Snap(on: false, ambient: 21))).Claims);
    }

    [Fact]
    public void Com_aquecer_ligado_aquece_ate_a_minima()
    {
        var r = Rule(new ConfortoConfig(Dev, AllowHeat: true));
        r.Evaluate(Ctx(At(7), 15, Snap(on: false, ambient: 21)));

        var o = r.Evaluate(Ctx(At(7, 5), 15, Snap(on: false, ambient: 21)));

        Assert.Contains(new SetMode(AcMode.Heat), o.Claims[Dev]);
        Assert.Contains(new SetTarget(23), o.Claims[Dev]);
    }

    [Fact]
    public void Antecipa_pela_sensacao_termica_prevista_uma_vez_por_dia()
    {
        // 29 °C de temperatura, 31 °C de sensação: o limite padrão (30) olha a sensação.
        var clima = new WeatherSnapshot(27, 28, 60, [new HourlyPoint(At(15), 29, 31)], At(12));
        var r = Rule();
        Assert.Empty(r.Evaluate(Ctx(At(14, 20), null, Snap(on: false, ambient: 24), weather: clima)).Claims);

        var o = r.Evaluate(Ctx(At(14, 35), null, Snap(on: false, ambient: 24), weather: clima));

        Assert.Equal(Manter, o.Claims[Dev]);
        Assert.Contains("calor forte", Assert.Single(o.Notifications).Body);
        Assert.Equal(DateOnly.FromDateTime(At(14).DateTime), r.State.AntecipouEm);
    }

    [Fact]
    public void Com_ninguem_no_pc_nao_liga_e_recomeca_na_volta()
    {
        var r = Rule();
        var longe = new PresenceState(true, TimeSpan.FromMinutes(10), Locked: true);

        Assert.Empty(r.Evaluate(Ctx(At(14), 30, Snap(on: false, ambient: 27), longe)).Claims);
        Assert.Empty(r.Evaluate(Ctx(At(14, 10), 30, Snap(on: false, ambient: 27), longe)).Claims);
        Assert.Equal(ConfortoFase.Parado, r.State.Fase);
    }

    [Fact]
    public void Avisa_quando_a_sensacao_la_fora_esta_mais_fresca_com_carencia()
    {
        var r = Rule();

        Assert.Single(r.Evaluate(Ctx(At(18), 20, Snap(ambient: 25))).Notifications);
        Assert.Empty(r.Evaluate(Ctx(At(19), 20, Snap(ambient: 25))).Notifications);
        Assert.Single(r.Evaluate(Ctx(At(21), 20, Snap(ambient: 25))).Notifications);
    }

    [Fact]
    public void Ajuste_manual_recomeca_a_avaliacao()
    {
        var r = Rule();
        r.Evaluate(Ctx(At(14), 30, Snap(on: false, ambient: 27)));
        r.Evaluate(Ctx(At(14, 5), 30, Snap(on: false, ambient: 27)));
        Assert.Equal(ConfortoFase.Turbo, r.State.Fase);

        r.OnManualChange(Dev);

        Assert.Equal(ConfortoFase.Parado, r.State.Fase);
    }

    [Fact]
    public void Estado_sobrevive_a_reinicio()
    {
        var r = Rule();
        r.Evaluate(Ctx(At(14), 30, Snap(ambient: 24)));
        var novo = Rule();
        novo.LoadState(r.SaveState());

        Assert.Equal(ConfortoFase.Manter, novo.State.Fase);
    }

    [Fact]
    public void Leitura_parada_nao_confirma_a_subida()
    {
        // Leitura a cada 5 min ou leitura que falhou: o mesmo 25 °C não pode valer como 5 min acima.
        var r = Rule();
        r.Evaluate(Ctx(At(14), 30, Snap(on: false, ambient: 25), readAt: At(14)));

        Assert.Empty(r.Evaluate(Ctx(At(14, 6), 30, Snap(on: false, ambient: 25), readAt: At(14))).Claims);
        Assert.Equal(Manter, r.Evaluate(Ctx(At(14, 7), 30, Snap(on: false, ambient: 25))).Claims[Dev]);
    }

    [Fact]
    public void Ausencia_no_turbo_tira_a_ventilacao_do_turbo()
    {
        var r = Rule();
        r.Evaluate(Ctx(At(14), 30, Snap(on: false, ambient: 26)));
        r.Evaluate(Ctx(At(14, 5), 30, Snap(on: false, ambient: 26)));
        Assert.Equal(ConfortoFase.Turbo, r.State.Fase);

        var longe = new PresenceState(true, TimeSpan.FromMinutes(5));
        var saindo = r.Evaluate(Ctx(At(14, 10), 30, Snap(fan: FanSpeed.Turbo, ambient: 25), longe));
        Assert.Equal([new SetFan(FanSpeed.Auto)], saindo.Claims[Dev]);

        Assert.Empty(r.Evaluate(Ctx(At(14, 11), 30, Snap(fan: FanSpeed.Auto, ambient: 25), longe)).Claims);
    }

    [Fact]
    public void Nao_troca_o_aquecimento_que_voce_ligou()
    {
        var r = Rule();

        Assert.Empty(r.Evaluate(Ctx(At(7), 15, Snap(mode: AcMode.Heat, ambient: 21))).Claims);
        Assert.Empty(r.Evaluate(Ctx(At(7, 20), 15, Snap(mode: AcMode.Heat, ambient: 25))).Claims);
    }

    [Fact]
    public void Bloqueado_nao_avanca_nem_avisa()
    {
        var r = Rule();
        RuleContext Pausado(DateTimeOffset t) => Ctx(t, 30, Snap(on: false, ambient: 25)) with { Blocked = _ => true };

        Assert.Empty(r.Evaluate(Pausado(At(14))).Notifications);
        Assert.Empty(r.Evaluate(Pausado(At(14, 10))).Notifications);
        Assert.Equal(ConfortoFase.Parado, r.State.Fase);

        // Fim do bloqueio: recomeça a confirmação em vez de ligar na hora.
        Assert.Empty(r.Evaluate(Ctx(At(14, 11), 30, Snap(on: false, ambient: 25))).Claims);
    }

    [Fact]
    public void Bloqueado_nao_gasta_a_antecipacao_do_dia()
    {
        var clima = new WeatherSnapshot(27, 28, 60, [new HourlyPoint(At(15), 29, 31)], At(12));
        var r = Rule();

        r.Evaluate(Ctx(At(14, 35), null, Snap(on: false, ambient: 24), weather: clima) with { Blocked = _ => true });
        Assert.Null(r.State.AntecipouEm);

        var o = r.Evaluate(Ctx(At(14, 40), null, Snap(on: false, ambient: 24), weather: clima));
        Assert.Equal(Manter, o.Claims[Dev]);
    }

    [Fact]
    public void Parado_fica_de_olho_sem_comandar()
    {
        var o = Rule().Evaluate(Ctx(At(14), 30, Snap(on: false, ambient: 24)));

        Assert.Empty(o.Claims);
        var (st, espera) = o.Observed[Dev];
        Assert.False(espera);
        Assert.Equal("Conforto de olho", st.Title);
        Assert.Equal("Dentro está 24 °C, na faixa. Liga se passar de 24 °C por 5 min.", st.Detail);
    }

    [Fact]
    public void Ausente_ou_aquecendo_fica_em_espera()
    {
        var longe = Rule().Evaluate(Ctx(At(14), 30, Snap(on: false), new PresenceState(true, TimeSpan.FromMinutes(6))));
        Assert.True(longe.Observed[Dev].Waiting);
        Assert.Equal("Você está longe do PC. Volta a agir quando você voltar.", longe.Observed[Dev].Status.Detail);

        var quente = Rule().Evaluate(Ctx(At(7), 15, Snap(mode: AcMode.Heat, ambient: 21)));
        Assert.True(quente.Observed[Dev].Waiting);
    }
}

public class AusenteTests
{
    private static PresenceState Longe(int min) => new(true, TimeSpan.FromMinutes(min));
    private static PresenceState Bloqueado(int min) => new(true, TimeSpan.FromMinutes(min), Locked: true);
    private static SaiDoPcRule Restaurando() => new(new SaiDoPcConfig(Dev, RestoreOnReturn: true));

    [Fact]
    public void Ausencia_curta_sem_bloqueio_nao_mexe()
    {
        var r = new SaiDoPcRule(new SaiDoPcConfig(Dev));
        Assert.Empty(r.Evaluate(Ctx(At(14), 30, Snap(), Longe(19))).Claims);
    }

    [Fact]
    public void Tela_bloqueada_desliga_na_hora()
    {
        var r = new SaiDoPcRule(new SaiDoPcConfig(Dev));

        var o = r.Evaluate(Ctx(At(14), 30, Snap(), Bloqueado(0)));

        Assert.Equal([new Power(false)], o.Claims[Dev]);
        Assert.Equal("Tela bloqueada: ar desligado. Quando você voltar, as regras ativas decidem se ele liga.", o.Status!.Detail);
    }

    [Fact]
    public void Tela_bloqueada_com_carencia_espera_antes_de_desligar()
    {
        var r = new SaiDoPcRule(new SaiDoPcConfig(Dev, OffOnLockMin: 3));

        Assert.Empty(r.Evaluate(Ctx(At(14), 30, Snap(), Bloqueado(2))).Claims);
        Assert.Equal([new Power(false)], r.Evaluate(Ctx(At(14, 1), 30, Snap(), Bloqueado(3))).Claims[Dev]);
    }

    [Fact]
    public void Bloqueio_desativado_segue_o_tempo_de_inatividade()
    {
        var r = new SaiDoPcRule(new SaiDoPcConfig(Dev, OffOnLock: false));

        Assert.Empty(r.Evaluate(Ctx(At(14), 30, Snap(), Bloqueado(5))).Claims);
    }

    [Fact]
    public void Na_volta_sem_restaurar_solta_o_aparelho_para_as_regras()
    {
        var r = new SaiDoPcRule(new SaiDoPcConfig(Dev));
        r.Evaluate(Ctx(At(14), 30, Snap(), Bloqueado(0)));

        var volta = r.Evaluate(Ctx(At(15), 30, Snap(on: false)));

        Assert.Empty(volta.Claims);
        Assert.Equal(SaidaEstagio.Presente, r.State.Estagio);
    }

    [Fact]
    public void Economia_em_frio_com_20_min_desliga_com_60_e_restaura_na_volta()
    {
        var r = Restaurando();
        var original = Snap(on: true, mode: AcMode.Cool, fan: FanSpeed.Low, target: 22);

        var eco = r.Evaluate(Ctx(At(14, 20), 30, original, Longe(20)));
        Assert.Equal([new Power(true), new SetMode(AcMode.Cool), new SetTarget(26)], eco.Claims[Dev]);

        var off = r.Evaluate(Ctx(At(15), 30, Snap(on: true, mode: AcMode.Cool, target: 26), Longe(60)));
        Assert.Equal([new Power(false)], off.Claims[Dev]);

        var volta = r.Evaluate(Ctx(At(15, 30), 30, Snap(on: false, mode: AcMode.Cool, target: 26)));
        Assert.Equal([new Power(true), new SetMode(AcMode.Cool), new SetFan(FanSpeed.Low), new SetTarget(22)], volta.Claims[Dev]);

        var restaurado = r.Evaluate(Ctx(At(15, 31), 30, original));
        Assert.Empty(restaurado.Claims);
        Assert.Single(restaurado.Notifications);
        Assert.Equal(SaidaEstagio.Presente, r.State.Estagio);
    }

    [Fact]
    public void Aquecendo_nao_alivia_e_desliga_no_tempo()
    {
        var r = new SaiDoPcRule(new SaiDoPcConfig(Dev));
        var quente = Snap(mode: AcMode.Heat, target: 23);

        Assert.Empty(r.Evaluate(Ctx(At(7, 20), 15, quente, Longe(20))).Claims);
        Assert.Equal([new Power(false)], r.Evaluate(Ctx(At(8), 15, quente, Longe(60))).Claims[Dev]);
    }

    [Fact]
    public void Restauracao_bloqueada_continua_pedida_ate_o_aparelho_voltar()
    {
        var r = Restaurando();
        r.Evaluate(Ctx(At(14, 20), 30, Snap(on: true, mode: AcMode.Cool, target: 22), Longe(20)));
        r.Evaluate(Ctx(At(15), 30, Snap(on: true, mode: AcMode.Cool, target: 26), Longe(60)));

        // Primeiro ciclo de volta: o motor pode ter bloqueado o pedido (pausa, outra regra, intervalo de 20 s).
        r.Evaluate(Ctx(At(15, 30), 30, Snap(on: false, mode: AcMode.Cool, target: 26)));
        var segundo = r.Evaluate(Ctx(At(15, 32), 30, Snap(on: false, mode: AcMode.Cool, target: 26)));
        Assert.Contains(new Power(true), segundo.Claims[Dev]);

        var desistiu = r.Evaluate(Ctx(At(15, 46), 30, Snap(on: false, mode: AcMode.Cool, target: 26)));
        Assert.Empty(desistiu.Claims);
        Assert.Equal(SaidaEstagio.Presente, r.State.Estagio);
    }

    [Fact]
    public void Restaurar_ventilar_nao_pede_temperatura_e_confirma()
    {
        var r = Restaurando();
        var ventilar = Snap(on: true, mode: AcMode.FanOnly, fan: FanSpeed.High, target: 22);
        r.Evaluate(Ctx(At(14, 20), 30, ventilar, Longe(20)));

        var volta = r.Evaluate(Ctx(At(15), 30, ventilar with { TargetC = 26 }));

        Assert.Empty(volta.Claims);
        Assert.Single(volta.Notifications);
    }

    [Fact]
    public void Aparelho_desligado_ao_sair_continua_desligado_na_volta()
    {
        var r = Restaurando();
        r.Evaluate(Ctx(At(14, 20), 30, Snap(on: false), Bloqueado(30)));

        var volta = r.Evaluate(Ctx(At(15), 30, Snap(on: false)));

        Assert.Empty(volta.Claims);
    }

    [Fact]
    public void Alivio_tira_do_turbo()
    {
        var r = new SaiDoPcRule(new SaiDoPcConfig(Dev));

        var o = r.Evaluate(Ctx(At(14), 30, Snap(fan: FanSpeed.Turbo), Longe(20)));

        Assert.Contains(new SetFan(FanSpeed.Auto), o.Claims[Dev]);
    }
}

public class JanelaAbertaTests
{
    private static FakeHistory Historico(params (int Min, int Amb)[] pontos) =>
        new([.. pontos.Select(p => new HistorySample(At(14).AddMinutes(p.Min), true, true, AcMode.Cool, p.Amb, 22))]);

    private static FakeHistory Subindo() => Historico((0, 23), (5, 23), (10, 23), (15, 23), (20, 24), (22, 25), (23, 25), (24, 25), (25, 25));

    [Fact]
    public void Dentro_subindo_2_graus_e_ficando_la_desliga_avisa_e_explica()
    {
        var r = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));

        var o = r.Evaluate(Ctx(At(14, 25), 30, Snap(ambient: 25), history: Subindo()));

        Assert.Equal([new Power(false)], o.Claims[Dev]);
        Assert.Single(o.Notifications);
        Assert.Equal("Dentro subiu de 23 para 25 °C com o ar no frio: janela ou porta aberta? Ar desligado até 15:25 ou até você religar.", o.Status!.Detail);
    }

    [Fact]
    public void Salto_numa_leitura_so_nao_dispara()
    {
        // Caso real: 22 °C estável, uma leitura de 24 °C e volta a 23 °C no minuto seguinte.
        var h = Historico((0, 22), (10, 22), (20, 22), (24, 24));
        var r = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));

        Assert.Empty(r.Evaluate(Ctx(At(14, 24), 30, Snap(ambient: 24), history: h)).Claims);
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
        var h = Historico((15, 23), (20, 24), (21, 25), (25, 25));
        var r = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));

        Assert.Empty(r.Evaluate(Ctx(At(14, 25), 30, Snap(ambient: 25), history: h)).Claims);
    }

    [Fact]
    public void Mantem_desligado_durante_a_carencia_e_depois_solta()
    {
        var r = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));
        r.Evaluate(Ctx(At(14, 25), 30, Snap(ambient: 25), history: Subindo()));

        Assert.NotEmpty(r.Evaluate(Ctx(At(15, 0), 30, Snap(on: false), history: Subindo())).Claims);
        Assert.Empty(r.Evaluate(Ctx(At(15, 30), 30, Snap(on: false), history: Subindo())).Claims);
    }

    [Fact]
    public void Ajuste_manual_cancela_o_disparo()
    {
        var r = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));
        r.Evaluate(Ctx(At(14, 25), 30, Snap(ambient: 25), history: Subindo()));

        r.OnManualChange(Dev);

        Assert.Empty(r.Evaluate(Ctx(At(14, 50), 30, Snap(ambient: 25), history: Subindo())).Claims);
    }

    [Fact]
    public void Dentro_voltando_a_cair_com_o_ar_ligado_cancela_o_disparo()
    {
        var r = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));
        r.Evaluate(Ctx(At(14, 25), 30, Snap(ambient: 25), history: Subindo()));

        // O desligamento ficou bloqueado (pausa manual) e o ar seguiu esfriando: não era janela.
        var o = r.Evaluate(Ctx(At(14, 45), 30, Snap(ambient: 24), history: Subindo()));

        Assert.Empty(o.Claims);
        Assert.Null(r.State.DisparouEm);
    }

    [Fact]
    public void Leitura_alta_isolada_que_fica_parada_nao_dispara()
    {
        // Leitura a cada 5 min: o 24 °C isolado fica como última leitura; o relógio sozinho não confirma.
        var h = Historico((0, 22), (10, 22), (20, 22), (24, 24));
        var r = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));

        Assert.Empty(r.Evaluate(Ctx(At(14, 28), 30, Snap(ambient: 24), history: h)).Claims);
    }

    [Fact]
    public void Duas_leituras_altas_espacadas_disparam()
    {
        var h = Historico((0, 22), (10, 22), (20, 22), (24, 24), (29, 24));
        var r = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));

        Assert.Equal([new Power(false)], r.Evaluate(Ctx(At(14, 29), 30, Snap(ambient: 24), history: h)).Claims[Dev]);
    }
}

public class RuleMigrationTests
{
    [Fact]
    public void Herda_presenca_do_antes_do_pico_e_limiar_do_fora_mais_fresco()
    {
        RuleConfig[] antigas = [new AntesDoPicoConfig(Dev, RequirePresence: false), new ForaMaisFrescoConfig(DeltaC: 4)];

        var conforto = Assert.IsType<ConfortoConfig>(Assert.Single(RuleMigration.Migrate(antigas)!));

        Assert.False(conforto.RequirePresence);
        Assert.Equal(4, conforto.CoolerOutsideC);
    }

    [Fact]
    public void Converte_as_regras_antigas_num_conforto_por_aparelho()
    {
        var agenda = new Schedule([DayOfWeek.Monday], new TimeOnly(8, 0), new TimeOnly(18, 0));
        RuleConfig[] antigas =
        [
            new EsfriarRapidoConfig(Dev, RequirePresence: false, Schedule: agenda),
            new SaiDoPcConfig(Dev),
            new AntesDoPicoConfig(Dev),
            new AntesDoPicoConfig("dev-sala") { Enabled = false },
            new ForaMaisFrescoConfig(),
            new JanelaAbertaConfig(Dev),
        ];

        var novas = RuleMigration.Migrate(antigas)!;

        Assert.Equal(4, novas.Count);
        var escritorio = Assert.IsType<ConfortoConfig>(novas[0]);
        Assert.Equal(Dev, escritorio.DeviceId);
        Assert.False(escritorio.RequirePresence);
        Assert.Equal(agenda, escritorio.Schedule);
        Assert.IsType<SaiDoPcConfig>(novas[1]);
        var sala = Assert.IsType<ConfortoConfig>(novas[2]);
        Assert.False(sala.Enabled);
        Assert.IsType<JanelaAbertaConfig>(novas[3]);
        Assert.DoesNotContain(novas, RuleMigration.IsLegacy);
    }

    [Fact]
    public void Settings_da_v0_1_desserializa_e_converte()
    {
        const string json = """
            [
              {"tipo":"esfriar-rapido","deviceId":"dev-a","triggerOutsideC":24,"stopOutsideC":23,"stopAfterMin":30,"phase1TargetC":22,
               "phase2TargetC":24,"phase2Mode":"Auto","phase2EnergySaving":true,"maxBoostMin":60,"requirePresence":true,"schedule":null,"id":"a1","enabled":true},
              {"tipo":"sai-do-pc","deviceId":"dev-a","ecoAfterMin":20,"ecoTargetC":26,"offAfterMin":60,"id":"a2","enabled":true},
              {"tipo":"antes-do-pico","deviceId":"dev-a","forecastThresholdC":32,"leadMin":30,"targetC":24,"mode":"Auto","requirePresence":true,"id":"a3","enabled":true},
              {"tipo":"fora-mais-fresco","deltaC":3,"cooldownMin":180,"from":"07:00:00","to":"22:00:00","fromOrDefault":"07:00:00","toOrDefault":"22:00:00","id":"a4","enabled":true}
            ]
            """;
        var o = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };
        o.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter<AcMode>());
        var antigas = System.Text.Json.JsonSerializer.Deserialize<List<RuleConfig>>(json, o)!;

        var novas = RuleMigration.Migrate(antigas)!;

        Assert.Collection(novas,
            c => Assert.Equal("dev-a", Assert.IsType<ConfortoConfig>(c).DeviceId),
            c => Assert.True(Assert.IsType<SaiDoPcConfig>(c) is { OffOnLock: true, OffOnLockMin: 0, RestoreOnReturn: false }));
    }

    [Fact]
    public void Herda_a_faixa_do_alvo_de_manutencao_do_esfriar_rapido()
    {
        var conforto = Assert.IsType<ConfortoConfig>(Assert.Single(RuleMigration.Migrate([new EsfriarRapidoConfig(Dev, Phase2TargetC: 26)])!));

        Assert.Equal((25, 26, 28), (conforto.MinC, conforto.MaxC, conforto.TurboFromC));
    }

    [Fact]
    public void Esfriar_rapido_desligado_deixa_o_conforto_desligado()
    {
        RuleConfig[] antigas = [new EsfriarRapidoConfig(Dev) { Enabled = false }, new AntesDoPicoConfig(Dev)];

        Assert.False(Assert.IsType<ConfortoConfig>(Assert.Single(RuleMigration.Migrate(antigas)!)).Enabled);
    }

    [Fact]
    public void Fora_mais_fresco_desligado_cala_o_aviso()
    {
        RuleConfig[] antigas = [new EsfriarRapidoConfig(Dev), new ForaMaisFrescoConfig(DeltaC: 4) { Enabled = false }];

        Assert.Equal(15, Assert.IsType<ConfortoConfig>(Assert.Single(RuleMigration.Migrate(antigas)!)).CoolerOutsideC);
    }

    [Fact]
    public void Sem_regra_antiga_nao_converte()
    {
        Assert.Null(RuleMigration.Migrate([new ConfortoConfig(Dev), new SaiDoPcConfig(Dev)]));
    }

    [Fact]
    public void Conforto_existente_nao_e_duplicado()
    {
        var novas = RuleMigration.Migrate([new ConfortoConfig(Dev), new EsfriarRapidoConfig(Dev)])!;

        Assert.Single(novas);
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
