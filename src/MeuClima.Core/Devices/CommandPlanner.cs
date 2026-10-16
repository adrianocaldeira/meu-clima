namespace MeuClima.Core.Devices;

/// <summary>Ordena comandos, insere pré-requisitos do aparelho e descarta o que já está vigente.</summary>
/// <remarks>
/// Restrições lidas dos <c>triggers</c> do /info (XI09R, 14/10/2026): Turbo não é aceito com o modo
/// econômico ligado; em Ventilar a temperatura alvo fica desabilitada.
/// </remarks>
public static class CommandPlanner
{
    public static IReadOnlyList<DeviceCommand> Plan(DeviceSnapshot current, IEnumerable<DeviceCommand> desired)
    {
        var wanted = desired.ToList();
        var ordered = new List<DeviceCommand>();
        void Add(DeviceCommand c) { if (!ordered.Contains(c)) ordered.Add(c); }

        var power = wanted.OfType<DeviceCommand.Power>().LastOrDefault();
        if (power is { On: false }) return power.IsSatisfiedBy(current) ? [] : [power];
        if (power is not null) Add(power);

        var mode = wanted.OfType<DeviceCommand.SetMode>().LastOrDefault();
        if (mode is not null) Add(mode);
        var finalMode = mode?.Mode ?? current.Mode;

        var fan = wanted.OfType<DeviceCommand.SetFan>().LastOrDefault();
        var eco = wanted.OfType<DeviceCommand.SetEnergySaving>().LastOrDefault();
        if (fan is { Fan: FanSpeed.Turbo })
        {
            Add(new DeviceCommand.SetEnergySaving(false));
            Add(fan);
        }
        else
        {
            if (fan is not null) Add(fan);
            if (eco is not null) Add(eco);
        }

        var target = wanted.OfType<DeviceCommand.SetTarget>().LastOrDefault();
        if (target is not null && finalMode != AcMode.FanOnly) Add(target);

        foreach (var other in wanted.Where(c => c is DeviceCommand.SetStopTimer or DeviceCommand.SetOscillate))
            Add(other);

        var simulated = current;
        var result = new List<DeviceCommand>();
        foreach (var c in ordered)
        {
            if (!c.IsSatisfiedBy(simulated)) result.Add(c);
            simulated = c.ApplyTo(simulated);
        }
        return result;
    }

    /// <summary>Ajusta ao que o aparelho declara aceitar: alvo dentro da faixa; ventilação fora da lista sai; modo fora da lista cancela o plano.</summary>
    /// <remarks>Sem o modo, o resto do plano ligaria o ar no último modo usado (em geral Frio).</remarks>
    public static IReadOnlyList<DeviceCommand> Fit(IReadOnlyList<DeviceCommand> plan, DeviceCapabilities? caps, out IReadOnlyList<DeviceCommand> dropped)
    {
        dropped = [];
        if (caps is null) return plan;
        var fora = new List<DeviceCommand>();
        var result = new List<DeviceCommand>();
        foreach (var c in plan)
        {
            switch (c)
            {
                case DeviceCommand.SetMode m when caps.Modes.Count > 0 && !caps.Modes.Contains(m.Mode):
                case DeviceCommand.SetFan f when caps.FanSpeeds.Count > 0 && !caps.FanSpeeds.Contains(f.Fan):
                    fora.Add(c);
                    break;
                case DeviceCommand.SetTarget t when caps.MinTargetC < caps.MaxTargetC
                                                  && (t.TargetC < caps.MinTargetC || t.TargetC > caps.MaxTargetC):
                    fora.Add(c);
                    result.Add(new DeviceCommand.SetTarget(Math.Clamp(t.TargetC, caps.MinTargetC, caps.MaxTargetC)));
                    break;
                default:
                    result.Add(c);
                    break;
            }
        }
        dropped = fora;
        return fora.Any(c => c is DeviceCommand.SetMode) ? [] : result;
    }
}
