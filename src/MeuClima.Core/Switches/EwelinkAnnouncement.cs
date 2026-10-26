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
            try { json = EwelinkCrypto.Decrypt(raw, txt.GetValueOrDefault("iv") ?? "", key); }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                return new(id, ip, seq, null, false, now, AnnouncementFailure.ChaveNaoDecifra);
            }
        }
        else json = raw;

        try
        {
            var state = JsonNode.Parse(json);
            if (state?["switches"] is JsonArray canais)
                return new(id, ip, seq, Ligado(canais.FirstOrDefault(c => (int?)c?["outlet"] == 0)?["switch"]), true, now);
            return new(id, ip, seq, Ligado(state?["switch"]), false, now);
        }
        catch (JsonException)
        {
            return new(id, ip, seq, null, false, now, AnnouncementFailure.EstadoIlegivel);
        }
    }

    private static bool? Ligado(JsonNode? valor) => (string?)valor switch
    {
        "on" => true,
        "off" => false,
        _ => null,
    };
}
