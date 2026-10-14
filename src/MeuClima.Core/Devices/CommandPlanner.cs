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
}
