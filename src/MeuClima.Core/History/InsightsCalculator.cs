using MeuClima.Core.Devices;
using MeuClima.Core.Rules;

namespace MeuClima.Core.History;

/// <summary>Cálculos dos insights a partir do histórico. Funções puras, testáveis sem banco.</summary>
public static class InsightsCalculator
{
    /// <summary>Intervalo maior que isso entre leituras é tratado como lacuna (app fechado, PC desligado).</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(5);

    public static TimeSpan Runtime(IReadOnlyList<HistorySample> samples, DateTimeOffset from, DateTimeOffset to)
    {
        var total = TimeSpan.Zero;
        for (var i = 0; i < samples.Count - 1; i++)
        {
            var a = samples[i];
            if (!a.On || a.Time >= to) continue;
            var ini = a.Time < from ? from : a.Time;
            var fim = samples[i + 1].Time > to ? to : samples[i + 1].Time;
            if (fim <= ini || samples[i + 1].Time - a.Time > MaxGap) continue;
            total += fim - ini;
        }
        return total;
    }

    public static double EnergyKwh(TimeSpan runtime, double averageWatts) => runtime.TotalHours * averageWatts / 1000.0;

    /// <summary>Minutos médios para o ambiente cair 1 °C com o ar ligado em Frio.</summary>
    /// <remarks>O sensor é inteiro: mede o tempo entre degraus consecutivos de queda dentro da mesma sessão.</remarks>
    public static double? MinutesPerDegree(IReadOnlyList<HistorySample> samples)
    {
        var duracoes = new List<double>();
        DateTimeOffset? ultimoDegrau = null;
        int? ultimoValor = null;
        HistorySample? anterior = null;
        foreach (var s in samples)
        {
            var esfriando = s.On && s.Mode == AcMode.Cool && s.AmbientC is not null;
            var quebra = anterior is null || s.Time - anterior.Time > MaxGap || !esfriando;
            if (quebra)
            {
                ultimoDegrau = null;
                ultimoValor = esfriando ? s.AmbientC : null;
                anterior = s;
                continue;
            }
            if (s.AmbientC < ultimoValor)
            {
                if (ultimoDegrau is { } d && ultimoValor - s.AmbientC == 1) duracoes.Add((s.Time - d).TotalMinutes);
                ultimoDegrau = s.Time;
            }
            else if (s.AmbientC > ultimoValor)
            {
                ultimoDegrau = null;
            }
            ultimoValor = s.AmbientC;
            anterior = s;
        }
        return duracoes.Count == 0 ? null : duracoes.Average();
    }

    public static int Disconnections(IReadOnlyList<HistorySample> samples)
    {
        var n = 0;
        for (var i = 1; i < samples.Count; i++)
            if (samples[i - 1].Connected && !samples[i].Connected) n++;
        return n;
    }

    public static DateTimeOffset? DisconnectedSince(IReadOnlyList<HistorySample> samples)
    {
        DateTimeOffset? desde = null;
        foreach (var s in samples) desde = s.Connected ? null : desde ?? s.Time;
        return desde;
    }

    /// <summary>Tempo em que "Saí do PC" manteve o aparelho desligado (pares desligou → voltou).</summary>
    public static TimeSpan AwayOffTime(IReadOnlyList<EventRecord> events, DateTimeOffset now)
    {
        var total = TimeSpan.Zero;
        DateTimeOffset? desligou = null;
        foreach (var e in events.Where(e => e.Kind.StartsWith("sai-do-pc", StringComparison.Ordinal)))
        {
            if (e.Kind == "sai-do-pc:desligou") desligou ??= e.Time;
            else if (e.Kind == "sai-do-pc:voltou" && desligou is { } d)
            {
                total += e.Time - d;
                desligou = null;
            }
        }
        if (desligou is { } aberto) total += now - aberto;
        return total;
    }
}
