using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeuClima.Core.Devices;

namespace MeuClima.Core.Electrolux;

public sealed record ElectroluxCredentials(string ApiKey, string RefreshToken)
{
    // O ToString gerado pelo record imprimiria os segredos em qualquer log interpolado.
    public override string ToString() => "ElectroluxCredentials { *** }";
}

/// <summary>Guarda as credenciais; o refresh token muda a cada renovação e precisa ser salvo na hora.</summary>
public interface ITokenStore
{
    ElectroluxCredentials? Load();
    void Save(ElectroluxCredentials credentials);
}

public sealed class ElectroluxAuthException(string message) : Exception(message);

/// <summary>Cliente da Electrolux Group API (developer.electrolux.one).</summary>
public sealed class ElectroluxClient(HttpClient http, ITokenStore store, TimeProvider clock) : IClimateProvider
{
    public const string BaseUrl = "https://api.developer.electrolux.one/api/v1";
    private const int MaxAttempts = 3;

    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Campos de temperatura do estado reportado, como vieram, para diagnóstico do sensor.</summary>
    public event Action<string, string>? StateRead;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private ElectroluxCredentials? _unsaved;
    private string? _accessToken;
    private DateTimeOffset _accessExpiry = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default)
    {
        var json = await GetJsonAsync("/appliances", ct);
        var list = new List<DeviceInfo>();
        foreach (var a in json.AsArray())
        {
            var id = a?["applianceId"]?.GetValue<string>();
            if (id is null) continue;
            list.Add(new DeviceInfo(id, a!["applianceName"]?.GetValue<string>() ?? id, "Electrolux", ""));
        }
        return list;
    }

    public async Task<DeviceCapabilities> GetCapabilitiesAsync(string deviceId, CancellationToken ct = default)
    {
        var json = await GetJsonAsync($"/appliances/{Uri.EscapeDataString(deviceId)}/info", ct);
        return ElectroluxMapping.ParseCapabilities(json);
    }

    public async Task<DeviceSnapshot> GetStateAsync(string deviceId, CancellationToken ct = default)
    {
        var json = await GetJsonAsync($"/appliances/{Uri.EscapeDataString(deviceId)}/state", ct);
        StateRead?.Invoke(deviceId, ElectroluxMapping.TemperatureFields(json));
        return ElectroluxMapping.ParseState(deviceId, json, clock.GetUtcNow());
    }

    public async Task<CommandResult> SendAsync(string deviceId, DeviceCommand command, CancellationToken ct = default)
    {
        var body = ElectroluxMapping.ToCommandBody(command).ToJsonString();
        var url = $"{BaseUrl}/appliances/{Uri.EscapeDataString(deviceId)}/command";
        using var resp = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }, ct);
        return Resultado(resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Envio único no desligamento do PC: só com o access token em cache, sem renovar nem repetir.</summary>
    /// <remarks>
    /// Renovar gira o refresh token; se o Windows encerrar o processo antes de gravar o novo, a conta cai.
    /// Devolve null quando não há token válido para tentar.
    /// </remarks>
    public async Task<CommandResult?> SendWithoutRefreshAsync(string deviceId, DeviceCommand command, TimeSpan timeout)
    {
        var token = _accessToken;
        if (token is null || clock.GetUtcNow() >= _accessExpiry || CurrentCredentials is not { } creds) return null;
        using var cts = new CancellationTokenSource(timeout);
        using var req = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/appliances/{Uri.EscapeDataString(deviceId)}/command")
        {
            Content = new StringContent(ElectroluxMapping.ToCommandBody(command).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-api-key", creds.ApiKey);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var resp = await http.SendAsync(req, cts.Token);
        return Resultado(resp.StatusCode, await resp.Content.ReadAsStringAsync(cts.Token));
    }

    private static CommandResult Resultado(HttpStatusCode status, string text) =>
        status switch
        {
            HttpStatusCode.OK or HttpStatusCode.NoContent => new CommandResult(CommandOutcome.Applied),
            // 202 com "already in desired state": medido em 14/10/2026 ao reenviar valores vigentes.
            HttpStatusCode.Accepted => text.Contains("already", StringComparison.OrdinalIgnoreCase)
                ? new CommandResult(CommandOutcome.AlreadyInState)
                : new CommandResult(CommandOutcome.Applied, text),
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotAcceptable
                => new CommandResult(CommandOutcome.Rejected, text),
            _ => new CommandResult(CommandOutcome.Failed, $"HTTP {(int)status}: {text}"),
        };

    private async Task<JsonNode> GetJsonAsync(string path, CancellationToken ct)
    {
        using var resp = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, BaseUrl + path), ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"GET {path} devolveu HTTP {(int)resp.StatusCode}: {text}", null, resp.StatusCode);
        return JsonNode.Parse(text) ?? throw new InvalidDataException($"GET {path} devolveu corpo vazio");
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> build, CancellationToken ct)
    {
        var creds = store.Load() ?? throw new ElectroluxAuthException("Credenciais da Electrolux não configuradas.");
        for (var attempt = 1; ; attempt++)
        {
            var token = await GetAccessTokenAsync(ct);
            using var req = build();
            req.Headers.Add("x-api-key", creds.ApiKey);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var resp = await http.SendAsync(req, ct);

            if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt < MaxAttempts)
            {
                resp.Dispose();
                _accessExpiry = DateTimeOffset.MinValue;
                continue;
            }
            // 5xx/429 são transitórios (o /info deu 504 na primeira medição).
            var transient = (int)resp.StatusCode >= 500 || resp.StatusCode == HttpStatusCode.TooManyRequests;
            if (transient && attempt < MaxAttempts)
            {
                resp.Dispose();
                await Task.Delay(RetryBaseDelay * attempt, clock, ct);
                continue;
            }
            return resp;
        }
    }

    /// <summary>Credencial em uso: a girada ainda não gravada, se o disco falhou, senão a do store.</summary>
    public ElectroluxCredentials? CurrentCredentials => _unsaved ?? store.Load();

    /// <summary>Gravação do token girado falhou; o token segue em memória.</summary>
    public event Action<Exception>? SaveFailed;

    /// <summary>Passa a usar estas credenciais; se o disco falhar, ficam em memória. Devolve se gravou.</summary>
    public bool Adopt(ElectroluxCredentials creds)
    {
        ResetSession();
        _unsaved = creds;
        return TrySaveUnsaved();
    }

    private bool TrySaveUnsaved()
    {
        try { store.Save(_unsaved!); _unsaved = null; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            SaveFailed?.Invoke(ex);
            return false;
        }
    }

    /// <summary>Descarta o access token em cache (troca de conta).</summary>
    public void ResetSession()
    {
        _unsaved = null;
        _accessToken = null;
        _accessExpiry = DateTimeOffset.MinValue;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && clock.GetUtcNow() < _accessExpiry) return _accessToken;
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && clock.GetUtcNow() < _accessExpiry) return _accessToken;
            var creds = _unsaved ?? store.Load() ?? throw new ElectroluxAuthException("Credenciais da Electrolux não configuradas.");
            var payload = JsonSerializer.Serialize(new { refreshToken = creds.RefreshToken });
            // Sem ct: cancelar depois que a Electrolux girou o token perderia o novo e derrubaria a sessão.
            using var resp = await http.PostAsync($"{BaseUrl}/token/refresh",
                new StringContent(payload, Encoding.UTF8, "application/json"), CancellationToken.None);
            var text = await resp.Content.ReadAsStringAsync(CancellationToken.None);
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                throw new ElectroluxAuthException("Refresh token inválido ou expirado. Gere um novo em developer.electrolux.one.");
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Renovação do token devolveu HTTP {(int)resp.StatusCode}", null, resp.StatusCode);

            var json = JsonNode.Parse(text) as JsonObject
                       ?? throw new HttpRequestException("Renovação do token devolveu corpo inválido.");
            // O token antigo já foi consumido: grava o novo antes de qualquer outra leitura que possa falhar.
            if (json["refreshToken"] is JsonValue rv && rv.TryGetValue<string>(out var rotated)
                && !string.IsNullOrEmpty(rotated) && rotated != creds.RefreshToken)
            {
                // Se o disco falhar, o token novo fica em memória e é a base da próxima renovação.
                _unsaved = creds with { RefreshToken = rotated };
                TrySaveUnsaved();
            }

            var access = json["accessToken"] is JsonValue av && av.TryGetValue<string>(out var a) && a.Length > 0
                ? a : throw new HttpRequestException("Renovação do token veio sem accessToken.");
            var expiresIn = json["expiresIn"] is JsonValue ev
                            && (ev.TryGetValue<int>(out var n) || (ev.TryGetValue<string>(out var es) && int.TryParse(es, out n)))
                ? n : 3600;
            _accessToken = access;
            // Renova 5 min antes do vencimento para não pegar 401 no meio de um ciclo.
            _accessExpiry = clock.GetUtcNow().AddSeconds(Math.Max(60, expiresIn - 300));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
