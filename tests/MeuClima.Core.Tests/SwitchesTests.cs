using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MeuClima.Core.Switches;

namespace MeuClima.Core.Tests;

// Vetores gerados pela POC em Python (implementação independente), com id, chave e IV inventados.
public class EwelinkCryptoTests
{
    private static JsonNode Vetor => JsonNode.Parse(TestKit.Fixture("ewelink-vetor.json"))!;

    [Fact]
    public void Cifra_igual_a_referencia_com_o_mesmo_IV()
    {
        var v = Vetor;
        var (data, iv) = EwelinkCrypto.Encrypt((string)v["plain"]!, (string)v["devicekey"]!, Convert.FromBase64String((string)v["ivBase64"]!));

        Assert.Equal((string)v["cipherBase64"]!, data);
        Assert.Equal((string)v["ivBase64"]!, iv);
    }

    [Fact]
    public void Decifra_a_referencia()
    {
        var v = Vetor;
        Assert.Equal((string)v["plain"]!, EwelinkCrypto.Decrypt((string)v["cipherBase64"]!, (string)v["ivBase64"]!, (string)v["devicekey"]!));
    }

    [Fact]
    public void Ida_e_volta_com_IV_aleatorio()
    {
        var (data, iv) = EwelinkCrypto.Encrypt("""{"switch":"off"}""", "chave-falsa");
        var (data2, iv2) = EwelinkCrypto.Encrypt("""{"switch":"off"}""", "chave-falsa");

        Assert.NotEqual(iv, iv2);
        Assert.NotEqual(data, data2);
        Assert.Equal("""{"switch":"off"}""", EwelinkCrypto.Decrypt(data, iv, "chave-falsa"));
    }

    [Fact]
    public void Chave_errada_lanca_CryptographicException()
    {
        var v = Vetor;
        Assert.ThrowsAny<CryptographicException>(() => EwelinkCrypto.Decrypt((string)v["cipherBase64"]!, (string)v["ivBase64"]!, "outra-chave"));
    }
}

public class MdnsMessagesTests
{
    private static byte[] Pacote(string nome = "mdns-ewelink.bin") =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", nome));

    [Fact]
    public void Consulta_pede_PTR_de_ewelink()
    {
        var q = MdnsMessages.BuildQuery();

        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 }, q[..12]);
        Assert.Contains("ewelink", Encoding.ASCII.GetString(q));
        Assert.Equal(new byte[] { 0, 12, 0, 1 }, q[^4..]);
    }

    [Fact]
    public void Le_o_TXT_com_nome_comprimido()
    {
        var txt = Assert.Single(MdnsMessages.ParseTxt(Pacote()));

        Assert.Equal("1000abcdef", txt["id"]);
        Assert.Equal("plug", txt["type"]);
        Assert.Equal("32", txt["seq"]);
        Assert.Equal("true", txt["encrypt"]);
        Assert.True(txt.ContainsKey("data1"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(40)]
    [InlineData(200)]
    public void Pacote_truncado_nao_lanca(int tamanho)
    {
        var p = Pacote()[..tamanho];
        var r = MdnsMessages.ParseTxt(p);
        Assert.All(r, t => Assert.NotNull(t));
    }

    [Fact]
    public void Ponteiro_de_compressao_nao_e_seguido()
    {
        // Nome que aponta para si mesmo: seguir o ponteiro travaria; o parser só o pula.
        byte[] p = [0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0xC0, 12, 0, 16, 0, 1, 0, 0, 0, 1, 0, 0];
        Assert.All(MdnsMessages.ParseTxt(p), Assert.Empty);
    }
}

public class EwelinkAnnouncementTests
{
    private const string Chave = "00000000-1111-2222-3333-444444444444";
    private static readonly DateTimeOffset Agora = TestKit.At(14);

    private static IReadOnlyDictionary<string, string> Txt(string nome = "mdns-ewelink.bin") =>
        Assert.Single(MdnsMessages.ParseTxt(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", nome))));

    [Fact]
    public void Decifra_o_estado_do_anuncio()
    {
        var a = EwelinkAnnouncement.From(Txt(), "192.168.0.42", Agora, id => id == "1000abcdef" ? Chave : null)!;

        Assert.Equal("1000abcdef", a.DeviceId);
        Assert.Equal("192.168.0.42", a.Ip);
        Assert.Equal(32, a.Seq);
        Assert.True(a.On);
        Assert.False(a.Multicanal);
        Assert.Null(a.Falha);
        Assert.Equal(Agora, a.VistoEm);
    }

    [Fact]
    public void Junta_data1_a_data4_em_ordem()
    {
        var txt = Txt("mdns-ewelink-partido.bin");
        Assert.True(txt.ContainsKey("data3"));

        var a = EwelinkAnnouncement.From(txt, "192.168.0.42", Agora, _ => Chave)!;

        Assert.False(a.On);
        Assert.Null(a.Falha);
    }

    [Fact]
    public void Sem_chave_ou_chave_errada_vira_falha_sem_excecao()
    {
        var semChave = EwelinkAnnouncement.From(Txt(), "192.168.0.42", Agora, _ => null)!;
        var errada = EwelinkAnnouncement.From(Txt(), "192.168.0.42", Agora, _ => "outra-chave")!;

        Assert.Null(semChave.On);
        Assert.Equal(AnnouncementFailure.SemChave, semChave.Falha);
        Assert.Null(errada.On);
        Assert.Equal(AnnouncementFailure.ChaveNaoDecifra, errada.Falha);
    }

    [Fact]
    public void Modo_DIY_nao_cifrado_e_multicanal()
    {
        var txt = new Dictionary<string, string>
        {
            ["id"] = "1000abcdef", ["type"] = "strip", ["seq"] = "7", ["encrypt"] = "false",
            ["data1"] = """{"switches":[{"switch":"on","outlet":0},{"switch":"off","outlet":1}]}""",
        };

        var a = EwelinkAnnouncement.From(txt, "192.168.0.42", Agora, _ => null)!;

        Assert.True(a.On);
        Assert.True(a.Multicanal);
    }

    [Theory]
    [InlineData("""{"switch":1}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""{"switches":[{"switch":"on","outlet":"zero"}]}""")]
    [InlineData("""{"switches":"on"}""")]
    [InlineData("nao-e-json")]
    public void Estado_com_tipo_inesperado_vira_falha_sem_excecao(string data)
    {
        var txt = new Dictionary<string, string> { ["id"] = "1000abcdef", ["type"] = "plug", ["encrypt"] = "false", ["data1"] = data };

        var a = EwelinkAnnouncement.From(txt, "192.168.0.42", Agora, _ => null)!;

        Assert.Null(a.On);
        Assert.Equal(AnnouncementFailure.EstadoIlegivel, a.Falha);
    }

    [Theory]
    [InlineData("MTIz")]
    [InlineData("%%%")]
    [InlineData("")]
    public void IV_invalido_vira_falha_sem_excecao(string iv)
    {
        var txt = new Dictionary<string, string>(Txt()) { ["iv"] = iv };

        var a = EwelinkAnnouncement.From(txt, "192.168.0.42", Agora, _ => Chave)!;

        Assert.Equal(AnnouncementFailure.ChaveNaoDecifra, a.Falha);
    }

    [Fact]
    public void TXT_que_nao_e_ewelink_e_ignorado()
    {
        Assert.Null(EwelinkAnnouncement.From(new Dictionary<string, string> { ["model"] = "x" }, "192.168.0.9", Agora, _ => null));
    }
}

public class EwelinkLanClientTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;
        public long? ContentLength;
        public bool? Chunked;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            // Lido antes do corpo: ler bufferiza e passa a calcular o tamanho, escondendo o envio chunked.
            ContentLength = request.Content?.Headers.ContentLength;
            Chunked = request.Headers.TransferEncodingChunked;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return await responder(request, ct);
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(false, true, "/zeroconf/switch", """{"switch":"on"}""")]
    [InlineData(true, false, "/zeroconf/switches", """{"switches":[{"switch":"off","outlet":0}]}""")]
    public async Task Envia_o_comando_cifrado_no_formato_do_aparelho(bool multicanal, bool on, string caminho, string esperado)
    {
        var h = new Handler((_, _) => Task.FromResult(Json("""{"seq":40,"sequence":"1","error":0}""")));
        var c = new EwelinkLanClient(new HttpClient(h));

        var r = await c.SetSwitchAsync("192.168.0.42", "1000abcdef", "chave-falsa", multicanal, on, TimeSpan.FromSeconds(2));

        Assert.True(r.Ok);
        Assert.Equal(HttpMethod.Post, h.Request!.Method);
        Assert.Equal($"http://192.168.0.42:8081{caminho}", h.Request.RequestUri!.ToString());
        var corpo = JsonNode.Parse(h.Body!)!;
        Assert.Equal("1000abcdef", (string)corpo["deviceid"]!);
        Assert.Equal("123", (string)corpo["selfApikey"]!);
        Assert.True((bool)corpo["encrypt"]!);
        Assert.False(string.IsNullOrEmpty((string)corpo["sequence"]!));
        Assert.Equal(esperado, EwelinkCrypto.Decrypt((string)corpo["data"]!, (string)corpo["iv"]!, "chave-falsa"));
    }

    [Fact]
    public async Task Corpo_vai_com_Content_Length_porque_o_aparelho_ignora_chunked()
    {
        var h = new Handler((_, _) => Task.FromResult(Json("""{"seq":1,"error":0}""")));
        var c = new EwelinkLanClient(new HttpClient(h));

        await c.SetSwitchAsync("192.168.0.42", "1000abcdef", "chave-falsa", false, true, TimeSpan.FromSeconds(2));

        Assert.NotEqual(true, h.Chunked);
        Assert.Equal(Encoding.UTF8.GetByteCount(h.Body!), h.ContentLength);
    }

    [Fact]
    public async Task Erro_do_aparelho_volta_no_resultado()
    {
        var c = new EwelinkLanClient(new HttpClient(new Handler((_, _) => Task.FromResult(Json("""{"seq":1,"error":400}""")))));

        var r = await c.SetSwitchAsync("192.168.0.42", "1000abcdef", "chave-falsa", false, true, TimeSpan.FromSeconds(2));

        Assert.False(r.Ok);
        Assert.Equal(400, r.Error);
    }

    [Fact]
    public async Task Resposta_fora_do_protocolo_vira_resultado_sem_excecao()
    {
        var c = new EwelinkLanClient(new HttpClient(new Handler((_, _) => Task.FromResult(Json("""{"error":"x"}""")))));

        var r = await c.SetSwitchAsync("192.168.0.42", "1000abcdef", "chave-falsa", false, true, TimeSpan.FromSeconds(2));

        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Timeout_vira_resultado_sem_excecao()
    {
        var c = new EwelinkLanClient(new HttpClient(new Handler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return Json("{}");
        })));

        var inicio = DateTime.UtcNow;
        var r = await c.SetSwitchAsync("192.168.0.42", "1000abcdef", "chave-falsa", false, true, TimeSpan.FromMilliseconds(200));

        Assert.False(r.Ok);
        Assert.Null(r.Error);
        Assert.True(DateTime.UtcNow - inicio < TimeSpan.FromSeconds(2));
    }
}

public class LuzPolicyTests
{
    private static readonly DateTimeOffset T0 = TestKit.At(14);
    private static readonly LuzConfig Padrao = new(ApagarAoBloquear: true, Espera: TimeSpan.Zero, ApagarAoDesligar: true);

    private static EwelinkAnnouncement Anuncio(bool? on, DateTimeOffset em, long seq = 1, AnnouncementFailure? falha = null) =>
        new("1000abcdef", "192.168.0.42", seq, on, false, em, falha);

    private static LuzPolicy Acesa(DateTimeOffset? em = null)
    {
        var p = new LuzPolicy();
        p.Registrar(Anuncio(true, em ?? T0));
        return p;
    }

    [Fact]
    public void CA2_bloqueio_com_a_luz_acesa_apaga()
    {
        var p = Acesa();
        p.Bloqueou(T0.AddSeconds(5));

        var d = p.DecidirBloqueio(Padrao, T0.AddSeconds(5));

        Assert.True(d.Apagar);
    }

    [Fact]
    public void CA2_com_espera_so_apaga_depois_dela()
    {
        var cfg = Padrao with { Espera = TimeSpan.FromSeconds(60) };
        var p = Acesa();
        p.Bloqueou(T0);

        var cedo = p.DecidirBloqueio(cfg, T0.AddSeconds(20));
        var depois = p.DecidirBloqueio(cfg, T0.AddSeconds(60));

        Assert.False(cedo.Apagar);
        Assert.Equal(TimeSpan.FromSeconds(40), cedo.ReavaliarEm);
        Assert.True(depois.Apagar);
    }

    [Fact]
    public void CA2_desbloqueio_antes_da_espera_nao_apaga()
    {
        var cfg = Padrao with { Espera = TimeSpan.FromSeconds(60) };
        var p = Acesa();
        p.Bloqueou(T0);
        p.Desbloqueou(T0.AddSeconds(20));

        Assert.False(p.DecidirBloqueio(cfg, T0.AddSeconds(60)).Apagar);
    }

    [Fact]
    public void Opcao_desligada_nao_apaga()
    {
        var p = Acesa();
        p.Bloqueou(T0);
        Assert.False(p.DecidirBloqueio(Padrao with { ApagarAoBloquear = false }, T0).Apagar);
        Assert.False(p.DecidirDesligamento(Padrao with { ApagarAoDesligar = false }, T0).Apagar);
    }

    [Fact]
    public void CA3_desligamento_com_a_luz_acesa_apaga_sem_estar_bloqueado()
    {
        Assert.True(Acesa().DecidirDesligamento(Padrao, T0.AddMinutes(1)).Apagar);
    }

    [Fact]
    public void CA4_ja_apagada_nao_manda_comando()
    {
        var p = new LuzPolicy();
        p.Registrar(Anuncio(false, T0));
        p.Bloqueou(T0);

        var d = p.DecidirBloqueio(Padrao, T0);

        Assert.False(d.Apagar);
        Assert.Contains("apagada", d.Motivo);
        Assert.False(p.DecidirDesligamento(Padrao, T0).Apagar);
    }

    [Fact]
    public void CA5_acesa_na_parede_durante_o_bloqueio_nao_apaga_de_novo()
    {
        var p = Acesa();
        p.Bloqueou(T0);
        p.RegistrarComando(on: false, T0.AddSeconds(1));
        p.Registrar(Anuncio(false, T0.AddSeconds(2), seq: 2));
        p.Registrar(Anuncio(true, T0.AddMinutes(30), seq: 3));

        var d = p.DecidirBloqueio(Padrao, T0.AddMinutes(31));

        Assert.False(d.Apagar);
        Assert.Contains("interruptor", d.Motivo);
        Assert.False(p.DecidirDesligamento(Padrao, T0.AddMinutes(32)).Apagar);
    }

    [Fact]
    public void CA5_vale_ate_o_proximo_desbloqueio()
    {
        var p = Acesa();
        p.Bloqueou(T0);
        p.Registrar(Anuncio(false, T0.AddSeconds(2), seq: 2));
        p.Registrar(Anuncio(true, T0.AddMinutes(30), seq: 3));
        p.Desbloqueou(T0.AddMinutes(40));
        p.Registrar(Anuncio(true, T0.AddMinutes(50), seq: 3));
        p.Bloqueou(T0.AddMinutes(50));

        Assert.True(p.DecidirBloqueio(Padrao, T0.AddMinutes(50)).Apagar);
    }

    [Fact]
    public void Acender_pelo_proprio_app_durante_o_bloqueio_nao_conta_como_parede()
    {
        var p = new LuzPolicy();
        p.Registrar(Anuncio(false, T0));
        p.Bloqueou(T0);
        p.RegistrarComando(on: true, T0.AddMinutes(5));
        p.Registrar(Anuncio(true, T0.AddMinutes(5).AddSeconds(1), seq: 2));

        Assert.True(p.DecidirBloqueio(Padrao, T0.AddMinutes(6)).Apagar);
    }

    [Fact]
    public void Anuncio_repetido_com_o_mesmo_seq_nao_e_transicao()
    {
        var p = Acesa();
        p.Bloqueou(T0);
        p.Registrar(Anuncio(true, T0.AddMinutes(1)));

        Assert.True(p.DecidirBloqueio(Padrao, T0.AddMinutes(1)).Apagar);
    }

    [Fact]
    public void CA6_anuncio_velho_nao_gera_comando()
    {
        var p = Acesa(T0);
        p.Bloqueou(T0.AddMinutes(11));

        var d = p.DecidirBloqueio(Padrao, T0.AddMinutes(11));

        Assert.False(d.Apagar);
        Assert.Contains("sem anúncio recente", d.Motivo);
        Assert.False(p.DecidirDesligamento(Padrao, T0.AddMinutes(11)).Apagar);
    }

    [Fact]
    public void CA6_sem_nenhum_anuncio_nao_gera_comando()
    {
        var p = new LuzPolicy();
        p.Bloqueou(T0);
        Assert.False(p.DecidirBloqueio(Padrao, T0).Apagar);
        Assert.False(p.DecidirDesligamento(Padrao, T0).Apagar);
    }

    [Fact]
    public void CA7_falha_de_decifra_nao_gera_comando()
    {
        var p = new LuzPolicy();
        p.Registrar(Anuncio(null, T0, falha: AnnouncementFailure.ChaveNaoDecifra));
        p.Bloqueou(T0);

        var d = p.DecidirBloqueio(Padrao, T0);

        Assert.False(d.Apagar);
        Assert.Contains("sincronize", d.Motivo);
    }

    [Fact]
    public void PC_em_uso_nao_apaga()
    {
        Assert.False(Acesa().DecidirBloqueio(Padrao, T0).Apagar);
    }

    [Fact]
    public void Apagar_pelo_menu_nao_trava_o_apagar_no_desligamento_se_acenderam_depois()
    {
        var p = Acesa();
        p.RegistrarComando(on: false, T0.AddSeconds(1));
        p.Registrar(Anuncio(false, T0.AddSeconds(2), seq: 2));
        p.Registrar(Anuncio(true, T0.AddSeconds(20), seq: 3));

        Assert.True(p.DecidirDesligamento(Padrao, T0.AddSeconds(30)).Apagar);
    }

    [Fact]
    public void CA5_reconhece_a_parede_mesmo_sem_ver_o_anuncio_de_apagada()
    {
        var p = Acesa();
        p.Bloqueou(T0);
        p.RegistrarComando(on: false, T0.AddSeconds(1));
        p.Registrar(Anuncio(true, T0.AddMinutes(5), seq: 3));

        var d = p.DecidirBloqueio(Padrao, T0.AddMinutes(6));

        Assert.False(d.Apagar);
        Assert.Contains("interruptor", d.Motivo);
    }

    [Fact]
    public void Depois_de_apagar_nao_repete_o_comando()
    {
        var p = Acesa();
        p.Bloqueou(T0);
        p.RegistrarComando(on: false, T0);

        Assert.False(p.DecidirBloqueio(Padrao, T0.AddSeconds(30)).Apagar);
    }
}
