using MeuClima.Core.Devices;

namespace MeuClima.Core.Rules;

public sealed record PlannedCommands(string DeviceId, string RuleName, IReadOnlyList<DeviceCommand> Commands)
{
    /// <summary>Avisos da regra sobre este aparelho: saem só quando o envio for aceito.</summary>
    public IReadOnlyList<Notification> Notifications { get; init; } = [];
}

public enum ActiveKind { Acting, Watching, Waiting }

public sealed record ActiveRule(string RuleId, string DeviceId, RuleStatus Status, string? BlockedBy, ActiveKind Kind = ActiveKind.Acting);

public sealed record RuleTrace(string RuleName, string DeviceId, string Text);

public sealed record EngineResult(
    IReadOnlyList<PlannedCommands> Commands,
    IReadOnlyList<Notification> Notifications,
    IReadOnlyList<ActiveRule> Active)
{
    public IReadOnlyList<RuleTrace> Traces { get; init; } = [];
}

/// <summary>Avalia as regras, decide quem controla cada aparelho e planeja o que enviar.</summary>
/// <remarks>
/// Ajuste manual pausa as automações do aparelho. Mudança no estado reportado que o Meu Clima não
/// comandou na janela de confirmação (~30 s medidos) conta como manual: controle remoto ou app do celular.
/// </remarks>
public sealed class RuleEngine(IEnumerable<IRule> rules)
{
    public static readonly TimeSpan ManualPause = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan DefaultConfirmationWindow = TimeSpan.FromMinutes(3);

    /// <summary>Precisa cobrir o intervalo de leitura; senão o próprio comando volta como "manual".</summary>
    public TimeSpan ConfirmationWindow { get; set; } = DefaultConfirmationWindow;

    /// <summary>Entre leituras mais distantes que isto (suspensão, rede fora), não dá para saber quem mudou o aparelho.</summary>
    public TimeSpan MaxObservationGap { get; set; } = RuleContext.DefaultMaxReadingAge;

    /// <summary>O que cada aparelho declara aceitar; o plano é ajustado antes de comparar com o estado esperado.</summary>
    public Func<string, DeviceCapabilities?> Capabilities { get; set; } = _ => null;

    private readonly Dictionary<(string, string), string> _foraRegistrado = [];
    private readonly List<IRule> _rules = [.. rules.OrderByDescending(r => r.Priority)];
    private readonly Dictionary<string, DateTimeOffset> _pausedUntil = [];
    private readonly Dictionary<string, DateTimeOffset> _lastSent = [];
    private readonly Dictionary<string, (DateTimeOffset At, List<DeviceCommand> Cmds)> _pending = [];
    private readonly HashSet<string> _failingRules = [];

    public IReadOnlyList<IRule> Rules => _rules;

    public DateTimeOffset? PausedUntil(string deviceId, DateTimeOffset now) =>
        _pausedUntil.TryGetValue(deviceId, out var u) && u > now ? u : null;

    public void Pause(string deviceId, DateTimeOffset until) => _pausedUntil[deviceId] = until;

    public void Resume(string deviceId) => _pausedUntil.Remove(deviceId);

    /// <summary>Pausas manuais em JSON, para sobreviver a reinício do app (atualização, PC desligado).</summary>
    public string SavePauses() => System.Text.Json.JsonSerializer.Serialize(_pausedUntil);

    public void LoadPauses(string json)
    {
        try
        {
            foreach (var (id, ate) in System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(json) ?? [])
                _pausedUntil[id] = ate;
        }
        catch (System.Text.Json.JsonException) { }
    }

    /// <summary>Ajuste do usuário: pausa as automações do aparelho e avisa as regras (ex.: cancela a "Janela aberta").</summary>
    public void ManualChange(string deviceId, DateTimeOffset now)
    {
        Pause(deviceId, now + ManualPause);
        foreach (var r in _rules) r.OnManualChange(deviceId);
    }

    /// <summary>Leva pausas, envios e pendências para um motor recriado (regra salva ou excluída).</summary>
    public void CopyRuntimeFrom(RuleEngine other)
    {
        ConfirmationWindow = other.ConfirmationWindow;
        MaxObservationGap = other.MaxObservationGap;
        foreach (var (k, v) in other._pausedUntil) _pausedUntil[k] = v;
        foreach (var (k, v) in other._lastSent) _lastSent[k] = v;
        foreach (var (k, v) in other._pending) _pending[k] = (v.At, [.. v.Cmds]);
        _failingRules.UnionWith(other._failingRules);
    }

    /// <summary>Tira do estado otimista comandos que não chegaram ao aparelho.</summary>
    public void DiscardPending(string deviceId, IEnumerable<DeviceCommand> cmds)
    {
        if (!_pending.TryGetValue(deviceId, out var p)) return;
        foreach (var c in cmds) p.Cmds.Remove(c);
        if (p.Cmds.Count == 0) _pending.Remove(deviceId);
    }

    /// <summary>Registra comando enviado (por regra ou pelo usuário) para o estado otimista e a detecção de manual.</summary>
    public void RecordSent(string deviceId, IEnumerable<DeviceCommand> cmds, DateTimeOffset now)
    {
        _lastSent[deviceId] = now;
        if (!_pending.TryGetValue(deviceId, out var p) || now - p.At > ConfirmationWindow) p = (now, []);
        var novos = cmds.ToList();
        // Comando novo substitui o pendente do mesmo tipo; senão o "Frio" antigo nunca se confirma depois do "Auto".
        p.Cmds.RemoveAll(antigo => novos.Any(n => n.GetType() == antigo.GetType()));
        p.Cmds.AddRange(novos);
        _pending[deviceId] = (now, p.Cmds);
    }

    /// <summary>Compara leituras consecutivas; mudança sem comando recente pausa as automações do aparelho.</summary>
    public bool ObserveReading(DeviceSnapshot previous, DeviceSnapshot current, DateTimeOffset now)
    {
        if (!previous.Connected || !current.Connected || current.ReadAt - previous.ReadAt > MaxObservationGap) return false;
        var mudou = previous.On != current.On || previous.Mode != current.Mode || previous.Fan != current.Fan
                    || previous.TargetC != current.TargetC;
        if (!mudou) return false;
        if (_lastSent.TryGetValue(current.Id, out var t) && now - t <= ConfirmationWindow
            && _pending.TryGetValue(current.Id, out var p) && !Contradicts(previous, current, p.Cmds))
            return false;
        ManualChange(current.Id, now);
        // Sem isso, Expected reaplicaria o pedido do app por cima do ajuste do usuário até a janela fechar.
        _pending.Remove(current.Id);
        return true;
    }

    /// <summary>Mudança que diverge do que foi pedido (ex.: pedi 22 °C, apareceu 26 °C) é do usuário.</summary>
    /// <remarks>
    /// Campo sem comando pendente só pode mudar sozinho depois de ligar ou trocar o modo (o aparelho reajusta
    /// ventilação e alvo até três leituras depois, medido em 14/10); fora disso, mudança sem pedido é do usuário.
    /// A energia nunca muda sozinha.
    /// </remarks>
    private static bool Contradicts(DeviceSnapshot prev, DeviceSnapshot cur, List<DeviceCommand> cmds)
    {
        if (prev.On != cur.On && !cmds.OfType<DeviceCommand.Power>().Any(c => c.On == cur.On)) return true;
        var colateral = cmds.Any(c => c is DeviceCommand.SetMode or DeviceCommand.Power { On: true });
        bool Diverge<T>(Func<T, bool> pede, bool mudou) =>
            mudou && (cmds.OfType<T>().Any() ? !cmds.OfType<T>().Any(pede) : !colateral);
        return Diverge<DeviceCommand.SetTarget>(c => c.TargetC == cur.TargetC, prev.TargetC != cur.TargetC)
               || Diverge<DeviceCommand.SetMode>(c => c.Mode == cur.Mode, prev.Mode != cur.Mode)
               || Diverge<DeviceCommand.SetFan>(c => c.Fan == cur.Fan, prev.Fan != cur.Fan);
    }

    /// <summary>Estado esperado: leitura da nuvem com os comandos ainda não confirmados aplicados por cima.</summary>
    public DeviceSnapshot Expected(DeviceSnapshot reading, DateTimeOffset now)
    {
        if (!_pending.TryGetValue(reading.Id, out var p)) return reading;
        if (now - p.At > ConfirmationWindow)
        {
            _pending.Remove(reading.Id);
            return reading;
        }
        // Confirmado: o pendente segue até o fim da janela para ObserveReading separar efeito colateral de ajuste manual.
        if (p.Cmds.All(c => c.IsSatisfiedBy(reading))) return reading;
        return p.Cmds.Aggregate(reading, (s, c) => c.ApplyTo(s));
    }

    public EngineResult Evaluate(RuleContext ctx)
    {
        var expected = ctx.Devices.ToDictionary(kv => kv.Key, kv => Expected(kv.Value, ctx.Now));
        var owner = new Dictionary<string, IRule>();
        bool Velha(string id) => ctx.Devices.TryGetValue(id, out var d) && ctx.Now - d.ReadAt > ctx.MaxReadingAge;
        var ctxExp = ctx with
        {
            Devices = expected,
            Blocked = id => owner.ContainsKey(id) || PausedUntil(id, ctx.Now) is not null || Velha(id),
        };
        var commands = new List<PlannedCommands>();
        var notes = new List<Notification>();
        var active = new List<ActiveRule>();
        var traces = new List<RuleTrace>();

        foreach (var rule in _rules.Where(r => r.Config.Enabled))
        {
            RuleOutput output;
            try { output = rule.Evaluate(ctxExp); }
            catch (Exception ex)
            {
                // Avisa uma vez por sequência de erros; o ciclo roda a cada poucos segundos.
                if (_failingRules.Add(rule.Config.Id)) notes.Add(new Notification($"Erro na regra {rule.Name}", ex.Message));
                continue;
            }
            _failingRules.Remove(rule.Config.Id);
            notes.AddRange(output.Notifications.Where(n => n.DeviceId is null).Select(n => n with { RuleName = rule.Name }));
            traces.AddRange(output.Traces.Select(t => new RuleTrace(rule.Name, t.DeviceId, t.Text)));

            foreach (var (deviceId, desired) in output.Claims)
            {
                string? bloqueio = null;
                if (owner.TryGetValue(deviceId, out var dono)) bloqueio = dono.Name;
                else if (PausedUntil(deviceId, ctx.Now) is { } ate) bloqueio = $"ajuste manual · volta às {ate.ToOffset(ctx.TimeZone.GetUtcOffset(ate)):HH\\:mm}";
                else if (!expected.TryGetValue(deviceId, out var dev) || !dev.Connected) bloqueio = "aparelho sem conexão";
                else if (Velha(deviceId)) bloqueio = $"sem leitura nova desde {dev.ReadAt.ToOffset(ctx.TimeZone.GetUtcOffset(dev.ReadAt)):HH\\:mm}";

                if (output.Status is { } st) active.Add(new ActiveRule(rule.Config.Id, deviceId, st, bloqueio));
                if (bloqueio is not null) continue;

                owner[deviceId] = rule;
                // Ajustar antes do Plan: alvo limitado à faixa vira o esperado e não reenvia a cada ciclo.
                var cabe = CommandPlanner.Fit([.. desired], Capabilities(deviceId), out var fora);
                var foraTexto = string.Join(", ", fora);
                if (_foraRegistrado.GetValueOrDefault((deviceId, rule.Name), "") != foraTexto)
                {
                    _foraRegistrado[(deviceId, rule.Name)] = foraTexto;
                    if (fora.Count > 0)
                        traces.Add(new RuleTrace(rule.Name, deviceId, $"fora do que o aparelho aceita: {foraTexto}{(cabe.Count == 0 ? "; nada enviado" : "")}"));
                }
                var plan = CommandPlanner.Plan(expected[deviceId], cabe);
                if (plan.Count > 0)
                    commands.Add(new PlannedCommands(deviceId, rule.Name, plan)
                    {
                        Notifications = [.. output.Notifications.Where(n => n.DeviceId == deviceId).Select(n => n with { RuleName = rule.Name })],
                    });
            }
            foreach (var (deviceId, (st, waiting)) in output.Observed)
                if (!output.Claims.ContainsKey(deviceId))
                    active.Add(new ActiveRule(rule.Config.Id, deviceId, st, null, waiting ? ActiveKind.Waiting : ActiveKind.Watching));
        }
        return new EngineResult(commands, notes, active) { Traces = traces };
    }
}
