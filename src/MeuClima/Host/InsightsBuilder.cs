using System.Globalization;
using System.Text.Json;
using MeuClima.Core.Devices;
using MeuClima.Core.History;
using MeuClima.Core.Rules;
using MeuClima.Core.Weather;

namespace MeuClima.Host;

/// <summary>Monta o JSON <c>Insights</c> do contrato da ponte.</summary>
public static class InsightsBuilder
{
    private const int MaxPoints = 300;

    public static string Build(AppSettings s, IReadOnlyList<DeviceInfo> devices, IReadOnlyDictionary<string, DeviceSnapshot> readings,
        HistoryStore history, WeatherSnapshot? weather, RuleEngine engine, DateTimeOffset now, int days)
    {
        var tz = TimeZoneInfo.Local;
        var localNow = TimeZoneInfo.ConvertTime(now, tz);
        var meiaNoite = new DateTimeOffset(localNow.Date, localNow.Offset);
        var ontem = meiaNoite.AddDays(-1);
        var from = now.AddDays(-Math.Clamp(days, 1, 30));

        var amostras = devices.ToDictionary(d => d.Id, d => history.Samples(d.Id, now.AddDays(-30)));
        TimeSpan Soma(DateTimeOffset a, DateTimeOffset b) =>
            amostras.Values.Aggregate(TimeSpan.Zero, (acc, x) => acc + InsightsCalculator.Runtime(x, a, b));
        var hoje = Soma(meiaNoite, now);
        var ontemT = Soma(ontem, meiaNoite);
        var kwh = InsightsCalculator.EnergyKwh(hoje, s.AverageWatts);

        var refId = s.PcDeviceId ?? devices.FirstOrDefault()?.Id;
        var refSamples = refId is null ? [] : amostras[refId];
        var mpd7 = InsightsCalculator.MinutesPerDegree(refSamples.Where(x => x.Time >= now.AddDays(-7)).ToList());
        var mpd30 = InsightsCalculator.MinutesPerDegree(refSamples);

        var chart = new
        {
            from = from.ToString("O", CultureInfo.InvariantCulture),
            to = now.ToString("O", CultureInfo.InvariantCulture),
            outside = Downsample(history.Outside(from).Select(o => (o.Time, o.TempC)).ToList())
                .Select(p => new object[] { p.Time.ToString("O", CultureInfo.InvariantCulture), Math.Round(p.Value, 1) }),
            devices = devices.Select(d => new
            {
                id = d.Id,
                name = d.Name,
                points = DownsampleDevice(amostras[d.Id].Where(x => x.Time >= from && x.AmbientC is not null).ToList()),
            }),
        };

        var items = new List<object>();
        foreach (var d in devices)
        {
            var am = amostras[d.Id];
            var semana = am.Where(x => x.Time >= now.AddDays(-7)).ToList();
            var quedas = InsightsCalculator.Disconnections(semana);
            if (InsightsCalculator.DisconnectedSince(am) is { } desde)
                items.Add(Item("warn", $"{d.Name} está sem conexão desde {ViewStateBuilder.Hm(desde)}",
                    quedas > 1 ? $"Caiu {quedas} vezes nos últimos 7 dias. Verifique o sinal do Wi-Fi perto do aparelho." : "Confira se o aparelho está na tomada e com Wi-Fi."));
            else if (quedas >= 3)
                items.Add(Item("warn", $"{d.Name} perdeu a conexão {quedas} vezes nesta semana", "Sinal de Wi-Fi fraco perto do aparelho costuma causar isso."));

            if (readings.TryGetValue(d.Id, out var r))
            {
                if (r.FilterState is { } f && !f.Equals("GOOD", StringComparison.OrdinalIgnoreCase))
                    items.Add(Item("warn", $"Filtro do {d.Name}: {FiltroTexto(f)}", "Filtro sujo aumenta o consumo e demora mais para esfriar."));
                if (r.Alerts.Count > 0)
                    items.Add(Item("warn", $"{d.Name} reportou alerta", string.Join(", ", r.Alerts)));
            }
        }

        if (mpd7 is { } a && mpd30 is { } b && a > b * 1.15 && refId is not null)
            items.Add(Item("cool", $"{Nome(devices, refId)} está esfriando {Math.Round((a / b - 1) * 100)}% mais devagar",
                $"Leva {a:0.#} min por grau nos últimos 7 dias contra {b:0.#} min no mês. Costuma ser filtro sujo, mesmo com o aparelho dizendo que está ok."));

        var longe = InsightsCalculator.AwayOffTime(history.Events(now.AddDays(-7)), now);
        if (longe >= TimeSpan.FromMinutes(30))
            items.Add(Item("ok", $"\"Saí do PC\" manteve o ar desligado por {longe.TotalHours:0.#} h nesta semana",
                $"Cerca de {InsightsCalculator.EnergyKwh(longe, s.AverageWatts):0.#} kWh a menos, pela estimativa."));

        if (ontemT > TimeSpan.FromHours(1) && hoje > ontemT * 1.3)
            items.Add(Item("cool", "O ar ficou mais tempo ligado hoje", $"{hoje.TotalHours:0.#} h hoje contra {ontemT.TotalHours:0.#} h ontem."));

        if (weather?.PeakOn(DateOnly.FromDateTime(localNow.Date).AddDays(1), tz) is { } pico)
        {
            var antes = engine.Rules.OfType<AntesDoPicoRule>().FirstOrDefault();
            var texto = antes is { Cfg.Enabled: true } && pico.TempC >= antes.Cfg.ForecastThresholdC
                ? $"\"Antes do pico\" vai ligar o ar {antes.Cfg.LeadMin} min antes, em {antes.Cfg.TargetC} °C."
                : pico.TempC >= 30 ? "Ative \"Antes do pico\" para resfriar antes do calor, sem precisar do turbo." : "Dia ameno, sem necessidade de pré-resfriar.";
            items.Add(Item("sun", $"Amanhã o pico será de {pico.TempC:0} °C às {ViewStateBuilder.Hm(pico.Time)}", texto));
        }

        return JsonSerializer.Serialize(new
        {
            kpis = new
            {
                runtimeTodayH = Math.Round(hoje.TotalHours, 1),
                runtimeYesterdayH = Math.Round(ontemT.TotalHours, 1),
                energyTodayKwh = Math.Round(kwh, 1),
                costToday = Math.Round(kwh * s.TariffPerKwh, 2),
                minPerDegree7d = mpd7 is { } m7 ? Math.Round(m7, 1) : (double?)null,
                minPerDegree30d = mpd30 is { } m30 ? Math.Round(m30, 1) : (double?)null,
            },
            chart,
            items,
        }, Json.Options);
    }

    private static object Item(string kind, string title, string body) => new { kind, title, body };

    private static string Nome(IReadOnlyList<DeviceInfo> devices, string id) => devices.FirstOrDefault(d => d.Id == id)?.Name ?? id;

    private static string FiltroTexto(string f) => f.ToUpperInvariant() switch
    {
        "CLEAN" => "precisa limpar", "CHANGE" => "precisa trocar", "BUY" => "compre um novo", _ => f.ToLowerInvariant(),
    };

    private static List<(DateTimeOffset Time, double Value)> Downsample(List<(DateTimeOffset Time, double Value)> pts)
    {
        if (pts.Count <= MaxPoints) return pts;
        var step = (int)Math.Ceiling(pts.Count / (double)MaxPoints);
        return pts.Chunk(step).Select(c => (c[c.Length / 2].Time, c.Average(p => p.Value))).ToList();
    }

    private static IEnumerable<object[]> DownsampleDevice(List<HistorySample> pts)
    {
        var step = Math.Max(1, (int)Math.Ceiling(pts.Count / (double)MaxPoints));
        return pts.Chunk(step).Select(c => new object[]
        {
            c[c.Length / 2].Time.ToString("O", CultureInfo.InvariantCulture),
            Math.Round(c.Average(p => p.AmbientC!.Value), 1),
            c.Count(p => p.On) * 2 >= c.Length,
        });
    }
}
