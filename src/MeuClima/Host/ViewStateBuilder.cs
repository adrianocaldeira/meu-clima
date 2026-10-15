using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeuClima.Core.Devices;
using MeuClima.Core.History;
using MeuClima.Core.Rules;
using MeuClima.Core.Weather;

namespace MeuClima.Host;

/// <summary>Estado serializado para as telas; a versão impede que um estado velho substitua um novo.</summary>
public sealed record StateMessage(long Version, string Json);

public sealed record ViewInputs(
    AppSettings Settings,
    bool HasCredentials,
    string? AccountError,
    DateTimeOffset? UpdatedAt,
    WeatherSnapshot? Weather,
    IReadOnlyList<DeviceInfo> Devices,
    IReadOnlyDictionary<string, DeviceSnapshot> Readings,
    IReadOnlyDictionary<string, DeviceCapabilities> Caps,
    RuleEngine Engine,
    EngineResult? LastResult,
    HistoryStore History,
    DateTimeOffset Now);

/// <summary>Monta o JSON <c>ViewState</c> do contrato em docs/specs/2026-10-14-ponte-ui.md.</summary>
public static class ViewStateBuilder
{
    public static string Hm(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, TimeZoneInfo.Local).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"14:02" hoje, "ontem 17:53" ou "13/10 17:53": para quando o horário pode ser de outro dia.</summary>
    public static string QuandoDesde(DateTimeOffset t, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(t, TimeZoneInfo.Local);
        var dias = (TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).Date - local.Date).Days;
        var hm = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        return dias switch { <= 0 => hm, 1 => $"ontem {hm}", _ => $"{local:dd/MM} {hm}" };
    }

    public static string Build(ViewInputs v)
    {
        var s = v.Settings;
        var tz = TimeZoneInfo.Local;
        var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(v.Now, tz).DateTime);

        object? weather = v.Weather is { } w ? new
        {
            city = s.City is { } c ? $"{c.Name} / {c.Region}" : null,
            tempC = Math.Round(w.TempC, 1),
            apparentC = Math.Round(w.ApparentC, 1),
            humidity = w.HumidityPct,
            readAt = Hm(w.ReadAt),
            peakToday = Peak(w.PeakOn(hoje, tz)),
            peakTomorrow = Peak(w.PeakOn(hoje.AddDays(1), tz)),
        } : null;

        var devices = v.Devices.Select(d =>
        {
            v.Readings.TryGetValue(d.Id, out var r);
            var exp = r is null ? null : v.Engine.Expected(r, v.Now);
            v.Caps.TryGetValue(d.Id, out var cap);
            var desde = InsightsCalculator.DisconnectedSince(v.History.Samples(d.Id, v.Now.AddDays(-2)));
            var noite = v.Engine.Rules.OfType<ModoNoiteRule>().Any(n => n.Cfg.DeviceId == d.Id && n.Active);
            return new
            {
                id = d.Id,
                name = d.Name,
                model = cap?.Model,
                connected = exp?.Connected ?? false,
                disconnectedSince = exp is { Connected: false } && desde is { } ds ? QuandoDesde(ds, v.Now) : null,
                on = exp?.On ?? false,
                mode = ModeApi(exp?.Mode),
                fan = FanApi(exp?.Fan),
                targetC = exp?.TargetC,
                ambientC = exp?.AmbientC,
                eco = exp?.EnergySaving,
                oscillate = exp?.Oscillate,
                stopTimeSec = exp?.StopTimeSeconds ?? 0,
                filter = exp?.FilterState,
                alerts = exp?.Alerts ?? [],
                pending = r is not null && exp != r,
                pausedUntil = v.Engine.PausedUntil(d.Id, v.Now) is { } p ? Hm(p) : null,
                isPcRoom = d.Id == s.PcDeviceId,
                nightMode = noite,
                caps = new
                {
                    modes = (cap?.Modes ?? [AcMode.Auto, AcMode.Cool, AcMode.FanOnly]).Select(m => ModeApi(m)),
                    fans = (cap?.FanSpeeds ?? [FanSpeed.Auto, FanSpeed.Low, FanSpeed.Middle, FanSpeed.High, FanSpeed.Turbo]).Select(f => FanApi(f)),
                    min = cap?.MinTargetC ?? 16,
                    max = cap?.MaxTargetC ?? 30,
                },
            };
        }).ToList();

        var active = (v.LastResult?.Active ?? []).Select(a => new
        {
            ruleId = a.RuleId,
            deviceId = a.DeviceId,
            title = a.Status.Title,
            detail = a.Status.Detail,
            progress = a.Status.Progress is { } pr ? Math.Round(pr, 2) : (double?)null,
            blockedBy = a.BlockedBy,
            kind = a.Kind switch { ActiveKind.Watching => "watching", ActiveKind.Waiting => "waiting", _ => "acting" },
        });

        var rules = v.Engine.Rules
            .OrderBy(r => s.Rules.FindIndex(c => c.Id == r.Config.Id))
            .Select(r => new
            {
                id = r.Config.Id,
                tipo = Tipo(r.Config),
                name = r.Name,
                enabled = r.Config.Enabled,
                summary = Summary(r.Config, v.Devices),
                config = JsonSerializer.SerializeToNode(r.Config, typeof(RuleConfig), Json.Options),
            });

        var state = new
        {
            updatedAt = v.UpdatedAt is { } u ? Hm(u) : null,
            setup = new { credentials = v.HasCredentials, city = s.City is not null, cityName = s.City is { } cn ? $"{cn.Name} / {cn.Region}" : null, pcDevice = s.PcDeviceId is not null },
            account = new { error = v.AccountError },
            weather,
            devices,
            activeRules = active,
            rules,
            settings = new
            {
                startWithWindows = s.StartWithWindows,
                weatherIntervalMin = s.WeatherIntervalMin,
                deviceIntervalMin = s.DeviceIntervalMin,
                notifications = s.Notifications,
                averageWatts = s.AverageWatts,
                tariffPerKwh = s.TariffPerKwh,
                pcDeviceId = s.PcDeviceId,
                theme = s.Theme,
                checkUpdates = s.CheckUpdates,
            },
            version = ClimateService.VersionLabel,
            update = UpdateState.Current,
        };
        return JsonSerializer.Serialize(state, Json.Options);
    }

    private static object? Peak(HourlyPoint? p) => p is null ? null : new { tempC = Math.Round(p.TempC, 1), time = Hm(p.Time) };

    public static string? ModeApi(AcMode? m) => m switch
    {
        AcMode.Auto => "AUTO", AcMode.Cool => "COOL", AcMode.Heat => "HEAT", AcMode.FanOnly => "FANONLY", AcMode.Dry => "DRY", _ => null,
    };

    public static string? FanApi(FanSpeed? f) => f switch
    {
        FanSpeed.Auto => "AUTO", FanSpeed.Low => "LOW", FanSpeed.Middle => "MIDDLE", FanSpeed.High => "HIGH", FanSpeed.Turbo => "TURBO", _ => null,
    };

    public static string Tipo(RuleConfig c) =>
        JsonSerializer.SerializeToNode(c, typeof(RuleConfig), Json.Options)?["tipo"]?.GetValue<string>() ?? "";

    private static string Summary(RuleConfig c, IReadOnlyList<DeviceInfo> devices)
    {
        string N(string id) => devices.FirstOrDefault(d => d.Id == id)?.Name ?? "aparelho removido";
        string Bloqueio(SaiDoPcConfig x) => !x.OffOnLock ? "" : x.OffOnLockMin == 0 ? "tela bloqueada → desliga na hora; " : $"tela bloqueada {x.OffOnLockMin} min → desliga; ";
        return c switch
        {
            ConfortoConfig x => $"{N(x.DeviceId)} · faixa {x.MinC}–{x.MaxC} °C · frio econômico em {x.MaxC} °C" +
                                (x.FanAtMin ? $", ventila em {x.MinC} °C" : "") + $", turbo a partir de {x.TurboFromC} °C",
            SaiDoPcConfig x => $"{N(x.DeviceId)} · {Bloqueio(x)}sem uso {x.EcoAfterMin} min → {x.EcoTargetC} °C, {x.OffAfterMin} min → desliga",
            JanelaAbertaConfig x => $"{N(x.DeviceId)} · sobe {x.RiseC} °C em {x.WindowMin} min no frio → desliga",
            ModoNoiteConfig x => $"{N(x.DeviceId)} · +{x.StepC} °C a cada {x.StepEveryMin} min, desliga em {x.DurationHours} h" +
                                 (x.DailyStart is { } h ? $" · todo dia às {h:HH\\:mm}" : " · ativação manual"),
            _ => "",
        };
    }
}

/// <summary>Estado da autoatualização exposto à tela; preenchido pela integração do UpdateService.</summary>
public static class UpdateState
{
    public static object Current { get; set; } = new { available = false, version = (string?)null, downloading = false };
}
