using System.Globalization;
using MeuClima.Core.Devices;
using MeuClima.Core.Rules;
using static MeuClima.Core.Devices.DeviceCommand;
using static MeuClima.Core.Tests.TestKit;

namespace MeuClima.Core.Tests.Simulacao;

/// <summary>Três dias por semente com suspensão, desligamento do PC, reinício do app, ajuste manual, aparelho sem
/// conexão, leitura que falha e recusa da nuvem. Falha imprime a semente para reproduzir.</summary>
public class SimulacaoTests
{
    private static List<RuleConfig> Regras() =>
        [new ConfortoConfig(Dev), new SaiDoPcConfig(Dev), new JanelaAbertaConfig(Dev), new ModoNoiteConfig(Dev)];

    [Fact]
    public void Invariantes_valem_em_tres_dias_simulados_por_semente()
    {
        var falhas = new List<string>();
        for (var semente = 1; semente <= 30; semente++)
        {
            var h = Simular(semente, TimeSpan.FromDays(3));
            falhas.AddRange(h.Violacoes.Take(3).Select(v => $"semente {semente}: {v}"));
        }
        Assert.True(falhas.Count == 0, string.Join(Environment.NewLine, falhas.Take(30)));
    }

    private static Anfitriao Simular(int semente, TimeSpan duracao)
    {
        var rng = new Random(semente);
        var comodo = new Comodo(rng, 23 + rng.Next(4));
        var host = new Anfitriao(Regras()) { Recusa = _ => rng.NextDouble() < 0.03 };
        var t = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.FromHours(-3));
        var fim = t + duracao;
        DateTimeOffset pcVolta = t, longeAte = t, semConexaoAte = t, falhaLeituraAte = t;
        var bloqueado = false;
        var reiniciarAoVoltar = false;
        DateTimeOffset? desligaPorFora = null;

        for (; t < fim; t = t.AddMinutes(1))
        {
            var fora = 25 + 6 * Math.Sin((t.Hour + t.Minute / 60.0 - 9) / 24 * 2 * Math.PI);
            comodo.Passo(fora, TimeSpan.FromMinutes(1));
            comodo.Conectado = t >= semConexaoAte;
            if (desligaPorFora is { } d && t >= d)
            {
                comodo.Estado = comodo.Estado with { On = false };
                desligaPorFora = null;
            }

            if (t < pcVolta) continue;
            if (reiniciarAoVoltar)
            {
                host.Reiniciar();
                reiniciarAoVoltar = false;
            }

            var longe = t < longeAte;
            var presenca = longe ? new PresenceState(true, TimeSpan.FromMinutes(10), bloqueado) : PresenceState.Present;
            var leitura = t < falhaLeituraAte ? null : comodo.Ler(t);
            comodo.Aplicar(host.Ciclo(t, presenca, fora, leitura));

            var sorteio = rng.NextDouble();
            if (sorteio < 0.002)
            {
                // PC desligado: o app desliga o ar do Ausente e fecha; às vezes alguém desliga pelo controle depois.
                if (host.DesligaAoSair(t) && rng.NextDouble() < 0.9) comodo.Aplicar([new Power(false)]);
                pcVolta = t.AddMinutes(rng.Next(30, 720));
                reiniciarAoVoltar = true;
                if (rng.NextDouble() < 0.5) desligaPorFora = t.AddMinutes(rng.Next(5, 30));
            }
            else if (sorteio < 0.004)
            {
                if (host.DesligaAoSair(t) && rng.NextDouble() < 0.9) comodo.Aplicar([new Power(false)]);
                pcVolta = t.AddMinutes(rng.Next(10, 600));
            }
            else if (sorteio < 0.005) host.Reiniciar();
            else if (sorteio < 0.009) comodo.Estado = Ajuste(rng, comodo.Estado);
            else if (sorteio < 0.014)
            {
                longeAte = t.AddMinutes(rng.Next(5, 90));
                bloqueado = rng.NextDouble() < 0.5;
            }
            else if (sorteio < 0.016) semConexaoAte = t.AddMinutes(rng.Next(1, 10));
            else if (sorteio < 0.018) falhaLeituraAte = t.AddMinutes(rng.Next(1, 8));
        }
        return host;
    }

    private static DeviceSnapshot Ajuste(Random rng, DeviceSnapshot s) => rng.Next(4) switch
    {
        0 => s with { On = !s.On },
        1 => s with { TargetC = Math.Clamp((s.TargetC ?? 24) + (rng.Next(2) == 0 ? -1 : 1), 17, 30) },
        2 => s with { Fan = s.Fan == FanSpeed.High ? FanSpeed.Low : FanSpeed.High },
        _ => s with { On = true, Mode = AcMode.Cool, TargetC = 20 },
    };

    [Fact]
    public void Simulador_acusa_regra_que_liga_sem_calor_sustentado()
    {
        var host = new Anfitriao([new ConfortoConfig(Dev)], c => new LigaSempre((ConfortoConfig)c));
        host.Ciclo(At(8), PresenceState.Present, 20, Snap(on: false, ambient: 22) with { ReadAt = At(8) });

        Assert.Contains(host.Violacoes, v => v.Contains("ligou sem calor sustentado"));
    }

    private sealed class LigaSempre(ConfortoConfig cfg) : IRule
    {
        public RuleConfig Config => cfg;
        public string Name => "Conforto";
        public int Priority => RulePriority.Conforto;
        public RuleOutput Evaluate(RuleContext ctx) => new RuleOutput().Claim(cfg.DeviceId, new Power(true));
        public string SaveState() => "{}";
        public void LoadState(string json) { }
        public void OnManualChange(string deviceId) { }
    }

    /// <summary>Noite real de 15→16/10: o ar ligou às 20:31, o PC desligou com o ar ligado e o app abriu às 08:42.</summary>
    [Fact]
    public void Replay_da_noite_de_15_10_nao_religa_o_ar_de_manha_com_dentro_abaixo_da_faixa()
    {
        var host = new Anfitriao([new ConfortoConfig(Dev) { Id = "conforto" }, new SaiDoPcConfig(Dev) { Id = "ausente" }]);
        DateTimeOffset? anterior = null;
        foreach (var (hora, leitura, fora) in Replay("replay-2026-10-15-noite.csv"))
        {
            if (anterior is { } a && hora - a > TimeSpan.FromMinutes(30)) host.Reiniciar();
            anterior = hora;
            host.Ciclo(hora, PresenceState.Present, fora, leitura);
        }

        var manha = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.FromHours(-3));
        Assert.DoesNotContain(host.Enviados, e => e.Quando >= manha && e.Comando == new Power(true));
        Assert.Contains(host.Enviados, e => e.Quando < manha && e.Comando == new Power(true));
        Assert.Empty(host.Violacoes);
    }

    private static IEnumerable<(DateTimeOffset Hora, DeviceSnapshot Leitura, double? Fora)> Replay(string arquivo)
    {
        foreach (var linha in Fixture(arquivo).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
        {
            var c = linha.Split(',');
            var hora = DateTimeOffset.Parse(c[0], CultureInfo.InvariantCulture);
            int? Int(string s) => s.Length == 0 ? null : int.Parse(s, CultureInfo.InvariantCulture);
            var leitura = Snap(on: c[2] == "1", mode: Enum.Parse<AcMode>(c[3]), fan: Enum.Parse<FanSpeed>(c[4]),
                target: Int(c[5]), ambient: Int(c[6]), eco: null, connected: c[1] == "1") with { ReadAt = hora };
            yield return (hora, leitura, c[7].Length == 0 ? null : double.Parse(c[7], CultureInfo.InvariantCulture));
        }
    }
}
