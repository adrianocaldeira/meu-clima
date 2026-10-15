using System.Net;
using System.Text.Json.Nodes;
using MeuClima.Core.Devices;
using MeuClima.Core.Electrolux;
using Microsoft.Extensions.Time.Testing;

namespace MeuClima.Core.Tests;

public class ElectroluxMappingTests
{
    [Fact]
    public void Campos_de_temperatura_para_o_log_sem_identificadores()
    {
        var t = ElectroluxMapping.TemperatureFields(JsonNode.Parse(TestKit.Fixture("state-xi09r.json"))!);

        Assert.Contains("ambientTemperatureC=25", t);
        Assert.Contains("targetTemperatureC=23", t);
        Assert.DoesNotContain("applianceId", t);
    }

    [Fact]
    public void ParseState_le_o_estado_real_do_XI09R()
    {
        var s = ElectroluxMapping.ParseState("x", JsonNode.Parse(TestKit.Fixture("state-xi09r.json"))!, TestKit.At(12));

        Assert.True(s.Connected);
        Assert.True(s.On);
        Assert.Equal(AcMode.Cool, s.Mode);
        Assert.Equal(FanSpeed.Low, s.Fan);
        Assert.Equal(23, s.TargetC);
        Assert.Equal(25, s.AmbientC);
        Assert.True(s.EnergySaving);
        Assert.False(s.Oscillate);
        Assert.Equal("GOOD", s.FilterState);
    }

    [Fact]
    public void ParseCapabilities_le_modos_ventilacao_e_faixa()
    {
        var c = ElectroluxMapping.ParseCapabilities(JsonNode.Parse(TestKit.Fixture("info-xi09r.json"))!);

        Assert.Equal([AcMode.Auto, AcMode.Cool, AcMode.Heat, AcMode.FanOnly], c.Modes.Order());
        Assert.Contains(FanSpeed.Turbo, c.FanSpeeds);
        Assert.Equal(16, c.MinTargetC);
        Assert.Equal(30, c.MaxTargetC);
        Assert.Equal("XI09R", c.Model);
    }

    [Theory]
    [InlineData("power-on", """{"executeCommand":"ON"}""")]
    [InlineData("turbo", """{"fanSpeedSetting":"TURBO"}""")]
    [InlineData("alvo", """{"targetTemperatureC":24}""")]
    [InlineData("timer", """{"stopTime":14400}""")]
    [InlineData("eco-off", """{"energySavingMode":"OFF"}""")]
    public void ToCommandBody_gera_o_json_que_a_api_aceita(string caso, string esperado)
    {
        DeviceCommand cmd = caso switch
        {
            "power-on" => new DeviceCommand.Power(true),
            "turbo" => new DeviceCommand.SetFan(FanSpeed.Turbo),
            "alvo" => new DeviceCommand.SetTarget(24),
            "timer" => new DeviceCommand.SetStopTimer(14400),
            _ => new DeviceCommand.SetEnergySaving(false),
        };
        Assert.Equal(esperado, ElectroluxMapping.ToCommandBody(cmd).ToJsonString());
    }
}

public class ElectroluxClientTests
{
    private sealed class MemoryStore(ElectroluxCredentials? c) : ITokenStore
    {
        public ElectroluxCredentials? Current = c;
        public int Saves;
        public ElectroluxCredentials? Load() => Current;
        public void Save(ElectroluxCredentials credentials) { Current = credentials; Saves++; }
    }

    private sealed class BrokenStore(ElectroluxCredentials c) : ITokenStore
    {
        public ElectroluxCredentials? Load() => c;
        public void Save(ElectroluxCredentials credentials) =>
            throw new System.Security.Cryptography.CryptographicException("DPAPI indisponível");
    }

    private sealed class Handler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Calls.Add($"{req.Method} {req.RequestUri!.AbsolutePath}");
            return Task.FromResult(respond(req, Calls.Count));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body) };

    private static HttpResponseMessage Token(string rt = "rt-novo") =>
        Json(HttpStatusCode.OK, $$"""{"accessToken":"at","refreshToken":"{{rt}}","expiresIn":43200}""");

    private static (ElectroluxClient, Handler, MemoryStore) Build(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var store = new MemoryStore(new ElectroluxCredentials("key", "rt-velho"));
        var h = new Handler(respond);
        var client = new ElectroluxClient(new HttpClient(h), store, new FakeTimeProvider(TestKit.At(12)))
        {
            RetryBaseDelay = TimeSpan.Zero,
        };
        return (client, h, store);
    }

    [Fact]
    public async Task Refresh_token_rotacionado_e_salvo_na_hora()
    {
        var (client, _, store) = Build((req, _) => req.RequestUri!.AbsolutePath.EndsWith("/token/refresh")
            ? Token("rt-2") : Json(HttpStatusCode.OK, "[]"));

        await client.ListDevicesAsync();

        Assert.Equal("rt-2", store.Current!.RefreshToken);
        Assert.Equal("key", store.Current.ApiKey);
        Assert.Equal(1, store.Saves);
    }

    [Theory]
    [InlineData("""{"refreshToken":"rt-2","expiresIn":"x"}""")]
    [InlineData("""{"refreshToken":"rt-2","accessToken":null}""")]
    public async Task Token_girado_e_salvo_mesmo_com_resto_da_resposta_invalido(string corpo)
    {
        var (client, _, store) = Build((req, _) => req.RequestUri!.AbsolutePath.EndsWith("/token/refresh")
            ? Json(HttpStatusCode.OK, corpo) : Json(HttpStatusCode.OK, "[]"));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ListDevicesAsync());

        Assert.Equal("rt-2", store.Current!.RefreshToken);
    }

    [Fact]
    public async Task Falha_do_DPAPI_mantem_o_token_girado_em_memoria()
    {
        var client = new ElectroluxClient(new HttpClient(new Handler((req, _) =>
                req.RequestUri!.AbsolutePath.EndsWith("/token/refresh") ? Token("rt-2") : Json(HttpStatusCode.OK, "[]"))),
            new BrokenStore(new ElectroluxCredentials("key", "rt-velho")), new FakeTimeProvider(TestKit.At(12)));
        Exception? aviso = null;
        client.SaveFailed += ex => aviso = ex;

        await client.ListDevicesAsync();

        Assert.IsType<System.Security.Cryptography.CryptographicException>(aviso);
        Assert.Equal("rt-2", client.CurrentCredentials!.RefreshToken);
    }

    [Fact]
    public async Task ExpiresIn_como_texto_e_aceito()
    {
        var (client, _, store) = Build((req, _) => req.RequestUri!.AbsolutePath.EndsWith("/token/refresh")
            ? Json(HttpStatusCode.OK, """{"accessToken":"at","refreshToken":"rt-2","expiresIn":"43200"}""")
            : Json(HttpStatusCode.OK, "[]"));

        await client.ListDevicesAsync();

        Assert.Equal("rt-2", store.Current!.RefreshToken);
    }

    [Fact]
    public void Credenciais_nao_aparecem_no_ToString()
    {
        var texto = new ElectroluxCredentials("chave-secreta", "token-secreto").ToString();

        Assert.DoesNotContain("secret", texto);
    }

    [Fact]
    public async Task Status_504_e_repetido_ate_dar_certo()
    {
        var gets = 0;
        var (client, h, _) = Build((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/token/refresh")) return Token();
            return ++gets == 1 ? Json(HttpStatusCode.GatewayTimeout, """{"message":"Endpoint request timed out"}""")
                               : Json(HttpStatusCode.OK, TestKit.Fixture("info-xi09r.json"));
        });

        var caps = await client.GetCapabilitiesAsync("id");

        Assert.Equal(2, gets);
        Assert.Equal("XI09R", caps.Model);
    }

    [Fact]
    public async Task Status_401_renova_o_token_e_repete()
    {
        var refreshes = 0;
        var gets = 0;
        var (client, _, _) = Build((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/token/refresh")) { refreshes++; return Token($"rt-{refreshes}"); }
            return ++gets == 1 ? Json(HttpStatusCode.Unauthorized, "") : Json(HttpStatusCode.OK, "[]");
        });

        await client.ListDevicesAsync();

        Assert.Equal(2, refreshes);
    }

    [Fact]
    public async Task Status_202_already_vira_AlreadyInState()
    {
        var (client, h, _) = Build((req, _) => req.RequestUri!.AbsolutePath.EndsWith("/token/refresh")
            ? Token() : Json(HttpStatusCode.Accepted, """{"message": "Appliance already in desired state"}"""));

        var r = await client.SendAsync("id", new DeviceCommand.SetTarget(23));

        Assert.Equal(CommandOutcome.AlreadyInState, r.Outcome);
        Assert.True(r.Ok);
        Assert.Contains("PUT /api/v1/appliances/id/command", h.Calls);
    }

    [Fact]
    public async Task Refresh_token_invalido_gera_erro_de_autenticacao_explicito()
    {
        var (client, _, _) = Build((_, _) => Json(HttpStatusCode.Unauthorized, ""));

        var ex = await Assert.ThrowsAsync<ElectroluxAuthException>(() => client.ListDevicesAsync());
        Assert.Contains("developer.electrolux.one", ex.Message);
    }
}
