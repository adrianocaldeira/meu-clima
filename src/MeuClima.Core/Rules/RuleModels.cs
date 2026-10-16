using System.Text.Json.Serialization;
using MeuClima.Core.Devices;
using MeuClima.Core.Weather;

namespace MeuClima.Core.Rules;

/// <summary>Ausência do usuário no PC: tela bloqueada (<paramref name="Locked"/>) ou sem uso de teclado/mouse.</summary>
public sealed record PresenceState(bool Away, TimeSpan AwayFor, bool Locked = false)
{
    public static readonly PresenceState Present = new(false, TimeSpan.Zero);
}

public sealed record HistorySample(DateTimeOffset Time, bool Connected, bool On, AcMode Mode, int? AmbientC, int? TargetC);

public interface IHistoryReader
{
    IReadOnlyList<HistorySample> Samples(string deviceId, DateTimeOffset since);
}

public sealed record RuleContext(
    DateTimeOffset Now,
    TimeZoneInfo TimeZone,
    WeatherSnapshot? Weather,
    IReadOnlyDictionary<string, DeviceSnapshot> Devices,
    PresenceState Presence,
    IHistoryReader History,
    Func<string, bool>? Blocked = null)
{
    public static readonly TimeSpan DefaultMaxReadingAge = TimeSpan.FromMinutes(3);

    /// <summary>Leitura mais velha que isto não gera comando: o aparelho pode ter mudado sem o app ver.</summary>
    public TimeSpan MaxReadingAge { get; init; } = DefaultMaxReadingAge;

    public DateTime LocalNow => TimeZoneInfo.ConvertTime(Now, TimeZone).DateTime;

    /// <summary>Aparelho em pausa manual ou já reivindicado por regra de prioridade maior neste ciclo.</summary>
    public bool IsBlocked(string deviceId) => Blocked?.Invoke(deviceId) ?? false;
}

public sealed record Notification(string Title, string Body);

/// <summary>Situação visível de uma regra ativa (aviso na bandeja).</summary>
public sealed record RuleStatus(string Title, string Detail, double? Progress);

/// <summary>O que a regra quer neste ciclo. Reivindicar um aparelho bloqueia regras de prioridade menor.</summary>
public sealed class RuleOutput
{
    public Dictionary<string, List<DeviceCommand>> Claims { get; } = [];
    public List<Notification> Notifications { get; } = [];
    public RuleStatus? Status { get; set; }

    public RuleOutput Claim(string deviceId, params DeviceCommand[] desired)
    {
        Claims[deviceId] = [.. desired];
        return this;
    }

    /// <summary>Situação sem comando: de olho no aparelho ou em espera (ausência, agenda). Não bloqueia ninguém.</summary>
    public Dictionary<string, (RuleStatus Status, bool Waiting)> Observed { get; } = [];

    public RuleOutput Observe(string deviceId, RuleStatus status, bool waiting = false)
    {
        Observed[deviceId] = (status, waiting);
        return this;
    }

    public RuleOutput Notify(string title, string body)
    {
        Notifications.Add(new Notification(title, body));
        return this;
    }

    /// <summary>Motivo de uma decisão, para o log; não aparece na tela.</summary>
    public List<(string DeviceId, string Text)> Traces { get; } = [];

    public RuleOutput Trace(string deviceId, string text)
    {
        Traces.Add((deviceId, text));
        return this;
    }

    public static RuleOutput None => new();
}

/// <summary>Janela semanal em horário local.</summary>
public sealed record Schedule(DayOfWeek[] Days, TimeOnly Start, TimeOnly End)
{
    public static Schedule Always => new(Enum.GetValues<DayOfWeek>(), TimeOnly.MinValue, TimeOnly.MaxValue);

    public bool Contains(DateTime local)
    {
        if (!Days.Contains(local.DayOfWeek)) return false;
        var t = TimeOnly.FromDateTime(local);
        return Start <= End ? t >= Start && t <= End : t >= Start || t <= End;
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "tipo")]
[JsonDerivedType(typeof(ConfortoConfig), "conforto")]
[JsonDerivedType(typeof(SaiDoPcConfig), "sai-do-pc")]
[JsonDerivedType(typeof(JanelaAbertaConfig), "janela-aberta")]
[JsonDerivedType(typeof(ModoNoiteConfig), "modo-noite")]
[JsonDerivedType(typeof(EsfriarRapidoConfig), "esfriar-rapido")]
[JsonDerivedType(typeof(AntesDoPicoConfig), "antes-do-pico")]
[JsonDerivedType(typeof(ForaMaisFrescoConfig), "fora-mais-fresco")]
public abstract record RuleConfig
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public bool Enabled { get; init; } = true;
}

/// <summary>Mantém o ambiente na faixa gastando o mínimo: Frio econômico no topo, ventilar no piso, turbo só no calor forte.</summary>
public sealed record ConfortoConfig(
    string DeviceId,
    int MinC = 23,
    int MaxC = 24,
    int TurboFromC = 26,
    int MaxTurboMin = 30,
    bool FanAtMin = true,
    bool AllowHeat = false,
    double PreCoolApparentC = 30,
    int PreCoolLeadMin = 30,
    double CoolerOutsideC = 3,
    bool RequirePresence = true,
    Schedule? Schedule = null) : RuleConfig;

/// <summary>Formato anterior à v0.2, só para converter settings antigos (<see cref="RuleMigration"/>).</summary>
public sealed record EsfriarRapidoConfig(
    string DeviceId,
    double TriggerOutsideC = 24,
    double StopOutsideC = 23,
    int StopAfterMin = 30,
    int Phase1TargetC = 22,
    int Phase2TargetC = 24,
    AcMode Phase2Mode = AcMode.Auto,
    bool Phase2EnergySaving = true,
    int MaxBoostMin = 60,
    bool RequirePresence = true,
    Schedule? Schedule = null) : RuleConfig;

public sealed record SaiDoPcConfig(
    string DeviceId,
    int EcoAfterMin = 20,
    int EcoTargetC = 26,
    int OffAfterMin = 60,
    bool OffOnLock = true,
    int OffOnLockMin = 0,
    bool RestoreOnReturn = false,
    bool OffOnShutdown = true) : RuleConfig;

/// <summary>Formato anterior à v0.2, só para converter settings antigos.</summary>
public sealed record AntesDoPicoConfig(
    string DeviceId,
    double ForecastThresholdC = 32,
    int LeadMin = 30,
    int TargetC = 24,
    AcMode Mode = AcMode.Auto,
    bool RequirePresence = true) : RuleConfig;

public sealed record JanelaAbertaConfig(
    string DeviceId,
    int RiseC = 2,
    int WindowMin = 15,
    int MinOnMin = 15,
    int CooldownMin = 60) : RuleConfig;

public sealed record ModoNoiteConfig(
    string DeviceId,
    int DurationHours = 4,
    int StepC = 1,
    int StepEveryMin = 60,
    int MaxTargetC = 28,
    TimeOnly? DailyStart = null) : RuleConfig;

/// <summary>Formato anterior à v0.2, só para converter settings antigos.</summary>
public sealed record ForaMaisFrescoConfig(
    double DeltaC = 3,
    int CooldownMin = 180,
    TimeOnly From = default,
    TimeOnly To = default) : RuleConfig
{
    private bool Unset => From == default && To == default;
    public TimeOnly FromOrDefault => Unset ? new TimeOnly(7, 0) : From;
    public TimeOnly ToOrDefault => Unset ? new TimeOnly(22, 0) : To;
}

/// <summary>Converte as regras "Esfriar rápido", "Antes do pico" e "Fora mais fresco" num Conforto por aparelho.</summary>
public static class RuleMigration
{
    public static bool IsLegacy(RuleConfig c) => c is EsfriarRapidoConfig or AntesDoPicoConfig or ForaMaisFrescoConfig;

    /// <summary>Lista convertida, ou null quando não há nada a converter.</summary>
    public static List<RuleConfig>? Migrate(IReadOnlyList<RuleConfig> rules)
    {
        if (!rules.Any(IsLegacy)) return null;
        var result = new List<RuleConfig>();
        foreach (var c in rules)
        {
            if (!IsLegacy(c)) { result.Add(c); continue; }
            var deviceId = DeviceOf(c);
            if (deviceId is null || rules.Concat(result).OfType<ConfortoConfig>().Any(x => x.DeviceId == deviceId)) continue;
            var legado = rules.Where(r => IsLegacy(r) && DeviceOf(r) == deviceId).ToList();
            var esfriar = legado.OfType<EsfriarRapidoConfig>().FirstOrDefault();
            var pico = legado.OfType<AntesDoPicoConfig>().FirstOrDefault();
            var fora = rules.OfType<ForaMaisFrescoConfig>().FirstOrDefault();
            // A faixa vem do alvo de manutenção do Esfriar rápido; quem só tinha o Antes do pico (um
            // pré-resfriamento por dia) não ganha controle o dia todo. O Conforto não tem chave para o
            // aviso de fora mais fresco: desligado, vai para o limite (15 °C), que na prática o cala.
            var max = Math.Clamp(esfriar?.Phase2TargetC ?? 24, 17, 30);
            result.Add(new ConfortoConfig(deviceId,
                MinC: max - 1,
                MaxC: max,
                TurboFromC: max + 2,
                CoolerOutsideC: fora is null ? 3 : fora.Enabled ? fora.DeltaC : 15,
                RequirePresence: esfriar?.RequirePresence ?? pico?.RequirePresence ?? true,
                Schedule: esfriar?.Schedule) { Enabled = esfriar?.Enabled ?? pico?.Enabled ?? false });
        }
        return result;
    }

    private static string? DeviceOf(RuleConfig c) =>
        c switch { EsfriarRapidoConfig e => e.DeviceId, AntesDoPicoConfig a => a.DeviceId, _ => null };
}
