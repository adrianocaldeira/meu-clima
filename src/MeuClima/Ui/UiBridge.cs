using System.Text.Json;
using MeuClima.Core.Devices;
using MeuClima.Core.Electrolux;
using MeuClima.Core.Rules;
using MeuClima.Core.Weather;
using MeuClima.Host;

namespace MeuClima.Ui;

/// <summary>Trata as mensagens UI → host do contrato (docs/specs/2026-10-14-ponte-ui.md).</summary>
public sealed class UiBridge(ClimateService climate, Action<string?> openCentral, Action closeFlyout, Action quit)
{
    public Func<Task<(bool Ok, string Text)>>? CheckUpdate { get; set; }
    public Func<Task>? InstallUpdate { get; set; }

    public async Task HandleAsync(WebWindow source, JsonElement m)
    {
        var type = m.GetProperty("type").GetString();
        string S(string name) => m.GetProperty(name).GetString() ?? "";
        switch (type)
        {
            case "ready":
                source.Post("state", await climate.BuildStateAsync());
                break;
            case "command":
                if (ParseCommand(m.GetProperty("command")) is { } cmd) await climate.UserCommandAsync(S("deviceId"), cmd);
                break;
            case "pause":
                await climate.PauseDeviceAsync(S("deviceId"), m.GetProperty("minutes").GetInt32());
                break;
            case "resume":
                await climate.ResumeDeviceAsync(S("deviceId"));
                break;
            case "nightMode":
                await climate.SetNightModeAsync(S("deviceId"), m.GetProperty("on").GetBoolean());
                break;
            case "openCentral":
                openCentral(m.TryGetProperty("tab", out var tab) ? tab.GetString() : null);
                break;
            case "closeFlyout":
                closeFlyout();
                break;
            case "resize":
                if (source is FlyoutWindow fly && m.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number)
                    fly.FitTo(h.GetDouble());
                break;
            case "getInsights":
                var days = m.TryGetProperty("days", out var d) ? d.GetInt32() : 1;
                source.Post("insights", await climate.BuildInsightsAsync(days));
                break;
            case "saveRule":
                RuleConfig? rule;
                try { rule = m.GetProperty("rule").Deserialize<RuleConfig>(Json.Options); }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or KeyNotFoundException)
                {
                    Log.Write($"Regra recusada: {ex.Message}");
                    rule = null;
                }
                if (rule is null)
                {
                    Toast(source, "error", "A regra não foi salva: algum valor está em formato inválido.");
                    break;
                }
                if (rule.GetType().GetProperty("DeviceId")?.GetValue(rule) is null && rule is not ForaMaisFrescoConfig)
                {
                    Toast(source, "error", "A regra não foi salva: escolha o aparelho.");
                    break;
                }
                await climate.SaveRuleAsync(rule);
                Toast(source, "ok", "Regra salva.");
                break;
            case "deleteRule":
                await climate.DeleteRuleAsync(S("id"));
                Toast(source, "ok", "Regra excluída.");
                break;
            case "toggleRule":
                await climate.ToggleRuleAsync(S("id"), m.GetProperty("enabled").GetBoolean());
                break;
            case "saveSettings":
                var p = m.GetProperty("settings");
                await climate.UpdateSettingsAsync(s => s with
                {
                    StartWithWindows = Bool(p, "startWithWindows") ?? s.StartWithWindows,
                    WeatherIntervalMin = Int(p, "weatherIntervalMin") ?? s.WeatherIntervalMin,
                    DeviceIntervalMin = Int(p, "deviceIntervalMin") ?? s.DeviceIntervalMin,
                    Notifications = Bool(p, "notifications") ?? s.Notifications,
                    AverageWatts = Dbl(p, "averageWatts") ?? s.AverageWatts,
                    TariffPerKwh = Dbl(p, "tariffPerKwh") ?? s.TariffPerKwh,
                    PcDeviceId = p.TryGetProperty("pcDeviceId", out var pc) ? pc.GetString() : s.PcDeviceId,
                    Theme = p.TryGetProperty("theme", out var th) && th.GetString() is "system" or "light" or "dark" ? th.GetString()! : s.Theme,
                });
                break;
            case "searchCity":
                var cities = await climate.SearchCityAsync(S("query"));
                source.Post("cities", JsonSerializer.Serialize(cities, Json.Options));
                break;
            case "setCity":
                var city = m.GetProperty("city").Deserialize<Place>(Json.Options);
                await climate.UpdateSettingsAsync(s => s with { City = city });
                Toast(source, "ok", $"Clima de {city?.Name} configurado.");
                break;
            case "setCredentials":
                var (ok, msg) = await climate.SetCredentialsAsync(S("apiKey"), S("refreshToken"));
                Toast(source, ok ? "ok" : "error", msg);
                break;
            case "checkUpdate":
                if (CheckUpdate is { } check)
                {
                    var (okUpd, text) = await check();
                    Toast(source, okUpd ? "ok" : "error", text);
                }
                break;
            case "installUpdate":
                if (InstallUpdate is { } install) await install();
                break;
            case "quit":
                quit();
                break;
        }
    }

    private static void Toast(WebWindow w, string kind, string text) =>
        w.Post("toast", JsonSerializer.Serialize(new { kind, text }));

    public static DeviceCommand? ParseCommand(JsonElement c)
    {
        DeviceCommand? cmd = c.GetProperty("kind").GetString() switch
        {
            "power" => new DeviceCommand.Power(c.GetProperty("on").GetBoolean()),
            "mode" => new DeviceCommand.SetMode(ElectroluxMapping.ParseMode(c.GetProperty("value").GetString())),
            "fan" => new DeviceCommand.SetFan(ElectroluxMapping.ParseFan(c.GetProperty("value").GetString())),
            "target" => new DeviceCommand.SetTarget(c.GetProperty("value").GetInt32()),
            "timer" => new DeviceCommand.SetStopTimer(c.GetProperty("seconds").GetInt32()),
            "oscillate" => new DeviceCommand.SetOscillate(c.GetProperty("on").GetBoolean()),
            "eco" => new DeviceCommand.SetEnergySaving(c.GetProperty("on").GetBoolean()),
            _ => null,
        };
        return cmd is DeviceCommand.SetMode { Mode: AcMode.Unknown } or DeviceCommand.SetFan { Fan: FanSpeed.Unknown } ? null : cmd;
    }

    private static bool? Bool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
    private static int? Int(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    private static double? Dbl(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
