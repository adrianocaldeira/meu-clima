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
    /// <summary>Correção da leitura de dentro por aparelho, em °C inteiros (de -3 a +3).</summary>
    public Dictionary<string, int> SensorOffsets { get; init; } = [];

    /// <summary>Quando cada correção mudou: o histórico anterior está em outra escala, inclusive depois de reiniciar.</summary>
    public Dictionary<string, DateTimeOffset> SensorOffsetsDesde { get; init; } = [];
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

    public SettingsStore() => (Current, ReadOnly) = Load();

    public AppSettings Current { get; private set; }

    /// <summary>settings.json de uma versão mais nova: o app roda sem regras e não grava por cima.</summary>
    public bool ReadOnly { get; }

    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        lock (_lock)
        {
            Current = change(Current);
            if (ReadOnly) return Current;
            var tmp = AppPaths.Settings + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json.Options));
            File.Move(tmp, AppPaths.Settings, overwrite: true);
            return Current;
        }
    }

    private static (AppSettings, bool) Load()
    {
        if (!File.Exists(AppPaths.Settings)) return (new(), false);
        var texto = File.ReadAllText(AppPaths.Settings);
        try
        {
            return (JsonSerializer.Deserialize<AppSettings>(texto, Json.Options) ?? new(), false);
        }
        // NotSupportedException: regra sem "tipo" (o System.Text.Json não cria a classe abstrata).
        catch (Exception ex) when (ex is JsonException or NotSupportedException && JsonValido(texto))
        {
            // JSON íntegro que esta versão não entende (ex.: regra criada por versão mais nova): recriar as regras
            // padrão e gravar por cima apagaria a configuração do usuário.
            Log.Write($"settings.json de outra versão, abrindo só para leitura e sem regras: {ex.Message}");
            return (new() { DefaultRulesCreated = true }, true);
        }
        catch (JsonException ex)
        {
            Log.Write($"settings.json inválido, usando padrão: {ex.Message}");
            File.Copy(AppPaths.Settings, AppPaths.Settings + ".corrompido", overwrite: true);
            return (new(), false);
        }
    }

    private static bool JsonValido(string texto)
    {
        try
        {
            using var _ = JsonDocument.Parse(texto);
            return true;
        }
        catch (JsonException) { return false; }
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
