using System.Globalization;
using System.Text.Json.Nodes;

namespace MeuClima.Core.Weather;

public sealed record HourlyPoint(DateTimeOffset Time, double TempC, double? ApparentC = null);

public sealed record WeatherSnapshot(
    double TempC,
    double ApparentC,
    int HumidityPct,
    IReadOnlyList<HourlyPoint> Hourly,
    DateTimeOffset ReadAt)
{
    /// <summary>Maior temperatura prevista no dia local de <paramref name="day"/>.</summary>
    public HourlyPoint? PeakOn(DateOnly day, TimeZoneInfo tz) =>
        Hourly.Where(h => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(h.Time, tz).DateTime) == day)
              .MaxBy(h => h.TempC);

    /// <summary>Maior sensação térmica prevista no dia local (sem sensação, usa a temperatura).</summary>
    public double? MaxApparentOn(DateOnly day, TimeZoneInfo tz) =>
        Hourly.Where(h => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(h.Time, tz).DateTime) == day)
              .Select(h => (double?)(h.ApparentC ?? h.TempC)).Max();
}

public sealed record Place(string Name, string Region, double Latitude, double Longitude, string TimeZone);

/// <summary>Clima atual, previsão horária e geocodificação via Open-Meteo (sem chave, uso não comercial).</summary>
public sealed class OpenMeteoClient(HttpClient http, TimeProvider clock)
{
    public async Task<WeatherSnapshot> GetAsync(double lat, double lon, CancellationToken ct = default)
    {
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,relative_humidity_2m,apparent_temperature&hourly=temperature_2m,apparent_temperature&forecast_days=2&timezone=UTC");
        var json = JsonNode.Parse(await http.GetStringAsync(url, ct))!;
        return Parse(json, clock.GetUtcNow());
    }

    public async Task<IReadOnlyList<Place>> SearchAsync(string name, CancellationToken ct = default)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name)}&count=5&language=pt";
        var json = JsonNode.Parse(await http.GetStringAsync(url, ct))!;
        if (json["results"] is not JsonArray arr) return [];
        return arr.Select(r => new Place(
                r!["name"]!.GetValue<string>(),
                string.Join(" / ", new[] { r["admin1"]?.GetValue<string>(), r["country_code"]?.GetValue<string>() }
                    .Where(s => !string.IsNullOrEmpty(s))),
                r["latitude"]!.GetValue<double>(),
                r["longitude"]!.GetValue<double>(),
                r["timezone"]?.GetValue<string>() ?? "UTC"))
            .ToList();
    }

    public static WeatherSnapshot Parse(JsonNode json, DateTimeOffset readAt)
    {
        var cur = json["current"]!;
        var times = json["hourly"]!["time"]!.AsArray();
        var temps = json["hourly"]!["temperature_2m"]!.AsArray();
        var sente = json["hourly"]!["apparent_temperature"] as JsonArray;
        var hourly = new List<HourlyPoint>(times.Count);
        for (var i = 0; i < times.Count; i++)
        {
            if (temps[i] is null) continue;
            // timezone=UTC na URL: horários vêm sem offset e são UTC.
            var t = DateTime.Parse(times[i]!.GetValue<string>(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            var aparente = sente is not null && i < sente.Count ? sente[i]?.GetValue<double>() : null;
            hourly.Add(new HourlyPoint(new DateTimeOffset(t, TimeSpan.Zero), temps[i]!.GetValue<double>(), aparente));
        }
        return new WeatherSnapshot(
            cur["temperature_2m"]!.GetValue<double>(),
            cur["apparent_temperature"]!.GetValue<double>(),
            (int)Math.Round(cur["relative_humidity_2m"]!.GetValue<double>()),
            hourly,
            readAt);
    }
}
