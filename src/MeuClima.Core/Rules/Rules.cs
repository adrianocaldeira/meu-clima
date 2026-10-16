using System.Globalization;
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
    public DateTimeOffset? AcimaDesde { get; set; }
    public DateTimeOffset? QuenteDesde { get; set; }
    public DateTimeOffset? NoPisoDesde { get; set; }
    public DateTimeOffset? AbaixoDesde { get; set; }
    public DateOnly? AntecipouEm { get; set; }
    public DateTimeOffset? UltimoAvisoFora { get; set; }
    /// <summary>Saiu do turbo por ausência ou fim da agenda: ainda falta tirar a ventilação do Turbo.</summary>
    public bool SairDoTurbo { get; set; }
    /// <summary>Hora da última leitura avaliada; uma lacuna maior que <see cref="ConfortoRule.Lacuna"/> recomeça as fases.</summary>
    public DateTimeOffset? UltimaLeitura { get; set; }
}

/// <summary>Mantém dentro entre a mínima e a máxima com o menor gasto: frio econômico, ventilar e turbo só no calor forte.</summary>
/// <remarks>
/// O sensor mede graus inteiros e às vezes salta numa leitura só: mudar de fase pede a condição sustentada
/// por <see cref="Confirmacao"/> (sair do frio para ventilar, <see cref="ConfirmacaoPiso"/>; 2 °C ou mais acima
/// da máxima, <see cref="ConfirmacaoForte"/>). Nunca usa o modo
/// Auto, que aquece sozinho quando dentro fica abaixo do alvo.
/// </remarks>
public sealed class ConfortoRule(ConfortoConfig cfg) : Rule<ConfortoConfig, ConfortoState>(cfg)
{
    public static readonly TimeSpan Confirmacao = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ConfirmacaoPiso = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ConfirmacaoForte = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan AvisoForaCarencia = TimeSpan.FromHours(3);
    /// <summary>Sem leitura por mais que isto (PC desligado, suspenso, app fechado), nada do que se via antes vale.</summary>
    public static readonly TimeSpan Lacuna = TimeSpan.FromMinutes(10);

    public override string Name => "Conforto";
    public override int Priority => RulePriority.Conforto;

    public override void OnManualChange(string deviceId)
    {
        if (deviceId == Cfg.DeviceId) Reiniciar();
    }

    private void Reiniciar() => State = Recomeco(State);

    /// <summary>Recomeça as fases, mas guarda o que vale por dia e a trava do turbo, que só some quando dentro baixa:
    /// pausa, ausência ou outra regra no comando não podem liberar outro turbo.</summary>
    private static ConfortoState Recomeco(ConfortoState s) => new()
    {
        AntecipouEm = s.AntecipouEm,
        UltimoAvisoFora = s.UltimoAvisoFora,
        TurboEsgotado = s.TurboEsgotado,
        SensorParadoEm = s.SensorParadoEm,
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
            // Avalia do zero só para mostrar o que faria, e descarta o estado e os avisos.
            var guardado = Recomeco(State);
            Reiniciar();
            var previa = Avaliar(o, ctx, dev);
            previa.Notifications.Clear();
            previa.Observed.Clear();
            State = guardado;
            return previa;
        }
        return Avaliar(o, ctx, dev);
    }

    private RuleOutput Avaliar(RuleOutput o, RuleContext ctx, DeviceSnapshot dev)
    {
        var naAgenda = (Cfg.Schedule ?? Schedule.Always).Contains(ctx.LocalNow);
        if (!naAgenda || (Cfg.RequirePresence && ctx.Presence.Away))
            return Sair(o, dev, naAgenda ? "ninguém no PC" : "fora da agenda");
        State.SairDoTurbo = false;
        if (dev.AmbientC is not { } amb) return Reivindicar(o, ctx, dev, null);

        var lacuna = Lacuna > ctx.MaxReadingAge * 2 ? Lacuna : ctx.MaxReadingAge * 2;
        if (State.UltimaLeitura is not { } ultima || dev.ReadAt - ultima > lacuna)
        {
            if (State.Fase != ConfortoFase.Parado || State.AcimaDesde is not null || State.NoPisoDesde is not null)
                o.Trace(Cfg.DeviceId, State.UltimaLeitura is { } u
                    ? $"recomeça: {(dev.ReadAt - u).TotalMinutes:0} min sem leitura (fase {State.Fase})"
                    : $"recomeça: estado sem hora da última leitura (fase {State.Fase})");
            Reiniciar();
        }
        State.UltimaLeitura = dev.ReadAt;

        Acompanhar(dev.ReadAt, amb);
        var antes = State.Fase;
        var antecipou = false;
        State.Fase = Proxima(ctx, dev, amb, ref antecipou);
        if (State.Fase != antes) o.Trace(Cfg.DeviceId, $"fase {antes} → {State.Fase}: dentro {amb} °C, {(dev.On ? "ligado" : "desligado")} em {dev.Mode}");
        if (State.Fase != ConfortoFase.Turbo) (State.TurboDesde, State.TurboInicioC) = (null, null);
        else
        {
            State.TurboDesde ??= ctx.Now;
            State.TurboInicioC ??= amb;
        }

        if (State.Fase != antes && State.Fase == ConfortoFase.Turbo)
            o.Notify("Conforto: turbo", $"{dev.Name} em {amb} °C. Turbo até {Cfg.MaxC} °C, por no máximo {Cfg.MaxTurboMin} min.");
        else if (antes == ConfortoFase.Parado && !dev.On && State.Fase == ConfortoFase.Manter)
            o.Notify("Conforto ligou o ar", antecipou
                ? $"{dev.Name}: o calor forte chega em até {Cfg.PreCoolLeadMin} min. Frio econômico em {Cfg.MaxC} °C."
                : $"{dev.Name}: dentro em {amb} °C, acima de {Cfg.MaxC} °C. Frio econômico em {Cfg.MaxC} °C.");
        AvisarForaMaisFresco(o, ctx, dev, amb);
        return Reivindicar(o, ctx, dev, amb);
    }

    /// <summary>Marca desde quando cada condição vale, pelo horário da leitura que a mostrou.</summary>
    private void Acompanhar(DateTimeOffset leitura, int amb)
    {
        State.AcimaDesde = amb > Cfg.MaxC ? State.AcimaDesde ?? leitura : null;
        State.QuenteDesde = amb >= Cfg.TurboFromC ? State.QuenteDesde ?? leitura : null;
        State.NoPisoDesde = amb <= Cfg.MinC ? State.NoPisoDesde ?? leitura : null;
        State.AbaixoDesde = amb < Cfg.MinC ? State.AbaixoDesde ?? leitura : null;
        if (amb <= Cfg.MaxC) State.TurboEsgotado = false;
        if (State.SensorParadoEm is { } p && p != amb) State.SensorParadoEm = null;
    }

    /// <summary>Ausência ou fim da agenda: solta o aparelho, mas não o deixa em turbo sem prazo.</summary>
    private RuleOutput Sair(RuleOutput o, DeviceSnapshot dev, string motivo)
    {
        var estavaNoTurbo = State.Fase == ConfortoFase.Turbo || State.SairDoTurbo;
        Reiniciar();
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

    private ConfortoFase Proxima(RuleContext ctx, DeviceSnapshot dev, int amb, ref bool antecipou)
    {
        var now = ctx.Now;
        // Exige uma leitura posterior à que começou a contar: uma leitura isolada que fica parada não confirma.
        bool Por(DateTimeOffset? desde, TimeSpan t) => desde is { } d && now - d >= t && dev.ReadAt > d;
        // Ligar só pela passagem a partir de Parado: fase de ar ligado com o ar desligado não religa sozinha.
        var fase = dev.On ? State.Fase : ConfortoFase.Parado;

        if (fase == ConfortoFase.Turbo)
        {
            var noTeto = State.TurboDesde is { } t0 && now - t0 >= TimeSpan.FromMinutes(Cfg.MaxTurboMin);
            if (amb > Cfg.MaxC && !noTeto) return ConfortoFase.Turbo;
            State.TurboEsgotado = amb > Cfg.MaxC;
            if (noTeto && State.TurboInicioC is { } inicio && amb >= inicio) State.SensorParadoEm = amb;
            return amb <= Cfg.MinC ? Piso(amb) : ConfortoFase.Manter;
        }
        if (!State.TurboEsgotado && Por(State.QuenteDesde, EsperaAcima(amb))) return ConfortoFase.Turbo;
        if (Por(State.AcimaDesde, EsperaAcima(amb))) return ConfortoFase.Manter;
        // Aparelho já ligado (por você ou antes do app abrir): assume sem esperar a confirmação.
        // Já ventilando dentro da faixa, segue ventilando: não liga o compressor sem estar acima da máxima.
        if (fase == ConfortoFase.Parado && dev.On)
            return amb <= Cfg.MinC || (amb <= Cfg.MaxC && dev.Mode == AcMode.FanOnly && Cfg.FanAtMin) ? Piso(amb) : ConfortoFase.Manter;
        if (fase == ConfortoFase.Manter && Por(State.NoPisoDesde, ConfirmacaoPiso)) return Piso(amb);
        if (fase is ConfortoFase.Ventilar or ConfortoFase.Parado && Cfg.AllowHeat && Por(State.AbaixoDesde, Confirmacao))
            return ConfortoFase.Aquecer;
        if (fase == ConfortoFase.Parado && amb >= Cfg.MaxC && CalorChegando(ctx))
        {
            State.AntecipouEm = DateOnly.FromDateTime(ctx.LocalNow);
            antecipou = true;
            return ConfortoFase.Manter;
        }
        return fase;
    }

    private TimeSpan EsperaAcima(int amb) => amb >= Cfg.MaxC + 2 ? ConfirmacaoForte : Confirmacao;

    /// <summary>"acima de 24 °C desde 15:59. Se continuar até 16:01," enquanto a subida não confirmou.</summary>
    private string? AcimaAte(RuleContext ctx, int? amb) =>
        amb is { } a && a > Cfg.MaxC && State.AcimaDesde is { } d
            ? $"acima de {Cfg.MaxC} °C desde {Hm(ctx, d)}. Se continuar até {Hm(ctx, d + EsperaAcima(a))},"
            : null;

    private ConfortoFase Piso(int amb) =>
        Cfg.AllowHeat && amb < Cfg.MinC ? ConfortoFase.Aquecer : Cfg.FanAtMin ? ConfortoFase.Ventilar : ConfortoFase.Parado;

    /// <summary>Previsão de sensação térmica acima do limite dentro da antecedência; uma vez por dia.</summary>
    private bool CalorChegando(RuleContext ctx)
    {
        if (ctx.Weather is not { } w || State.AntecipouEm == DateOnly.FromDateTime(ctx.LocalNow)) return false;
        var limite = ctx.Now + TimeSpan.FromMinutes(Cfg.PreCoolLeadMin);
        return w.Hourly.Any(h => h.Time > ctx.Now && h.Time <= limite && (h.ApparentC ?? h.TempC) >= Cfg.PreCoolApparentC);
    }

    private void AvisarForaMaisFresco(RuleOutput o, RuleContext ctx, DeviceSnapshot dev, int amb)
    {
        if (State.Fase is not (ConfortoFase.Manter or ConfortoFase.Turbo) || ctx.Weather is not { } w) return;
        var hora = ctx.LocalNow.Hour;
        if (hora < 7 || hora >= 22 || w.ApparentC > amb - Cfg.CoolerOutsideC) return;
        if (State.UltimoAvisoFora is { } u && ctx.Now - u < AvisoForaCarencia) return;
        State.UltimoAvisoFora = ctx.Now;
        o.Notify("Lá fora está mais fresco",
            $"Sensação lá fora de {w.ApparentC:0.#} °C, {dev.Name} em {amb} °C. Abrir a janela e desligar o ar resfria sem gastar energia.");
    }

    private RuleOutput Reivindicar(RuleOutput o, RuleContext ctx, DeviceSnapshot dev, int? amb)
    {
        var dentro = amb is { } x ? $"Dentro está {x} °C" : "Sem leitura de dentro";
        switch (State.Fase)
        {
            case ConfortoFase.Turbo:
                o.Claim(Cfg.DeviceId, new Power(true), new SetMode(AcMode.Cool), new SetFan(FanSpeed.Turbo), new SetTarget(Cfg.MinC));
                var ate = State.TurboDesde is { } t ? $", no máximo até {Hm(ctx, t.AddMinutes(Cfg.MaxTurboMin))}" : "";
                o.Status = new RuleStatus($"{Name} · turbo", $"{dentro}, calor forte. Turbo até chegar a {Cfg.MaxC} °C{ate}.", null);
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
            case ConfortoFase.Parado when dev.On && amb <= Cfg.MinC:
                o.Claim(Cfg.DeviceId, new Power(false));
                o.Status = new RuleStatus(Name, $"{dentro}, na mínima. Desligando o ar para economizar.", null);
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
}

/// <summary>Longe do PC: tela bloqueada desliga (na hora ou após N min); sem uso, alivia e depois desliga.</summary>
/// <remarks>Na volta, restaura o ajuste anterior só se configurado; senão as outras regras (Conforto) decidem.</remarks>
public sealed class SaiDoPcRule(SaiDoPcConfig cfg) : Rule<SaiDoPcConfig, SaiDoPcState>(cfg)
{
    public static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(15);

    public override string Name => "Ausente";
    public override int Priority => RulePriority.SaiDoPc;

    public override RuleOutput Evaluate(RuleContext ctx)
    {
        var o = new RuleOutput();
        if (!ctx.Devices.TryGetValue(Cfg.DeviceId, out var dev)) return o;
        var p = ctx.Presence;
        var ausente = p.Away ? p.AwayFor : TimeSpan.Zero;

        if (!p.Away)
        {
            if (State.Estagio == SaidaEstagio.Presente) return o;
            if (State.EstavaLigado != true || !Cfg.RestoreOnReturn) { State = new(); return o; }
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
        // Aliviar subindo o alvo só serve esfriando; aquecendo, espera a hora de desligar.
        else if (State.Modo != AcMode.Heat)
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

        if (!dev.On || dev.Mode != AcMode.Cool || dev.AmbientC is not { } agora) return o;
        var amostras = ctx.History.Samples(Cfg.DeviceId, ctx.Now.AddMinutes(-Math.Max(Cfg.WindowMin, Cfg.MinOnMin) - 10));
        var ligadoDesde = amostras.Reverse().TakeWhile(s => s.On && s.Mode == AcMode.Cool).LastOrDefault()?.Time;
        if (ligadoDesde is null || ctx.Now - ligadoDesde < TimeSpan.FromMinutes(Cfg.MinOnMin)) return o;

        var janela = amostras.Where(s => s.Time >= ctx.Now.AddMinutes(-Cfg.WindowMin) && s.AmbientC is not null).ToList();
        if (janela.Count == 0) return o;
        var minimo = janela.Min(s => s.AmbientC!.Value);
        if (agora - minimo < Cfg.RiseC) return o;
        // Pelo menos duas leituras altas: com leitura a cada 5 min, uma só ficaria "sustentada" pelo relógio.
        var alta = janela.AsEnumerable().Reverse().TakeWhile(s => s.AmbientC - minimo >= Cfg.RiseC).ToList();
        if (alta.Count < 2 || ctx.Now - alta[^1].Time < Sustentacao) return o;

        State = new JanelaAbertaState { DisparouEm = ctx.Now, De = minimo, Para = agora };
        o.Notify("Janela aberta?", $"{dev.Name} subiu de {minimo} para {agora} °C em até {Cfg.WindowMin} min com o ar no frio. Desliguei.");
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
                o.Status = new RuleStatus(Name, "Fim do modo noite: desligando o ar.", 1);
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
        var cada = Cfg.StepEveryMin == 60 ? "hora" : $"{Cfg.StepEveryMin} min";
        o.Status = new RuleStatus(Name, $"Em {alvo} °C, subindo {Cfg.StepC} °C a cada {cada} para você não sentir frio. Desliga em {restante:h\\:mm}.",
            decorrido.TotalHours / Cfg.DurationHours);
        return o;
    }
}
