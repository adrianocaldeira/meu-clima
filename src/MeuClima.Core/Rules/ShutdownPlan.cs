namespace MeuClima.Core.Rules;

public sealed record ShutdownDecision(string DeviceId, bool TurnOff, string Reason);

/// <summary>O que fazer com cada aparelho do Ausente se o PC desligar ou suspender agora.</summary>
/// <remarks>
/// Calculado a cada ciclo, sob a trava: no desligamento o Windows dá poucos segundos e não há tempo para ler
/// o aparelho nem avaliar as regras.
/// </remarks>
public static class ShutdownPlan
{
    public static IReadOnlyList<ShutdownDecision> Decide(
        RuleEngine engine, IReadOnlyDictionary<string, Devices.DeviceSnapshot> readings, DateTimeOffset now, TimeSpan maxReadingAge)
    {
        var noite = engine.Rules.OfType<ModoNoiteRule>().Where(r => r.Cfg.Enabled && r.Active).Select(r => r.Cfg.DeviceId).ToHashSet();
        return engine.Rules.OfType<SaiDoPcRule>()
            .Where(r => r.Cfg.Enabled && r.Cfg.OffOnShutdown)
            .Select(r => r.Cfg.DeviceId)
            .Distinct()
            .Select(Decidir)
            .ToList();

        ShutdownDecision Decidir(string id)
        {
            if (!readings.TryGetValue(id, out var lida) || !lida.Connected) return new(id, false, "sem leitura do aparelho");
            if (now - lida.ReadAt > maxReadingAge) return new(id, false, "leitura antiga, o aparelho pode ter mudado");
            if (engine.PausedUntil(id, now) is not null) return new(id, false, "ajuste manual em vigor");
            if (noite.Contains(id)) return new(id, false, "modo noite ativo, o timer do aparelho desliga");
            if (!engine.Expected(lida, now).On) return new(id, false, "já desligado");
            return new(id, true, "ligado, com o Ausente ativo");
        }
    }
}
