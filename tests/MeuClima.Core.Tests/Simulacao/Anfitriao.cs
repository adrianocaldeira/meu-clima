using MeuClima.Core.Devices;
using MeuClima.Core.Rules;
using MeuClima.Core.Weather;
using static MeuClima.Core.Devices.DeviceCommand;
using static MeuClima.Core.Tests.TestKit;

namespace MeuClima.Core.Tests.Simulacao;

/// <summary>Repete o laço do ClimateService (ler, observar, avaliar, enviar, gravar estado) e confere as invariantes a cada ciclo.</summary>
internal sealed class Anfitriao
{
    public static readonly TimeSpan JanelaConfirmacao = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan LeituraVelha = TimeSpan.FromMinutes(3);

    private readonly IReadOnlyList<RuleConfig> _configs;
    private readonly Func<RuleConfig, IRule> _criar;
    private Dictionary<string, string> _estadoSalvo = [];
    private string? _pausasSalvas;
    private readonly Dictionary<string, DateTimeOffset> _ultimoEnvio = [];
    private readonly Dictionary<(string, string), DateTimeOffset> _esperaRecusa = [];
    private readonly List<DeviceSnapshot> _sessao = [];
    private DateTimeOffset? _pausaAte;
    private readonly HashSet<DateTimeOffset> _confortoPreso = [];

    public Anfitriao(IReadOnlyList<RuleConfig> configs, Func<RuleConfig, IRule>? criar = null)
    {
        _configs = configs;
        _criar = criar ?? Criar;
        Motor = Montar();
    }

    public RuleEngine Motor { get; private set; }
    public Dictionary<string, DeviceSnapshot> Leituras { get; } = [];
    public FakeHistory Historico { get; } = new();
    public List<string> Violacoes { get; } = [];
    public List<(DateTimeOffset Quando, string Regra, DeviceCommand Comando)> Enviados { get; } = [];
    public Func<DeviceCommand, bool> Recusa { get; set; } = _ => false;
    public ConfortoConfig Conforto => _configs.OfType<ConfortoConfig>().Single();

    public ConfortoRule? RegraConforto => Motor.Rules.OfType<ConfortoRule>().SingleOrDefault();

    private RuleEngine Montar()
    {
        var regras = _configs.Select(_criar).ToList();
        foreach (var r in regras)
            if (_estadoSalvo.TryGetValue(r.Config.Id, out var json)) r.LoadState(json);
        var motor = new RuleEngine(regras) { ConfirmationWindow = JanelaConfirmacao, MaxObservationGap = LeituraVelha };
        if (_pausasSalvas is { } p) motor.LoadPauses(p);
        return motor;
    }

    private static IRule Criar(RuleConfig c) => c switch
    {
        ConfortoConfig x => new ConfortoRule(x),
        SaiDoPcConfig x => new SaiDoPcRule(x),
        JanelaAbertaConfig x => new JanelaAbertaRule(x),
        ModoNoiteConfig x => new ModoNoiteRule(x),
        _ => throw new NotSupportedException(c.GetType().Name),
    };

    /// <summary>App fechado e aberto de novo: só o estado gravado em disco e o histórico sobrevivem.</summary>
    /// <param name="semLacuna">Reabriu na hora: as leituras de antes seguem valendo para as invariantes.</param>
    public void Reiniciar(bool semLacuna = false)
    {
        Gravar();
        Motor = Montar();
        Leituras.Clear();
        _ultimoEnvio.Clear();
        _esperaRecusa.Clear();
        if (!semLacuna) _sessao.Clear();
    }

    private void Gravar()
    {
        _estadoSalvo = Motor.Rules.ToDictionary(r => r.Config.Id, r => r.SaveState());
        _pausasSalvas = Motor.SavePauses();
    }

    /// <summary>Um ciclo; devolve o que chegou ao aparelho. Sem leitura nova, avalia com a última (falha de leitura).</summary>
    public List<DeviceCommand> Ciclo(DateTimeOffset now, PresenceState presenca, double? fora, DeviceSnapshot? leitura)
    {
        if (leitura is { } atual)
        {
            if (Leituras.TryGetValue(atual.Id, out var anterior) && Motor.ObserveReading(anterior, atual, now))
                _pausaAte = now + RuleEngine.ManualPause;
            Leituras[atual.Id] = atual;
            _sessao.Add(atual);
            if (_pausaAte > now || _esperaRecusa.GetValueOrDefault((atual.Id, "Conforto")) > now) _confortoPreso.Add(atual.ReadAt);
            Historico.All.Add(new HistorySample(now, atual.Connected, atual.On, atual.Mode, atual.AmbientC, atual.TargetC));
            Historico.All.RemoveAll(s => s.Time < now.AddHours(-3));
        }
        if (Leituras.Count == 0) return [];

        var clima = fora is { } f ? new WeatherSnapshot(f, f, 60, [], now) : null;
        var resultado = Motor.Evaluate(new RuleContext(now, Brt, clima, Leituras, presenca, Historico) { MaxReadingAge = LeituraVelha });
        Checar(now, resultado);
        Gravar();

        var chegaram = new List<DeviceCommand>();
        foreach (var plano in resultado.Commands)
        {
            if (_ultimoEnvio.TryGetValue(plano.DeviceId, out var u) && now - u < TimeSpan.FromSeconds(20)) continue;
            var chave = (plano.DeviceId, plano.RuleName);
            if (_esperaRecusa.TryGetValue(chave, out var espera) && now < espera) continue;
            _ultimoEnvio[plano.DeviceId] = now;
            var enviados = plano.Commands.TakeWhile(c => !Recusa(c)).ToList();
            // Como o ClimateService: recusa no primeiro comando espera 10 min; depois de um aceito, só adia.
            if (enviados.Count == 0) _esperaRecusa[chave] = now + TimeSpan.FromMinutes(10);
            if (enviados.Count == 0) continue;
            Motor.RecordSent(plano.DeviceId, enviados, now);
            foreach (var c in enviados) Enviados.Add((now, plano.RuleName, c));
            chegaram.AddRange(enviados);
        }
        return chegaram;
    }

    private void Checar(DateTimeOffset now, EngineResult r)
    {
        void Falha(string msg) => Violacoes.Add($"{now:dd/MM HH:mm} {msg}");
        var leitura = Leituras[Dev];
        var cfg = Conforto;
        if (RegraConforto?.State.Fase is ConfortoFase.Manter or ConfortoFase.Turbo && leitura.On && AbaixoDaMinimaHa(cfg) > TimeSpan.FromMinutes(12))
            Falha($"Conforto esfriando com dentro abaixo de {cfg.MinC} °C há {AbaixoDaMinimaHa(cfg).TotalMinutes:0} min");
        // Dois ciclos de folga: o comando sai na avaliação seguinte e o aparelho muda na leitura depois.
        if (TurboNasUltimas2h(now) > TimeSpan.FromMinutes(cfg.MaxTurboMin + 2))
            Falha($"turbo por {TurboNasUltimas2h(now).TotalMinutes:0} min nas últimas 2 h");
        if (r.Commands.Count == 0) return;

        if (now - leitura.ReadAt > LeituraVelha)
            Falha($"comando com leitura de {(now - leitura.ReadAt).TotalMinutes:0} min atrás");
        if (_pausaAte is { } ate && now < ate)
            Falha($"comando de regra durante a pausa manual (até {ate:HH:mm}): {Lista(r)}");

        foreach (var p in r.Commands)
        {
            if (p.Commands.Contains(new Power(true)) && !leitura.On)
            {
                if (p.RuleName != "Conforto") Falha($"{p.RuleName} ligou o ar");
                else if (!Sustentado(a => a > cfg.MaxC))
                    Falha($"Conforto ligou sem calor sustentado (dentro {leitura.AmbientC}, máxima {cfg.MaxC}; {Trilha()})");
            }
            if (p.Commands.Contains(new SetFan(FanSpeed.Turbo)) && leitura.Fan != FanSpeed.Turbo && !Sustentado(a => a >= cfg.TurboFromC))
                Falha($"turbo sem calor forte sustentado (dentro {leitura.AmbientC}; {Trilha()})");
            if (p.RuleName == "Conforto" && CompressorTroca(p.Commands, leitura) is { } liga && UltimaTrocaDoCompressor(liga) is { } ha && ha < ConfortoRule.CicloMinimo)
                Falha($"Conforto {(liga ? "ligou" : "desligou")} o compressor {ha.TotalMinutes:0.#} min depois da última troca");
        }
    }

    private static bool Compressor(bool on, AcMode mode) => on && mode is AcMode.Cool or AcMode.Heat or AcMode.Auto or AcMode.Dry;

    /// <summary>true se o plano liga o compressor, false se desliga, null se não muda.</summary>
    private static bool? CompressorTroca(IReadOnlyList<DeviceCommand> plano, DeviceSnapshot leitura)
    {
        var on = plano.OfType<Power>().LastOrDefault()?.On ?? leitura.On;
        var modo = plano.OfType<SetMode>().LastOrDefault()?.Mode ?? leitura.Mode;
        var antes = Compressor(leitura.On, leitura.Mode);
        var depois = Compressor(on, modo);
        return antes == depois ? null : depois;
    }

    /// <summary>Tempo desde que o compressor ficou no estado atual (o oposto de <paramref name="liga"/>).</summary>
    private TimeSpan? UltimaTrocaDoCompressor(bool liga)
    {
        var lidas = _sessao.Where(s => s.Connected).ToList();
        for (var i = lidas.Count - 1; i > 0; i--)
        {
            // Sem leitura por mais que a lacuna, o momento da troca é desconhecido.
            if (lidas[i].ReadAt - lidas[i - 1].ReadAt > ConfortoRule.Lacuna) return null;
            if (Compressor(lidas[i - 1].On, lidas[i - 1].Mode) == liga && Compressor(lidas[i].On, lidas[i].Mode) != liga)
                return lidas[^1].ReadAt - lidas[i].ReadAt;
        }
        return null;
    }

    /// <summary>Turbo nas últimas 2 h em trechos que o Conforto podia mudar: sem pausa manual, recusa ou lacuna.</summary>
    private TimeSpan TurboNasUltimas2h(DateTimeOffset now)
    {
        var total = TimeSpan.Zero;
        var lidas = _sessao.Where(s => s.ReadAt >= now - ConfortoRule.JanelaTurbo).ToList();
        for (var i = 1; i < lidas.Count; i++)
            if (lidas[i - 1].On && lidas[i - 1].Fan == FanSpeed.Turbo && !_confortoPreso.Contains(lidas[i - 1].ReadAt)
                && lidas[i].ReadAt - lidas[i - 1].ReadAt <= TimeSpan.FromMinutes(2))
                total += lidas[i].ReadAt - lidas[i - 1].ReadAt;
        return total;
    }

    /// <summary>PC desligando ou suspendendo: o que o app desligaria agora.</summary>
    public bool DesligaAoSair(DateTimeOffset now) =>
        ShutdownPlan.Decide(Motor, Leituras, now, LeituraVelha).Any(d => d.TurnOff);

    private static string Lista(EngineResult r) => string.Join("; ", r.Commands.Select(p => $"{p.RuleName}: {string.Join(", ", p.Commands)}"));

    /// <summary>Condição por pelo menos 2 min, sem lacuna, com a leitura atual do lado dela. Depois de duas leituras do
    /// lado, uma isolada do outro lado não corta o trecho; duas seguidas cortam.</summary>
    private bool Sustentado(Func<int, bool> condicao)
    {
        DateTimeOffset? desde = null, anterior = null;
        int lado = 0, contra = 0;
        foreach (var s in _sessao.Where(s => s.Connected))
        {
            if (anterior is { } a && s.ReadAt - a > ConfortoRule.Lacuna) (desde, lado, contra) = (null, 0, 0);
            anterior = s.ReadAt;
            if (s.AmbientC is { } x && condicao(x)) (desde, lado, contra) = (desde ?? s.ReadAt, lado + 1, 0);
            else if (desde is not null && lado >= 2 && contra == 0) contra = 1;
            else (desde, lado, contra) = (null, 0, 0);
        }
        return desde is { } d && contra == 0 && lado >= 2 && anterior - d >= TimeSpan.FromMinutes(2);
    }

    private TimeSpan AbaixoDaMinimaHa(ConfortoConfig cfg)
    {
        var trecho = TrechoFinal(s => s.AmbientC is { } a && a < cfg.MinC);
        return trecho.Count < 2 ? TimeSpan.Zero : trecho[^1].ReadAt - trecho[0].ReadAt;
    }

    private List<DeviceSnapshot> TrechoFinal(Func<DeviceSnapshot, bool> condicao)
    {
        var trecho = new List<DeviceSnapshot>();
        for (var i = _sessao.Count - 1; i >= 0; i--)
        {
            var s = _sessao[i];
            // A regra mantém a contagem através de uma leitura sem conexão; a lacuna abaixo ainda corta o trecho.
            if (!s.Connected) continue;
            if (!condicao(s)) break;
            if (trecho.Count > 0 && trecho[0].ReadAt - s.ReadAt > ConfortoRule.Lacuna) break;
            trecho.Insert(0, s);
        }
        return trecho;
    }

    private string Trilha() => string.Join(" ", _sessao.TakeLast(6).Select(s => $"{s.ReadAt:HH:mm}={s.AmbientC}{(s.On ? "L" : "D")}"));
}
