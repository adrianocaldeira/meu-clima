using System.Text.Json;
using MeuClima.Core.Devices;
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

    protected static string DeviceName(RuleContext ctx, string id) =>
        ctx.Devices.TryGetValue(id, out var d) ? d.Name : id;
}

public static class RulePriority
{
    public const int JanelaAberta = 50, ModoNoite = 40, SaiDoPc = 30, EsfriarRapido = 20, AntesDoPico = 10, Aviso = 0;
}

public enum EsfriarFase { Parada, Fase1, Fase2 }

public sealed class EsfriarRapidoState
{
    public EsfriarFase Fase { get; set; }
    public DateTimeOffset? Fase1Desde { get; set; }
    public DateTimeOffset? AbaixoDesde { get; set; }
    public int? AmbienteInicial { get; set; }
}

/// <summary>Fora quente: Turbo até dentro chegar ao alvo da fase 1, depois mantém no alvo da fase 2.</summary>
public sealed class EsfriarRapidoRule(EsfriarRapidoConfig cfg) : Rule<EsfriarRapidoConfig, EsfriarRapidoState>(cfg)
{
    public override string Name => "Esfriar rápido";
    public override int Priority => RulePriority.EsfriarRapido;

    public override RuleOutput Evaluate(RuleContext ctx)
    {
        var o = new RuleOutput();
        if (!ctx.Devices.TryGetValue(Cfg.DeviceId, out var dev)) return o;
        if (ctx.Weather is null) return Hold(o, ctx, dev);
        var fora = ctx.Weather.TempC;
        var naAgenda = (Cfg.Schedule ?? Schedule.Always).Contains(ctx.LocalNow);

        if (State.Fase != EsfriarFase.Parada)
        {
            State.AbaixoDesde = fora < Cfg.StopOutsideC ? State.AbaixoDesde ?? ctx.Now : null;
            var esfriouLaFora = State.AbaixoDesde is { } desde && ctx.Now - desde >= TimeSpan.FromMinutes(Cfg.StopAfterMin);
            if (esfriouLaFora || !naAgenda)
            {
                State = new();
                return o;
            }
        }
        else if (fora >= Cfg.TriggerOutsideC && naAgenda && Presente(ctx))
        {
            State.Fase = dev.AmbientC is { } amb && amb <= Cfg.Phase1TargetC ? EsfriarFase.Fase2 : EsfriarFase.Fase1;
            State.Fase1Desde = ctx.Now;
            State.AmbienteInicial = dev.AmbientC;
            o.Notify($"{Name} começou", $"{dev.Name}: fora está {fora:0.#} °C.");
        }

        if (State.Fase == EsfriarFase.Fase1)
        {
            var estourou = ctx.Now - State.Fase1Desde >= TimeSpan.FromMinutes(Cfg.MaxBoostMin);
            if (dev.AmbientC is { } amb && amb <= Cfg.Phase1TargetC || estourou)
                State.Fase = EsfriarFase.Fase2;
        }
        return Hold(o, ctx, dev);
    }

    private bool Presente(RuleContext ctx) => !Cfg.RequirePresence || !ctx.Presence.Away;

    private RuleOutput Hold(RuleOutput o, RuleContext ctx, DeviceSnapshot? dev)
    {
        if (dev is null || State.Fase == EsfriarFase.Parada || !Presente(ctx)) return o;
        if (State.Fase == EsfriarFase.Fase1)
        {
            o.Claim(Cfg.DeviceId, new Power(true), new SetMode(AcMode.Cool), new SetFan(FanSpeed.Turbo),
                new SetTarget(Cfg.Phase1TargetC));
            double? progresso = State.AmbienteInicial is { } ini && dev.AmbientC is { } a && ini > Cfg.Phase1TargetC
                ? Math.Clamp((ini - a) / (double)(ini - Cfg.Phase1TargetC), 0, 1)
                : null;
            o.Status = new RuleStatus($"{Name} · fase 1 de 2",
                $"{dev.AmbientC} → {Cfg.Phase1TargetC} °C · depois {Cfg.Phase2TargetC} °C", progresso);
        }
        else
        {
            o.Claim(Cfg.DeviceId, new Power(true), new SetMode(Cfg.Phase2Mode), new SetFan(FanSpeed.Auto),
                new SetEnergySaving(Cfg.Phase2EnergySaving), new SetTarget(Cfg.Phase2TargetC));
            o.Status = new RuleStatus($"{Name} · fase 2 de 2", $"mantendo {Cfg.Phase2TargetC} °C", null);
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
}

/// <summary>Ausente do PC: economia após N min, desliga após M min; restaura ao voltar.</summary>
public sealed class SaiDoPcRule(SaiDoPcConfig cfg) : Rule<SaiDoPcConfig, SaiDoPcState>(cfg)
{
    public static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(15);

    public override string Name => "Saí do PC";
    public override int Priority => RulePriority.SaiDoPc;

    public override RuleOutput Evaluate(RuleContext ctx)
    {
        var o = new RuleOutput();
        if (!ctx.Devices.TryGetValue(Cfg.DeviceId, out var dev)) return o;
        var ausente = ctx.Presence.Away ? ctx.Presence.AwayFor : TimeSpan.Zero;

        if (!ctx.Presence.Away)
        {
            if (State.Estagio == SaidaEstagio.Presente) return o;
            if (State.EstavaLigado != true) { State = new(); return o; }
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
            o.Status = new RuleStatus(Name, $"{dev.Name} voltando ao ajuste anterior", null);
            return o;
        }

        State.VoltouEm = null;
        if (State.Estagio == SaidaEstagio.Presente)
        {
            if (ausente < TimeSpan.FromMinutes(Cfg.EcoAfterMin) || !dev.On) return o;
            State = new SaiDoPcState
            {
                Estagio = SaidaEstagio.Economia, EstavaLigado = true,
                Modo = dev.Mode, Ventilacao = dev.Fan, Alvo = dev.TargetC,
            };
        }
        if (State.Estagio == SaidaEstagio.Economia && ausente >= TimeSpan.FromMinutes(Cfg.OffAfterMin))
            State.Estagio = SaidaEstagio.Desligado;

        if (State.Estagio == SaidaEstagio.Economia)
        {
            o.Claim(Cfg.DeviceId, new Power(true), new SetMode(AcMode.Auto), new SetTarget(Cfg.EcoTargetC));
            o.Status = new RuleStatus(Name, $"{dev.Name} em {Cfg.EcoTargetC} °C · desliga com {Cfg.OffAfterMin} min longe", null);
        }
        else
        {
            o.Claim(Cfg.DeviceId, new Power(false));
            o.Status = new RuleStatus(Name, $"{dev.Name} desligado · volta ao desbloquear", null);
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

public sealed class AntesDoPicoState
{
    public DateOnly? Dia { get; set; }
    public DateTimeOffset? JanelaFim { get; set; }
}

/// <summary>Liga antes da primeira hora prevista acima do limite, uma vez por dia.</summary>
public sealed class AntesDoPicoRule(AntesDoPicoConfig cfg) : Rule<AntesDoPicoConfig, AntesDoPicoState>(cfg)
{
    public override string Name => "Antes do pico";
    public override int Priority => RulePriority.AntesDoPico;

    public override RuleOutput Evaluate(RuleContext ctx)
    {
        var o = new RuleOutput();
        if (!ctx.Devices.TryGetValue(Cfg.DeviceId, out var dev)) return o;
        var hoje = DateOnly.FromDateTime(ctx.LocalNow);

        if (State.Dia == hoje)
        {
            if (State.JanelaFim is { } fim && ctx.Now < fim)
            {
                o.Claim(Cfg.DeviceId, new Power(true), new SetMode(Cfg.Mode), new SetTarget(Cfg.TargetC));
                o.Status = new RuleStatus(Name, $"{dev.Name} pré-resfriando em {Cfg.TargetC} °C", null);
            }
            return o;
        }
        if (ctx.Weather is null) return o;

        var pico = ctx.Weather.Hourly
            .Where(h => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(h.Time, ctx.TimeZone).DateTime) == hoje)
            .Where(h => h.TempC >= Cfg.ForecastThresholdC)
            .OrderBy(h => h.Time)
            .FirstOrDefault();
        if (pico is null) return o;
        var inicio = pico.Time.AddMinutes(-Cfg.LeadMin);
        if (ctx.Now < inicio || ctx.Now >= pico.Time) return o;

        State.Dia = hoje;
        if (dev.On || (Cfg.RequirePresence && ctx.Presence.Away)) return o;
        State.JanelaFim = pico.Time;
        o.Claim(Cfg.DeviceId, new Power(true), new SetMode(Cfg.Mode), new SetTarget(Cfg.TargetC));
        o.Notify(Name, $"Previsão de {pico.TempC:0} °C às {TimeZoneInfo.ConvertTime(pico.Time, ctx.TimeZone):HH\\:mm}. Liguei {dev.Name} em {Cfg.TargetC} °C.");
        o.Status = new RuleStatus(Name, $"{dev.Name} pré-resfriando em {Cfg.TargetC} °C", null);
        return o;
    }
}

public sealed class JanelaAbertaState
{
    public DateTimeOffset? DisparouEm { get; set; }
}

/// <summary>Em Frio, dentro subindo rápido indica janela/porta aberta: desliga e avisa.</summary>
public sealed class JanelaAbertaRule(JanelaAbertaConfig cfg) : Rule<JanelaAbertaConfig, JanelaAbertaState>(cfg)
{
    public override string Name => "Janela aberta";
    public override int Priority => RulePriority.JanelaAberta;

    public override RuleOutput Evaluate(RuleContext ctx)
    {
        var o = new RuleOutput();
        if (!ctx.Devices.TryGetValue(Cfg.DeviceId, out var dev)) return o;

        if (State.DisparouEm is { } quando)
        {
            if (ctx.Now - quando < TimeSpan.FromMinutes(Cfg.CooldownMin))
            {
                o.Claim(Cfg.DeviceId, new Power(false));
                o.Status = new RuleStatus(Name, $"{dev.Name} desligado · janela aberta?", null);
                return o;
            }
            State.DisparouEm = null;
        }

        if (!dev.On || dev.Mode != AcMode.Cool || dev.AmbientC is not { } agora) return o;
        var amostras = ctx.History.Samples(Cfg.DeviceId, ctx.Now.AddMinutes(-Math.Max(Cfg.WindowMin, Cfg.MinOnMin) - 10));
        var ligadoDesde = amostras.Reverse().TakeWhile(s => s.On && s.Mode == AcMode.Cool).LastOrDefault()?.Time;
        if (ligadoDesde is null || ctx.Now - ligadoDesde < TimeSpan.FromMinutes(Cfg.MinOnMin)) return o;

        var janela = amostras.Where(s => s.Time >= ctx.Now.AddMinutes(-Cfg.WindowMin) && s.AmbientC is not null).ToList();
        if (janela.Count == 0) return o;
        var minimo = janela.Min(s => s.AmbientC!.Value);
        if (agora - minimo < Cfg.RiseC) return o;

        State.DisparouEm = ctx.Now;
        o.Claim(Cfg.DeviceId, new Power(false));
        o.Notify("Janela aberta?", $"{dev.Name} subiu de {minimo} para {agora} °C em {Cfg.WindowMin} min com o ar no frio. Desliguei.");
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

        if (decorrido >= TimeSpan.FromHours(Cfg.DurationHours))
        {
            // Timer nunca chegou ao aparelho (bloqueado ou recusado): desliga por aqui, com 30 min de tolerância.
            if (!State.TimerEnviado && dev.On && decorrido < TimeSpan.FromHours(Cfg.DurationHours) + TimeSpan.FromMinutes(30))
            {
                o.Claim(Cfg.DeviceId, new Power(false));
                o.Status = new RuleStatus(Name, $"{dev.Name} desligando · fim do modo noite", 1);
                return o;
            }
            Cancel();
            return o;
        }
        State.AlvoBase ??= dev.TargetC ?? 24;
        var degraus = (int)(decorrido.TotalMinutes / Cfg.StepEveryMin);
        var alvo = Math.Min(Cfg.MaxTargetC, State.AlvoBase.Value + degraus * Cfg.StepC);

        var cmds = new List<DeviceCommand> { new SetTarget(alvo) };
        if (!State.TimerEnviado)
        {
            // Timer do próprio aparelho: desliga mesmo se o PC for desligado. Restante, não a duração cheia.
            var restanteH = (int)Math.Ceiling((TimeSpan.FromHours(Cfg.DurationHours) - decorrido).TotalHours);
            cmds.Add(new SetStopTimer(Math.Clamp(restanteH, 1, 24) * 3600));
        }
        o.Claim(Cfg.DeviceId, [.. cmds]);
        var restante = TimeSpan.FromHours(Cfg.DurationHours) - decorrido;
        o.Status = new RuleStatus(Name, $"{dev.Name} em {alvo} °C · desliga em {restante:h\\:mm}",
            decorrido.TotalHours / Cfg.DurationHours);
        return o;
    }
}

public sealed class ForaMaisFrescoState
{
    public DateTimeOffset? UltimoAviso { get; set; }
}

/// <summary>Avisa quando abrir a janela resolve melhor que o ar ligado.</summary>
public sealed class ForaMaisFrescoRule(ForaMaisFrescoConfig cfg) : Rule<ForaMaisFrescoConfig, ForaMaisFrescoState>(cfg)
{
    public override string Name => "Fora mais fresco";
    public override int Priority => RulePriority.Aviso;

    public override RuleOutput Evaluate(RuleContext ctx)
    {
        var o = new RuleOutput();
        if (ctx.Weather is null) return o;
        var t = TimeOnly.FromDateTime(ctx.LocalNow);
        if (t < Cfg.FromOrDefault || t > Cfg.ToOrDefault) return o;
        if (State.UltimoAviso is { } u && ctx.Now - u < TimeSpan.FromMinutes(Cfg.CooldownMin)) return o;

        var fora = ctx.Weather.TempC;
        var candidato = ctx.Devices.Values
            .Where(d => d.On && d.Mode == AcMode.Cool && d.AmbientC is { } a && fora <= a - Cfg.DeltaC)
            .FirstOrDefault();
        if (candidato is null) return o;

        State.UltimoAviso = ctx.Now;
        o.Notify("Lá fora está mais fresco",
            $"Fora {fora:0.#} °C, {candidato.Name} {candidato.AmbientC} °C. Abrir a janela resfria sem gastar energia.");
        return o;
    }
}
