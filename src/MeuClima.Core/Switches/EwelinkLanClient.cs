using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeuClima.Core.Switches;

/// <summary><see cref="Error"/> é o código do aparelho; nulo quando nem houve resposta (rede ou prazo).</summary>
public sealed record SwitchResult(bool Ok, int? Error = null, string? Detail = null);

/// <summary>Comando do modo LAN eWeLink: <c>POST http://ip:8081/zeroconf/switch[es]</c> com o corpo cifrado.</summary>
public sealed class EwelinkLanClient(HttpClient http, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<SwitchResult> SetSwitchAsync(string ip, string deviceId, string deviceKey, bool multicanal, bool on,
        TimeSpan timeout, CancellationToken ct = default)
    {
        var valor = on ? "on" : "off";
        var (caminho, estado) = multicanal
            ? ("switches", new JsonObject { ["switches"] = new JsonArray(new JsonObject { ["switch"] = valor, ["outlet"] = 0 }) })
            : ("switch", new JsonObject { ["switch"] = valor });
        var (data, iv) = EwelinkCrypto.Encrypt(estado.ToJsonString(), deviceKey);
        var corpo = new JsonObject
        {
            ["sequence"] = _clock.GetUtcNow().ToUnixTimeMilliseconds().ToString(),
            ["deviceid"] = deviceId,
            ["selfApikey"] = "123",
            ["iv"] = iv,
            ["encrypt"] = true,
            ["data"] = data,
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            // StringContent leva Content-Length; o JsonContent sai chunked e o aparelho fica mudo (medido em 27/10/2026).
            using var conteudo = new StringContent(corpo.ToJsonString(), Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync($"http://{ip}:8081/zeroconf/{caminho}", conteudo, cts.Token);
            var json = await resp.Content.ReadFromJsonAsync<JsonObject>(cts.Token);
            var erro = (int?)json?["error"];
            return erro == 0 ? new SwitchResult(true, 0) : new SwitchResult(false, erro ?? (int)resp.StatusCode, $"HTTP {(int)resp.StatusCode}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new SwitchResult(false, Detail: "sem resposta no prazo");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return new SwitchResult(false, Detail: ex.GetType().Name);
        }
    }
}
