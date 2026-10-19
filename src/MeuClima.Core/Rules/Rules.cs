using System.Globalization;
using System.Text.Json;
using MeuClima.Core.Devices;
using MeuClima.Core.Weather;
using static MeuClima.Core.Devices.DeviceCommand;

namespace MeuClima.Core.Rules;

public interface IRule
{
    RuleConfig Config { get; }
    string Name { get; }
    int Priority { get; }
    RuleOutput Evaluate(RuleContext ctx);
    string SaveState();
    void LoadState(string json);

    /// <summary>O usuário mexeu no aparelho (controle, app do celular ou bandeja).</summary>
    void OnManualChange(string deviceId);
}

/// <summary>Regra com estado tipado, persistido em JSON para sobreviver a reinício do app.</summary>
public abstract class Rule<TConfig, TState>(TConfig config) : IRule
    where TConfig : RuleConfig
    where TState : class, new()
{
    public TConfig Cfg { get; } = config;
    public RuleConfig Config => Cfg;
    public TState State { get; protected set; } = new();
    public abstract string Name { get; }
    public abstract int Priority { get; }
    public abstract RuleOutput Evaluate(RuleContext ctx);
    public string SaveState() => JsonSerializer.Serialize(State);
    public void LoadState(string json) => State = JsonSerializer.Deserialize<TState>(json) ?? new();
    public virtual void OnManualChange(string deviceId) { }

    protected static string Hm(RuleContext ctx, DateTimeOffset t) =>
        TimeZoneInfo.ConvertTime(t, ctx.TimeZone).ToString("HH:mm", CultureInfo.InvariantCulture);
}

public static class RulePriority
{
    public const int JanelaAberta = 50, ModoNoite = 40, SaiDoPc = 30, Conforto = 20;
}

public enum ConfortoFase { Parado, Turbo, Manter, Ventilar, Aquecer }

/// <summary>Trecho de turbo; o teto vale pela soma numa janela de <see cref="ConfortoRule.JanelaTurbo"/>.</summary>
public sealed record TurboUso(DateTimeOffset Inicio, DateTimeOffset Fim);

/// <summary>Desde quando uma condição vale. Depois de duas leituras do mesmo lado, uma leitura isolada do outro lado
/// não zera a contagem (o sensor oscila 1 °C); duas seguidas zeram.</summary>
public sealed class Marca
{
    public DateTimeOffset? Desde { get; set; }
    public int Lado { get; set; }
    public int Contra { get; set; }

    public void Ler(bool vale, DateTimeOffset leitura)
    {
        if (vale)
        {
            Desde ??= leitura;
            Lado++;
            Contra = 0;
            return;
        }
        if (Desde is null) return;
        if (Lado >= 2 && ++Contra < ConfortoRule.LeiturasContra) return;
        (Desde, Lado, Contra) = (null, 0, 0);
    }
}

public sealed class ConfortoState
{
    public ConfortoFase Fase { get; set; }
    public DateTimeOffset? TurboDesde { get; set; }
    /// <summary>Turbo bateu o teto sem chegar à máxima: só volta depois que dentro entrar na faixa.</summary>
    public bool TurboEsgotado { get; set; }
    /// <summary>Leitura de dentro quando o turbo começou.</summary>
    public int? TurboInicioC { get; set; }
    /// <summary>Turbo inteiro sem baixar 1 °C: leitura que o sensor travou. Some quando a leitura muda.</summary>
    public int? SensorParadoEm { get; set; }
    /// <summary>Leitura no primeiro turbo da janela: turbos picados por ausência contam como um só.</summary>
    public int? TurboSerieC { get; set; }
    public List<TurboUso> Turbos { get; set; } = [];
    public Marca Acima { get; set; } = new();
    public Marca Quente { get; set; } = new();
    public Marca NoPiso { get; set; } = new();
    public Marca Abaixo { get; set; } = new();
    /// <summary>Dentro acima da mínima: tira do aquecimento.</summary>
    public Marca Morno { get; set; } = new();
    public int? UltimaAmb { get; set; }
    /// <summary>Subida de 3 °C ou mais de uma leitura para a outra: pede a confirmação normal, não a curta.</summary>
    public DateTimeOffset? SaltoEm { get; set; }
    public DateOnly? AntecipouEm { get; set; }
    public DateTimeOffset? UltimoAvisoFora { get; set; }
    /// <summary>Lá fora mais fresco: ligar o ar espera até aqui, para dar tempo de abrir a janela.</summary>
    public DateTimeOffset? ForaAdiaAte { get; set; }
    /// <summary>Compressor mudou há pouco: a troca pedida espera até aqui.</summary>
    public DateTimeOffset? ProtegeAte { get; set; }
    public bool AvisouLigar { get; set; }
    public bool AvisouTurbo { get; set; }
    /// <summary>Saiu do turbo por ausência ou fim da agenda: ainda falta tirar a ventilação do Turbo.</summary>
    public bool SairDoTurbo { get; set; }
    /// <summary>Hora da última leitura avaliada; uma lacuna maior que <see cref="ConfortoRule.Lacuna"/> recomeça as fases.</summary>
    public DateTimeOffset? UltimaLeitura { get; set; }
}

/// <summary>Mantém dentro entre a mínima e a máxima com o menor gasto: frio econômico, ventilar e turbo só no calor forte.</summary>
/// <remarks>
/// O sensor mede graus inteiros e às vezes salta numa leitura só: mudar de fase pede a condição sustentada
/// por <see cref="Confirmacao"/> (sair do frio para ventilar, <see cref="ConfirmacaoPiso"/>; 2 °C ou mais acima
/// da máxima, <see cref="ConfirmacaoForte"/>), e uma leitura isolada do outro lado não zera a contagem.
/// O compressor não muda de estado antes de <see cref="CicloMinimo"/>. Nunca usa o modo Auto, que aquece
/// sozinho quando dentro fica abaixo do alvo.
/// </remarks>
public sealed class ConfortoRule(ConfortoConfig cfg) : Rule<ConfortoConfig, ConfortoState>(cfg)
{
    public static readonly TimeSpan Confirmacao = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ConfirmacaoPiso = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ConfirmacaoForte = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan AvisoForaCarencia = TimeSpan.FromHours(3);
    /// <summary>Sem leitura por mais que isto (PC desligado, suspenso, app fechado), nada do que se via antes vale.</summary>
    public static readonly TimeSpan Lacuna = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan JanelaTurbo = TimeSpan.FromHours(2);
    /// <summary>Tempo sem usar o PC (inclui os 5 min até contar como ausente) em que a fase fica guardada.</summary>
    public static readonly TimeSpan SaidaCurta = TimeSpan.FromMinutes(15);
    /// <summary>Turbo com menos que isto de saldo não começa: um turbo curto só gastaria o saldo.</summary>
    public static readonly TimeSpan TurboMinimo = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan CicloMinimo = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan AdiaPorFora = TimeSpan.FromMinutes(20);
    public const int LeiturasContra = 2;
    public const int SaltoC = 3;
    /// <summary>Ar de fora mais úmido que isto não refresca, mesmo mais frio.</summary>
    public const double OrvalhoMaxC = 18;

    public override string Name => "Conforto";
    public override int Priority => RulePriority.Conforto;

    public override void OnManualChange(string deviceId)
    {
        if (deviceId == Cfg.DeviceId) Reiniciar();
    }

    private void Reiniciar() => State = Recomeco(State);

    /// <summary>Recomeça as fases, mas guarda o que vale por dia e as travas do turbo, que só somem com o tempo ou
    /// quando dentro baixa: pausa, ausência ou outra regra no comando não podem liberar outro turbo.</summary>
    private static ConfortoState Recomeco(ConfortoState s) => new()
    {
        AntecipouEm = s.AntecipouEm,
        UltimoAvisoFora = s.UltimoAvisoFora,
        TurboEsgotado = s.TurboEsgotado,
        SensorParadoEm = s.SensorParadoEm,
        TurboSerieC = s.TurboSerieC,
        Turbos = [.. s.Turbos],
    };

    public override RuleOutput Evaluate(RuleContext ctx)
    {
        var o = new RuleOutput();
        if (!ctx.Devices.TryGetValue(Cfg.DeviceId, out var dev)) return o;
        // Aquecimento ligado por você, com "Aquecer" desligado na regra, não é trocado por frio ou ventilar.
        if (dev.On && dev.Mode == AcMode.Heat && !Cfg.AllowHeat)
        {
            Reiniciar();
            return o.Observe(Cfg.DeviceId, new RuleStatus($"{Name} em espera", "Você ligou em Quente. O Conforto não mexe no aquecimento.", null), waiting: true);
        }
        if (ctx.IsBlocked(Cfg.DeviceId))
        {
            // Bloqueado, a fase avançaria sem comando (aviso falso, turbo e antecipação gastos à toa).
            // Avalia do zero só para mostrar o que faria, e descarta o estado, os avisos e o rastro
            // (a prévia sempre parte de Parado e logaria a mesma troca de fase a cada ciclo).
            // Na saída curta do PC, guarda tudo: um ciclo de leitura velha não pode fazer a volta reenviar comandos.
            var saidaCurta = Cfg.RequirePresence && ctx.Presence.Away && ctx.Presence.AwayFor <= SaidaCurta;
            var inteiro = saidaCurta ? SaveState() : null;
            var guardado = Recomeco(State);
            Reiniciar();
            var previa = Avaliar(o, ctx, dev);
            previa.Notifications.Clear();
            previa.Observed.Clear();
            previa.Traces.Clear();
            if (inteiro is null) State = guardado;
            else
            {
                LoadState(inteiro);
                AcompanharLonge(ctx, dev);
            }
            return previa;
        }
        return Avaliar(o, ctx, dev);
    }

    private RuleOutput Avaliar(RuleOutput o, RuleContext ctx, DeviceSnapshot dev)
    {
        var naAgenda = (Cfg.Schedule ?? Schedule.Always).Contains(ctx.LocalNow);
        if (!naAgenda || (Cfg.RequirePresence && ctx.Presence.Away))
            return Sair(o, ctx, dev, naAgenda ? "ninguém no PC" : "fora da agenda");
        State.SairDoTurbo = false;
        if (dev.AmbientC is not { } amb) return Reivindicar(o, ctx, dev, null);

        var lacuna = Lacuna > ctx.MaxReadingAge * 2 ? Lacuna : ctx.MaxReadingAge * 2;
        if (State.UltimaLeitura is not { } ultima || dev.ReadAt - ultima > lacuna)
        {
            if (State.Fase != ConfortoFase.Parado || State.Acima.Desde is not null || State.NoPiso.Desde is not null)
                o.Trace(Cfg.DeviceId, State.UltimaLeitura is { } u
                    ? $"recomeça: {(dev.ReadAt - u).TotalMinutes:0} min sem leitura (fase {State.Fase})"
                    : $"recomeça: estado sem hora da última leitura (fase {State.Fase})");
            Reiniciar();
        }
        // O ciclo roda a cada poucos segundos com a mesma leitura: só leitura nova conta como "do outro lado".
        if (State.UltimaLeitura != dev.ReadAt) Acompanhar(dev.ReadAt, amb);
        State.UltimaLeitura = dev.ReadAt;

        var antes = State.Fase;
        var antecipou = false;
        State.Fase = Proxima(ctx, dev, amb, ref antecipou);
        var liga = antes == ConfortoFase.Parado && !dev.On && State.Fase is ConfortoFase.Manter or ConfortoFase.Turbo;
        if (liga && !antecipou && AdiarPorFora(o, ctx, dev, amb))
        {
            State.Fase = antes;
            liga = false;
        }
        if (State.Fase != antes) o.Trace(Cfg.DeviceId, $"fase {antes} → {State.Fase}: dentro {amb} °C, {(dev.On ? "ligado" : "desligado")} em {dev.Mode}");
        Proteger(o, ctx, dev, amb);

        ContarTurbo(ctx.Now, amb, State.Fase == ConfortoFase.Turbo && State.ProtegeAte is null);
        if (State.Fase != ConfortoFase.Turbo) State.AvisouTurbo = false;
        if (State.Fase is ConfortoFase.Parado or ConfortoFase.Ventilar) State.AvisouLigar = false;

        if (State.ProtegeAte is null)
        {
            if (State.Fase == ConfortoFase.Turbo && !State.AvisouTurbo)
            {
                State.AvisouTurbo = true;
                o.Notify("Conforto: turbo", $"{dev.Name} em {amb} °C. Turbo até {Cfg.MaxC} °C, por no máximo {TurboRestante(ctx.Now).TotalMinutes:0} min.", Cfg.DeviceId);
            }
            else if (State.Fase == ConfortoFase.Manter && !dev.On && !State.AvisouLigar)
            {
                State.AvisouLigar = true;
                o.Notify("Conforto ligou o ar", antecipou || (State.AntecipouEm == DateOnly.FromDateTime(ctx.LocalNow) && amb <= Cfg.MaxC)
                    ? $"{dev.Name}: o calor forte chega em até {Cfg.PreCoolLeadMin} min. Frio econômico em {Cfg.MaxC} °C."
                    : $"{dev.Name}: dentro em {amb} °C, acima de {Cfg.MaxC} °C. Frio econômico em {Cfg.MaxC} °C.", Cfg.DeviceId);
            }
        }
        if (!liga) AvisarForaMaisFresco(o, ctx, dev, amb);
        return Reivindicar(o, ctx, dev, amb);
    }

    /// <summary>Marca desde quando cada condição vale, pelo horário da leitura que a mostrou.</summary>
    private void Acompanhar(DateTimeOffset leitura, int amb)
    {
        State.Acima.Ler(amb > Cfg.MaxC, leitura);
        State.Quente.Ler(amb >= Cfg.TurboFromC, leitura);
        State.NoPiso.Ler(amb <= Cfg.MinC, leitura);
        State.Abaixo.Ler(amb < Cfg.MinC, leitura);
        State.Morno.Ler(amb > Cfg.MinC, leitura);
        if (State.UltimaAmb is { } u && amb - u >= SaltoC && State.UltimaLeitura is { } t && leitura - t <= TimeSpan.FromMinutes(2))
            State.SaltoEm = leitura;
        State.UltimaAmb = amb;
        if (State.Acima.Desde is null) (State.SaltoEm, State.ForaAdiaAte) = (null, null);
        if (amb <= Cfg.MaxC) (State.TurboEsgotado, State.TurboSerieC) = (false, null);
        if (State.SensorParadoEm is { } p && p != amb) State.SensorParadoEm = null;
    }

    /// <summary>Ausência ou fim da agenda: solta o aparelho, mas não o deixa em turbo sem prazo.</summary>
    /// <remarks>
    /// Saída curta guarda a fase e segue acompanhando as leituras: quem volta continua de onde parou, sem refazer
    /// os comandos (cada um apita no aparelho). Ausência mais longa recomeça a cada ciclo.
    /// </remarks>
    private RuleOutput Sair(RuleOutput o, RuleContext ctx, DeviceSnapshot dev, string motivo)
    {
        var estavaNoTurbo = State.Fase == ConfortoFase.Turbo || State.SairDoTurbo;
        if (motivo == "ninguém no PC" && ctx.Presence.AwayFor <= SaidaCurta)
            AcompanharLonge(ctx, dev);
        else Reiniciar();
        State.SairDoTurbo = estavaNoTurbo && dev.On && dev.Fan == FanSpeed.Turbo;
        if (!State.SairDoTurbo)
            return o.Observe(Cfg.DeviceId, new RuleStatus($"{Name} em espera",
                motivo == "ninguém no PC" ? "Você está longe do PC. Volta a agir quando você voltar." : "Fora do horário definido na regra.", null),
                waiting: true);
        o.Claim(Cfg.DeviceId, new SetFan(FanSpeed.Auto));
        o.Status = new RuleStatus(Name, motivo == "ninguém no PC"
            ? "Você saiu do PC: tirando a ventilação do turbo."
            : "Fim do horário da regra: tirando a ventilação do turbo.", null);
        return o;
    }

    /// <summary>Saída curta: fecha o turbo e segue marcando as leituras, sem trocar de fase.</summary>
    private void AcompanharLonge(RuleContext ctx, DeviceSnapshot dev)
    {
        ContarTurbo(ctx.Now, 0, segue: false);
        if (dev.AmbientC is { } amb && State.UltimaLeitura != dev.ReadAt) Acompanhar(dev.ReadAt, amb);
        State.UltimaLeitura = dev.ReadAt;
    }

    private ConfortoFase Proxima(RuleContext ctx, DeviceSnapshot dev, int amb, ref bool antecipou)
    {
        var now = ctx.Now;
        // Exige uma leitura posterior à que começou a contar e a leitura atual do mesmo lado.
        bool Por(Marca m, TimeSpan t) => m.Desde is { } d && m.Contra == 0 && now - d >= t && dev.ReadAt > d;
        // Ligar só pela passagem a partir de Parado: fase de ar ligado com o ar desligado não religa sozinha.
        var fase = dev.On ? State.Fase : ConfortoFase.Parado;

        if (fase == ConfortoFase.Turbo)
        {
            var noTeto = TurboRestante(now) <= TimeSpan.Zero;
            if (amb > Cfg.MaxC && !noTeto) return ConfortoFase.Turbo;
            State.TurboEsgotado = amb > Cfg.MaxC;
            if (noTeto && State.TurboSerieC is { } inicio && amb >= inicio) State.SensorParadoEm = amb;
            return amb <= Cfg.MinC ? Piso(amb) : ConfortoFase.Manter;
        }
        // Aquecendo (só com "Aquecer" ligado): sai para ventilar ou desligar, nunca direto para o frio.
        if (dev.On && dev.Mode == AcMode.Heat)
        {
            if (fase != ConfortoFase.Aquecer) return amb <= Cfg.MinC ? ConfortoFase.Aquecer : SemAquecer();
            return Por(State.Morno, Confirmacao) ? SemAquecer() : ConfortoFase.Aquecer;
        }
        if (!State.TurboEsgotado && TurboRestante(now) >= TurboMinimo && Por(State.Quente, EsperaAcima(amb)))
            return ConfortoFase.Turbo;
        if (Por(State.Acima, EsperaAcima(amb))) return ConfortoFase.Manter;
        // Aparelho já ligado (por você ou antes do app abrir): assume sem esperar a confirmação.
        // Já ventilando dentro da faixa, segue ventilando: não liga o compressor sem estar acima da máxima.
        if (fase == ConfortoFase.Parado && dev.On)
            return amb <= Cfg.MinC || (amb <= Cfg.MaxC && dev.Mode == AcMode.FanOnly && Cfg.FanAtMin) ? Piso(amb) : ConfortoFase.Manter;
        if (fase == ConfortoFase.Manter && Por(State.NoPiso, ConfirmacaoPiso)) return Piso(amb);
        if (fase is ConfortoFase.Ventilar or ConfortoFase.Parado && Cfg.AllowHeat && Por(State.Abaixo, Confirmacao))
            return ConfortoFase.Aquecer;
        if (fase == ConfortoFase.Parado && amb >= Cfg.MaxC && CalorChegando(ctx))
        {
            State.AntecipouEm = DateOnly.FromDateTime(ctx.LocalNow);
            antecipou = true;
            return ConfortoFase.Manter;
        }
        return fase;
    }

    private TimeSpan EsperaAcima(int amb) =>
        amb >= Cfg.MaxC + 2 && !(State.SaltoEm is { } s && State.Acima.Desde is { } d && s >= d) ? ConfirmacaoForte : Confirmacao;

    /// <summary>"acima de 24 °C desde 15:59. Se continuar até 16:01," enquanto a subida não confirmou.</summary>
    private string? AcimaAte(RuleContext ctx, int? amb) =>
        amb is { } a && a > Cfg.MaxC && State.Acima.Desde is { } d
            ? $"acima de {Cfg.MaxC} °C desde {Hm(ctx, d)}. Se continuar até {Hm(ctx, d + EsperaAcima(a))},"
            : null;

    private ConfortoFase Piso(int amb) =>
        Cfg.AllowHeat && amb < Cfg.MinC ? ConfortoFase.Aquecer : SemAquecer();

    private ConfortoFase SemAquecer() => Cfg.FanAtMin ? ConfortoFase.Ventilar : ConfortoFase.Parado;

    /// <summary>Parado com o ar ligado na mínima, ou aquecendo acima dela: desliga.</summary>
    private bool DesligaParado(DeviceSnapshot dev, int? amb) =>
        dev.On && amb is { } a && (a <= Cfg.MinC || dev.Mode == AcMode.Heat);

    public TimeSpan TurboUsado(DateTimeOffset now)
    {
        var soma = TimeSpan.Zero;
        foreach (var u in State.Turbos)
        {
            var ini = u.Inicio > now - JanelaTurbo ? u.Inicio : now - JanelaTurbo;
            // O trecho em andamento vai até agora, não até a última avaliação.
            var fim = u.Inicio == State.TurboDesde || u.Fim > now ? now : u.Fim;
            if (fim > ini) soma += fim - ini;
        }
        return soma;
    }

    private TimeSpan TurboRestante(DateTimeOffset now) => TimeSpan.FromMinutes(Cfg.MaxTurboMin) - TurboUsado(now);

    /// <summary>Estende o trecho de turbo aberto até agora; fecha ao sair, abre ao entrar.</summary>
    private void ContarTurbo(DateTimeOffset now, int amb, bool segue)
    {
        if (State.TurboDesde is { } aberto && State.Turbos.Count > 0 && State.Turbos[^1].Inicio == aberto)
            State.Turbos[^1] = State.Turbos[^1] with { Fim = now };
        State.Turbos.RemoveAll(u => u.Fim < now - JanelaTurbo);
        if (State.Turbos.Count == 0) State.TurboSerieC = null;
        if (!segue)
        {
            (State.TurboDesde, State.TurboInicioC) = (null, null);
            return;
        }
        if (State.TurboDesde is not null) return;
        (State.TurboDesde, State.TurboInicioC) = (now, amb);
        State.TurboSerieC ??= amb;
        State.Turbos.Add(new TurboUso(now, now));
    }

    private static bool CompressorLigado(bool on, AcMode mode) => on && mode is AcMode.Cool or AcMode.Heat or AcMode.Auto or AcMode.Dry;

    /// <summary>Segura a troca do compressor (ligar ou desligar) até <see cref="CicloMinimo"/> depois da última.</summary>
    private void Proteger(RuleOutput o, RuleContext ctx, DeviceSnapshot dev, int amb)
    {
        bool? pedido = State.Fase switch
        {
            ConfortoFase.Turbo or ConfortoFase.Manter or ConfortoFase.Aquecer => true,
            ConfortoFase.Ventilar => false,
            _ => DesligaParado(dev, amb) ? false : null,
        };
        var agora = CompressorLigado(dev.On, dev.Mode);
        DateTimeOffset? ate = null;
        if (pedido is { } p && p != agora)
        {
            // Atrás até a lacuna: com o aparelho sem conexão, a troca conta a partir da primeira leitura que a mostrou.
            var amostras = ctx.History.Samples(Cfg.DeviceId, ctx.Now - CicloMinimo - Lacuna).Where(s => s.Connected).ToList();
            var oposta = amostras.LastOrDefault(s => CompressorLigado(s.On, s.Mode) != agora);
            if (oposta is not null)
            {
                var mudou = amostras.FirstOrDefault(s => s.Time > oposta.Time)?.Time ?? dev.ReadAt;
                if (ctx.Now < mudou + CicloMinimo) ate = mudou + CicloMinimo;
            }
        }
        if (ate is { } a && State.ProtegeAte is null)
            o.Trace(Cfg.DeviceId, $"compressor {(agora ? "ligado" : "desligado")} há pouco: {(agora ? "desligar" : "ligar")} espera até {Hm(ctx, a)}");
        State.ProtegeAte = ate;
    }

    /// <summary>Previsão de sensação térmica acima do limite dentro da antecedência; uma vez por dia.</summary>
    private bool CalorChegando(RuleContext ctx)
    {
        if (ctx.Weather is not { } w || State.AntecipouEm == DateOnly.FromDateTime(ctx.LocalNow)) return false;
        var limite = ctx.Now + TimeSpan.FromMinutes(Cfg.PreCoolLeadMin);
        return w.Hourly.Any(h => h.Time > ctx.Now && h.Time <= limite && (h.ApparentC ?? h.TempC) >= Cfg.PreCoolApparentC);
    }

    /// <summary>Ar de fora mais frio pela diferença configurada e seco o bastante para refrescar; das 7h às 22h.</summary>
    private bool ForaMaisFresco(RuleContext ctx, int amb, out WeatherSnapshot clima)
    {
        clima = ctx.Weather!;
        if (ctx.Weather is not { } w) return false;
        var hora = ctx.LocalNow.Hour;
        return hora is >= 7 and < 22 && w.TempC <= amb - Cfg.CoolerOutsideC && (w.DewPointC is not { } orvalho || orvalho <= OrvalhoMaxC);
    }

    /// <summary>Antes de ligar com fora mais fresco: avisa e espera <see cref="AdiaPorFora"/> para dar tempo de abrir a janela.</summary>
    private bool AdiarPorFora(RuleOutput o, RuleContext ctx, DeviceSnapshot dev, int amb)
    {
        if (!ForaMaisFresco(ctx, amb, out var w)) return false;
        if (State.ForaAdiaAte is { } ate) return ctx.Now < ate;
        ate = ctx.Now + AdiaPorFora;
        State.ForaAdiaAte = ate;
        State.UltimoAvisoFora = ctx.Now;
        o.Trace(Cfg.DeviceId, $"ligar adiado até {Hm(ctx, ate)}: lá fora {w.TempC:0.#} °C");
        o.Notify("Lá fora está mais fresco",
            $"Lá fora {w.TempC:0.#} °C, {dev.Name} em {amb} °C. Abra a janela: se dentro continuar acima de {Cfg.MaxC} °C até {Hm(ctx, ate)}, o ar liga.");
        return true;
    }

    private void AvisarForaMaisFresco(RuleOutput o, RuleContext ctx, DeviceSnapshot dev, int amb)
    {
        if (State.Fase is not (ConfortoFase.Manter or ConfortoFase.Turbo) || !ForaMaisFresco(ctx, amb, out var w)) return;
        if (State.UltimoAvisoFora is { } u && ctx.Now - u < AvisoForaCarencia) return;
        State.UltimoAvisoFora = ctx.Now;
        o.Notify("Lá fora está mais fresco",
            $"Lá fora {w.TempC:0.#} °C, {dev.Name} em {amb} °C. Abrir a janela e desligar o ar resfria sem gastar energia.");
    }

    private RuleOutput Reivindicar(RuleOutput o, RuleContext ctx, DeviceSnapshot dev, int? amb)
    {
        var dentro = amb is { } x ? $"Dentro está {x} °C" : "Sem leitura de dentro";
        if (State.ProtegeAte is { } protege)
        {
            var acao = CompressorLigado(dev.On, dev.Mode) ? "desliga o compressor" : "liga o compressor";
            return o.Observe(Cfg.DeviceId, new RuleStatus($"{Name} de olho",
                $"{dentro}. O compressor mudou há pouco: {acao} às {Hm(ctx, protege)}, para poupar o aparelho.", null));
        }
        switch (State.Fase)
        {
            case ConfortoFase.Turbo:
                o.Claim(Cfg.DeviceId, new Power(true), new SetMode(AcMode.Cool), new SetFan(FanSpeed.Turbo), new SetTarget(Cfg.MinC));
                o.Status = new RuleStatus($"{Name} · turbo",
                    $"{dentro}, calor forte. Turbo até chegar a {Cfg.MaxC} °C, no máximo até {Hm(ctx, ctx.Now + TurboRestante(ctx.Now))}.", null);
                break;
            case ConfortoFase.Manter:
                o.Claim(Cfg.DeviceId, new Power(true), new SetMode(AcMode.Cool), new SetFan(FanSpeed.Auto),
                    new SetEnergySaving(true), new SetTarget(Cfg.MaxC));
                var piso = Cfg.FanAtMin ? "só ventila" : "desliga";
                o.Status = new RuleStatus(Name, State.SensorParadoEm is { } travado
                    ? $"O sensor do aparelho marca {travado} °C mesmo após {Cfg.MaxTurboMin} min de turbo; pode estar medindo perto do teto. Mantendo o modo econômico em {Cfg.MaxC} °C, sem novo turbo."
                    : amb > Cfg.MaxC
                    ? $"{dentro}, acima de {Cfg.MaxC} °C. Esfriando no modo econômico até {Cfg.MaxC} °C."
                    : $"{dentro}. Mantendo {Cfg.MaxC} °C no modo econômico; ao chegar a {Cfg.MinC} °C, {piso}.", null);
                break;
            case ConfortoFase.Ventilar:
                o.Claim(Cfg.DeviceId, new Power(true), new SetMode(AcMode.FanOnly), new SetFan(FanSpeed.Low));
                o.Status = new RuleStatus(Name, AcimaAte(ctx, amb) is { } subiu
                    ? $"{dentro}, {subiu} volta a esfriar."
                    : $"{dentro}. Compressor desligado, só ventilando para economizar; volta a esfriar acima de {Cfg.MaxC} °C.", null);
                break;
            case ConfortoFase.Aquecer:
                o.Claim(Cfg.DeviceId, new Power(true), new SetMode(AcMode.Heat), new SetFan(FanSpeed.Auto), new SetTarget(Cfg.MinC));
                o.Status = new RuleStatus(Name, $"{dentro}, abaixo de {Cfg.MinC} °C. Aquecendo até {Cfg.MinC} °C.", null);
                break;
            case ConfortoFase.Parado when DesligaParado(dev, amb):
                o.Claim(Cfg.DeviceId, new Power(false));
                o.Status = new RuleStatus(Name, dev.Mode == AcMode.Heat && amb > Cfg.MinC
                    ? $"{dentro}, acima de {Cfg.MinC} °C. Desligando o aquecimento."
                    : $"{dentro}, na mínima. Desligando o ar para economizar.", null);
                break;
            case ConfortoFase.Parado when amb is { } a && State.ForaAdiaAte is { } adia && ctx.Now < adia:
                o.Observe(Cfg.DeviceId, new RuleStatus($"{Name} de olho",
                    $"Dentro está {a} °C e lá fora está mais fresco. Se abrir a janela não resolver, liga às {Hm(ctx, adia)}.", null));
                break;
            case ConfortoFase.Parado when amb is { } a:
                var espera = $"{Confirmacao.TotalMinutes:0} min";
                var onde = a > Cfg.MaxC ? $"acima de {Cfg.MaxC} °C" : a >= Cfg.MinC ? "na faixa" : "abaixo da faixa";
                o.Observe(Cfg.DeviceId, new RuleStatus($"{Name} de olho", AcimaAte(ctx, a) is { } subiuAte
                    ? $"Dentro está {a} °C, {subiuAte} liga no modo econômico."
                    : $"Dentro está {a} °C, {onde}. Liga se passar de {Cfg.MaxC} °C por {espera}.", null));
                break;
        }
        return o;
    }
}

public enum SaidaEstagio { Presente, Economia, Desligado }

public sealed class SaiDoPcState
{
    public SaidaEstagio Estagio { get; set; }
    public bool? EstavaLigado { get; set; }
    public AcMode? Modo { get; set; }
    public FanSpeed? Ventilacao { get; set; }
    public int? Alvo { get; set; }
    public DateTimeOffset? VoltouEm { get; set; }
    public bool Ausente { get; set; }
    /// <summary>Você mexeu no ar durante a ausência: o Ausente não age até você voltar.</summary>
    public bool Liberado { get; set; }
}

/// <summary>Longe do PC: tela bloqueada desliga (na hora ou após N min); sem uso, alivia e depois desliga.</summary>
/// <remarks>Na volta, restaura o ajuste anterior só se configurado; senão as outras regras (Conforto) decidem.</remarks>
public sealed class SaiDoPcRule(SaiDoPcConfig cfg) : Rule<SaiDoPcConfig, SaiDoPcState>(cfg)
{
    public static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(15);

    public override string Name => "Ausente";
    public override int Priority => RulePriority.SaiDoPc;

    public override void OnManualChange(string deviceId)
    {
        if (deviceId == Cfg.DeviceId && (State.Ausente || State.Estagio != SaidaEstagio.Presente)) State.Liberado = true;
    }

    public override RuleOutput Evaluate(RuleContext ctx)
    {
        var o = new RuleOutput();
        if (!ctx.Devices.TryGetValue(Cfg.DeviceId, out var dev)) return o;
        var p = ctx.Presence;
        var ausente = p.Away ? p.AwayFor : TimeSpan.Zero;
        State.Ausente = p.Away;

        if (!p.Away)
        {
            if (State.Estagio == SaidaEstagio.Presente && !State.Liberado) return o;
            if (State.EstavaLigado != true || !Cfg.RestoreOnReturn || State.Liberado) { State = new(); return o; }
            // Mantém o pedido até o aparelho bater com o ajuste salvo: um ciclo bloqueado não pode perder a restauração.
            State.VoltouEm ??= ctx.Now;
            var restaurar = Restaurar(State);
            if (restaurar.All(c => c.IsSatisfiedBy(dev)))
            {
                State = new();
                o.Notify("Bem-vindo de volta", $"{dev.Name} voltou ao ajuste anterior.");
                return o;
            }
            if (ctx.Now - State.VoltouEm.Value > RestoreTimeout) { State = new(); return o; }
            o.Claim(Cfg.DeviceId, restaurar);
            o.Status = new RuleStatus(Name, "Você voltou. Devolvendo o ajuste que estava antes.", null);
            return o;
        }

        State.VoltouEm = null;
        if (State.Liberado)
            return o.Observe(Cfg.DeviceId, new RuleStatus($"{Name} em espera",
                "Você ajustou o ar durante a ausência. O Ausente volta a agir depois que você voltar ao PC.", null), waiting: true);
        var bloqueou = p.Locked && Cfg.OffOnLock && ausente >= TimeSpan.FromMinutes(Cfg.OffOnLockMin);
        if (State.Estagio == SaidaEstagio.Presente)
        {
            if (!dev.On || (!bloqueou && ausente < TimeSpan.FromMinutes(Cfg.EcoAfterMin))) return o;
            State = new SaiDoPcState
            {
                Estagio = SaidaEstagio.Economia, EstavaLigado = true,
                Modo = dev.Mode, Ventilacao = dev.Fan, Alvo = dev.TargetC,
            };
        }
        if (State.Estagio == SaidaEstagio.Economia && (bloqueou || ausente >= TimeSpan.FromMinutes(Cfg.OffAfterMin)))
            State.Estagio = SaidaEstagio.Desligado;

        var motivo = p.Locked ? "Tela bloqueada" : $"{(int)ausente.TotalMinutes} min sem uso do PC";
        if (State.Estagio == SaidaEstagio.Desligado)
        {
            o.Claim(Cfg.DeviceId, new Power(false));
            var volta = Cfg.RestoreOnReturn ? "ele volta como estava" : "as regras ativas decidem se ele liga";
            o.Status = new RuleStatus(Name, $"{motivo}: ar desligado. Quando você voltar, {volta}.", null);
        }
        // Aliviar subindo o alvo só serve esfriando; aquecendo ou só ventilando (trocar por Frio ligaria o
        // compressor), espera a hora de desligar.
        else if (State.Modo is AcMode.Heat or AcMode.FanOnly)
            o.Observe(Cfg.DeviceId, new RuleStatus(Name, $"{motivo}: desliga com {Cfg.OffAfterMin} min sem uso.", null));
        else
        {
            DeviceCommand[] aliviar = State.Ventilacao == FanSpeed.Turbo
                ? [new Power(true), new SetMode(AcMode.Cool), new SetTarget(Cfg.EcoTargetC), new SetFan(FanSpeed.Auto)]
                : [new Power(true), new SetMode(AcMode.Cool), new SetTarget(Cfg.EcoTargetC)];
            o.Claim(Cfg.DeviceId, aliviar);
            o.Status = new RuleStatus(Name,
                $"{motivo}: alvo em {Cfg.EcoTargetC} °C para economizar. Desliga com {Cfg.OffAfterMin} min sem uso.", null);
        }
        return o;
    }

    private static DeviceCommand[] Restaurar(SaiDoPcState s)
    {
        var cmds = new List<DeviceCommand> { new Power(true) };
        if (s.Modo is { } m and not AcMode.Unknown) cmds.Add(new SetMode(m));
        if (s.Ventilacao is { } f and not FanSpeed.Unknown) cmds.Add(new SetFan(f));
        if (s.Alvo is { } t && s.Modo != AcMode.FanOnly) cmds.Add(new SetTarget(t));
        return [.. cmds];
    }
}

public sealed class JanelaAbertaState
{
    public DateTimeOffset? DisparouEm { get; set; }
    public int? De { get; set; }
    public int? Para { get; set; }
}

/// <summary>Em Frio, dentro subindo rápido e sem voltar indica janela/porta aberta: desliga e avisa.</summary>
/// <remarks>
/// O sensor salta um grau ou dois numa leitura isolada: a subida precisa durar <see cref="Sustentacao"/>.
/// Ajuste manual, ou dentro voltando a cair com o ar ainda ligado, cancela o disparo.
/// </remarks>
public sealed class JanelaAbertaRule(JanelaAbertaConfig cfg) : Rule<JanelaAbertaConfig, JanelaAbertaState>(cfg)
{
    public static readonly TimeSpan Sustentacao = TimeSpan.FromMinutes(3);

    public override string Name => "Janela aberta";
    public override int Priority => RulePriority.JanelaAberta;

    public override void OnManualChange(string deviceId)
    {
        if (deviceId == Cfg.DeviceId) State = new();
    }

    public override RuleOutput Evaluate(RuleContext ctx)
    {
        var o = new RuleOutput();
        if (!ctx.Devices.TryGetValue(Cfg.DeviceId, out var dev)) return o;

        if (State.DisparouEm is { } quando)
        {
            var esfriouDeNovo = dev.On && dev.AmbientC is { } a && State.Para is { } para && a < para;
            if (ctx.Now - quando < TimeSpan.FromMinutes(Cfg.CooldownMin) && !esfriouDeNovo)
                return Desligar(o, ctx, quando);
            State = new();
        }

        // Em pausa manual não dispara: o desligamento sairia só no fim da pausa, com a janela talvez já fechada.
        if (!dev.On || dev.Mode != AcMode.Cool || dev.AmbientC is not { } agora || ctx.IsBlocked(Cfg.DeviceId)) return o;
        // Alvo no nível de dentro ou acima (alívio do Ausente, Modo noite) deixa dentro subir sem janela aberta.
        if (dev.TargetC is { } alvo && alvo >= agora) return o;
        var amostras = ctx.History.Samples(Cfg.DeviceId, ctx.Now.AddMinutes(-Math.Max(Cfg.WindowMin, Cfg.MinOnMin) - 10));
        var ligadoDesde = amostras.Reverse().TakeWhile(s => s.On && s.Mode == AcMode.Cool).LastOrDefault()?.Time;
        if (ligadoDesde is null || ctx.Now - ligadoDesde < TimeSpan.FromMinutes(Cfg.MinOnMin)) return o;

        var janela = amostras.Where(s => s.Time >= ctx.Now.AddMinutes(-Cfg.WindowMin) && s.AmbientC is not null).ToList();
        if (janela.Count == 0 || janela.Any(s => s.TargetC != dev.TargetC)) return o;
        var minimo = janela.Min(s => s.AmbientC!.Value);
        if (agora - minimo < Cfg.RiseC) return o;
        // Pelo menos duas leituras altas: com leitura a cada 5 min, uma só ficaria "sustentada" pelo relógio.
        var alta = janela.AsEnumerable().Reverse().TakeWhile(s => s.AmbientC - minimo >= Cfg.RiseC).ToList();
        if (alta.Count < 2 || ctx.Now - alta[^1].Time < Sustentacao) return o;

        State = new JanelaAbertaState { DisparouEm = ctx.Now, De = minimo, Para = agora };
        o.Notify("Janela aberta?", $"{dev.Name} subiu de {minimo} para {agora} °C em até {Cfg.WindowMin} min com o ar no frio. Desliguei.", Cfg.DeviceId);
        return Desligar(o, ctx, ctx.Now);
    }

    private RuleOutput Desligar(RuleOutput o, RuleContext ctx, DateTimeOffset quando)
    {
        o.Claim(Cfg.DeviceId, new Power(false));
        var ate = Hm(ctx, quando.AddMinutes(Cfg.CooldownMin));
        o.Status = new RuleStatus(Name, $"Dentro subiu de {State.De} para {State.Para} °C com o ar no frio: janela ou porta aberta? Ar desligado até {ate} ou até você religar.", null);
        return o;
    }
}

public sealed class ModoNoiteState
{
    public DateTimeOffset? Inicio { get; set; }
    public int? AlvoBase { get; set; }
    public bool TimerEnviado { get; set; }
    /// <summary>O timer no aparelho bate com o fim do modo noite; só esse é zerado ao cancelar.</summary>
    public bool TimerDoModoNoite { get; set; }
    /// <summary>Timer (s) da avaliação anterior e a hora da leitura que o mostrou: base para ver mudança feita fora do app.</summary>
    public int? TimerVisto { get; set; }
    public DateTimeOffset? TimerVistoEm { get; set; }
    public DateOnly? UltimoDiaAgendado { get; set; }
}

/// <summary>Programa o timer do aparelho e sobe a temperatura aos poucos durante a noite.</summary>
public sealed class ModoNoiteRule(ModoNoiteConfig cfg) : Rule<ModoNoiteConfig, ModoNoiteState>(cfg)
{
    public override string Name => "Modo noite";
    public override int Priority => RulePriority.ModoNoite;

    public bool Active => State.Inicio is not null;

    public void Activate(DateTimeOffset now, int? alvoAtual) =>
        State = new ModoNoiteState { Inicio = now, AlvoBase = alvoAtual, UltimoDiaAgendado = State.UltimoDiaAgendado };

    public void Cancel() => State = new ModoNoiteState { UltimoDiaAgendado = State.UltimoDiaAgendado };

    /// <summary>Você mexeu no timer: vale até a próxima noite, o modo noite não programa outro.</summary>
    public void TimerDoUsuario()
    {
        if (!Active) return;
        State.TimerEnviado = true;
        State.TimerDoModoNoite = false;
    }

    /// <summary>Timer do modo noite mudado pelo controle ou pelo celular: passa a ser do usuário.</summary>
    /// <remarks>
    /// Não se sabe se o stopTime da API conta para baixo ou fica no valor programado: aceita os dois. Zerado
    /// longe do fim, ou longe dos dois valores esperados, foi mudança de fora.
    /// </remarks>
    private void ConferirTimer(RuleOutput o, DeviceSnapshot dev, TimeSpan restanteAteFim)
    {
        if (!dev.Connected) return;
        var st = dev.StopTimeSeconds ?? 0;
        if (State.TimerDoModoNoite && State.TimerVisto is { } antes && State.TimerVistoEm is { } em && dev.ReadAt > em)
        {
            var passou = (int)(dev.ReadAt - em).TotalSeconds;
            bool Perto(int esperado) => Math.Abs(st - esperado) <= 120;
            if (st == 0 ? restanteAteFim > TimeSpan.FromMinutes(5) : !Perto(antes) && !Perto(antes - passou))
            {
                o.Trace(Cfg.DeviceId, $"timer mudado fora do app: {antes / 60} → {st / 60} min; o modo noite não reprograma");
                TimerDoUsuario();
            }
        }
        // Valor repetido (timer fixo ou o otimista do envio, que dura minutos) não move a base da contagem.
        if (st != State.TimerVisto) (State.TimerVisto, State.TimerVistoEm) = (st, dev.ReadAt);
    }

    public override RuleOutput Evaluate(RuleContext ctx)
    {
        var o = new RuleOutput();
        if (!ctx.Devices.TryGetValue(Cfg.DeviceId, out var dev)) return o;

        var hoje = DateOnly.FromDateTime(ctx.LocalNow);
        if (!Active && Cfg.DailyStart is { } h && State.UltimoDiaAgendado != hoje
            && TimeOnly.FromDateTime(ctx.LocalNow) is var t && t >= h && t < h.AddMinutes(30))
        {
            State.UltimoDiaAgendado = hoje;
            if (dev.On) Activate(ctx.Now, dev.TargetC);
        }
        if (!Active) return o;

        var decorrido = ctx.Now - State.Inicio!.Value;
        // Conta como enviado quando o estado (otimista ou lido) mostra um timer que termina até 1 h depois do fim;
        // um timer mais longo, do próprio usuário, não desliga a tempo.
        var restanteAteFim = TimeSpan.FromHours(Cfg.DurationHours) - decorrido;
        if (dev.StopTimeSeconds is > 0 and var st && TimeSpan.FromSeconds(st) <= restanteAteFim + TimeSpan.FromHours(1))
        {
            State.TimerEnviado = true;
            // O pedido é o restante arredondado para cima em horas; um timer bem mais curto foi o usuário que pôs.
            State.TimerDoModoNoite |= TimeSpan.FromSeconds(st) >= restanteAteFim - TimeSpan.FromMinutes(15);
        }
        ConferirTimer(o, dev, restanteAteFim);

        if (decorrido >= TimeSpan.FromHours(Cfg.DurationHours))
        {
            // Timer nunca chegou ao aparelho (bloqueado ou recusado): desliga por aqui, com 30 min de tolerância.
            if (!State.TimerEnviado && dev.On && decorrido < TimeSpan.FromHours(Cfg.DurationHours) + TimeSpan.FromMinutes(30))
            {
                o.Claim(Cfg.DeviceId, new Power(false));
                o.Status = new RuleStatus(Name, "Fim do modo noite: desligando o ar.", 1);
                return o;
            }
            Cancel();
            return o;
        }
        State.AlvoBase ??= dev.TargetC ?? 24;
        var degraus = (int)(decorrido.TotalMinutes / Cfg.StepEveryMin);
        // Aquecendo, subir o alvo esquentaria o quarto a noite toda.
        var alvo = dev.Mode == AcMode.Heat ? State.AlvoBase.Value : Math.Min(Cfg.MaxTargetC, State.AlvoBase.Value + degraus * Cfg.StepC);

        var cmds = new List<DeviceCommand> { new SetTarget(alvo) };
        if (!State.TimerEnviado)
        {
            // Timer do próprio aparelho: desliga mesmo se o PC for desligado. Restante, não a duração cheia.
            var restanteH = (int)Math.Ceiling((TimeSpan.FromHours(Cfg.DurationHours) - decorrido).TotalHours);
            cmds.Add(new SetStopTimer(Math.Clamp(restanteH, 1, 24) * 3600));
        }
        o.Claim(Cfg.DeviceId, [.. cmds]);
        var restante = TimeSpan.FromHours(Cfg.DurationHours) - decorrido;
        var cada = Cfg.StepEveryMin == 60 ? "hora" : $"{Cfg.StepEveryMin} min";
        // Timer mexido por você: o Modo noite não desliga o ar, então não promete horário.
        var desliga = !State.TimerEnviado || State.TimerDoModoNoite ? $"Desliga em {restante:h\\:mm}."
            : dev.StopTimeSeconds is > 0 ? "Desliga pelo timer que você programou."
            : "Você zerou o timer: o ar não desliga sozinho.";
        o.Status = new RuleStatus(Name, dev.Mode switch
            {
                AcMode.Heat => $"Aquecendo em {alvo} °C, sem subir o alvo. {desliga}",
                AcMode.FanOnly => $"Só ventilando: sem alvo para subir. {desliga}",
                _ => $"Em {alvo} °C, subindo {Cfg.StepC} °C a cada {cada} para você não sentir frio. {desliga}",
            },
            decorrido.TotalHours / Cfg.DurationHours);
        return o;
    }
}
