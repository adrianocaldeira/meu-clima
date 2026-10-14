using System.IO;
using System.Net.Http;
using System.Text.Json;
using MeuClima.Core.Devices;
using MeuClima.Core.Electrolux;
using MeuClima.Core.History;
using MeuClima.Core.Rules;
using MeuClima.Core.Weather;

namespace MeuClima.Host;

/// <summary>Laço do app: lê aparelhos e clima, grava histórico, roda regras, envia comandos e publica o estado.</summary>
public sealed class ClimateService : IDisposable
{
    public static readonly TimeSpan RuleCommandGap = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan RuleRetryAfterFailure = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan RuleRetryAfterReject = TimeSpan.FromMinutes(10);

    private static TimeSpan ConfirmationFor(AppSettings s) =>
        TimeSpan.FromMinutes(Math.Max(RuleEngine.DefaultConfirmationWindow.TotalMinutes, Math.Max(1, s.DeviceIntervalMin) + 1));

    private readonly SettingsStore _settings;
    private readonly ITokenStore _tokens;
    private readonly ElectroluxClient _electrolux;
    private readonly OpenMeteoClient _weather;
    private readonly HistoryStore _history;
    private readonly PresenceMonitor _presence;
    private readonly TimeProvider _clock = TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _kick = new(0, int.MaxValue);

    private RuleEngine _engine = new([]);
    private List<DeviceInfo> _devices = [];
    private readonly Dictionary<string, DeviceCapabilities> _caps = [];
    private readonly Dictionary<string, DeviceSnapshot> _readings = [];
    private readonly Dictionary<string, DateTimeOffset> _lastRuleSend = [];
    private readonly Dictionary<(string Device, string Rule), DateTimeOffset> _ruleRetryAt = [];
    private readonly HashSet<(string Device, string Origem)> _rejectNotified = [];
    private readonly Dictionary<string, SaidaEstagio> _saidaEstagio = [];
    private WeatherSnapshot? _weatherNow;
    private DateTimeOffset _nextDevicePoll = DateTimeOffset.MinValue;
    private DateTimeOffset _nextWeatherPoll = DateTimeOffset.MinValue;
    private DateTimeOffset _nextPrune = DateTimeOffset.MinValue;
    private EngineResult? _lastResult;
    private string? _accountError;
    private DateTimeOffset? _updatedAt;

    public ClimateService(SettingsStore settings, ITokenStore tokens, PresenceMonitor presence)
    {
        _settings = settings;
        _tokens = tokens;
        _presence = presence;
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MeuClima/{VersionLabel}");
        _electrolux = new ElectroluxClient(http, tokens, _clock);
        _electrolux.SaveFailed += ex => Log.Write($"Token renovado não foi gravado em disco (segue em memória): {ex.GetType().Name}");
        _weather = new OpenMeteoClient(http, _clock);
        _history = new HistoryStore(AppPaths.History);
        _presence.Changed += Kick;
        RebuildEngine();
    }

    public event Action<string>? StateChanged;
    public event Action<string, string>? Notify;

    public static string VersionLabel => VersionInfo.Current;

    public AppSettings Settings => _settings.Current;

    public void Kick() => _kick.Release();

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CycleAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Log.Write($"Ciclo falhou: {ex}");
            }
            await _kick.WaitAsync(TimeSpan.FromSeconds(5), ct).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    private async Task CycleAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = _clock.GetUtcNow();
            var s = _settings.Current;
            var changed = false;

            if (s.City is { } city && now >= _nextWeatherPoll)
            {
                _nextWeatherPoll = now.AddMinutes(Math.Max(5, s.WeatherIntervalMin));
                try
                {
                    _weatherNow = await _weather.GetAsync(city.Latitude, city.Longitude, ct);
                    _history.RecordOutside(now, _weatherNow.TempC);
                    changed = true;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    Log.Write($"Clima falhou: {ex.Message}");
                    _nextWeatherPoll = now.AddMinutes(2);
                }
            }

            if (_tokens.Load() is not null && now >= _nextDevicePoll)
            {
                _nextDevicePoll = now.AddMinutes(Math.Max(1, s.DeviceIntervalMin));
                changed |= await PollDevicesAsync(now, ct);
            }

            if (_readings.Count > 0)
            {
                await EvaluateAsync(now, ct);
                changed = true;
            }

            if (now >= _nextPrune)
            {
                _nextPrune = now.AddHours(12);
                _history.Prune(now.AddDays(-90));
            }
            if (changed) Publish();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> PollDevicesAsync(DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            if (_devices.Count == 0) _devices = [.. await _electrolux.ListDevicesAsync(ct)];
            foreach (var d in _devices)
            {
                if (!_caps.ContainsKey(d.Id))
                {
                    try { _caps[d.Id] = await _electrolux.GetCapabilitiesAsync(d.Id, ct); }
                    catch (HttpRequestException ex) { Log.Write($"Capabilities de {d.Name} falhou: {ex.Message}"); }
                }
                var snap = (await _electrolux.GetStateAsync(d.Id, ct)) with { Name = d.Name };
                if (_readings.TryGetValue(d.Id, out var prev) && _engine.ObserveReading(prev, snap, now))
                {
                    _history.RecordEvent(now, d.Id, "manual", "mudança feita fora do Meu Clima");
                    Raise("Automações pausadas", $"{d.Name} foi ajustado fora do Meu Clima. As regras voltam em 1 hora.");
                }
                _readings[d.Id] = snap;
                _history.Record(snap);
            }
            _accountError = null;
            _updatedAt = now;
            EnsureDefaultRules();
            return true;
        }
        catch (ElectroluxAuthException ex)
        {
            _accountError = ex.Message;
            Log.Write($"Conta Electrolux: {ex.Message}");
            return true;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
        {
            _accountError = "A Electrolux recusou a API key. Confira a chave em Configuração.";
            Log.Write($"Leitura dos aparelhos recusada: HTTP {(int)ex.StatusCode!}");
            _nextDevicePoll = now.AddMinutes(5);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            Log.Write($"Leitura dos aparelhos falhou: {ex.Message}");
            _nextDevicePoll = now.AddSeconds(30);
            return false;
        }
    }

    private async Task EvaluateAsync(DateTimeOffset now, CancellationToken ct)
    {
        _engine.ConfirmationWindow = ConfirmationFor(_settings.Current);
        var ctx = new RuleContext(now, TimeZoneInfo.Local, _weatherNow, _readings, _presence.Current(now), _history);
        var result = _engine.Evaluate(ctx);
        _lastResult = result;
        TrackSaidaEvents(now);
        SaveRuleState();

        foreach (var n in result.Notifications) Raise(n.Title, n.Body);
        _rejectNotified.RemoveWhere(k => !result.Commands.Any(p => p.DeviceId == k.Device && p.RuleName == k.Origem));
        foreach (var plan in result.Commands)
        {
            if (_lastRuleSend.TryGetValue(plan.DeviceId, out var last) && now - last < RuleCommandGap) continue;
            var chave = (plan.DeviceId, plan.RuleName);
            if (_ruleRetryAt.TryGetValue(chave, out var retry) && now < retry) continue;
            _lastRuleSend[plan.DeviceId] = now;
            // A regra planejaria o mesmo comando a cada ciclo: recusa espera mais que falha de rede.
            var resultado = await SendAsync(plan.DeviceId, plan.Commands, plan.RuleName, now, ct);
            if (resultado == SendResult.Ok) _ruleRetryAt.Remove(chave);
            else _ruleRetryAt[chave] = now + (resultado == SendResult.Rejected ? RuleRetryAfterReject : RuleRetryAfterFailure);
        }
    }

    private enum SendResult { Ok, Rejected, Failed }

    private const string UserOrigin = "você";

    private async Task<SendResult> SendAsync(string deviceId, IReadOnlyList<DeviceCommand> cmds, string origem,
        DateTimeOffset now, CancellationToken ct)
    {
        var enviados = new List<DeviceCommand>();
        var resultado = SendResult.Ok;
        try
        {
            foreach (var c in cmds)
            {
                var r = await _electrolux.SendAsync(deviceId, c, ct);
                if (!r.Ok)
                {
                    Log.Write($"{origem}: comando {c} não aplicado em {deviceId}: {r.Outcome} {r.Detail}");
                    var recusa = r.Outcome == CommandOutcome.Rejected;
                    // Regra: um aviso por sequência de recusas. Comando do usuário: sempre avisa.
                    if (origem == UserOrigin || (recusa && _rejectNotified.Add((deviceId, origem))))
                        Raise("Comando não aplicado", recusa
                            ? $"{NameOf(deviceId)} recusou um ajuste de {origem}."
                            : $"A Electrolux não respondeu ao ajuste de {origem} em {NameOf(deviceId)}. Tente de novo.");
                    resultado = recusa ? SendResult.Rejected : SendResult.Failed;
                    break;
                }
                enviados.Add(c);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ElectroluxAuthException)
        {
            Log.Write($"{origem}: envio para {deviceId} falhou: {ex.Message}");
            if (origem == UserOrigin) Raise("Comando não aplicado", $"Não deu para falar com a Electrolux agora. Tente de novo.");
            resultado = SendResult.Failed;
        }
        if (resultado == SendResult.Ok) _rejectNotified.Remove((deviceId, origem));
        // O comando do usuário entra no estado otimista antes do envio; o que não foi aplicado sai dele.
        else _engine.DiscardPending(deviceId, cmds.Skip(enviados.Count));
        if (enviados.Count > 0)
        {
            _engine.RecordSent(deviceId, enviados, now);
            _history.RecordEvent(now, deviceId, "comando", $"{origem}: {string.Join(", ", enviados)}");
        }
        return resultado;
    }

    // ---- ações vindas da tela ----

    public async Task UserCommandAsync(string deviceId, DeviceCommand command)
    {
        await _gate.WaitAsync();
        try
        {
            var now = _clock.GetUtcNow();
            if (!_readings.TryGetValue(deviceId, out var reading)) return;
            var plan = CommandPlanner.Plan(_engine.Expected(reading, now), [command]);
            if (plan.Count == 0) return;
            _engine.RecordSent(deviceId, plan, now);
            _engine.Pause(deviceId, now + RuleEngine.ManualPause);
            Publish();
            await SendAsync(deviceId, plan, UserOrigin, now, CancellationToken.None);
            Publish();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Executa a ação sob a mesma trava do ciclo, depois publica e acorda o laço.</summary>
    private async Task WithGateAsync(Action action)
    {
        await _gate.WaitAsync();
        try
        {
            action();
            Publish();
        }
        finally { _gate.Release(); }
        Kick();
    }

    public Task PauseDeviceAsync(string deviceId, int minutes) =>
        WithGateAsync(() => _engine.Pause(deviceId, _clock.GetUtcNow().AddMinutes(minutes)));

    public Task ResumeDeviceAsync(string deviceId) => WithGateAsync(() => _engine.Resume(deviceId));

    public async Task SetNightModeAsync(string deviceId, bool on)
    {
        var zerarTimer = false;
        await WithGateAsync(() =>
        {
            if (!on)
            {
                foreach (var ativa in _engine.Rules.OfType<ModoNoiteRule>().Where(r => r.Cfg.DeviceId == deviceId && r.Active))
                {
                    zerarTimer |= ativa.State.TimerDoModoNoite;
                    ativa.Cancel();
                }
                SaveRuleState();
                return;
            }
            var existente = _engine.Rules.OfType<ModoNoiteRule>().FirstOrDefault(r => r.Cfg.DeviceId == deviceId && r.Cfg.Enabled)
                            ?? _engine.Rules.OfType<ModoNoiteRule>().FirstOrDefault(r => r.Cfg.DeviceId == deviceId);
            if (existente is null) SaveRuleUnlocked(new ModoNoiteConfig(deviceId));
            else if (!existente.Cfg.Enabled) SaveRuleUnlocked(existente.Cfg with { Enabled = true });
            var rule = _engine.Rules.OfType<ModoNoiteRule>().First(r => r.Cfg.DeviceId == deviceId && r.Cfg.Enabled);
            rule.Activate(_clock.GetUtcNow(), _readings.TryGetValue(deviceId, out var r) ? r.TargetC : null);
            _engine.Resume(deviceId);
            SaveRuleState();
        });
        if (!zerarTimer) return;
        // Zera só o timer que o modo noite programou, sem pausar as automações (não é ajuste do usuário).
        await _gate.WaitAsync();
        try
        {
            await SendAsync(deviceId, [new DeviceCommand.SetStopTimer(0)], "Modo noite", _clock.GetUtcNow(), CancellationToken.None);
            Publish();
        }
        finally { _gate.Release(); }
    }

    public Task SaveRuleAsync(RuleConfig config) => WithGateAsync(() => SaveRuleUnlocked(config));

    private void SaveRuleUnlocked(RuleConfig config)
    {
        _settings.Update(s =>
        {
            var list = s.Rules.Where(r => r.Id != config.Id).ToList();
            var idx = s.Rules.FindIndex(r => r.Id == config.Id);
            list.Insert(idx < 0 ? list.Count : idx, config);
            return s with { Rules = list };
        });
        RebuildEngine();
    }

    public Task DeleteRuleAsync(string id) => WithGateAsync(() =>
    {
        _settings.Update(s => s with { Rules = s.Rules.Where(r => r.Id != id).ToList() });
        RebuildEngine();
    });

    public Task ToggleRuleAsync(string id, bool enabled) => WithGateAsync(() =>
    {
        var cfg = _settings.Current.Rules.FirstOrDefault(r => r.Id == id);
        if (cfg is not null) SaveRuleUnlocked(cfg with { Enabled = enabled });
    });

    public Task UpdateSettingsAsync(Func<AppSettings, AppSettings> change) => WithGateAsync(() =>
    {
        var before = _settings.Current;
        var after = _settings.Update(change);
        if (after.StartWithWindows != before.StartWithWindows) Autostart.Set(after.StartWithWindows);
        if (after.City != before.City || after.WeatherIntervalMin != before.WeatherIntervalMin) _nextWeatherPoll = DateTimeOffset.MinValue;
        if (after.DeviceIntervalMin != before.DeviceIntervalMin) _nextDevicePoll = DateTimeOffset.MinValue;
        if (after.PcDeviceId != before.PcDeviceId) EnsureDefaultRules();
        if (after.Theme != before.Theme) ThemeChanged?.Invoke(after.Theme);
    });

    public event Action<string>? ThemeChanged;

    public Task<IReadOnlyList<Place>> SearchCityAsync(string query) => _weather.SearchAsync(query);

    /// <summary>Valida as credenciais listando os aparelhos antes de gravar.</summary>
    /// <remarks>
    /// Roda sob a trava do ciclo: uma renovação do cliente principal em paralelo gravaria por cima.
    /// Refresh token em branco reaproveita o guardado (corrigir só a API key depois de uma recusa).
    /// </remarks>
    public async Task<(bool Ok, string Message)> SetCredentialsAsync(string apiKey, string refreshToken)
    {
        await _gate.WaitAsync();
        try { return await SetCredentialsUnlocked(apiKey.Trim(), refreshToken.Trim()); }
        finally { _gate.Release(); }
    }

    private async Task<(bool Ok, string Message)> SetCredentialsUnlocked(string apiKey, string refreshToken)
    {
        var guardado = _electrolux.CurrentCredentials;
        if (refreshToken.Length == 0)
        {
            if (guardado is null) return (false, "Cole o refresh token gerado no portal da Electrolux.");
            refreshToken = guardado.RefreshToken;
        }
        if (apiKey.Length == 0) return (false, "Cole a API key gerada no portal da Electrolux.");

        var colado = new ElectroluxCredentials(apiKey, refreshToken);
        var probeStore = new MemoryTokenStore(colado);
        var probe = new ElectroluxClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, probeStore, _clock);
        // A Electrolux girou o token assim que o teste renovou: o novo é gravado mesmo se a API key falhar depois,
        // com a key que já funcionava (se havia uma) para não derrubar a conta por causa da key colada errada.
        // Token novo colado com conta já guardada: o guardado não foi consumido, então fica como está.
        var substituiGuardado = guardado is null || guardado.RefreshToken == colado.RefreshToken;
        void GuardarSeGirou()
        {
            if (!substituiGuardado || probeStore.Current is not { } atual || atual.RefreshToken == colado.RefreshToken) return;
            _electrolux.Adopt(guardado is null ? atual : atual with { ApiKey = guardado.ApiKey });
        }
        try
        {
            var devices = await probe.ListDevicesAsync();
            var gravou = _electrolux.Adopt(probeStore.Current!);
            _devices = [];
            _caps.Clear();
            _readings.Clear();
            _nextDevicePoll = DateTimeOffset.MinValue;
            _accountError = null;
            Kick();
            if (!gravou)
                return (true, "Conta conectada, mas o Windows não deixou gravar as credenciais neste PC. Elas valem até fechar o app; depois será preciso conectar de novo.");
            return (true, devices.Count == 0
                ? "Conta conectada, mas nenhum aparelho apareceu. Confira se o ar está cadastrado no app Electrolux."
                : $"Conta conectada: {devices.Count} aparelho(s) encontrado(s).");
        }
        catch (ElectroluxAuthException)
        {
            GuardarSeGirou();
            return (false, "A Electrolux recusou o refresh token. Gere um novo no portal e cole de novo.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
        {
            GuardarSeGirou();
            return (false, guardado is null
                ? "A Electrolux recusou a API key. Corrija a chave e conecte de novo deixando o refresh token em branco: o token já ficou guardado."
                : substituiGuardado
                    ? "A Electrolux recusou a API key nova. A conta segue com a chave anterior; corrija a chave e tente de novo."
                    : "A Electrolux recusou a API key nova. Nada mudou: a conta segue com as credenciais anteriores. O token colado foi consumido; gere outro no portal para tentar de novo.");
        }
        catch (Exception ex)
        {
            GuardarSeGirou();
            Log.Write($"Teste das credenciais falhou: {ex.GetType().Name}: {ex.Message}");
            return (false, ex is HttpRequestException or TaskCanceledException
                ? $"Não foi possível falar com a Electrolux: {ex.Message}"
                : "A conexão com a Electrolux falhou de um jeito inesperado. Veja o log e tente de novo.");
        }
    }

    // ---- regras ----

    private void RebuildEngine()
    {
        var states = LoadRuleStates();
        foreach (var r in _engine.Rules) states[r.Config.Id] = r.SaveState();
        var rules = _settings.Current.Rules.Select(RuleFactory.Create).ToList();
        foreach (var r in rules)
            if (states.TryGetValue(r.Config.Id, out var json))
                try { r.LoadState(json); } catch (JsonException) { }
        var engine = new RuleEngine(rules);
        engine.CopyRuntimeFrom(_engine);
        _engine = engine;
    }

    private static Dictionary<string, string> LoadRuleStates()
    {
        try
        {
            return File.Exists(AppPaths.RuleState)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(AppPaths.RuleState)) ?? []
                : [];
        }
        catch (JsonException) { return []; }
    }

    private void SaveRuleState()
    {
        var states = _engine.Rules.ToDictionary(r => r.Config.Id, r => r.SaveState());
        File.WriteAllText(AppPaths.RuleState, JsonSerializer.Serialize(states));
    }

    private void EnsureDefaultRules()
    {
        var s = _settings.Current;
        if (s.DefaultRulesCreated || s.PcDeviceId is not { } pc || _devices.Count == 0) return;
        var rules = new List<RuleConfig>
        {
            new EsfriarRapidoConfig(pc),
            new SaiDoPcConfig(pc),
            new AntesDoPicoConfig(pc),
        };
        rules.AddRange(_devices.Select(d => new JanelaAbertaConfig(d.Id)));
        rules.AddRange(_devices.Select(d => new ModoNoiteConfig(d.Id)));
        rules.Add(new ForaMaisFrescoConfig(From: new TimeOnly(7, 0), To: new TimeOnly(22, 0)));
        _settings.Update(x => x with { Rules = [.. x.Rules, .. rules], DefaultRulesCreated = true });
        RebuildEngine();
    }

    private void TrackSaidaEvents(DateTimeOffset now)
    {
        foreach (var r in _engine.Rules.OfType<SaiDoPcRule>())
        {
            var atual = r.State.Estagio;
            var antes = _saidaEstagio.GetValueOrDefault(r.Cfg.Id, SaidaEstagio.Presente);
            if (atual == antes) continue;
            if (atual == SaidaEstagio.Desligado) _history.RecordEvent(now, r.Cfg.DeviceId, "sai-do-pc:desligou", "");
            if (atual == SaidaEstagio.Presente && antes == SaidaEstagio.Desligado)
                _history.RecordEvent(now, r.Cfg.DeviceId, "sai-do-pc:voltou", "");
            _saidaEstagio[r.Cfg.Id] = atual;
        }
    }

    // ---- publicação ----

    private void Raise(string title, string body)
    {
        Log.Write($"Aviso: {title} — {body}");
        if (_settings.Current.Notifications) Notify?.Invoke(title, body);
    }

    private string NameOf(string deviceId) => _devices.FirstOrDefault(d => d.Id == deviceId)?.Name ?? deviceId;

    /// <summary>Chamar só com a trava do ciclo adquirida.</summary>
    private void Publish() => StateChanged?.Invoke(BuildState());

    public async Task RepublishAsync()
    {
        await _gate.WaitAsync();
        try { Publish(); }
        finally { _gate.Release(); }
    }

    public async Task<string> BuildStateAsync()
    {
        await _gate.WaitAsync();
        try { return BuildState(); }
        finally { _gate.Release(); }
    }

    public async Task<string> BuildInsightsAsync(int days)
    {
        await _gate.WaitAsync();
        try { return BuildInsights(days); }
        finally { _gate.Release(); }
    }

    private string BuildState() =>
        ViewStateBuilder.Build(new ViewInputs(
            _settings.Current, _tokens.Load() is not null, _accountError, _updatedAt, _weatherNow,
            _devices, _readings, _caps, _engine, _lastResult, _history, _clock.GetUtcNow()));

    private string BuildInsights(int days) =>
        InsightsBuilder.Build(_settings.Current, _devices, _readings, _history, _weatherNow, _engine, _clock.GetUtcNow(), days);

    public void Dispose()
    {
        _presence.Changed -= Kick;
        _history.Dispose();
    }

    private sealed class MemoryTokenStore(ElectroluxCredentials initial) : ITokenStore
    {
        public ElectroluxCredentials? Current { get; private set; } = initial;
        public ElectroluxCredentials? Load() => Current;
        public void Save(ElectroluxCredentials credentials) => Current = credentials;
    }
}

public static class RuleFactory
{
    public static IRule Create(RuleConfig c) => c switch
    {
        EsfriarRapidoConfig x => new EsfriarRapidoRule(x),
        SaiDoPcConfig x => new SaiDoPcRule(x),
        AntesDoPicoConfig x => new AntesDoPicoRule(x),
        JanelaAbertaConfig x => new JanelaAbertaRule(x),
        ModoNoiteConfig x => new ModoNoiteRule(x),
        ForaMaisFrescoConfig x => new ForaMaisFrescoRule(x),
        _ => throw new NotSupportedException(c.GetType().Name),
    };
}
