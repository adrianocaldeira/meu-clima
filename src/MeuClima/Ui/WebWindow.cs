using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using MeuClima.Host;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MeuClima.Ui;

/// <summary>Janela que hospeda uma página das telas em WebView2 e troca mensagens pela ponte.</summary>
public class WebWindow : Window
{
    public const string Host = "app.meuclima";
    private static Task<CoreWebView2Environment>? _env;
    private readonly WebView2 _web = new() { DefaultBackgroundColor = System.Drawing.Color.Transparent };
    private readonly string _page;
    private readonly List<string> _queue = [];
    private bool _ready;
    private long _stateSent;

    public WebWindow(string page)
    {
        _page = page;
        Content = _web;
        Open.Add(this);
        SourceInitialized += (_, _) => ApplyThemeHere();
        Closed += (_, _) => Open.Remove(this);
        Icon = AppIcon.ImageSource;
        Loaded += async (_, _) => await InitAsync();
    }

    public event Func<WebWindow, JsonElement, Task>? Message;

    public static string WebRoot { get; set; } = "";

    private static readonly List<WebWindow> Open = [];
    private static string _theme = "system";

    /// <summary>Aplica o tema a todas as janelas: esquema de cores do WebView2 e barra de título.</summary>
    public static void ApplyTheme(string theme)
    {
        _theme = theme;
        foreach (var w in Open) w.ApplyThemeHere();
    }

    /// <summary>Reaplica quando o Windows troca de tema (só importa no modo "system").</summary>
    public static void RefreshSystemTheme()
    {
        if (_theme == "system") ApplyTheme(_theme);
    }

    private static bool EffectiveDark => _theme switch
    {
        "dark" => true,
        "light" => false,
        _ => Microsoft.Win32.Registry.GetValue(
                 @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0,
    };

    private void ApplyThemeHere()
    {
        if (_web.CoreWebView2 is { } core)
            core.Profile.PreferredColorScheme = _theme switch
            {
                "dark" => CoreWebView2PreferredColorScheme.Dark,
                "light" => CoreWebView2PreferredColorScheme.Light,
                _ => CoreWebView2PreferredColorScheme.Auto,
            };
        // Mesma cor de --panel do app.css: a janela aparece antes de o WebView2 pintar a página.
        Background = new System.Windows.Media.SolidColorBrush(EffectiveDark
            ? System.Windows.Media.Color.FromRgb(0x1b, 0x22, 0x28)
            : System.Windows.Media.Color.FromRgb(0xf8, 0xfa, 0xfb));
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = EffectiveDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    private async Task InitAsync()
    {
        try
        {
            _env ??= CoreWebView2Environment.CreateAsync(null, AppPaths.WebView2Data);
            await _web.EnsureCoreWebView2Async(await _env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show("O Meu Clima precisa do Microsoft Edge WebView2 Runtime. Instale em go.microsoft.com/fwlink/p/?LinkId=2124703 e abra de novo.",
                "Meu Clima", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Windows.Application.Current.Shutdown();
            return;
        }
        var core = _web.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping(Host, WebRoot, CoreWebView2HostResourceAccessKind.DenyCors);
        ApplyThemeHere();
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.NavigationStarting += (_, e) =>
        {
            // Só as telas locais navegam dentro do app; links externos abrem no navegador.
            if (e.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            if (e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) OpenExternal(e.Uri);
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) OpenExternal(e.Uri);
        };
        core.WebMessageReceived += async (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                if (doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "ready")
                {
                    // Página (re)carregada: precisa receber o estado de novo, mesmo que repetido.
                    _stateSent = 0;
                    Flush();
                }
                if (Message is { } handler) await handler(this, doc.RootElement.Clone());
            }
            catch (Exception ex)
            {
                Log.Write($"Mensagem da tela falhou: {ex}");
            }
        };
        core.Navigate($"https://{Host}/{_page}");
    }

    /// <summary>Envia mensagem à página; enfileira até a página avisar <c>ready</c>.</summary>
    public void Post(string type, string dataJson)
    {
        var msg = $"{{\"type\":{JsonSerializer.Serialize(type)},\"data\":{dataJson}}}";
        Dispatcher.InvokeAsync(() => Send(msg));
    }

    /// <summary>Envia o estado, descartando um mais antigo que o último enviado (entregas fora de ordem).</summary>
    public void PostState(StateMessage state)
    {
        var msg = $"{{\"type\":\"state\",\"data\":{state.Json}}}";
        Dispatcher.InvokeAsync(() =>
        {
            if (state.Version <= _stateSent) return;
            _stateSent = state.Version;
            Send(msg);
        });
    }

    private void Send(string msg)
    {
        if (!_ready) { _queue.Add(msg); return; }
        _web.CoreWebView2?.PostWebMessageAsJson(msg);
    }

    private void Flush()
    {
        _ready = true;
        foreach (var m in _queue) _web.CoreWebView2?.PostWebMessageAsJson(m);
        _queue.Clear();
    }

    private static void OpenExternal(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

    protected void RoundCorners()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var pref = 2; // DWMWCP_ROUND (Windows 11); ignorado no Windows 10.
        _ = DwmSetWindowAttribute(hwnd, 33, ref pref, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}

public sealed class FlyoutWindow : WebWindow
{
    private DateTime _shownAt;

    public FlyoutWindow() : base("flyout.html")
    {
        Title = "Meu Clima";
        Width = 380;
        Height = 640;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SourceInitialized += (_, _) => RoundCorners();
        Deactivated += (_, _) =>
        {
            // O clique no ícone da bandeja desativa a janela logo após mostrar; ignora esse primeiro instante.
            if ((DateTime.Now - _shownAt).TotalMilliseconds > 300) Hide();
        };
    }

    /// <summary>Ajusta a altura ao conteúdo informado pela tela (mensagem <c>resize</c>), ancorada no canto da barra.</summary>
    public void FitTo(double contentHeight)
    {
        Height = Math.Clamp(contentHeight, 160, 640);
        Place();
    }

    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 12;
        Top = area.Bottom - Height - 12;
    }

    public void Toggle()
    {
        if (IsVisible) { Hide(); return; }
        Place();
        _shownAt = DateTime.Now;
        Show();
        Activate();
    }
}

public sealed class CentralWindow : WebWindow
{
    public CentralWindow() : base("central.html")
    {
        Title = "Meu Clima";
        Width = 1040;
        Height = 720;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Closing += (_, e) =>
        {
            // Fechar a central só esconde; o app continua na bandeja.
            if (AllowClose) return;
            e.Cancel = true;
            Hide();
        };
    }

    public bool AllowClose { get; set; }

    public void ShowFront(string? tab)
    {
        if (tab is not null) Post("navigate", JsonSerializer.Serialize(new { tab }));
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
}
