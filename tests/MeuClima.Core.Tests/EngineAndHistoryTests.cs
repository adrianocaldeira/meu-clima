using MeuClima.Core.Devices;
using MeuClima.Core.History;
using MeuClima.Core.Rules;
using MeuClima.Core.Weather;
using static MeuClima.Core.Devices.DeviceCommand;
using static MeuClima.Core.Tests.TestKit;

namespace MeuClima.Core.Tests;

public class RuleEngineTests
{
    private static FakeHistory JanelaAbertaHistorico() =>
        new([.. new[] { (0, 23), (10, 23), (20, 24), (22, 25), (25, 25) }
            .Select(p => new HistorySample(At(14).AddMinutes(p.Item1), true, true, AcMode.Cool, p.Item2, 22))]);

    [Fact]
    public void Prioridade_maior_fica_com_o_aparelho()
    {
        var engine = new RuleEngine([
            new ConfortoRule(new ConfortoConfig(Dev)),
            new JanelaAbertaRule(new JanelaAbertaConfig(Dev)),
        ]);

        var r = engine.Evaluate(Ctx(At(14, 25), 31, Snap(ambient: 25), history: JanelaAbertaHistorico()));

        var plan = Assert.Single(r.Commands);
        Assert.Equal("Janela aberta", plan.RuleName);
        Assert.Equal([new Power(false)], plan.Commands);
        Assert.Contains(r.Active, a => a.Status.Title == "Conforto" && a.BlockedBy == "Janela aberta");
    }

    [Fact]
    public void Ajuste_manual_pausa_por_60_min_e_depois_volta()
    {
        var engine = new RuleEngine([new ConfortoRule(new ConfortoConfig(Dev))]);
        engine.Pause(Dev, At(14) + RuleEngine.ManualPause);

        var pausado = engine.Evaluate(Ctx(At(14, 30), 31, Snap(ambient: 25)));
        Assert.Empty(pausado.Commands);
        Assert.Contains(pausado.Active, a => a.BlockedBy!.StartsWith("ajuste manual · volta às 15:00"));

        var depois = engine.Evaluate(Ctx(At(15, 1), 31, Snap(ambient: 25)));
        Assert.NotEmpty(depois.Commands);
    }

    [Fact]
    public void Mudanca_sem_comando_recente_conta_como_manual()
    {
        var engine = new RuleEngine([]);

        Assert.True(engine.ObserveReading(Snap(target: 23), Snap(target: 20), At(14)));
        Assert.NotNull(engine.PausedUntil(Dev, At(14, 59)));
        Assert.Null(engine.PausedUntil(Dev, At(15, 1)));
    }

    [Fact]
    public void Ajuste_manual_cancela_o_disparo_da_janela_aberta()
    {
        var janela = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));
        var engine = new RuleEngine([janela]);
        engine.Evaluate(Ctx(At(14, 25), 31, Snap(ambient: 25), history: JanelaAbertaHistorico()));
        Assert.NotNull(janela.State.DisparouEm);

        Assert.True(engine.ObserveReading(Snap(on: false), Snap(on: true), At(14, 30)));

        Assert.Null(janela.State.DisparouEm);
    }

    [Fact]
    public void Mudanca_logo_apos_comando_do_app_nao_conta_como_manual()
    {
        var engine = new RuleEngine([]);
        engine.RecordSent(Dev, [new SetTarget(20)], At(14));

        Assert.False(engine.ObserveReading(Snap(target: 23), Snap(target: 20), At(14, 1)));
        Assert.Null(engine.PausedUntil(Dev, At(14, 2)));
    }

    [Fact]
    public void Ajuste_diferente_do_pedido_dentro_da_janela_conta_como_manual()
    {
        var engine = new RuleEngine([]);
        engine.RecordSent(Dev, [new SetTarget(22)], At(14));

        Assert.True(engine.ObserveReading(Snap(target: 24), Snap(target: 26), At(14, 1)));
        Assert.NotNull(engine.PausedUntil(Dev, At(14, 30)));
    }

    [Fact]
    public void Desligar_no_controle_logo_apos_comando_conta_como_manual()
    {
        var engine = new RuleEngine([]);
        engine.RecordSent(Dev, [new SetTarget(22)], At(14));

        Assert.True(engine.ObserveReading(Snap(on: true, target: 24), Snap(on: false, target: 24), At(14, 1)));
    }

    [Fact]
    public void Efeito_colateral_depois_da_confirmacao_nao_conta_como_manual()
    {
        var engine = new RuleEngine([]);
        engine.RecordSent(Dev, [new SetMode(AcMode.Auto), new SetTarget(26)], At(14));
        var confirmado = Snap(mode: AcMode.Auto, fan: FanSpeed.Low, target: 26);
        Assert.False(engine.ObserveReading(Snap(target: 23), confirmado, At(14, 1)));
        engine.Expected(confirmado, At(14, 1).AddSeconds(30));

        Assert.False(engine.ObserveReading(confirmado, confirmado with { Fan = FanSpeed.Auto }, At(14, 2)));
    }

    [Fact]
    public void Trocar_o_modo_depois_de_um_ajuste_de_temperatura_conta_como_manual()
    {
        var engine = new RuleEngine([]);
        engine.RecordSent(Dev, [new SetTarget(22)], At(14));
        var confirmado = Snap(mode: AcMode.Cool, target: 22);
        Assert.False(engine.ObserveReading(Snap(target: 24), confirmado, At(14, 1)));

        Assert.True(engine.ObserveReading(confirmado, confirmado with { Mode = AcMode.Heat }, At(14, 2)));
    }

    [Fact]
    public void Comando_recusado_sai_do_estado_otimista()
    {
        var engine = new RuleEngine([]);
        engine.RecordSent(Dev, [new SetFan(FanSpeed.High), new SetTarget(22)], At(14));

        engine.DiscardPending(Dev, [new SetTarget(22)]);

        var leitura = Snap(target: 24);
        Assert.Equal(24, engine.Expected(leitura, At(14)).TargetC);
        Assert.Equal(FanSpeed.High, engine.Expected(leitura, At(14)).Fan);
    }

    [Fact]
    public void Ajuste_manual_descarta_o_pedido_pendente_do_app()
    {
        var engine = new RuleEngine([]);
        engine.RecordSent(Dev, [new SetTarget(22)], At(14));
        var doUsuario = Snap(target: 26);
        Assert.True(engine.ObserveReading(Snap(target: 24), doUsuario, At(14, 1)));

        Assert.Equal(26, engine.Expected(doUsuario, At(14, 1)).TargetC);
    }

    [Fact]
    public void Regra_com_erro_avisa_uma_vez()
    {
        var engine = new RuleEngine([new ConfortoRule(new ConfortoConfig(null!))]);

        var primeiro = engine.Evaluate(Ctx(At(14), 31, Snap(ambient: 25)));
        var segundo = engine.Evaluate(Ctx(At(14, 1), 31, Snap(ambient: 25)));

        Assert.Single(primeiro.Notifications);
        Assert.Empty(segundo.Notifications);
    }

    [Fact]
    public void Comando_novo_substitui_o_pendente_do_mesmo_tipo()
    {
        var engine = new RuleEngine([]);
        engine.RecordSent(Dev, [new SetMode(AcMode.Cool)], At(14));
        engine.RecordSent(Dev, [new SetMode(AcMode.Auto)], At(14, 1));
        var leitura = Snap(mode: AcMode.Auto);

        Assert.Same(leitura, engine.Expected(leitura, At(14, 2)));
    }

    [Fact]
    public void Motor_recriado_mantem_envio_recente()
    {
        var antigo = new RuleEngine([]);
        antigo.RecordSent(Dev, [new SetTarget(20)], At(14));
        var novo = new RuleEngine([]);
        novo.CopyRuntimeFrom(antigo);

        Assert.False(novo.ObserveReading(Snap(target: 23), Snap(target: 20), At(14, 1)));
    }

    [Fact]
    public void Janela_de_confirmacao_maior_cobre_leitura_a_cada_5_min()
    {
        var engine = new RuleEngine([]) { ConfirmationWindow = TimeSpan.FromMinutes(6) };
        engine.RecordSent(Dev, [new SetTarget(20)], At(14));

        Assert.False(engine.ObserveReading(Snap(target: 23), Snap(target: 20), At(14, 5)));
    }

    [Fact]
    public void Comando_aguardando_confirmacao_nao_e_reenviado()
    {
        var engine = new RuleEngine([new ConfortoRule(new ConfortoConfig(Dev))]);
        var primeiro = engine.Evaluate(Ctx(At(14), 31, Snap(ambient: 25)));
        var enviados = Assert.Single(primeiro.Commands).Commands;
        engine.RecordSent(Dev, enviados, At(14));

        var segundo = engine.Evaluate(Ctx(At(14, 1), 31, Snap(ambient: 25)));

        Assert.Empty(segundo.Commands);
    }

    [Fact]
    public void Aparelho_sem_conexao_nao_recebe_comando()
    {
        var engine = new RuleEngine([new ConfortoRule(new ConfortoConfig(Dev))]);

        var r = engine.Evaluate(Ctx(At(14), 31, Snap(ambient: 25, connected: false)));

        Assert.Empty(r.Commands);
        Assert.Contains(r.Active, a => a.BlockedBy == "aparelho sem conexão");
    }

    [Fact]
    public void Leitura_velha_nao_gera_comando()
    {
        var engine = new RuleEngine([new ConfortoRule(new ConfortoConfig(Dev))]);

        var r = engine.Evaluate(Ctx(At(14, 10), 31, Snap(ambient: 25), readAt: At(14, 6)));

        Assert.Empty(r.Commands);
        Assert.Contains(r.Active, a => a.BlockedBy == "sem leitura nova desde 14:06");
    }

    [Fact]
    public void Pausa_manual_sobrevive_a_reinicio_do_app()
    {
        var antes = new RuleEngine([]);
        antes.ManualChange(Dev, At(14));

        var depois = new RuleEngine([new ConfortoRule(new ConfortoConfig(Dev))]);
        depois.LoadPauses(antes.SavePauses());

        Assert.Equal(At(15), depois.PausedUntil(Dev, At(14, 20)));
        Assert.Empty(depois.Evaluate(Ctx(At(14, 20), 31, Snap(ambient: 25))).Commands);
    }

    private static RuleEngine ComAusente(SaiDoPcConfig? cfg = null, ModoNoiteRule? noite = null) =>
        new([new SaiDoPcRule(cfg ?? new SaiDoPcConfig(Dev)), noite ?? new ModoNoiteRule(new ModoNoiteConfig(Dev))]);

    private static readonly TimeSpan Velha = TimeSpan.FromMinutes(3);

    private static Dictionary<string, DeviceSnapshot> Lido(bool on) => new() { [Dev] = Snap(on: on) with { ReadAt = At(22) } };

    [Fact]
    public void Pc_desligando_desliga_o_ar_ligado_do_ausente()
    {
        var d = Assert.Single(ShutdownPlan.Decide(ComAusente(), Lido(on: true), At(22), Velha));

        Assert.True(d.TurnOff);
    }

    [Fact]
    public void Pc_desligando_respeita_ajuste_manual_modo_noite_ar_desligado_e_a_opcao()
    {
        var pausado = ComAusente();
        pausado.ManualChange(Dev, At(21, 30));
        Assert.False(Assert.Single(ShutdownPlan.Decide(pausado, Lido(on: true), At(22), Velha)).TurnOff);

        var noite = new ModoNoiteRule(new ModoNoiteConfig(Dev));
        noite.Activate(At(21), 24);
        Assert.False(Assert.Single(ShutdownPlan.Decide(ComAusente(noite: noite), Lido(on: true), At(22), Velha)).TurnOff);

        Assert.False(Assert.Single(ShutdownPlan.Decide(ComAusente(), Lido(on: false), At(22), Velha)).TurnOff);
        Assert.Empty(ShutdownPlan.Decide(ComAusente(new SaiDoPcConfig(Dev, OffOnShutdown: false)), Lido(on: true), At(22), Velha));
        Assert.False(Assert.Single(ShutdownPlan.Decide(ComAusente(), Lido(on: true), At(22, 5), Velha)).TurnOff);
    }

    [Fact]
    public void Mudanca_vista_depois_de_lacuna_nao_conta_como_ajuste_manual()
    {
        // O app desligou o ar ao suspender; na volta, a leitura "desligado" não pode pausar as regras.
        var engine = new RuleEngine([]);

        Assert.False(engine.ObserveReading(Snap(on: true) with { ReadAt = At(22) }, Snap(on: false) with { ReadAt = At(7, day: 15) }, At(7, day: 15)));
        Assert.Null(engine.PausedUntil(Dev, At(7, day: 15)));
        Assert.True(engine.ObserveReading(Snap(on: true) with { ReadAt = At(22) }, Snap(on: false) with { ReadAt = At(22, 1) }, At(22, 1)));
    }

    [Fact]
    public void Regra_de_olho_aparece_sem_comandar_nem_bloquear()
    {
        var engine = new RuleEngine([new ConfortoRule(new ConfortoConfig(Dev))]);

        var r = engine.Evaluate(Ctx(At(14), 30, Snap(on: false, ambient: 24)));

        Assert.Empty(r.Commands);
        var a = Assert.Single(r.Active);
        Assert.Equal(ActiveKind.Watching, a.Kind);
        Assert.Null(a.BlockedBy);
    }
}

public class InsightsCalculatorTests
{
    private static HistorySample S(int min, bool on, int amb, bool conn = true) =>
        new(At(14).AddMinutes(min), conn, on, AcMode.Cool, amb, 22);

    [Fact]
    public void Runtime_soma_trechos_ligados_e_ignora_lacunas()
    {
        HistorySample[] s = [S(0, true, 25), S(1, true, 25), S(2, false, 25), S(3, true, 25), S(60, true, 25), S(61, false, 25)];

        var t = InsightsCalculator.Runtime(s, At(14), At(15, 30));

        Assert.Equal(TimeSpan.FromMinutes(3), t);
    }

    [Fact]
    public void Energia_conta_so_o_ventilador_quando_esta_em_ventilar()
    {
        HistorySample[] s =
        [
            new(At(14), true, true, AcMode.Cool, 25, 24), new(At(14, 5), true, true, AcMode.Cool, 25, 24),
            new(At(14, 10), true, true, AcMode.FanOnly, 23, 24), new(At(14, 15), true, true, AcMode.FanOnly, 23, 24),
            new(At(14, 20), true, false, AcMode.FanOnly, 23, 24),
        ];

        var kwh = InsightsCalculator.EnergyKwh(s, At(14), At(15), averageWatts: 1200);

        // 10 min a 1200 W + 10 min a 40 W.
        Assert.Equal(0.2 + 40 / 6000.0, kwh, 6);
    }

    [Fact]
    public void Minutos_por_grau_mede_entre_degraus_de_queda()
    {
        HistorySample[] s = [S(0, true, 26), S(2, true, 25), S(5, true, 25), S(8, true, 24), S(10, true, 24), S(13, true, 24), S(16, true, 23)];

        Assert.Equal(7, InsightsCalculator.MinutesPerDegree(s));
    }

    [Fact]
    public void Quedas_de_conexao_e_desde_quando()
    {
        HistorySample[] s = [S(0, true, 25), S(1, true, 25, false), S(2, true, 25), S(3, true, 25, false), S(4, true, 25, false)];

        Assert.Equal(2, InsightsCalculator.Disconnections(s));
        Assert.Equal(At(14, 3), InsightsCalculator.DisconnectedSince(s));
    }

    [Fact]
    public void Tempo_desligado_pelo_sai_do_pc()
    {
        EventRecord[] e =
        [
            new(At(10), Dev, "sai-do-pc:desligou", ""), new(At(11), Dev, "sai-do-pc:voltou", ""),
            new(At(13), Dev, "sai-do-pc:desligou", ""),
        ];

        Assert.Equal(TimeSpan.FromMinutes(90), InsightsCalculator.AwayOffTime(e, At(13, 30)));
    }
}

public class HistoryStoreTests
{
    [Fact]
    public void Grava_e_le_leituras_clima_e_eventos()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meuclima-test-{Guid.NewGuid():N}.db");
        try
        {
            using (var store = new HistoryStore(path))
            {
                store.Record(Snap(ambient: 25) with { ReadAt = At(14) });
                store.Record(Snap(ambient: 24, on: false) with { ReadAt = At(14, 1) });
                store.RecordOutside(At(14), 31.5);
                store.RecordEvent(At(14), Dev, "sai-do-pc:desligou", "x");

                var s = store.Samples(Dev, At(13));
                Assert.Equal(2, s.Count);
                Assert.Equal(25, s[0].AmbientC);
                Assert.False(s[1].On);
                Assert.Equal(31.5, Assert.Single(store.Outside(At(13))).TempC);
                Assert.Single(store.Events(At(13)));

                store.Prune(At(14, 1));
                Assert.Single(store.Samples(Dev, At(13)));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}

public class OpenMeteoTests
{
    [Fact]
    public void Previsao_horaria_traz_a_sensacao_termica()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse("""
            {"current":{"temperature_2m":28.1,"apparent_temperature":30.4,"relative_humidity_2m":61},
             "hourly":{"time":["2026-10-14T17:00","2026-10-14T18:00"],"temperature_2m":[29.0,30.2],"apparent_temperature":[31.5,null]}}
            """)!;

        var w = OpenMeteoClient.Parse(json, At(14));

        Assert.Equal(30.4, w.ApparentC);
        Assert.Equal(31.5, w.Hourly[0].ApparentC);
        Assert.Null(w.Hourly[1].ApparentC);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 17, 0, 0, TimeSpan.Zero), w.Hourly[0].Time);
    }
}
