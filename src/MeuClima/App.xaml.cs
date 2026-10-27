using System.Threading;
using System.Windows;
using MeuClima.Host;
using MeuClima.Ui;

namespace MeuClima;

public partial class App : Application
{
    private const string InstanceName = "MeuClima.SingleInstance";
    private const string ShowSignalName = "MeuClima.ShowCentral";

    private Mutex? _instance;
    private EventWaitHandle? _showSignal;
    private readonly CancellationTokenSource _stop = new();
    private Tray? _tray;
    private PresenceMonitor? _presence;
    private ClimateService? _climate;
    private SwitchService? _switches;
    private UiBridge? _bridge;
    private FlyoutWindow? _flyout;
    private CentralWindow? _central;
    // A central foi aberta pela bandeja: ao fechá-la, a bandeja volta.
    private bool _voltarParaBandeja;
    private UpdateCoordinator? _updates;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(true, InstanceName, out var primeira);
        if (!primeira)
        {
            // Segunda execução (ex.: atalho do menu Iniciar): pede à instância aberta para mostrar a central.
            using var sinal = EventWaitHandle.OpenExisting(ShowSignalName);
            sinal.Set();
            Shutdown();
            return;
        }
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        new Thread(() =>
        {
            while (_showSignal.WaitOne() && !_stop.IsCancellationRequested)
                Dispatcher.InvokeAsync(() => OpenCentral(null));
        }) { IsBackground = true, Name = "MeuClima.ShowSignal" }.Start();

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Write($"Erro não tratado na UI: {ex.Exception}");
            ex.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Log.Write($"Tarefa com erro não observado: {ex.Exception}");
            ex.SetObserved();
        };

        ImportCredentials(e.Args);
        WebWindow.WebRoot = WebAssets.Extract();
        var settings = new SettingsStore();
        var switchKeys = new DpapiSwitchKeyStore();
        ImportEwelinkKeys(e.Args, settings, switchKeys);
        ApplyInstallerStartupChoice(settings);
#if !DEBUG
        // Build de desenvolvimento não se registra no logon: o Run apontaria para bin\Debug.
        if (settings.Current.StartWithWindows && !Autostart.IsEnabled()) Autostart.Set(true);
#endif

        _presence = new PresenceMonitor();
        _climate = new ClimateService(settings, new DpapiTokenStore(), _presence);
        _switches = new SwitchService(settings, switchKeys, _presence);
        _presence.Suspending += () => DesligarAntesDeSair("PC suspendendo", TimeSpan.FromSeconds(2));
        SessionEnding += (_, ev) => DesligarAntesDeSair(
            ev.ReasonSessionEnding == ReasonSessionEnding.Shutdown ? "PC desligando" : "Saindo da sessão", TimeSpan.FromSeconds(4));
        // Só a bandeja pede a central pela ponte; a central nunca envia openCentral.
        _bridge = new UiBridge(_climate, tab => OpenCentral(tab, daBandeja: true), () => _flyout?.Hide(), Quit);
        _tray = new Tray(ToggleFlyout, OpenCentral, Quit);
        _tray.AutostartChanged += on => _ = _climate.UpdateSettingsAsync(s => s with { StartWithWindows = on });
        _tray.LightState = () => (_switches.Configurada, _switches.Acesa);
        _tray.LightClicked += () => _ = _switches.ToggleAsync();
        _switches.Notify += (title, body) => Dispatcher.InvokeAsync(() => _tray?.Notify(title, body));

        _climate.StateChanged += st =>
        {
            _flyout?.PostState(st);
            _central?.PostState(st);
        };
        _climate.Notify += (title, body) => Dispatcher.InvokeAsync(() => _tray?.Notify(title, body));
        WebWindow.ApplyTheme(settings.Current.Theme);
        _climate.ThemeChanged += theme => Dispatcher.InvokeAsync(() => WebWindow.ApplyTheme(theme));
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, ev) =>
        {
            if (ev.Category == Microsoft.Win32.UserPreferenceCategory.General) Dispatcher.InvokeAsync(WebWindow.RefreshSystemTheme);
        };

        _updates = new UpdateCoordinator(
            (title, body) => Dispatcher.InvokeAsync(() => _tray?.Notify(title, body)),
            _climate.RepublishAsync,
            () => Dispatcher.Invoke(Quit),
            () => settings.Current.CheckUpdates);
        _bridge.CheckUpdate = _updates.CheckNowAsync;
        _bridge.InstallUpdate = _updates.InstallAsync;
        _tray.CheckUpdateClicked += () => _ = _updates.CheckFromTrayAsync();
        _updates.Start();

        _ = Task.Run(() => _climate.RunAsync(_stop.Token));
        _ = Task.Run(() => _switches.RunAsync(_stop.Token));

        if (settings.ReadOnly)
            _tray.Notify("Ajustes de outra versão", "O settings.json foi gravado por uma versão mais nova do Meu Clima. As regras ficam paradas e nada é gravado até você instalar a versão mais nova.");

        var viaBandeja = e.Args.Contains("--bandeja", StringComparer.OrdinalIgnoreCase);
        var s = settings.Current;
        var faltaConfigurar = s.City is null || s.PcDeviceId is null || new DpapiTokenStore().Load() is null;
        if (faltaConfigurar) OpenCentral("setup");
        else if (!viaBandeja) ToggleFlyout();
    }

    /// <summary>Segura o encerramento até o comando de desligar sair ou o prazo vencer.</summary>
    /// <remarks>Task.Run: o handler roda numa thread de janela; esperar a tarefa ali sem ela travaria.</remarks>
    private void DesligarAntesDeSair(string motivo, TimeSpan prazo)
    {
        var tarefas = new List<Task>();
        if (_climate is { } clima) tarefas.Add(Task.Run(() => clima.TurnOffForShutdownAsync(motivo, prazo)));
        if (_switches is { } luz) tarefas.Add(Task.Run(() => luz.TurnOffForShutdownAsync(motivo, prazo)));
        try { Task.WaitAll([.. tarefas], prazo + TimeSpan.FromMilliseconds(500)); }
        catch (AggregateException ex) { Log.Write($"{motivo}: desligar falhou: {ex.InnerException?.Message}"); }
    }

    /// <summary>
    /// <c>--importar-ewelink &lt;keys.json&gt; [--luz &lt;nome&gt;]</c>: chaves no formato <c>{ deviceid: { name, devicekey, uiid } }</c>;
    /// <c>--luz</c> escolhe, pelo nome no app eWeLink, o interruptor da luz do escritório.
    /// </summary>
    private static void ImportEwelinkKeys(string[] args, SettingsStore settings, DpapiSwitchKeyStore store)
    {
        var i = Array.FindIndex(args, a => a.Equals("--importar-ewelink", StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length) return;
        try
        {
            var lidas = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Core.Switches.SwitchKey>>(
                System.IO.File.ReadAllText(args[i + 1]), Json.Options) ?? [];
            var validas = lidas.Where(k => k.Value.DeviceKey is { Length: > 0 }).ToDictionary();
            store.Save(validas);
            Log.Write($"ewelink: {validas.Count} aparelho(s) importado(s).");

            var l = Array.FindIndex(args, a => a.Equals("--luz", StringComparison.OrdinalIgnoreCase));
            if (l < 0 || l + 1 >= args.Length) return;
            var alvo = SemAcento(args[l + 1]);
            var achados = validas.Where(k => SemAcento(k.Value.Name) == alvo).ToList();
            if (achados is [var (id, chave)])
            {
                settings.Update(s => s with { Luz = (s.Luz ?? new LuzSettings { DeviceId = id }) with { DeviceId = id } });
                Log.Write($"ewelink: luz do escritório = {chave.Name?.Trim()}.");
            }
            else Log.Write($"ewelink: --luz casou com {achados.Count} aparelho(s); nada configurado.");
        }
        // Só o tipo: a mensagem da JsonException traz o caminho, que tem o deviceid completo.
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log.Write($"Importação das chaves eWeLink falhou: {ex.GetType().Name}");
        }

        static string SemAcento(string? s) => new string((s ?? "").Trim().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray()).ToUpperInvariant();
    }

    /// <summary><c>--importar-credenciais &lt;arquivo.json&gt;</c> com <c>{ "apiKey", "refreshToken" }</c>.</summary>
    private static void ImportCredentials(string[] args)
    {
        var i = Array.FindIndex(args, a => a.Equals("--importar-credenciais", StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length) return;
        try
        {
            var creds = System.Text.Json.JsonSerializer.Deserialize<Core.Electrolux.ElectroluxCredentials>(
                System.IO.File.ReadAllText(args[i + 1]), Json.Options);
            if (creds is { ApiKey.Length: > 0, RefreshToken.Length: > 0 })
            {
                new DpapiTokenStore().Save(creds);
                Log.Write("Credenciais importadas por linha de comando.");
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Text.Json.JsonException)
        {
            Log.Write($"Importação de credenciais falhou: {ex.Message}");
        }
    }

    /// <summary>
    /// A instalação interativa grava "1;carimbo" ou "0;carimbo". Cada carimbo novo aplica a escolha uma vez;
    /// depois vale o que o usuário mudar em Ajustes (a atualização silenciosa não grava).
    /// </summary>
    private static void ApplyInstallerStartupChoice(SettingsStore settings)
    {
        var reg = new Services.RegistryReader();
        var valor = reg.ReadString(Microsoft.Win32.RegistryHive.CurrentUser, @"Software\MeuClima", "StartWithWindows")
                    ?? reg.ReadString(Microsoft.Win32.RegistryHive.LocalMachine, @"Software\MeuClima", "StartWithWindows");
        if (valor?.Split(';') is not [var on, var carimbo] || carimbo == settings.Current.InstallerChoiceApplied) return;
        settings.Update(s => s with { StartWithWindows = on == "1", InstallerChoiceApplied = carimbo });
#if !DEBUG
        if (on != "1" && Autostart.IsEnabled()) Autostart.Set(false);
#endif
    }

    private void ToggleFlyout()
    {
        if (_flyout is null)
        {
            _flyout = new FlyoutWindow();
            _flyout.Message += _bridge!.HandleAsync;
        }
        _flyout.Toggle();
    }

    private void OpenCentral(string? tab) => OpenCentral(tab, daBandeja: false);

    private void OpenCentral(string? tab, bool daBandeja)
    {
        _flyout?.Hide();
        if (_central is null)
        {
            _central = new CentralWindow();
            _central.Message += _bridge!.HandleAsync;
            _central.ClosedByUser += () =>
            {
                if (!_voltarParaBandeja) return;
                _voltarParaBandeja = false;
                if (_flyout is { IsVisible: false }) _flyout.Toggle();
            };
        }
        _voltarParaBandeja = daBandeja;
        _central.ShowFront(tab);
    }

    private void Quit()
    {
        _stop.Cancel();
        _showSignal?.Set();
        if (_central is not null) _central.AllowClose = true;
        _central?.Close();
        _flyout?.Close();
        _tray?.Dispose();
        _updates?.Dispose();
        _climate?.Dispose();
        _switches?.Dispose();
        _presence?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
