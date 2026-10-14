using System.Text.Json.Nodes;
using MeuClima.Core.Devices;

namespace MeuClima.Core.Electrolux;

/// <summary>Tradução entre o JSON da Electrolux Group API e os modelos do Meu Clima.</summary>
public static class ElectroluxMapping
{
    public static DeviceSnapshot ParseState(string deviceId, JsonNode json, DateTimeOffset readAt)
    {
        var r = json["properties"]?["reported"] ?? new JsonObject();
        var connected = string.Equals(Str(json["connectionState"]) ?? Str(r["connectivityState"]), "connected",
            StringComparison.OrdinalIgnoreCase);
        // applianceState: RUNNING = ligado; OFF/IDLE/... = desligado (IDLE medido em aparelho off-line).
        var on = string.Equals(Str(r["applianceState"]), "RUNNING", StringComparison.OrdinalIgnoreCase);
        var alerts = r["alerts"] is JsonArray arr
            ? arr.Select(a => a is JsonObject o ? Str(o["code"]) ?? o.ToJsonString() : Str(a) ?? "").Where(a => a != "").ToList()
            : [];
        return new DeviceSnapshot(
            deviceId,
            Name: deviceId,
            Connected: connected,
            On: on,
            Mode: ParseMode(Str(r["mode"])),
            Fan: ParseFan(Str(r["fanSpeedSetting"])),
            TargetC: Int(r["targetTemperatureC"]),
            AmbientC: Int(r["ambientTemperatureC"]),
            Oscillate: OnOff(r["flapOscillate"]),
            EnergySaving: OnOff(r["energySavingMode"]),
            StopTimeSeconds: Int(r["stopTime"]),
            FilterState: Str(r["filterState"]),
            Alerts: alerts,
            ReadAt: readAt);
    }

    public static DeviceCapabilities ParseCapabilities(JsonNode json)
    {
        var c = json["capabilities"] ?? new JsonObject();
        var modes = Keys(c["mode"]?["values"]).Select(ParseMode).Where(m => m != AcMode.Unknown).ToList();
        var fans = Keys(c["fanSpeedSetting"]?["values"]).Select(ParseFan).Where(f => f != FanSpeed.Unknown).ToList();
        var t = c["targetTemperatureC"];
        return new DeviceCapabilities(modes, fans, Int(t?["min"]) ?? 16, Int(t?["max"]) ?? 30,
            Str(json["applianceInfo"]?["model"]));
    }

    public static JsonObject ToCommandBody(DeviceCommand command) => command switch
    {
        DeviceCommand.Power p => new JsonObject { ["executeCommand"] = p.On ? "ON" : "OFF" },
        DeviceCommand.SetMode m => new JsonObject { ["mode"] = ModeToApi(m.Mode) },
        DeviceCommand.SetFan f => new JsonObject { ["fanSpeedSetting"] = FanToApi(f.Fan) },
        DeviceCommand.SetTarget t => new JsonObject { ["targetTemperatureC"] = t.TargetC },
        DeviceCommand.SetStopTimer s => new JsonObject { ["stopTime"] = s.Seconds },
        DeviceCommand.SetOscillate o => new JsonObject { ["flapOscillate"] = o.On ? "ON" : "OFF" },
        DeviceCommand.SetEnergySaving e => new JsonObject { ["energySavingMode"] = e.On ? "ON" : "OFF" },
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    public static AcMode ParseMode(string? v) => v?.ToUpperInvariant() switch
    {
        "AUTO" => AcMode.Auto, "COOL" => AcMode.Cool, "HEAT" => AcMode.Heat,
        "FANONLY" => AcMode.FanOnly, "DRY" => AcMode.Dry, _ => AcMode.Unknown,
    };

    public static FanSpeed ParseFan(string? v) => v?.ToUpperInvariant() switch
    {
        "AUTO" => FanSpeed.Auto, "LOW" => FanSpeed.Low, "MIDDLE" => FanSpeed.Middle,
        "HIGH" => FanSpeed.High, "TURBO" => FanSpeed.Turbo, _ => FanSpeed.Unknown,
    };

    private static string ModeToApi(AcMode m) => m switch
    {
        AcMode.Auto => "AUTO", AcMode.Cool => "COOL", AcMode.Heat => "HEAT",
        AcMode.FanOnly => "FANONLY", AcMode.Dry => "DRY", _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    private static string FanToApi(FanSpeed f) => f switch
    {
        FanSpeed.Auto => "AUTO", FanSpeed.Low => "LOW", FanSpeed.Middle => "MIDDLE",
        FanSpeed.High => "HIGH", FanSpeed.Turbo => "TURBO", _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };

    private static IEnumerable<string> Keys(JsonNode? n) => n is JsonObject o ? o.Select(kv => kv.Key) : [];

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? Int(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return (int)Math.Round(d);
        if (v.TryGetValue<int>(out var i)) return i;
        return null;
    }

    private static bool? OnOff(JsonNode? n) => Str(n)?.ToUpperInvariant() switch
    {
        "ON" => true, "OFF" => false, _ => null,
    };
}
