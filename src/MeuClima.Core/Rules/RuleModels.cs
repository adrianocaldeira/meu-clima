using System.Text.Json.Serialization;
using MeuClima.Core.Devices;
using MeuClima.Core.Weather;

namespace MeuClima.Core.Rules;

/// <summary>Ausência do usuário no PC (tela bloqueada ou sem uso de teclado/mouse).</summary>
public sealed record PresenceState(bool Away, TimeSpan AwayFor)
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
    IHistoryReader History)
{
    public DateTime LocalNow => TimeZoneInfo.ConvertTime(Now, TimeZone).DateTime;
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

    public RuleOutput Notify(string title, string body)
    {
        Notifications.Add(new Notification(title, body));
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
[JsonDerivedType(typeof(EsfriarRapidoConfig), "esfriar-rapido")]
[JsonDerivedType(typeof(SaiDoPcConfig), "sai-do-pc")]
[JsonDerivedType(typeof(AntesDoPicoConfig), "antes-do-pico")]
[JsonDerivedType(typeof(JanelaAbertaConfig), "janela-aberta")]
[JsonDerivedType(typeof(ModoNoiteConfig), "modo-noite")]
[JsonDerivedType(typeof(ForaMaisFrescoConfig), "fora-mais-fresco")]
public abstract record RuleConfig
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public bool Enabled { get; init; } = true;
}

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
    int OffAfterMin = 60) : RuleConfig;

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
