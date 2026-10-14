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
    private UiBridge? _bridge;
    private FlyoutWindow? _flyout;
    private CentralWindow? _central;
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
        ApplyInstallerStartupChoice(settings);
#if !DEBUG
        // Build de desenvolvimento não se registra no logon: o Run apontaria para bin\Debug.
        if (settings.Current.StartWithWindows && !Autostart.IsEnabled()) Autostart.Set(true);
#endif

        _presence = new PresenceMonitor();
        _climate = new ClimateService(settings, new DpapiTokenStore(), _presence);
        _bridge = new UiBridge(_climate, OpenCentral, () => _flyout?.Hide(), Quit);
        _tray = new Tray(ToggleFlyout, OpenCentral, Quit);
        _tray.AutostartChanged += on => _ = _climate.UpdateSettingsAsync(s => s with { StartWithWindows = on });

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

        var viaBandeja = e.Args.Contains("--bandeja", StringComparer.OrdinalIgnoreCase);
        var s = settings.Current;
        var faltaConfigurar = s.City is null || s.PcDeviceId is null || new DpapiTokenStore().Load() is null;
        if (faltaConfigurar) OpenCentral("setup");
        else if (!viaBandeja) ToggleFlyout();
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

    private void OpenCentral(string? tab)
    {
        _flyout?.Hide();
        if (_central is null)
        {
            _central = new CentralWindow();
            _central.Message += _bridge!.HandleAsync;
        }
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
