using MeuClima.Core.Devices;
using MeuClima.Core.Rules;
using MeuClima.Core.Weather;

namespace MeuClima.Core.Tests;

internal static class TestKit
{
    public const string Dev = "dev-escritorio";
    public static readonly TimeZoneInfo Brt = TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");

    /// <summary>Instante a partir de hora local BRT.</summary>
    public static DateTimeOffset At(int hour, int minute = 0, int day = 14) =>
        new(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(-3));

    public static DeviceSnapshot Snap(bool on = true, AcMode mode = AcMode.Cool, FanSpeed fan = FanSpeed.Low,
        int? target = 23, int? ambient = 25, bool? eco = true, bool connected = true, string id = Dev) =>
        new(id, "Escritório", connected, on, mode, fan, target, ambient, false, eco, 0, "GOOD", [], At(12));

    public static WeatherSnapshot Clima(double temp, params (DateTimeOffset Time, double Temp)[] hourly) =>
        new(temp, temp, 60, hourly.Select(h => new HourlyPoint(h.Time, h.Temp)).ToList(), At(12));

    public static RuleContext Ctx(DateTimeOffset now, double? outside, DeviceSnapshot? dev = null,
        PresenceState? presence = null, IHistoryReader? history = null, WeatherSnapshot? weather = null) =>
        new(now, Brt, weather ?? (outside is { } o ? Clima(o) : null),
            dev is null ? new Dictionary<string, DeviceSnapshot>() : new Dictionary<string, DeviceSnapshot> { [dev.Id] = dev },
            presence ?? PresenceState.Present, history ?? new FakeHistory());

    public static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}

internal sealed class FakeHistory(params HistorySample[] samples) : IHistoryReader
{
    public List<HistorySample> All { get; } = [.. samples];

    public IReadOnlyList<HistorySample> Samples(string deviceId, DateTimeOffset since) =>
        All.Where(s => s.Time >= since).OrderBy(s => s.Time).ToList();
}
