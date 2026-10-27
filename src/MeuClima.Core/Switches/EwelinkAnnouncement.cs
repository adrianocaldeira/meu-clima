using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeuClima.Core.Switches;

public enum AnnouncementFailure { SemChave, ChaveNaoDecifra, EstadoIlegivel }

/// <summary>Estado de um interruptor eWeLink tirado do anúncio mDNS; <see cref="On"/> nulo quando não deu para ler.</summary>
public sealed record EwelinkAnnouncement(
    string DeviceId, string Ip, long Seq, bool? On, bool Multicanal, DateTimeOffset VistoEm, AnnouncementFailure? Falha = null)
{
    public static EwelinkAnnouncement? From(IReadOnlyDictionary<string, string> txt, string ip, DateTimeOffset now, Func<string, string?> keyFor)
    {
        if (!txt.TryGetValue("id", out var id) || !txt.ContainsKey("type")) return null;
        long.TryParse(txt.GetValueOrDefault("seq"), out var seq);
        var raw = string.Concat(Enumerable.Range(1, 4).Select(i => txt.GetValueOrDefault($"data{i}") ?? ""));

        string json;
        if (txt.GetValueOrDefault("encrypt") == "true")
        {
            if (keyFor(id) is not { } key) return new(id, ip, seq, null, false, now, AnnouncementFailure.SemChave);
            // ArgumentException: IV com tamanho diferente de 16 bytes.
            try { json = EwelinkCrypto.Decrypt(raw, txt.GetValueOrDefault("iv") ?? "", key); }
            catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
            {
                return new(id, ip, seq, null, false, now, AnnouncementFailure.ChaveNaoDecifra);
            }
        }
        else json = raw;

        var (on, multicanal) = LerEstado(json);
        return new(id, ip, seq, on, multicanal, now, on is null ? AnnouncementFailure.EstadoIlegivel : null);
    }

    /// <summary>O pacote vem de qualquer host da rede: tipo inesperado vira "ilegível", nunca exceção.</summary>
    private static (bool? On, bool Multicanal) LerEstado(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject state) return (null, false);
            if (state["switches"] is JsonArray canais)
            {
                var canal0 = canais.OfType<JsonObject>().FirstOrDefault(c => c["outlet"] is JsonValue v && v.TryGetValue<int>(out var n) && n == 0);
                return (Ligado(canal0?["switch"]), true);
            }
            return (Ligado(state["switch"]), false);
        }
        catch (JsonException)
        {
            return (null, false);
        }
    }

    private static bool? Ligado(JsonNode? valor) =>
        valor is JsonValue v && v.TryGetValue<string>(out var s) ? s switch { "on" => true, "off" => false, _ => null } : null;
}
