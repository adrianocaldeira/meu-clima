namespace MeuClima.Core.Devices;

public enum AcMode { Auto, Cool, Heat, FanOnly, Dry, Unknown }

public enum FanSpeed { Auto, Low, Middle, High, Turbo, Unknown }

/// <summary>Aparelho cadastrado na conta do provedor.</summary>
public sealed record DeviceInfo(string Id, string Name, string Brand, string Model);

/// <summary>Estado lido do aparelho num instante. Temperaturas em °C.</summary>
public sealed record DeviceSnapshot(
    string Id,
    string Name,
    bool Connected,
    bool On,
    AcMode Mode,
    FanSpeed Fan,
    int? TargetC,
    int? AmbientC,
    bool? Oscillate,
    bool? EnergySaving,
    int? StopTimeSeconds,
    string? FilterState,
    IReadOnlyList<string> Alerts,
    DateTimeOffset ReadAt);

/// <summary>Faixas e valores aceitos pelo aparelho, vindos do provedor.</summary>
public sealed record DeviceCapabilities(
    IReadOnlyList<AcMode> Modes,
    IReadOnlyList<FanSpeed> FanSpeeds,
    int MinTargetC,
    int MaxTargetC,
    string? Model);

/// <summary>Um ajuste a aplicar no aparelho. Um comando = uma propriedade.</summary>
public abstract record DeviceCommand
{
    public sealed record Power(bool On) : DeviceCommand;
    public sealed record SetMode(AcMode Mode) : DeviceCommand;
    public sealed record SetFan(FanSpeed Fan) : DeviceCommand;
    public sealed record SetTarget(int TargetC) : DeviceCommand;
    public sealed record SetStopTimer(int Seconds) : DeviceCommand;
    public sealed record SetOscillate(bool On) : DeviceCommand;
    public sealed record SetEnergySaving(bool On) : DeviceCommand;

    /// <summary>Verdadeiro quando o estado atual já tem o valor pedido (comando dispensável).</summary>
    public bool IsSatisfiedBy(DeviceSnapshot s) => this switch
    {
        Power p => s.On == p.On,
        SetMode m => s.Mode == m.Mode,
        SetFan f => s.Fan == f.Fan,
        SetTarget t => s.TargetC == t.TargetC,
        SetStopTimer st => st.Seconds == 0 ? (s.StopTimeSeconds ?? 0) == 0 : false,
        SetOscillate o => s.Oscillate == o.On,
        SetEnergySaving e => s.EnergySaving == e.On,
        _ => false,
    };

    /// <summary>Aplica o comando a um snapshot (estado otimista enquanto a nuvem não confirma).</summary>
    public DeviceSnapshot ApplyTo(DeviceSnapshot s) => this switch
    {
        Power p => s with { On = p.On },
        SetMode m => s with { Mode = m.Mode },
        SetFan f => s with { Fan = f.Fan },
        SetTarget t => s with { TargetC = t.TargetC },
        SetStopTimer st => s with { StopTimeSeconds = st.Seconds },
        SetOscillate o => s with { Oscillate = o.On },
        SetEnergySaving e => s with { EnergySaving = e.On },
        _ => s,
    };
}

public enum CommandOutcome { Applied, AlreadyInState, Rejected, Failed }

public sealed record CommandResult(CommandOutcome Outcome, string? Detail = null)
{
    public bool Ok => Outcome is CommandOutcome.Applied or CommandOutcome.AlreadyInState;
}

/// <summary>Abstração de marca. v0.1 tem só a Electrolux.</summary>
public interface IClimateProvider
{
    Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default);
    Task<DeviceSnapshot> GetStateAsync(string deviceId, CancellationToken ct = default);
    Task<DeviceCapabilities> GetCapabilitiesAsync(string deviceId, CancellationToken ct = default);
    Task<CommandResult> SendAsync(string deviceId, DeviceCommand command, CancellationToken ct = default);
}
