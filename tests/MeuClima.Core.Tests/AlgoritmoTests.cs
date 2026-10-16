using MeuClima.Core.Devices;
using MeuClima.Core.Rules;
using MeuClima.Core.Weather;
using static MeuClima.Core.Devices.DeviceCommand;
using static MeuClima.Core.Tests.TestKit;

namespace MeuClima.Core.Tests;

/// <summary>Fases 2 e 3 do plano de confiabilidade (docs/planos/2026-10-16-confiabilidade-das-regras.md).</summary>
public class AlgoritmoTests
{
    private static ConfortoRule Conforto(bool allowHeat = false, bool fanAtMin = true) =>
        new(new ConfortoConfig(Dev, AllowHeat: allowHeat, FanAtMin: fanAtMin));

    private static RuleOutput Ler(IRule r, DateTimeOffset t, int amb, bool on = false, double fora = 30,
        AcMode mode = AcMode.Cool, IHistoryReader? historico = null, WeatherSnapshot? clima = null) =>
        r.Evaluate(Ctx(t, fora, Snap(on: on, mode: mode, ambient: amb), history: historico, weather: clima));

    [Fact]
    public void Fora_mais_fresco_adia_ligar_avisa_e_liga_depois_se_nao_resolver()
    {
        var r = Conforto();
        Ler(r, At(14), 25, fora: 20);

        var adiou = Ler(r, At(14, 5), 25, fora: 20);
        Assert.Empty(adiou.Claims);
        Assert.Equal("Lá fora está mais fresco", Assert.Single(adiou.Notifications).Title);
        Assert.Contains("14:25", adiou.Observed[Dev].Status.Detail);

        for (var m = 10; m <= 20; m += 5) Assert.Empty(Ler(r, At(14, m), 25, fora: 20).Claims);
        var ligou = Ler(r, At(14, 25), 25, fora: 20);
        Assert.Contains(new Power(true), ligou.Claims[Dev]);
        Assert.Equal("Conforto ligou o ar", Assert.Single(ligou.Notifications).Title);
    }

    [Fact]
    public void Fora_mais_frio_mas_umido_nao_adia()
    {
        var r = Conforto();
        var umido = new WeatherSnapshot(20, 22, 95, [], At(12), DewPointC: 20);
        Ler(r, At(14), 25, clima: umido);

        Assert.Contains(new Power(true), Ler(r, At(14, 5), 25, clima: umido).Claims[Dev]);
    }

    [Fact]
    public void Fora_mais_fresco_usa_a_temperatura_e_nao_a_sensacao()
    {
        var r = Conforto();
        // Sensação de 21 °C com 24 °C de fato: o ar de fora não refresca o cômodo a 25 °C.
        var clima = new WeatherSnapshot(24, 21, 40, [], At(12), DewPointC: 10);
        Ler(r, At(14), 25, clima: clima);

        Assert.Contains(new Power(true), Ler(r, At(14, 5), 25, clima: clima).Claims[Dev]);
    }

    [Fact]
    public void Bloqueado_mostra_o_que_faria_sem_registrar_troca_de_fase()
    {
        var r = Conforto();
        var bloqueado = Ctx(At(14), 30, Snap(on: true, mode: AcMode.FanOnly, ambient: 24)) with { Blocked = _ => true };

        var o = r.Evaluate(bloqueado);

        Assert.NotEmpty(o.Claims);
        Assert.Empty(o.Traces);
    }

    [Fact]
    public void Leitura_isolada_do_outro_lado_nao_zera_a_contagem()
    {
        var r = Conforto();
        Ler(r, At(14), 25);
        Ler(r, At(14, 1), 25);
        Ler(r, At(14, 2), 24);
        Ler(r, At(14, 3), 25);
        Ler(r, At(14, 4), 25);

        Assert.Contains(new Power(true), Ler(r, At(14, 5), 25).Claims[Dev]);
    }

    [Fact]
    public void Leitura_do_outro_lado_logo_no_comeco_zera_a_contagem()
    {
        var r = Conforto();
        Ler(r, At(14), 25);
        Ler(r, At(14, 1), 24);
        for (var m = 2; m <= 6; m++) Assert.Empty(Ler(r, At(14, m), 25).Claims);

        Assert.Contains(new Power(true), Ler(r, At(14, 7), 25).Claims[Dev]);
    }

    [Fact]
    public void Duas_leituras_seguidas_do_outro_lado_zeram_a_contagem()
    {
        var r = Conforto();
        Ler(r, At(14), 25);
        Ler(r, At(14, 1), 25);
        Ler(r, At(14, 2), 24);
        Ler(r, At(14, 3), 24);

        Assert.Empty(Ler(r, At(14, 5), 25).Claims);
    }

    [Fact]
    public void Salto_de_3_graus_numa_leitura_pede_a_confirmacao_normal_antes_do_turbo()
    {
        var r = Conforto();
        Ler(r, At(14), 23);
        Ler(r, At(14, 1), 26);
        Ler(r, At(14, 2), 26);
        Assert.Empty(Ler(r, At(14, 3), 26).Claims);
        Ler(r, At(14, 4), 26);
        Ler(r, At(14, 5), 26);

        Assert.Contains(new SetFan(FanSpeed.Turbo), Ler(r, At(14, 6), 26).Claims[Dev]);
    }

    [Fact]
    public void Compressor_desligado_ha_pouco_so_religa_5_min_depois()
    {
        var h = new FakeHistory(
            new HistorySample(At(13, 55), true, true, AcMode.Cool, 25, 24),
            new HistorySample(At(14), true, true, AcMode.Cool, 25, 24),
            new HistorySample(At(14, 1), true, false, AcMode.Cool, 25, 24));
        var r = Conforto();
        for (var m = 1; m <= 3; m++)
        {
            h.All.Add(new HistorySample(At(14, m), true, false, AcMode.Cool, 26, 24));
            Ler(r, At(14, m), 26, historico: h);
        }

        var esperando = Ler(r, At(14, 4), 26, historico: h);
        Assert.Empty(esperando.Claims);
        Assert.Contains("liga o compressor às 14:06", esperando.Observed[Dev].Status.Detail);
        Assert.Empty(esperando.Notifications);

        var ligou = Ler(r, At(14, 6), 26, historico: h);
        Assert.Contains(new Power(true), ligou.Claims[Dev]);
        Assert.Single(ligou.Notifications);
    }

    [Fact]
    public void Saldo_do_turbo_sobrevive_ao_ajuste_manual()
    {
        var r = Conforto();
        Ler(r, At(14), 27);
        Ler(r, At(14, 2), 27);
        for (var m = 5; m <= 33; m += 2) Ler(r, At(14, m), 26, on: true);
        Assert.Equal(ConfortoFase.Manter, r.State.Fase);

        r.OnManualChange(Dev);

        Assert.Equal(31, r.TurboUsado(At(14, 40)).TotalMinutes, 0);
    }

    [Fact]
    public void Aquecendo_acima_da_minima_passa_a_ventilar_e_nunca_vai_direto_para_o_frio()
    {
        var r = Conforto(allowHeat: true);
        Assert.Contains(new SetMode(AcMode.Heat), Ler(r, At(7), 22, on: true, mode: AcMode.Heat).Claims[Dev]);
        for (var m = 1; m <= 5; m++) Ler(r, At(7, m), 26, on: true, mode: AcMode.Heat);

        var o = Ler(r, At(7, 6), 26, on: true, mode: AcMode.Heat);
        Assert.Equal(ConfortoFase.Ventilar, r.State.Fase);
        Assert.Contains(new SetMode(AcMode.FanOnly), o.Claims[Dev]);
    }

    [Fact]
    public void Aquecendo_acima_da_minima_sem_ventilar_desliga()
    {
        var r = Conforto(allowHeat: true, fanAtMin: false);
        Ler(r, At(7), 22, on: true, mode: AcMode.Heat);
        for (var m = 1; m <= 5; m++) Ler(r, At(7, m), 24, on: true, mode: AcMode.Heat);

        Assert.Equal([new Power(false)], Ler(r, At(7, 6), 24, on: true, mode: AcMode.Heat).Claims[Dev]);
    }

    [Fact]
    public void Modo_noite_nao_sobe_o_alvo_aquecendo()
    {
        var r = new ModoNoiteRule(new ModoNoiteConfig(Dev));
        r.Activate(At(22), 22);

        var o = r.Evaluate(Ctx(At(0, 30, day: 15), 20, Snap(mode: AcMode.Heat, target: 22, ambient: 21)));

        Assert.Contains(new SetTarget(22), o.Claims[Dev]);
    }

    [Fact]
    public void Timer_mexido_por_voce_nao_e_reprogramado_pelo_modo_noite()
    {
        var r = new ModoNoiteRule(new ModoNoiteConfig(Dev));
        r.Activate(At(22), 24);
        r.TimerDoUsuario();

        var o = r.Evaluate(Ctx(At(23, 30), 20, Snap(target: 24, ambient: 24)));

        Assert.DoesNotContain(o.Claims[Dev], c => c is SetStopTimer);
        Assert.Contains("não desliga sozinho", o.Status!.Detail);
        Assert.DoesNotContain("Desliga em", o.Status.Detail);
    }

    [Fact]
    public void Modo_noite_so_ventilando_nao_promete_subir_o_alvo()
    {
        var r = new ModoNoiteRule(new ModoNoiteConfig(Dev));
        r.Activate(At(22), 24);

        var o = r.Evaluate(Ctx(At(23, 30), 20, Snap(mode: AcMode.FanOnly, target: 24, ambient: 24)));

        Assert.StartsWith("Só ventilando", o.Status!.Detail);
    }

    [Fact]
    public void Alivio_do_ausente_nao_troca_so_ventilar_por_frio()
    {
        var r = new SaiDoPcRule(new SaiDoPcConfig(Dev));

        var o = r.Evaluate(Ctx(At(14), 30, Snap(mode: AcMode.FanOnly), new PresenceState(true, TimeSpan.FromMinutes(25))));

        Assert.Empty(o.Claims);
    }

    [Fact]
    public void Ajuste_durante_a_ausencia_vale_ate_voce_voltar()
    {
        var r = new SaiDoPcRule(new SaiDoPcConfig(Dev));
        var bloqueado = new PresenceState(true, TimeSpan.FromMinutes(1), Locked: true);
        Assert.Equal([new Power(false)], r.Evaluate(Ctx(At(14), 30, Snap(), bloqueado)).Claims[Dev]);

        r.OnManualChange(Dev);

        Assert.Empty(r.Evaluate(Ctx(At(15, 30), 30, Snap(), bloqueado with { AwayFor = TimeSpan.FromMinutes(91) })).Claims);
        r.Evaluate(Ctx(At(15, 40), 30, Snap()));
        Assert.Equal([new Power(false)], r.Evaluate(Ctx(At(16), 30, Snap(), bloqueado)).Claims[Dev]);
    }

    [Fact]
    public void Ajuste_durante_a_ausencia_mantem_o_ar_ligado_no_desligamento_do_pc()
    {
        var ausente = new SaiDoPcRule(new SaiDoPcConfig(Dev));
        var engine = new RuleEngine([ausente]);
        ausente.Evaluate(Ctx(At(14), 30, Snap(), new PresenceState(true, TimeSpan.FromMinutes(1), Locked: true)));
        ausente.OnManualChange(Dev);
        var leituras = new Dictionary<string, DeviceSnapshot> { [Dev] = Snap() with { ReadAt = At(15, 30) } };

        Assert.False(Assert.Single(ShutdownPlan.Decide(engine, leituras, At(15, 30), TimeSpan.FromMinutes(3))).TurnOff);
    }

    private static FakeHistory Subindo(int alvo = 23) =>
        new([.. new[] { (0, 23), (5, 23), (10, 23), (15, 23), (20, 24), (22, 25), (23, 25), (24, 25), (25, 25) }
            .Select(p => new HistorySample(At(14).AddMinutes(p.Item1), true, true, AcMode.Cool, p.Item2, alvo))]);

    [Fact]
    public void Janela_aberta_nao_dispara_com_alvo_no_nivel_de_dentro()
    {
        var r = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));

        Assert.Empty(r.Evaluate(Ctx(At(14, 25), 30, Snap(target: 26, ambient: 25), history: Subindo(26))).Claims);
    }

    [Fact]
    public void Janela_aberta_nao_dispara_com_alvo_mudado_dentro_da_janela()
    {
        var h = Subindo();
        h.All.Add(new HistorySample(At(14, 24, 30), true, true, AcMode.Cool, 25, 24));
        var r = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));

        Assert.Empty(r.Evaluate(Ctx(At(14, 25), 30, Snap(target: 24, ambient: 25), history: h)).Claims);
    }

    [Fact]
    public void Janela_aberta_nao_dispara_em_pausa_manual()
    {
        var r = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));

        var o = r.Evaluate(Ctx(At(14, 25), 30, Snap(ambient: 25), history: Subindo()) with { Blocked = _ => true });

        Assert.Empty(o.Notifications);
        Assert.Null(r.State.DisparouEm);
    }

    [Fact]
    public void Aviso_de_acao_vai_junto_com_o_comando_e_some_se_bloqueado()
    {
        var engine = new RuleEngine([Conforto()]);
        engine.Evaluate(Ctx(At(14), 30, Snap(on: false, ambient: 25)));

        var r = engine.Evaluate(Ctx(At(14, 5), 30, Snap(on: false, ambient: 25)));
        Assert.Empty(r.Notifications);
        Assert.Equal("Conforto ligou o ar", Assert.Single(Assert.Single(r.Commands).Notifications).Title);

        var pausado = new RuleEngine([Conforto()]);
        pausado.Pause(Dev, At(16));
        pausado.Evaluate(Ctx(At(14), 30, Snap(on: false, ambient: 25)));
        var bloqueado = pausado.Evaluate(Ctx(At(14, 5), 30, Snap(on: false, ambient: 25)));
        Assert.Empty(bloqueado.Notifications);
        Assert.Empty(bloqueado.Commands);
    }
}

public class CapacidadesTests
{
    private static readonly DeviceCapabilities Caps =
        new([AcMode.Cool, AcMode.FanOnly], [FanSpeed.Auto, FanSpeed.Low], 18, 28, "teste");

    [Fact]
    public void Alvo_fora_da_faixa_vai_para_o_limite()
    {
        var plano = CommandPlanner.Fit([new SetTarget(30)], Caps, out var fora);

        Assert.Equal([new SetTarget(28)], plano);
        Assert.Equal([new SetTarget(30)], fora);
    }

    [Fact]
    public void Ventilacao_que_o_aparelho_nao_tem_sai_do_plano()
    {
        var plano = CommandPlanner.Fit([new Power(true), new SetMode(AcMode.Cool), new SetFan(FanSpeed.Turbo), new SetTarget(24)], Caps, out var fora);

        Assert.Equal([new Power(true), new SetMode(AcMode.Cool), new SetTarget(24)], plano);
        Assert.Equal([new SetFan(FanSpeed.Turbo)], fora);
    }

    [Fact]
    public void Modo_que_o_aparelho_nao_tem_cancela_o_plano_em_vez_de_ligar_no_ultimo_modo()
    {
        Assert.Empty(CommandPlanner.Fit([new Power(true), new SetMode(AcMode.Heat), new SetTarget(24)], Caps, out var fora));
        Assert.Contains(new SetMode(AcMode.Heat), fora);
    }

    private sealed class Quer(params DeviceCommand[] cmds) : IRule
    {
        public RuleConfig Config { get; } = new ConfortoConfig(Dev);
        public string Name => "Teste";
        public int Priority => RulePriority.Conforto;
        public RuleOutput Evaluate(RuleContext ctx) => new RuleOutput().Claim(Dev, cmds);
        public string SaveState() => "{}";
        public void LoadState(string json) { }
        public void OnManualChange(string deviceId) { }
    }

    [Fact]
    public void Alvo_limitado_a_faixa_nao_e_reenviado_a_cada_ciclo()
    {
        var engine = new RuleEngine([new Quer(new SetTarget(16))]) { Capabilities = _ => Caps };

        Assert.Empty(engine.Evaluate(Ctx(At(14), 30, Snap(target: 18))).Commands);
    }

    [Fact]
    public void Modo_que_o_aparelho_nao_tem_nao_envia_nada_e_registra_uma_vez()
    {
        var engine = new RuleEngine([new Quer(new Power(true), new SetMode(AcMode.Heat))]) { Capabilities = _ => Caps };

        var r = engine.Evaluate(Ctx(At(14), 30, Snap(on: false)));
        Assert.Empty(r.Commands);
        Assert.Contains("nada enviado", Assert.Single(r.Traces).Text);
        Assert.Empty(engine.Evaluate(Ctx(At(14, 1), 30, Snap(on: false))).Traces);
    }

    [Fact]
    public void Mudar_a_correcao_do_sensor_nao_parece_janela_aberta()
    {
        var h = new FakeHistory([.. Enumerable.Range(0, 26).Select(m =>
            new HistorySample(At(14).AddMinutes(m), true, true, AcMode.Cool, m < 21 ? 22 : 24, 22))]);
        var janela = new JanelaAbertaRule(new JanelaAbertaConfig(Dev));
        var corrigido = new HistoricoDesdeCorrecao(h, new Dictionary<string, DateTimeOffset> { [Dev] = At(14, 21) });

        Assert.Empty(janela.Evaluate(Ctx(At(14, 25), 30, Snap(target: 22, ambient: 24), history: corrigido)).Claims);
        Assert.NotEmpty(new JanelaAbertaRule(new JanelaAbertaConfig(Dev))
            .Evaluate(Ctx(At(14, 25), 30, Snap(target: 22, ambient: 24), history: h)).Claims);
    }

    [Fact]
    public void Sem_capacidades_o_plano_segue_igual()
    {
        IReadOnlyList<DeviceCommand> plano = [new SetFan(FanSpeed.Turbo)];

        Assert.Same(plano, CommandPlanner.Fit(plano, null, out var fora));
        Assert.Empty(fora);
    }
}
