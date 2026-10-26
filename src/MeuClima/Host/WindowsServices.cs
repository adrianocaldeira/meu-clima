using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MeuClima.Core.Electrolux;
using MeuClima.Core.Rules;
using MeuClima.Core.Switches;
using Microsoft.Win32;

namespace MeuClima.Host;

/// <summary>Credenciais da Electrolux cifradas com DPAPI (só o usuário atual do Windows decifra).</summary>
public sealed class DpapiTokenStore : ITokenStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MeuClima.Electrolux.v1");
    private readonly Lock _lock = new();

    public ElectroluxCredentials? Load()
    {
        lock (_lock)
        {
            if (!File.Exists(AppPaths.Credentials)) return null;
            try
            {
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(AppPaths.Credentials), Entropy, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<ElectroluxCredentials>(plain);
            }
            catch (CryptographicException)
            {
                Log.Write("credentials.dat não pôde ser decifrado (outro usuário ou arquivo corrompido).");
                return null;
            }
        }
    }

    public void Save(ElectroluxCredentials credentials)
    {
        lock (_lock)
        {
            var cipher = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(credentials), Entropy, DataProtectionScope.CurrentUser);
            var tmp = AppPaths.Credentials + ".tmp";
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                f.Write(cipher);
                f.Flush(flushToDisk: true);
            }
            File.Move(tmp, AppPaths.Credentials, overwrite: true);
        }
    }
}

/// <summary>Chaves dos interruptores eWeLink por deviceid, cifradas com DPAPI como as credenciais da Electrolux.</summary>
public sealed class DpapiSwitchKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MeuClima.Ewelink.v1");
    private readonly Lock _lock = new();

    public Dictionary<string, SwitchKey> Load()
    {
        lock (_lock)
        {
            if (!File.Exists(AppPaths.SwitchKeys)) return [];
            try
            {
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(AppPaths.SwitchKeys), Entropy, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<Dictionary<string, SwitchKey>>(plain) ?? [];
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException)
            {
                Log.Write("ewelink.dat não pôde ser lido (outro usuário ou arquivo corrompido).");
                return [];
            }
        }
    }

    public void Save(Dictionary<string, SwitchKey> keys)
    {
        lock (_lock)
        {
            var cipher = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(keys), Entropy, DataProtectionScope.CurrentUser);
            var tmp = AppPaths.SwitchKeys + ".tmp";
            File.WriteAllBytes(tmp, cipher);
            File.Move(tmp, AppPaths.SwitchKeys, overwrite: true);
        }
    }
}

/// <summary>Início com o Windows pela chave Run do usuário (não exige admin).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MeuClima";

    public static string Command => $"\"{Environment.ProcessPath}\" --bandeja";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string v && v.Equals(Command, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

/// <summary>Ausência = sessão bloqueada ou sem entrada de teclado/mouse há <see cref="IdleThreshold"/>.</summary>
public sealed class PresenceMonitor : IDisposable
{
    public static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(5);
    private DateTimeOffset? _lockedAt;
    private DateTimeOffset? _suspendedAt;

    public PresenceMonitor()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public event Action? Changed;

    /// <summary>PC entrando em suspensão; o handler roda antes de o Windows suspender, com prazo curto.</summary>
    public event Action? Suspending;

    public PresenceState Current(DateTimeOffset now)
    {
        if ((_lockedAt ?? _suspendedAt) is { } t) return new PresenceState(true, now - t, Locked: true);
        var idle = IdleTime();
        return idle >= IdleThreshold ? new PresenceState(true, idle) : PresenceState.Present;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect:
                _lockedAt ??= DateTimeOffset.UtcNow;
                break;
            case SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect:
                _lockedAt = null;
                break;
            default:
                return;
        }
        Log.Write($"Sessão: {e.Reason}");
        Changed?.Invoke();
    }

    /// <summary>Suspensão sem bloqueio conta como bloqueio; o desligamento do ar sai por <see cref="Suspending"/>.</summary>
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) _suspendedAt ??= DateTimeOffset.UtcNow;
        else if (e.Mode == PowerModes.Resume) _suspendedAt = null;
        else return;
        Log.Write($"Energia: {e.Mode}");
        if (e.Mode == PowerModes.Suspend) Suspending?.Invoke();
        Changed?.Invoke();
    }

    private static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        // Tick de 32 bits: a subtração em uint lida com a virada a cada ~49 dias.
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}

/// <summary>Extrai as telas embutidas para uma pasta por versão (o WebView2 serve arquivos de pasta).</summary>
public static class WebAssets
{
    public static string Extract()
    {
        var asm = Assembly.GetExecutingAssembly();
        var version = asm.GetName().Version?.ToString() ?? "dev";
        var root = Path.Combine(AppPaths.Local, "web", version);
#if DEBUG
        // Em desenvolvimento, sempre reextrai para refletir mudanças no HTML sem trocar de versão.
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
#endif
        if (Directory.Exists(root)) return root;
        var tmp = root + ".tmp";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("web/", StringComparison.Ordinal)))
        {
            var rel = name["web/".Length..].Replace('\\', '/');
            var dest = Path.Combine(tmp, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var src = asm.GetManifestResourceStream(name)!;
            using var dst = File.Create(dest);
            src.CopyTo(dst);
        }
        Directory.Move(tmp, root);
        return root;
    }
}
