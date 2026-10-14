using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MeuClima.Core.Devices;
using MeuClima.Core.Rules;
using MeuClima.Core.Weather;

namespace MeuClima.Host;

public static class AppPaths
{
    public static string Data { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeuClima"));

    public static string Local { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeuClima"));

    public static string Settings => Path.Combine(Data, "settings.json");
    public static string RuleState => Path.Combine(Data, "rules-state.json");
    public static string Credentials => Path.Combine(Data, "credentials.dat");
    public static string History => Path.Combine(Data, "history.db");
    public static string Log => Path.Combine(Local, "meuclima.log");
    public static string WebView2Data => Path.Combine(Local, "WebView2");

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public sealed record AppSettings
{
    public Place? City { get; init; }
    public string? PcDeviceId { get; init; }
    public bool StartWithWindows { get; init; }
    public bool CheckUpdates { get; init; } = true;
    /// <summary>Carimbo da última escolha do instalador já aplicada (App.ApplyInstallerStartupChoice).</summary>
    public string? InstallerChoiceApplied { get; init; }
    public int WeatherIntervalMin { get; init; } = 15;
    public int DeviceIntervalMin { get; init; } = 1;
    public bool Notifications { get; init; } = true;
    public double AverageWatts { get; init; } = 800;
    public double TariffPerKwh { get; init; } = 0.85;
    /// <summary><c>system</c> (segue o Windows), <c>light</c> ou <c>dark</c>.</summary>
    public string Theme { get; init; } = "system";
    public bool DefaultRulesCreated { get; init; }
    public List<RuleConfig> Rules { get; init; } = [];
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, AllowOutOfOrderMetadataProperties = true };
        o.Converters.Add(new JsonStringEnumConverter<AcMode>());
        o.Converters.Add(new JsonStringEnumConverter<FanSpeed>());
        return o;
    }
}

public sealed class SettingsStore
{
    private readonly Lock _lock = new();
    public AppSettings Current { get; private set; } = Load();

    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        lock (_lock)
        {
            Current = change(Current);
            var tmp = AppPaths.Settings + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json.Options));
            File.Move(tmp, AppPaths.Settings, overwrite: true);
            return Current;
        }
    }

    private static AppSettings Load()
    {
        try
        {
            return File.Exists(AppPaths.Settings)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.Settings), Json.Options) ?? new()
                : new();
        }
        catch (JsonException ex)
        {
            Log.Write($"settings.json inválido, usando padrão: {ex.Message}");
            File.Copy(AppPaths.Settings, AppPaths.Settings + ".corrompido", overwrite: true);
            return new();
        }
    }
}

public static class Log
{
    private static readonly Lock Gate = new();

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                var info = new FileInfo(AppPaths.Log);
                if (info.Exists && info.Length > 2_000_000) File.Move(AppPaths.Log, AppPaths.Log + ".1", overwrite: true);
                File.AppendAllText(AppPaths.Log, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (IOException) { }
        }
    }
}
