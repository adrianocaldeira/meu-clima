using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MeuClima.Core.Switches;

namespace MeuClima.Host;

/// <summary>Luz do escritório pela rede local: escuta os anúncios mDNS, apaga no bloqueio e no desligamento.</summary>
public sealed class SwitchService : IDisposable
{
    private static readonly IPAddress Grupo = IPAddress.Parse("224.0.0.251");
    private static readonly TimeSpan IntervaloConsulta = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PrazoComando = TimeSpan.FromSeconds(3);

    private readonly SettingsStore _settings;
    private readonly DpapiSwitchKeyStore _keyStore;
    private readonly PresenceMonitor _presence;
    private readonly TimeProvider _clock;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly EwelinkLanClient _lan;
    private readonly LuzPolicy _policy = new();
    private readonly Lock _lock = new();
    private Dictionary<string, SwitchKey> _keys;
    private string? _ultimoMotivo;
    private bool _avisouChave;

    public SwitchService(SettingsStore settings, DpapiSwitchKeyStore keyStore, PresenceMonitor presence, TimeProvider? clock = null)
    {
        _settings = settings;
        _keyStore = keyStore;
        _presence = presence;
        _clock = clock ?? TimeProvider.System;
        _lan = new EwelinkLanClient(_http, _clock);
        _keys = keyStore.Load();
        _presence.Changed += OnPresenceChanged;
    }

    /// <summary>Aviso único para o usuário (ex.: chave que não decifra).</summary>
    public event Action<string, string>? Notify;

    public bool Configurada => Luz is not null && _keys.ContainsKey(Luz.DeviceId);

    /// <summary>Último estado anunciado; nulo sem anúncio recente.</summary>
    public bool? Acesa
    {
        get
        {
            lock (_lock)
                return _policy.Ultimo is { } a && _clock.GetUtcNow() - a.VistoEm <= LuzPolicy.IdadeMaxima ? a.On : null;
        }
    }

    private LuzSettings? Luz => _settings.Current.Luz;

    public void ReloadKeys()
    {
        lock (_lock) _keys = _keyStore.Load();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await EscutarAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                // Qualquer falha reabre o socket: o laço parado deixaria a luz acesa no bloqueio sem rastro no log.
                Log.Write($"Luz: escuta mDNS caiu ({ex.GetType().Name}: {(ex as SocketException)?.SocketErrorCode.ToString() ?? ex.Message}); recomeço em 30 s.");
                await Task.Delay(IntervaloConsulta, ct).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    private async Task EscutarAsync(CancellationToken ct)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = false };
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
        var locais = InterfacesLocais();
        foreach (var ip in locais) udp.JoinMulticastGroup(Grupo, ip);
        Log.Write($"Luz: escutando mDNS em {locais.Count} interface(s).");

        var query = MdnsMessages.BuildQuery();
        var proximaConsulta = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            if (_clock.GetUtcNow() >= proximaConsulta && Configurada)
            {
                // Rede que sobe depois do logon (Wi-Fi) ou placa nova: refaz o socket com a lista atual.
                if (!InterfacesLocais().ToHashSet().SetEquals(locais)) return;
                // Sem fixar a interface, com VPN ligada a consulta sai pelo túnel e nada responde (medido em 26/10/2026).
                foreach (var ip in locais)
                {
                    udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, ip.GetAddressBytes());
                    await udp.SendAsync(query, new IPEndPoint(Grupo, 5353), ct);
                }
                proximaConsulta = _clock.GetUtcNow() + IntervaloConsulta;
            }

            using var tick = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tick.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                var r = await udp.ReceiveAsync(tick.Token);
                Receber(r.Buffer, r.RemoteEndPoint.Address.ToString());
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }

            if (Luz is { } luz && Configurada) await AvaliarBloqueioAsync(luz, ct);
        }
    }

    /// <summary>Placas físicas com IPv4 e gateway; túnel de VPN e loopback ficam de fora.</summary>
    private static List<IPAddress> InterfacesLocais() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet
                        && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address)
            .ToList();

    private void Receber(byte[] pacote, string ip)
    {
        if (Luz is not { } luz) return;
        foreach (var txt in MdnsMessages.ParseTxt(pacote))
        {
            if (txt.GetValueOrDefault("id") != luz.DeviceId) continue;
            var a = EwelinkAnnouncement.From(txt, ip, _clock.GetUtcNow(), id => _keys.GetValueOrDefault(id)?.DeviceKey);
            if (a is null) continue;
            lock (_lock)
            {
                if (_policy.Ultimo?.Seq != a.Seq) Log.Write($"Luz {NomeDa(a.DeviceId)}: {Descrever(a)} (seq {a.Seq})");
                _policy.Registrar(a);
            }
            if (a.Falha is AnnouncementFailure.ChaveNaoDecifra && !_avisouChave)
            {
                _avisouChave = true;
                Log.Write($"Luz {NomeDa(a.DeviceId)}: a chave não decifra mais; sincronize os aparelhos eWeLink.");
                Notify?.Invoke("Luz do escritório", "A chave da luz não decifra mais. Sincronize os aparelhos eWeLink.");
            }
        }
    }

    private void OnPresenceChanged()
    {
        var agora = _clock.GetUtcNow();
        lock (_lock)
        {
            if (_presence.Current(agora).Locked) _policy.Bloqueou(agora);
            else _policy.Desbloqueou(agora);
        }
    }

    private async Task AvaliarBloqueioAsync(LuzSettings luz, CancellationToken ct)
    {
        LuzDecisao d;
        lock (_lock) d = _policy.DecidirBloqueio(luz.Config, _clock.GetUtcNow());
        if (d.Motivo != _ultimoMotivo && d.Motivo != "PC em uso") Log.Write($"Luz: {d.Motivo}");
        _ultimoMotivo = d.Motivo;
        if (d.Apagar) await EnviarAsync(false, "PC bloqueado", PrazoComando, ct);
    }

    /// <summary>Acende ou apaga pelo menu; sem estado conhecido não faz nada.</summary>
    public async Task ToggleAsync()
    {
        if (Acesa is not { } acesa) return;
        await EnviarAsync(!acesa, "você pelo PC", PrazoComando, CancellationToken.None);
    }

    /// <summary>Desligamento ou suspensão: decide só com o último anúncio, sem consultar o aparelho.</summary>
    public async Task TurnOffForShutdownAsync(string motivo, TimeSpan prazo)
    {
        if (Luz is not { } luz || !Configurada) return;
        LuzDecisao d;
        lock (_lock) d = _policy.DecidirDesligamento(luz.Config, _clock.GetUtcNow());
        Log.Write($"{motivo}: luz {(d.Apagar ? "apagar" : "manter")} ({d.Motivo})");
        if (d.Apagar) await EnviarAsync(false, motivo, prazo, CancellationToken.None);
    }

    private async Task EnviarAsync(bool on, string origem, TimeSpan prazo, CancellationToken ct)
    {
        EwelinkAnnouncement a;
        SwitchKey key;
        lock (_lock)
        {
            if (_policy.Ultimo is not { } ultimo || !_keys.TryGetValue(ultimo.DeviceId, out var k)) return;
            (a, key) = (ultimo, k);
            _policy.RegistrarComando(on, _clock.GetUtcNow());
        }
        var r = await _lan.SetSwitchAsync(a.Ip, a.DeviceId, key.DeviceKey, a.Multicanal, on, prazo, ct);
        Log.Write($"Luz {NomeDa(a.DeviceId)}: {(on ? "acender" : "apagar")} ({origem}) → {(r.Ok ? "ok" : $"falhou {r.Error?.ToString() ?? ""} {r.Detail}".TrimEnd())}");
    }

    private string NomeDa(string deviceId) => _keys.GetValueOrDefault(deviceId)?.Name?.Trim() is { Length: > 0 } nome ? nome : deviceId[..Math.Min(4, deviceId.Length)] + "…";

    private static string Descrever(EwelinkAnnouncement a) => a.Falha is { } f ? f.ToString() : a.On switch
    {
        true => "acesa",
        false => "apagada",
        null => "estado desconhecido",
    };

    public void Dispose()
    {
        _presence.Changed -= OnPresenceChanged;
        _http.Dispose();
    }
}
