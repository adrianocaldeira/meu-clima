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
        double Energia(DateTimeOffset a, DateTimeOffset b) =>
            amostras.Values.Sum(x => InsightsCalculator.EnergyKwh(x, a, b, s.AverageWatts));
        var kwh = Energia(meiaNoite, now);

        // Projeção do mês pela média diária dos últimos 7 dias (ou menos, se o histórico for mais curto).
        var primeira = amostras.Values.Where(x => x.Count > 0).Select(x => x[0].Time).DefaultIfEmpty(now).Min();
        var diasMedidos = Math.Min(7, (now - primeira).TotalDays);
        double? kwhMes = diasMedidos >= 1
            ? Energia(now.AddDays(-diasMedidos), now) / diasMedidos * DateTime.DaysInMonth(localNow.Year, localNow.Month)
            : null;

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
                items.Add(Item("warn", $"{d.Name} está sem conexão desde {ViewStateBuilder.QuandoDesde(desde, now)}",
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
            items.Add(Item("ok", $"\"Ausente\" manteve o ar desligado por {longe.TotalHours:0.#} h nesta semana",
                $"Cerca de {InsightsCalculator.EnergyKwh(longe, s.AverageWatts):0.#} kWh a menos, pela estimativa."));

        if (ontemT > TimeSpan.FromHours(1) && hoje > ontemT * 1.3)
            items.Add(Item("cool", "O ar ficou mais tempo ligado hoje", $"{hoje.TotalHours:0.#} h hoje contra {ontemT.TotalHours:0.#} h ontem."));

        var amanha = DateOnly.FromDateTime(localNow.Date).AddDays(1);
        if (weather?.PeakOn(amanha, tz) is { } pico)
        {
            var conforto = engine.Rules.OfType<ConfortoRule>().FirstOrDefault(r => r.Cfg.Enabled);
            // O Conforto antecipa por qualquer hora quente, não só a da máxima.
            var sensacao = weather.MaxApparentOn(amanha, tz) ?? pico.TempC;
            var texto = conforto is not null && sensacao >= conforto.Cfg.PreCoolApparentC
                ? $"Sensação prevista de {sensacao:0} °C: o Conforto liga o ar {conforto.Cfg.PreCoolLeadMin} min antes se dentro estiver em {conforto.Cfg.MaxC} °C."
                : sensacao >= 30 ? "Ative o Conforto para resfriar antes do calor, sem precisar do turbo." : "Dia ameno, sem necessidade de pré-resfriar.";
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
                energyMonthKwh = kwhMes is { } km ? Math.Round(km, 0) : (double?)null,
                costMonth = kwhMes is { } cm ? Math.Round(cm * s.TariffPerKwh, 0) : (double?)null,
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
